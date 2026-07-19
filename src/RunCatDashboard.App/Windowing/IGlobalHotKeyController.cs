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

public interface IGlobalHotKeyController : IDisposable
{
    IReadOnlyList<GlobalHotKeyRegistrationState> Registrations { get; }

    IReadOnlyList<GlobalHotKeyRegistrationState> RegisterAll(nint windowHandle);

    bool TryGetAction(int message, nint parameter, out GlobalHotKeyAction action);
}
