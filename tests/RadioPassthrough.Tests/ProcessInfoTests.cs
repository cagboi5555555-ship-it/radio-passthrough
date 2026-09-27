using System.Diagnostics;
using RadioPassthrough.Core.Game;

namespace RadioPassthrough.Tests;

public class ProcessInfoTests
{
    [Fact]
    public void Image_path_is_read_without_opening_process_memory()
    {
        Assert.Equal(Environment.ProcessPath, ProcessInfo.ImagePath(Environment.ProcessId), StringComparer.OrdinalIgnoreCase);
        Assert.Null(ProcessInfo.ImagePath(0)); // the idle process can't be opened
    }

    [Fact]
    public void Finds_running_processes_by_exe_name_from_the_snapshot()
    {
        string self = Path.GetFileName(Environment.ProcessPath)!;
        int? found = ProcessInfo.FindProcess("no-such-program-rp.exe", self.ToUpperInvariant());
        Assert.NotNull(found);
        using var process = Process.GetProcessById(found!.Value);
        Assert.Equal(Path.GetFileNameWithoutExtension(self), process.ProcessName, StringComparer.OrdinalIgnoreCase);
        Assert.Null(ProcessInfo.FindProcess("no-such-program-rp.exe"));
    }

    [Fact]
    public void Friendly_name_comes_from_the_exe_description()
    {
        using var self = Process.GetCurrentProcess();
        string expected = FileVersionInfo.GetVersionInfo(Environment.ProcessPath!).FileDescription is { Length: > 0 } d ? d.Trim() : self.ProcessName;
        Assert.Equal(expected, ProcessInfo.FriendlyName(self));
    }
}
