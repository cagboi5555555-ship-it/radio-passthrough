using RadioPassthrough.Core.Ptt;

namespace RadioPassthrough.Tests;

public class PttTests
{
    private const int Caps = PttBinding.CapsLock;

    [Theory]
    [InlineData(Modifiers.None)]
    [InlineData(Modifiers.Shift)]
    [InlineData(Modifiers.Ctrl)]
    [InlineData(Modifiers.Alt)]
    public void Acre_radio_keys_open_the_gate(Modifiers held)
    {
        var state = new PttState(PttBinding.AcreDefaults());
        Assert.True(state.OnTrigger(TriggerKind.Key, Caps, true, held, gameFocused: true));
        Assert.True(state.IsOpen);
        Assert.True(state.OnTrigger(TriggerKind.Key, Caps, false, Modifiers.None, gameFocused: true));
        Assert.False(state.IsOpen);
    }

    [Fact]
    public void Ctrl_alt_caps_opens_the_radio_gui_not_the_gate()
    {
        var state = new PttState(PttBinding.AcreDefaults());
        state.OnTrigger(TriggerKind.Key, Caps, true, Modifiers.Ctrl | Modifiers.Alt, gameFocused: true);
        Assert.False(state.IsOpen);
    }

    [Fact]
    public void Nothing_happens_outside_the_game()
    {
        var state = new PttState(PttBinding.AcreDefaults());
        state.OnTrigger(TriggerKind.Key, Caps, true, Modifiers.None, gameFocused: false);
        Assert.False(state.IsOpen);
    }

    [Fact]
    public void Releasing_modifier_first_keeps_gate_until_key_is_released()
    {
        var state = new PttState(PttBinding.AcreDefaults());
        state.OnTrigger(TriggerKind.Key, Caps, true, Modifiers.Shift, gameFocused: true);
        Assert.True(state.IsOpen);
        state.OnTrigger(TriggerKind.Key, Caps, true, Modifiers.None, gameFocused: true);
        Assert.True(state.IsOpen);
        state.OnTrigger(TriggerKind.Key, Caps, false, Modifiers.None, gameFocused: true);
        Assert.False(state.IsOpen);
    }

    [Fact]
    public void Mouse_button_binding_works()
    {
        var state = new PttState([new PttBinding(TriggerKind.Mouse, MouseButtons.X1, Modifiers.None)]);
        state.OnTrigger(TriggerKind.Mouse, MouseButtons.X2, true, Modifiers.None, true);
        Assert.False(state.IsOpen);
        state.OnTrigger(TriggerKind.Mouse, MouseButtons.X1, true, Modifiers.None, true);
        Assert.True(state.IsOpen);
    }

    [Fact]
    public void Key_names_read_naturally()
    {
        Assert.Equal("Caps Lock", KeyNames.Describe(new PttBinding(TriggerKind.Key, Caps, Modifiers.None)));
        Assert.Equal("Ctrl + Caps Lock", KeyNames.Describe(new PttBinding(TriggerKind.Key, Caps, Modifiers.Ctrl)));
        Assert.Equal("Mouse 4", KeyNames.Describe(new PttBinding(TriggerKind.Mouse, MouseButtons.X1, Modifiers.None)));
    }
}
