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

    [Fact]
    public void ApplyInteractionGesture_SameRegisteredGesture_IsNoOp()
    {
        var native = new FakeNativeGlobalHotKeyApi();
        var controller = new GlobalHotKeyController(native);
        controller.RegisterAll(WindowHandle);

        GlobalHotKeyApplyResult result = controller.ApplyInteractionGesture(
            OverlayHotKeyGesture.Default);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsNoOp);
        Assert.Equal(2, native.RegisterCalls.Count);
        Assert.Empty(native.UnregisterCalls);
    }

    [Fact]
    public void ApplyInteractionGesture_NewRegistrationSucceeds_ReplacesOldGesture()
    {
        var native = new FakeNativeGlobalHotKeyApi();
        var controller = new GlobalHotKeyController(native);
        controller.RegisterAll(WindowHandle);
        var replacement = new OverlayHotKeyGesture(
            true, false, false, true, OverlayHotKeyKey.F12);

        GlobalHotKeyApplyResult result =
            controller.ApplyInteractionGesture(replacement);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsNoOp);
        Assert.Equal(replacement, controller.InteractionGesture);
        Assert.Equal(
            [GlobalHotKeyController.InteractionHotKeyIdentifier],
            native.UnregisterCalls);
        Assert.Contains(native.RegisterCalls, call =>
            call.Identifier == GlobalHotKeyController.InteractionHotKeyIdentifier &&
            call.Modifiers == (GlobalHotKeyController.ModifierControl |
                GlobalHotKeyController.ModifierWindows |
                GlobalHotKeyController.ModifierNoRepeat) &&
            call.VirtualKey == (uint)OverlayHotKeyKey.F12);
    }

    [Fact]
    public void ApplyInteractionGesture_NewRegistrationFails_RollsBackOldGesture()
    {
        var native = new FakeNativeGlobalHotKeyApi();
        native.InteractionRegisterSequence.Enqueue(null);
        native.InteractionRegisterSequence.Enqueue(new Win32Exception(1409));
        native.InteractionRegisterSequence.Enqueue(null);
        var controller = new GlobalHotKeyController(native);
        controller.RegisterAll(WindowHandle);
        var replacement = new OverlayHotKeyGesture(
            true, false, true, false, OverlayHotKeyKey.F11);

        GlobalHotKeyApplyResult result =
            controller.ApplyInteractionGesture(replacement);

        Assert.False(result.IsSuccess);
        Assert.True(result.RollbackSucceeded);
        Assert.False(result.RequiresSafeRecovery);
        Assert.Equal(OverlayHotKeyGesture.Default, controller.InteractionGesture);
        Assert.True(controller.Registrations.Single(state =>
            state.Action == GlobalHotKeyAction.ToggleInteractionMode).IsRegistered);
        Assert.Contains("已恢復原快捷鍵", result.Fault);
    }

    [Fact]
    public void ApplyInteractionGesture_NewRegistrationAndRollbackFail_RequiresSafeRecovery()
    {
        var native = new FakeNativeGlobalHotKeyApi();
        native.InteractionRegisterSequence.Enqueue(null);
        native.InteractionRegisterSequence.Enqueue(new Win32Exception(1409));
        native.InteractionRegisterSequence.Enqueue(new Win32Exception(5));
        var controller = new GlobalHotKeyController(native);
        controller.RegisterAll(WindowHandle);

        GlobalHotKeyApplyResult result = controller.ApplyInteractionGesture(
            new OverlayHotKeyGesture(true, true, false, false, OverlayHotKeyKey.F10));

        Assert.False(result.IsSuccess);
        Assert.False(result.RollbackSucceeded);
        Assert.True(result.RequiresSafeRecovery);
        Assert.False(controller.Registrations.Single(state =>
            state.Action == GlobalHotKeyAction.ToggleInteractionMode).IsRegistered);
        Assert.Contains("系統匣", result.Fault);
    }

    [Fact]
    public void RegisterAll_SavedGestureLoadsAtStartup()
    {
        var native = new FakeNativeGlobalHotKeyApi();
        var saved = new OverlayHotKeyGesture(
            false, true, true, false, OverlayHotKeyKey.F8);
        var controller = new GlobalHotKeyController(
            native,
            initialInteractionGesture: saved);

        controller.RegisterAll(WindowHandle);

        Assert.Equal(saved, controller.InteractionGesture);
        Assert.Contains(native.RegisterCalls, call =>
            call.Identifier == GlobalHotKeyController.InteractionHotKeyIdentifier &&
            call.VirtualKey == (uint)OverlayHotKeyKey.F8);
    }

    [Fact]
    public void RegisterAll_SavedGestureFailure_FallsBackToDefault()
    {
        var native = new FakeNativeGlobalHotKeyApi();
        native.InteractionRegisterSequence.Enqueue(new Win32Exception(1409));
        native.InteractionRegisterSequence.Enqueue(null);
        var saved = new OverlayHotKeyGesture(
            true, false, true, false, OverlayHotKeyKey.F8);
        var controller = new GlobalHotKeyController(
            native,
            initialInteractionGesture: saved);

        IReadOnlyList<GlobalHotKeyRegistrationState> states =
            controller.RegisterAll(WindowHandle);

        Assert.Equal(OverlayHotKeyGesture.Default, controller.InteractionGesture);
        GlobalHotKeyRegistrationState interaction = states.Single(state =>
            state.Action == GlobalHotKeyAction.ToggleInteractionMode);
        Assert.True(interaction.IsRegistered);
        Assert.Contains("目前改用預設", interaction.Fault);
    }

    private sealed class FakeNativeGlobalHotKeyApi : INativeGlobalHotKeyApi
    {
        internal List<(int Identifier, uint Modifiers, uint VirtualKey)> RegisterCalls { get; } = [];
        internal List<int> UnregisterCalls { get; } = [];
        internal Dictionary<int, Win32Exception> RegisterFailures { get; } = [];
        internal Dictionary<int, Win32Exception> UnregisterFailures { get; } = [];
        internal Queue<Win32Exception?> InteractionRegisterSequence { get; } = [];

        public void Register(nint windowHandle, int identifier, uint modifiers, uint virtualKey)
        {
            RegisterCalls.Add((identifier, modifiers, virtualKey));
            if (identifier == GlobalHotKeyController.InteractionHotKeyIdentifier &&
                InteractionRegisterSequence.TryDequeue(out Win32Exception? sequencedFailure))
            {
                if (sequencedFailure is not null)
                {
                    throw sequencedFailure;
                }
                return;
            }
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
