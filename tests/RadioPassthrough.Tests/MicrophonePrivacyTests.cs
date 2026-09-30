using Microsoft.Win32;
using RadioPassthrough.Core.Setup;

namespace RadioPassthrough.Tests;

public sealed class MicrophonePrivacyTests : IDisposable
{
    private readonly string _path = $@"Software\RadioPassthroughTests\{Guid.NewGuid():N}";
    private readonly RegistryKey _machine;
    private readonly RegistryKey _user;

    public MicrophonePrivacyTests()
    {
        _machine = Registry.CurrentUser.CreateSubKey(_path + @"\machine");
        _user = Registry.CurrentUser.CreateSubKey(_path + @"\user");
    }

    private bool Blocked() => MicrophonePrivacy.IsBlocked(_machine, _user);

    private void Set(RegistryKey root, string subPath, string value)
    {
        using var key = root.CreateSubKey(MicrophonePrivacy.ConsentPath + subPath);
        key.SetValue("Value", value);
    }

    [Fact]
    public void Nothing_set_means_allowed() => Assert.False(Blocked());

    [Fact]
    public void Allowed_everywhere_is_not_blocked()
    {
        Set(_machine, "", "Allow");
        Set(_user, "", "Allow");
        Set(_user, @"\NonPackaged", "Allow");
        Assert.False(Blocked());
    }

    [Theory]
    [InlineData("machine", "")]
    [InlineData("user", "")]
    [InlineData("user", @"\NonPackaged")]
    public void Any_deny_blocks(string which, string subPath)
    {
        Set(which == "machine" ? _machine : _user, subPath, "Deny");
        Assert.True(Blocked());
    }

    public void Dispose()
    {
        _machine.Dispose();
        _user.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
    }
}
