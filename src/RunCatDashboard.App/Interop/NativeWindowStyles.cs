namespace RunCatDashboard.App.Interop;

[Flags]
internal enum ExtendedWindowStyle : long
{
    None = 0,
    Transparent = 0x00000020L,
    ToolWindow = 0x00000080L,
    NoActivate = 0x08000000L
}

internal static class NativeWindowStyleBits
{
    internal static long Add(long currentStyle, ExtendedWindowStyle styles)
    {
        return currentStyle | (long)styles;
    }

    internal static long Remove(long currentStyle, ExtendedWindowStyle styles)
    {
        return currentStyle & ~(long)styles;
    }

    internal static nint ToNativeValue(long style)
    {
        return nint.Size == sizeof(long)
            ? new nint(style)
            : new nint(unchecked((int)style));
    }

    internal static long FromNativeValue(nint style)
    {
        return nint.Size == sizeof(long)
            ? style.ToInt64()
            : style.ToInt32();
    }
}
