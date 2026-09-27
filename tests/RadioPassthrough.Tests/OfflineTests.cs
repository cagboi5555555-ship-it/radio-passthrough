using RadioPassthrough.Core;

namespace RadioPassthrough.Tests;

public class OfflineTests
{
    // Radio Passthrough never connects to the internet. This fails if networking code creeps back in.
    [Fact]
    public void Core_does_not_reference_any_networking_library()
    {
        var references = typeof(AppInfo).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "").ToList();
        Assert.DoesNotContain(references, n => n.StartsWith("System.Net.Http", StringComparison.Ordinal));
        Assert.DoesNotContain(references, n => n.StartsWith("System.Net.Sockets", StringComparison.Ordinal));
        Assert.DoesNotContain(references, n => n.StartsWith("System.Net.WebClient", StringComparison.Ordinal));
    }
}
