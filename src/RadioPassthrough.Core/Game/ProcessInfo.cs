using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RadioPassthrough.Core.Game;

public static partial class ProcessInfo
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint Th32csSnapProcess = 0x2;
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

    // Id of a running process by exe name ("arma3_x64.exe"), earlier names preferred. Reads Windows' process
    // list snapshot: names and ids only, no handle to any process, and nothing allocated per process.
    public static unsafe int? FindProcess(params string[] exeNames)
    {
        IntPtr snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return null;
        try
        {
            int? best = null;
            int bestRank = exeNames.Length;
            var entry = new ProcessEntry32W { dwSize = (uint)sizeof(ProcessEntry32W) };
            for (bool ok = Process32FirstW(snapshot, ref entry); ok; ok = Process32NextW(snapshot, ref entry))
            {
                var name = MemoryMarshal.CreateReadOnlySpanFromNullTerminated((char*)entry.szExeFile);
                for (int rank = 0; rank < bestRank; rank++)
                {
                    if (!name.Equals(exeNames[rank], StringComparison.OrdinalIgnoreCase)) continue;
                    best = (int)entry.th32ProcessID;
                    bestRank = rank;
                    break;
                }
                if (bestRank == 0) break;
            }
            return best;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

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

    // The app's own description ("Firefox"), read from its exe file on disk; falls back to the process name.
    public static string FriendlyName(Process process)
    {
        try
        {
            if (ImagePath(process.Id) is { } path)
            {
                string? description = FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
        return process.ProcessName;
    }

    // Full path of a process's exe. Asks Windows for query-only access, never for access to the
    // process's memory (which Process.MainModule would need), so games and anti-cheat see nothing unusual.
    public static unsafe string? ImagePath(int pid)
    {
        IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            const int capacity = 1024;
            char* buffer = stackalloc char[capacity];
            int size = capacity;
            return QueryFullProcessImageNameW(process, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
        }
        finally
        {
            CloseHandle(process);
        }
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

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct ProcessEntry32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public UIntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        public fixed ushort szExeFile[260];
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32FirstW(IntPtr snapshot, ref ProcessEntry32W entry);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32NextW(IntPtr snapshot, ref ProcessEntry32W entry);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageNameW(IntPtr process, uint flags, char* exeName, ref int size);
}
