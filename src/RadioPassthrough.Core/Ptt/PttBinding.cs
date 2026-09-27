namespace RadioPassthrough.Core.Ptt;

[Flags]
public enum Modifiers
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4,
}

public enum TriggerKind
{
    Key,
    Mouse,
}

public static class MouseButtons
{
    public const int Middle = 3;
    public const int X1 = 4;
    public const int X2 = 5;
}

public sealed record PttBinding(TriggerKind Kind, int Code, Modifiers Modifiers)
{
    public const int CapsLock = 0x14;

    // ACRE2 defaults: Default radio, Alt radio 1 (Shift), 2 (Ctrl), 3 (Alt). Ctrl+Alt+Caps opens the
    // radio GUI and is intentionally not a transmit key.
    public static List<PttBinding> AcreDefaults() =>
    [
        new(TriggerKind.Key, CapsLock, Modifiers.None),
        new(TriggerKind.Key, CapsLock, Modifiers.Shift),
        new(TriggerKind.Key, CapsLock, Modifiers.Ctrl),
        new(TriggerKind.Key, CapsLock, Modifiers.Alt),
    ];
}
