using RadioPassthrough.Core.Ptt;

namespace RadioPassthrough.Tests;

public class KeyPollerTests
{
    private readonly HashSet<int> _down = new();
    private bool _focused = true;

    private KeyPoller Poller(List<PttBinding>? bindings = null) =>
        new(bindings ?? PttBinding.AcreDefaults(), () => _focused, vk => _down.Contains(vk));

    [Fact]
    public void Caps_opens_and_closes_the_gate()
    {
        var poller = Poller();
        var events = new List<bool>();
        poller.RadioKeyChanged += events.Add;

        _down.Add(PttBinding.CapsLock);
        poller.Poll();
        Assert.True(poller.IsRadioKeyHeld);

        poller.Poll(); // held: no repeat event
        _down.Remove(PttBinding.CapsLock);
        poller.Poll();

        Assert.False(poller.IsRadioKeyHeld);
        Assert.Equal([true, false], events);
    }

    [Fact]
    public void Modifiers_are_matched_exactly()
    {
        var poller = Poller();
        _down.UnionWith([KeyPoller.VkControl, KeyPoller.VkMenu, PttBinding.CapsLock]);
        poller.Poll();
        Assert.False(poller.IsRadioKeyHeld); // Ctrl+Alt+Caps opens ACRE's radio screen

        _down.Clear();
        poller.Poll();
        _down.UnionWith([KeyPoller.VkShift, PttBinding.CapsLock]);
        poller.Poll();
        Assert.True(poller.IsRadioKeyHeld);
    }

    [Fact]
    public void Ignores_keys_while_the_game_isnt_focused()
    {
        var poller = Poller();
        _focused = false;
        _down.Add(PttBinding.CapsLock);
        poller.Poll();
        Assert.False(poller.IsRadioKeyHeld);
    }

    [Fact]
    public void Mouse_side_buttons_map_to_their_virtual_keys()
    {
        var poller = Poller([new PttBinding(TriggerKind.Mouse, MouseButtons.X2, Modifiers.None)]);
        _down.Add(0x06);
        poller.Poll();
        Assert.True(poller.IsRadioKeyHeld);
    }

    [Fact]
    public async Task Capture_reports_the_next_new_key_with_modifiers()
    {
        var poller = Poller();
        var got = new TaskCompletionSource<PttBinding?>();
        _down.Add(0x41); // already held when capture starts: ignored
        poller.CaptureNext(b => got.TrySetResult(b));
        poller.Poll();

        _down.UnionWith([KeyPoller.VkControl, 0x56]); // Ctrl + V
        poller.Poll();

        var binding = await got.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(new PttBinding(TriggerKind.Key, 0x56, Modifiers.Ctrl), binding);
    }

    [Fact]
    public async Task Escape_cancels_capture()
    {
        var poller = Poller();
        var got = new TaskCompletionSource<PttBinding?>();
        poller.CaptureNext(b => got.TrySetResult(b));
        poller.Poll();
        _down.Add(KeyPoller.VkEscape);
        poller.Poll();
        Assert.Null(await got.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }
}
