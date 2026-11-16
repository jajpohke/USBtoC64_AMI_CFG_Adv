namespace UsbToC64.Core.Model;

/// <summary>Human readable names, aligned with the firmware <c>dumppad</c> output.</summary>
public static class Names
{
    public static string Func(JmFunc f) => f switch
    {
        JmFunc.Up => "UP",
        JmFunc.UpRight => "UP_RIGHT",
        JmFunc.Right => "RIGHT",
        JmFunc.RightDown => "RIGHT_DOWN",
        JmFunc.Down => "DOWN",
        JmFunc.DownLeft => "DOWN_LEFT",
        JmFunc.Left => "LEFT",
        JmFunc.LeftUp => "LEFT_UP",
        JmFunc.Fire1 => "FIRE 1",
        JmFunc.Fire2 => "FIRE 2",
        JmFunc.Fire3 => "FIRE 3",
        JmFunc.AutofireOn => "AUTO ON",
        JmFunc.AutofireOff => "AUTO OFF",
        JmFunc.AutofireHold => "AUTO HOLD",
        JmFunc.AutoLeftRightOn => "AUTO L/R ON",
        JmFunc.AutoLeftRightOff => "AUTO L/R OFF",
        _ => "???",
    };

    public static string Op(JmOp op) => op switch
    {
        JmOp.Eq => "==",
        JmOp.BitAny => "BIT",
        JmOp.Hat => "HAT",
        JmOp.SignedLess => "S<",
        JmOp.SignedGreater => "S>",
        _ => "???",
    };

    public static string Rule(JmRule r) => r.Op switch
    {
        JmOp.BitAny => $"Byte[{r.Index}] BIT 0x{r.Value:X2} -> {Func(r.Func)}",
        JmOp.SignedLess or JmOp.SignedGreater => $"Byte[{r.Index}] {Op(r.Op)} {(sbyte)r.Value,4} -> {Func(r.Func)}",
        _ => $"Byte[{r.Index}] {Op(r.Op)} {r.Value,3} -> {Func(r.Func)}",
    };
}
