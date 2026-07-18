using RunCatDashboard.App.Interop;

namespace RunCatDashboard.Tests.Windowing;

public sealed class NativeWindowStyleBitsTests
{
    [Fact]
    public void Add_PreservesExistingBitsAndAddsOnlyRequestedFlags()
    {
        const long existingStyle = 0x00004000L;

        long result = NativeWindowStyleBits.Add(
            existingStyle,
            ExtendedWindowStyle.Transparent | ExtendedWindowStyle.NoActivate);

        Assert.Equal(
            existingStyle |
            (long)ExtendedWindowStyle.Transparent |
            (long)ExtendedWindowStyle.NoActivate,
            result);
    }

    [Fact]
    public void Remove_RemovesOnlyRequestedFlags()
    {
        const long unrelatedStyle = 0x00004000L;
        long existingStyle = unrelatedStyle |
            (long)ExtendedWindowStyle.Transparent |
            (long)ExtendedWindowStyle.ToolWindow |
            (long)ExtendedWindowStyle.NoActivate;

        long result = NativeWindowStyleBits.Remove(
            existingStyle,
            ExtendedWindowStyle.Transparent | ExtendedWindowStyle.NoActivate);

        Assert.Equal(
            unrelatedStyle | (long)ExtendedWindowStyle.ToolWindow,
            result);
    }

    [Fact]
    public void NativeValueConversion_OnX64_RoundTripsPointerSizedStyle()
    {
        Assert.Equal(sizeof(long), nint.Size);
        const long style = 0x00000001080040A0L;

        nint nativeValue = NativeWindowStyleBits.ToNativeValue(style);
        long result = NativeWindowStyleBits.FromNativeValue(nativeValue);

        Assert.Equal(style, result);
    }
}
