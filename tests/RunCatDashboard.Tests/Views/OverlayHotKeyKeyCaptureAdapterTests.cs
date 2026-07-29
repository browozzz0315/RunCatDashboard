using System.Windows.Input;
using RunCatDashboard.App.Views;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Views;

public sealed class OverlayHotKeyKeyCaptureAdapterTests
{
    [Fact]
    public void Capture_AcceptsAllLettersDigitsAndFunctionKeys()
    {
        AssertRange(Key.A, Key.Z, OverlayHotKeyKey.A);
        AssertRange(Key.D0, Key.D9, OverlayHotKeyKey.D0);
        AssertRange(Key.F1, Key.F12, OverlayHotKeyKey.F1);
    }

    [Fact]
    public void Capture_EscapeCancelsWithoutAKey()
    {
        OverlayHotKeyCaptureResult result =
            OverlayHotKeyKeyCaptureAdapter.Capture(Key.Escape);

        Assert.Equal(OverlayHotKeyCaptureOutcome.Cancelled, result.Outcome);
        Assert.Null(result.Key);
    }

    [Theory]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.RightCtrl)]
    [InlineData(Key.LeftAlt)]
    [InlineData(Key.RightAlt)]
    [InlineData(Key.LeftShift)]
    [InlineData(Key.RightShift)]
    [InlineData(Key.LWin)]
    [InlineData(Key.RWin)]
    public void Capture_ModifierOnlyDoesNotProduceAKey(Key key)
    {
        OverlayHotKeyCaptureResult result = OverlayHotKeyKeyCaptureAdapter.Capture(key);

        Assert.Equal(OverlayHotKeyCaptureOutcome.ModifierOnly, result.Outcome);
        Assert.Null(result.Key);
    }

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    [InlineData(Key.Tab)]
    [InlineData(Key.OemPlus)]
    public void Capture_UnsupportedKeyDoesNotProduceAKey(Key key)
    {
        OverlayHotKeyCaptureResult result = OverlayHotKeyKeyCaptureAdapter.Capture(key);

        Assert.Equal(OverlayHotKeyCaptureOutcome.Unsupported, result.Outcome);
        Assert.Null(result.Key);
    }

    private static void AssertRange(Key first, Key last, OverlayHotKeyKey firstExpected)
    {
        for (int offset = 0; offset <= last - first; offset++)
        {
            OverlayHotKeyCaptureResult result =
                OverlayHotKeyKeyCaptureAdapter.Capture((Key)((int)first + offset));

            Assert.Equal(OverlayHotKeyCaptureOutcome.Captured, result.Outcome);
            Assert.Equal((OverlayHotKeyKey)((int)firstExpected + offset), result.Key);
        }
    }
}
