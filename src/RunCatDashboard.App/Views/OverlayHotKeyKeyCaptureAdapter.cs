using System.Windows.Input;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.App.Views;

internal enum OverlayHotKeyCaptureOutcome
{
    Captured,
    Cancelled,
    ModifierOnly,
    Unsupported
}

internal readonly record struct OverlayHotKeyCaptureResult(
    OverlayHotKeyCaptureOutcome Outcome,
    OverlayHotKeyKey? Key = null);

internal static class OverlayHotKeyKeyCaptureAdapter
{
    internal static OverlayHotKeyCaptureResult Capture(Key key)
    {
        if (key == Key.Escape)
        {
            return new(OverlayHotKeyCaptureOutcome.Cancelled);
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return new(OverlayHotKeyCaptureOutcome.ModifierOnly);
        }

        if (key is >= Key.A and <= Key.Z)
        {
            return Captured(
                (int)OverlayHotKeyKey.A + (key - Key.A));
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            return Captured(
                (int)OverlayHotKeyKey.D0 + (key - Key.D0));
        }

        if (key is >= Key.F1 and <= Key.F12)
        {
            return Captured(
                (int)OverlayHotKeyKey.F1 + (key - Key.F1));
        }

        return new(OverlayHotKeyCaptureOutcome.Unsupported);
    }

    private static OverlayHotKeyCaptureResult Captured(int key) =>
        new(OverlayHotKeyCaptureOutcome.Captured, (OverlayHotKeyKey)key);
}
