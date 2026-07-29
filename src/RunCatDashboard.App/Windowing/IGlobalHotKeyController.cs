namespace RunCatDashboard.App.Windowing;

public enum GlobalHotKeyAction
{
    ToggleInteractionMode,
    ToggleDashboardVisibility
}

public sealed record GlobalHotKeyRegistrationState(
    GlobalHotKeyAction Action,
    int Identifier,
    string GestureText,
    bool IsRegistered,
    string? Fault,
    int? NativeErrorCode);

public sealed record GlobalHotKeyApplyResult(
    bool IsSuccess,
    bool IsNoOp,
    bool RollbackSucceeded,
    bool RequiresSafeRecovery,
    string? Fault);

public sealed class HotKeyConfigurationException(string message) : Exception(message);

public interface IGlobalHotKeyController : IDisposable
{
    IReadOnlyList<GlobalHotKeyRegistrationState> Registrations { get; }

    OverlayHotKeyGesture InteractionGesture { get; }

    IReadOnlyList<GlobalHotKeyRegistrationState> RegisterAll(nint windowHandle);

    GlobalHotKeyApplyResult ApplyInteractionGesture(OverlayHotKeyGesture gesture);

    bool TryGetAction(int message, nint parameter, out GlobalHotKeyAction action);
}
