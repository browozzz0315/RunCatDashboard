namespace RunCatDashboard.App.Windowing;

internal static class WindowPositionPersistence
{
    internal static bool ShouldSave(bool isRestoreComplete, double left, double top) =>
        isRestoreComplete && double.IsFinite(left) && double.IsFinite(top);
}
