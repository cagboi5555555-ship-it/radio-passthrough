using RadioPassthrough.Core.Diagnostics;

namespace RadioPassthrough.Tests;

public class DiagnosticsTests
{
    // Diagnostics get pasted in public Discord channels: the Windows user name must never be in them.
    [Fact]
    public void Redact_removes_the_profile_path_and_user_name()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string user = Environment.UserName;
        string text = $"Running from: {profile}\\AppData\\Local\\Programs\\Radio Passthrough\\RadioPassthrough.exe\nsigned in as {user}";

        string redacted = DiagnosticsReport.Redact(text);

        Assert.DoesNotContain(profile, redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%USERPROFILE%", redacted);
        if (user.Length > 2) Assert.DoesNotContain(user, redacted, StringComparison.OrdinalIgnoreCase);
    }
}
