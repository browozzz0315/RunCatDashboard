namespace RunCatDashboard.App.Animation;

internal enum RunCatGaitPhase
{
    FrontPawContact,
    WeightBearingCompression,
    HindLegPushOff,
    AirborneExtension,
    LimbRecovery,
    FrontPawReach
}

internal static class RunCatFrameSequence
{
    internal static IReadOnlyList<RunCatGaitPhase> GaitPhases { get; } =
        Array.AsReadOnly(
        [
            RunCatGaitPhase.FrontPawContact,
            RunCatGaitPhase.WeightBearingCompression,
            RunCatGaitPhase.HindLegPushOff,
            RunCatGaitPhase.AirborneExtension,
            RunCatGaitPhase.LimbRecovery,
            RunCatGaitPhase.FrontPawReach
        ]);
}
