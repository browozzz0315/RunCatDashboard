using System.ComponentModel;
using RunCatDashboard.App.Interop;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Windowing;

public sealed class GlobalHotKeyControllerTests
{
    private static readonly nint WindowHandle = new(4321);

    [Fact]
    public void RegisterAll_RegistersDistinctRAndDHotKeysOnce()
    {
        var native = new FakeNativeGlobalHotKeyApi();
        var controller = new GlobalHotKeyController(native);

        IReadOnlyList<GlobalHotKeyRegistrationState> first =
            controller.RegisterAll(WindowHandle);
        IReadOnlyList<GlobalHotKeyRegistrationState> second =
            controller.RegisterAll(WindowHandle);

        Assert.Equal(2, native.RegisterCalls.Count);
        Assert.Equal(2, first.Count(state => state.IsRegistered));
        Assert.Equal(first, second);
        Assert.NotEqual(
            GlobalHotKeyController.InteractionHotKeyIdentifier,
            GlobalHotKeyController.VisibilityHotKeyIdentifier);
        Assert.Contains(native.RegisterCalls, call =>
            call.Identifier == GlobalHotKeyController.InteractionHotKeyIdentifier &&
            call.VirtualKey == GlobalHotKeyController.VirtualKeyR);
        Assert.Contains(native.RegisterCalls, call =>
            call.Identifier == GlobalHotKeyController.VisibilityHotKeyIdentifier &&
            call.VirtualKey == GlobalHotKeyController.VirtualKeyD);
        Assert.All(native.RegisterCalls, call =>
            Assert.Equal(GlobalHotKeyController.HotKeyModifiers, call.Modifiers));
    }

    [Fact]
    public void RegisterAll_WhenOneRegistrationFails_PreservesOtherAndFaultDetails()
    {
        var native = new FakeNativeGlobalHotKeyApi();
        native.RegisterFailures[GlobalHotKeyController.VisibilityHotKeyIdentifier] =
            new Win32Exception(1409, "Hot key is already registered.");
        var controller = new GlobalHotKeyController(native);

        IReadOnlyList<GlobalHotKeyRegistrationState> states =
            controller.RegisterAll(WindowHandle);

        GlobalHotKeyRegistrationState interaction = states.Single(state =>
            state.Action == GlobalHotKeyAction.ToggleInteractionMode);
        GlobalHotKeyRegistrationState visibility = states.Single(state =>
            state.Action == GlobalHotKeyAction.ToggleDashboardVisibility);
        Assert.True(interaction.IsRegistered);
        Assert.False(visibility.IsRegistered);
        Assert.Equal(1409, visibility.NativeErrorCode);
        Assert.Equal(
            "顯示／隱藏快捷鍵註冊失敗，可能已被其他程式使用。",
            visibility.Fault);
    }

    [Fact]
    public void TryGetAction_DispatchesOnlySuccessfullyRegisteredIdentifier()
    {
        var native = new FakeNativeGlobalHotKeyApi();
        native.RegisterFailures[GlobalHotKeyController.VisibilityHotKeyIdentifier] =
            new Win32Exception(1409);
        var controller = new GlobalHotKeyController(native);
        controller.RegisterAll(WindowHandle);

        Assert.True(controller.TryGetAction(
            GlobalHotKeyController.WindowMessageHotKey,
            new nint(GlobalHotKeyController.InteractionHotKeyIdentifier),
            out GlobalHotKeyAction action));
        Assert.Equal(GlobalHotKeyAction.ToggleInteractionMode, action);
        Assert.False(controller.TryGetAction(
            GlobalHotKeyController.WindowMessageHotKey,
            new nint(GlobalHotKeyController.VisibilityHotKeyIdentifier),
            out _));
    }

    [Fact]
    public void Dispose_UnregistersOnlySuccessfulRegistrationsAndIsIdempotent()
    {
        var native = new FakeNativeGlobalHotKeyApi();
        native.RegisterFailures[GlobalHotKeyController.VisibilityHotKeyIdentifier] =
            new Win32Exception(1409);
        var controller = new GlobalHotKeyController(native);
        controller.RegisterAll(WindowHandle);

        controller.Dispose();
        controller.Dispose();

        Assert.Equal(
            [GlobalHotKeyController.InteractionHotKeyIdentifier],
            native.UnregisterCalls);
    }

    [Fact]
    public void Dispose_WhenUnregisterFails_RetainsDiagnosticWithoutThrowing()
    {
        var native = new FakeNativeGlobalHotKeyApi();
        native.UnregisterFailures[GlobalHotKeyController.InteractionHotKeyIdentifier] =
            new Win32Exception(5, "Access denied.");
        var controller = new GlobalHotKeyController(native);
        controller.RegisterAll(WindowHandle);

        Exception? exception = Record.Exception(controller.Dispose);

        Assert.Null(exception);
        GlobalHotKeyRegistrationState state = controller.Registrations.Single(item =>
            item.Action == GlobalHotKeyAction.ToggleInteractionMode);
        Assert.True(state.IsRegistered);
        Assert.Equal(5, state.NativeErrorCode);
        Assert.Contains("解除快捷鍵", state.Fault);
    }

    private sealed class FakeNativeGlobalHotKeyApi : INativeGlobalHotKeyApi
    {
        internal List<(int Identifier, uint Modifiers, uint VirtualKey)> RegisterCalls { get; } = [];
        internal List<int> UnregisterCalls { get; } = [];
        internal Dictionary<int, Win32Exception> RegisterFailures { get; } = [];
        internal Dictionary<int, Win32Exception> UnregisterFailures { get; } = [];

        public void Register(nint windowHandle, int identifier, uint modifiers, uint virtualKey)
        {
            RegisterCalls.Add((identifier, modifiers, virtualKey));
            if (RegisterFailures.TryGetValue(identifier, out Win32Exception? failure))
            {
                throw failure;
            }
        }

        public void Unregister(nint windowHandle, int identifier)
        {
            UnregisterCalls.Add(identifier);
            if (UnregisterFailures.TryGetValue(identifier, out Win32Exception? failure))
            {
                throw failure;
            }
        }
    }
}
