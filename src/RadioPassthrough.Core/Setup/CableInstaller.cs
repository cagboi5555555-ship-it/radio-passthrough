using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using RadioPassthrough.Core.Audio;
using RadioPassthrough.Core.Diagnostics;

namespace RadioPassthrough.Core.Setup;

public enum CableInstallOutcome
{
    Installed,
    NeedsRestart,
    Cancelled,
    Failed,
}

public sealed record CableInstallResult(CableInstallOutcome Outcome, string Message);

// Downloads VB-CABLE from VB-Audio's own server, checks the installer carries VB-Audio's valid
// signature, and runs its silent install (one Windows admin prompt). Nothing is bundled with this app.
public static partial class CableInstaller
{
    public const string PackUrl = "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack45.zip";
    public const string WebsiteUrl = "https://vb-audio.com/Cable/";
    private const long MaxDownloadBytes = 50L * 1024 * 1024;
    private static readonly string[] TrustedSigners = ["Burel", "VB-Audio", "VB-AUDIO"];

    public static async Task<CableInstallResult> InstallAsync(IProgress<string>? progress, CancellationToken ct)
    {
        string work = Path.Combine(Path.GetTempPath(), "RadioPassthrough", "VBCABLE");
        try
        {
            if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
            Directory.CreateDirectory(work);

            progress?.Report("Downloading VB-CABLE from vb-audio.com…");
            string zip = Path.Combine(work, "VBCABLE_Driver_Pack.zip");
            await DownloadAsync(PackUrl, zip, ct).ConfigureAwait(false);

            progress?.Report("Checking the download…");
            string extracted = Path.Combine(work, "pack");
            ZipFile.ExtractToDirectory(zip, extracted, overwriteFiles: true);
            string? setup = FindSetup(extracted);
            if (setup is null)
                return Fail("The VB-CABLE download didn't contain an installer for this PC.");
            if (!IsSignedByVbAudio(setup, out string signer))
                return Fail($"The VB-CABLE installer's signature couldn't be verified ({signer}). Nothing was installed.");

            progress?.Report("Installing. Approve the Windows prompt…");
            Log.Info($"Running {Path.GetFileName(setup)} -i -h (signed by {signer}).");
            using (var process = Process.Start(new ProcessStartInfo(setup, "-i -h")
                   {
                       UseShellExecute = true,
                       Verb = "runas",
                       WorkingDirectory = Path.GetDirectoryName(setup),
                   }))
            {
                if (process is null) return Fail("The VB-CABLE installer didn't start.");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromMinutes(5));
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                Log.Info($"VB-CABLE installer exited with {process.ExitCode}.");
            }

            progress?.Report("Waiting for Windows to add the cable…");
            for (int i = 0; i < 30; i++)
            {
                if (AudioDevices.CableInput() is not null && AudioDevices.CableOutput() is not null)
                    return new CableInstallResult(CableInstallOutcome.Installed, "VB-CABLE is installed.");
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
            return new CableInstallResult(CableInstallOutcome.NeedsRestart, "VB-CABLE is installed. Restart your PC to finish, then open Radio Passthrough again.");
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            Log.Info("VB-CABLE install cancelled at the Windows prompt.");
            return new CableInstallResult(CableInstallOutcome.Cancelled, "Install cancelled. Press Install again when you're ready.");
        }
        catch (OperationCanceledException)
        {
            return new CableInstallResult(CableInstallOutcome.Cancelled, "Install cancelled.");
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or Win32Exception)
        {
            return Fail($"Couldn't install VB-CABLE automatically: {e.Message}");
        }
        finally
        {
            try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static CableInstallResult Fail(string message)
    {
        Log.Warn(message);
        return new CableInstallResult(CableInstallOutcome.Failed, message);
    }

    private static async Task DownloadAsync(string url, string target, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"RadioPassthrough/{AppInfo.Version}");
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxDownloadBytes)
            throw new InvalidDataException("The download is larger than expected.");

        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var file = File.Create(target);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > MaxDownloadBytes) throw new InvalidDataException("The download is larger than expected.");
            await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
    }

    internal static string? FindSetup(string folder)
    {
        var exes = Directory.EnumerateFiles(folder, "*.exe", SearchOption.AllDirectories).ToList();
        string? Pick(Func<string, bool> match) => exes.FirstOrDefault(f => match(Path.GetFileName(f)));

        return RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => Pick(n => n.Contains("arm64", StringComparison.OrdinalIgnoreCase) && n.Contains("setup", StringComparison.OrdinalIgnoreCase)),
            Architecture.X64 => Pick(n => n.Equals("VBCABLE_Setup_x64.exe", StringComparison.OrdinalIgnoreCase)),
            _ => Pick(n => n.Equals("VBCABLE_Setup.exe", StringComparison.OrdinalIgnoreCase)),
        };
    }

    // True when Windows trusts the file's Authenticode signature and the signer is VB-Audio.
    public static bool IsSignedByVbAudio(string path, out string signer)
    {
        signer = "unsigned";
        if (!WinTrust.Verify(path)) return false;
        try
        {
#pragma warning disable SYSLIB0057
            using var cert = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            signer = cert.Subject;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
        string s = signer;
        return TrustedSigners.Any(t => s.Contains(t, StringComparison.OrdinalIgnoreCase));
    }
}

internal static partial class WinTrust
{
    private static Guid _genericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public static bool Verify(string path)
    {
        var fileInfo = new FileInfoStruct
        {
            Size = (uint)Marshal.SizeOf<FileInfoStruct>(),
            FilePath = Marshal.StringToCoTaskMemUni(path),
        };
        IntPtr filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfoStruct>());
        try
        {
            Marshal.StructureToPtr(fileInfo, filePtr, false);
            var data = new TrustData
            {
                Size = (uint)Marshal.SizeOf<TrustData>(),
                UiChoice = 2,          // WTD_UI_NONE
                RevocationChecks = 0,  // WTD_REVOKE_NONE: works offline
                UnionChoice = 1,       // WTD_CHOICE_FILE
                File = filePtr,
                StateAction = 0,       // WTD_STATEACTION_IGNORE
            };
            return WinVerifyTrust(IntPtr.Zero, ref _genericVerifyV2, ref data) == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(filePtr);
            Marshal.FreeCoTaskMem(fileInfo.FilePath);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfoStruct
    {
        public uint Size;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    [LibraryImport("wintrust.dll")]
    private static partial int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref TrustData data);
}
