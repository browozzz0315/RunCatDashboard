using System.ComponentModel;
using RunCatDashboard.App.Interop;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Windowing;

public sealed class GlobalHotKeyControllerTests
{
    private static readonly nint WindowHandle = new(4321);

    [Fact]
    public void Register_WithValidHandle_RegistersFixedGesture()
    {
        var nativeApi = new FakeNativeGlobalHotKeyApi();
        var controller = new GlobalHotKeyController(nativeApi);

        bool registered = controller.Register(WindowHandle);

        Assert.True(registered);
        Assert.True(controller.IsRegistered);
        Assert.Equal("Ctrl + Alt + Shift + R", controller.GestureText);
        Assert.Equal(1, nativeApi.RegisterCount);
        Assert.Equal(GlobalHotKeyController.HotKeyIdentifier, nativeApi.Identifier);
        Assert.Equal(GlobalHotKeyController.HotKeyModifiers, nativeApi.Modifiers);
        Assert.Equal(GlobalHotKeyController.VirtualKeyR, nativeApi.VirtualKey);
    }

    [Fact]
    public void Register_WhenAlreadyRegistered_IsIdempotent()
    {
        var nativeApi = new FakeNativeGlobalHotKeyApi();
        var controller = new GlobalHotKeyController(nativeApi);
        controller.Register(WindowHandle);

        bool registered = controller.Register(WindowHandle);

        Assert.False(registered);
        Assert.Equal(1, nativeApi.RegisterCount);
    }

    [Fact]
    public void Register_WhenNativeRegistrationFails_HasUnderstandableError()
    {
        var nativeApi = new FakeNativeGlobalHotKeyApi
        {
            RegisterException = new Win32Exception(1409, "Hot key is already registered.")
        };
        var controller = new GlobalHotKeyController(nativeApi);

        GlobalHotKeyException exception = Assert.Throws<GlobalHotKeyException>(
            () => controller.Register(WindowHandle));

        Assert.Contains("Ctrl + Alt + Shift + R", exception.Message);
        Assert.Contains("already be in use", exception.Message);
        Assert.IsType<Win32Exception>(exception.InnerException);
        Assert.False(controller.IsRegistered);
        Assert.Equal(exception.Message, controller.LastError);
    }

    [Fact]
    public void Unregister_AfterRegistration_CallsNativeAndClearsState()
    {
        var nativeApi = new FakeNativeGlobalHotKeyApi();
        var controller = new GlobalHotKeyController(nativeApi);
        controller.Register(WindowHandle);

        bool unregistered = controller.Unregister();

        Assert.True(unregistered);
        Assert.False(controller.IsRegistered);
        Assert.Equal(1, nativeApi.UnregisterCount);
        Assert.Null(controller.LastError);
    }

    [Fact]
    public void Close_AfterRegistration_UnregistersHotKey()
    {
        var nativeApi = new FakeNativeGlobalHotKeyApi();
        var controller = new GlobalHotKeyController(nativeApi);
        controller.Register(WindowHandle);

        controller.Close();
        controller.Close();

        Assert.False(controller.IsRegistered);
        Assert.Equal(1, nativeApi.UnregisterCount);
    }

    [Fact]
    public void Unregister_WhenRepeated_IsSafeAndSkipsNativeCall()
    {
        var nativeApi = new FakeNativeGlobalHotKeyApi();
        var controller = new GlobalHotKeyController(nativeApi);
        controller.Register(WindowHandle);
        controller.Unregister();

        bool unregistered = controller.Unregister();

        Assert.False(unregistered);
        Assert.Equal(1, nativeApi.UnregisterCount);
    }

    private sealed class FakeNativeGlobalHotKeyApi : INativeGlobalHotKeyApi
    {
        internal Win32Exception? RegisterException { get; init; }

        internal int RegisterCount { get; private set; }

        internal int UnregisterCount { get; private set; }

        internal int Identifier { get; private set; }

        internal uint Modifiers { get; private set; }

        internal uint VirtualKey { get; private set; }

        public void Register(nint windowHandle, int identifier, uint modifiers, uint virtualKey)
        {
            RegisterCount++;
            if (RegisterException is not null)
            {
                throw RegisterException;
            }

            Identifier = identifier;
            Modifiers = modifiers;
            VirtualKey = virtualKey;
        }

        public void Unregister(nint windowHandle, int identifier)
        {
            UnregisterCount++;
        }
    }
}
