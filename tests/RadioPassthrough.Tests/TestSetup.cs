using System.Runtime.CompilerServices;
using RadioPassthrough.Core.Diagnostics;

namespace RadioPassthrough.Tests;

internal static class TestSetup
{
    // Keep test runs out of the real app log that diagnostics are built from.
    [ModuleInitializer]
    internal static void Init() => Log.Directory = Path.Combine(Path.GetTempPath(), "RadioPassthrough.Tests", "logs");
}
