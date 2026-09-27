using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RadioPassthrough.Core.Game;

public static partial class ProcessInfo
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevation = 20;

    public static int ForegroundProcessId()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return (int)pid;
    }

    public static bool IsCurrentProcessElevated => Environment.IsPrivilegedProcess;

    // True when the process runs as administrator. A process we cannot inspect from here is treated as
    // elevated, which is the only reason a normal process is refused its token.
    public static bool IsElevated(int pid)
    {
        IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
        if (process == IntPtr.Zero) return Marshal.GetLastWin32Error() == 5;
        try
        {
            if (!OpenProcessToken(process, TokenQuery, out IntPtr token))
                return Marshal.GetLastWin32Error() == 5;
            try
            {
                int elevation = 0;
                return GetTokenInformation(token, TokenElevation, ref elevation, sizeof(int), out _) && elevation != 0;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    public static string FriendlyName(Process process)
    {
        try
        {
            string? description = process.MainModule?.FileVersionInfo.FileDescription;
            if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
        }
        catch
        {
            // Protected or 32/64-bit mismatch: fall back to the process name.
        }
        return process.ProcessName;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, ref int tokenInformation, int tokenInformationLength, out int returnLength);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}
