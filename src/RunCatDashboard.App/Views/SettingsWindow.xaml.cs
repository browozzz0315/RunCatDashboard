using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using RunCatDashboard.App.ViewModels;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.App.Views;

public partial class SettingsWindow : Window, ISettingsWindowHost
{
    private const int WindowMessageNonClientLeftButtonDown = 0x00A1;
    private readonly SettingsWindowViewModel _viewModel;
    private HwndSource? _windowSource;
    private bool _allowRequestedClose;

    public SettingsWindow(SettingsWindowViewModel viewModel)
    {
        InitializeComponent();
        Icon = LoadWindowIcon();
        MaxHeight = SystemParameters.WorkArea.Height;
        Height = Math.Min(Height, MaxHeight);
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.CloseRequested += OnCloseRequested;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowSource = PresentationSource.FromVisual(this) as HwndSource;
        _windowSource?.AddHook(OnWindowMessage);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel.IsApplying)
        {
            if (!_allowRequestedClose)
            {
                e.Cancel = true;
                return;
            }
        }

        _viewModel.EndHotKeyCapture();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _windowSource?.RemoveHook(OnWindowMessage);
        _windowSource = null;
        _viewModel.EndHotKeyCapture();
        _viewModel.CloseRequested -= OnCloseRequested;
        base.OnClosed(e);
    }

    private void OnCloseRequested()
    {
        _allowRequestedClose = true;
        try
        {
            Close();
        }
        finally
        {
            _allowRequestedClose = false;
        }
    }

    private void OnHotKeyCapturePreviewMouseLeftButtonDown(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e) => BeginCaptureFor(sender);

    private void OnHotKeyCaptureGotKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e) => BeginCaptureFor(sender);

    private void OnWindowPreviewMouseDown(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            (ReferenceEquals(source, HotKeyCaptureField) ||
                HotKeyCaptureField.IsAncestorOf(source) ||
                ReferenceEquals(source, VisibilityHotKeyCaptureField) ||
                VisibilityHotKeyCaptureField.IsAncestorOf(source)))
        {
            return;
        }

        _viewModel.EndHotKeyCapture();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e) =>
        _viewModel.EndHotKeyCapture();

    private void OnHotKeyCapturePreviewKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        bool isVisibilityCapture = ReferenceEquals(sender, VisibilityHotKeyCaptureField);
        if (isVisibilityCapture
                ? !_viewModel.IsVisibilityHotKeyCaptureActive
                : !_viewModel.IsHotKeyCaptureActive)
        {
            return;
        }

        e.Handled = true;
        Key pressedKey = e.Key == Key.System ? e.SystemKey : e.Key;
        OverlayHotKeyCaptureResult result = OverlayHotKeyKeyCaptureAdapter.Capture(pressedKey);
        switch (result.Outcome)
        {
            case OverlayHotKeyCaptureOutcome.Captured:
                if (isVisibilityCapture)
                    _viewModel.ApplyCapturedVisibilityHotKeyKey(result.Key!.Value);
                else
                    _viewModel.ApplyCapturedHotKeyKey(result.Key!.Value);
                Keyboard.ClearFocus();
                break;
            case OverlayHotKeyCaptureOutcome.Cancelled:
                SetCaptureMessage(isVisibilityCapture, "已取消按鍵擷取，保留原本按鍵。");
                _viewModel.EndHotKeyCapture();
                Keyboard.ClearFocus();
                break;
            case OverlayHotKeyCaptureOutcome.ModifierOnly:
                SetCaptureMessage(
                    isVisibilityCapture,
                    "modifier 請使用上方選項；主要按鍵仍保留原值。");
                break;
            default:
                SetCaptureMessage(
                    isVisibilityCapture,
                    "只支援 A-Z、0-9 或 F1-F12；主要按鍵仍保留原值。");
                break;
        }
    }

    private void BeginCaptureFor(object sender)
    {
        if (ReferenceEquals(sender, VisibilityHotKeyCaptureField))
            _viewModel.BeginVisibilityHotKeyCapture();
        else
            _viewModel.BeginHotKeyCapture();
    }

    private void SetCaptureMessage(bool isVisibilityCapture, string message)
    {
        if (isVisibilityCapture)
            _viewModel.VisibilityHotKeyCaptureMessage = message;
        else
            _viewModel.HotKeyCaptureMessage = message;
    }

    private nint OnWindowMessage(
        nint windowHandle,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        if (message == WindowMessageNonClientLeftButtonDown)
        {
            _viewModel.EndHotKeyCapture();
        }

        return nint.Zero;
    }

    private static BitmapFrame LoadWindowIcon()
    {
        using Stream? stream = typeof(SettingsWindow).Assembly.GetManifestResourceStream(
            AssemblyTrayIconResourceLoader.ResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException("RunCatDashboard 靜態 icon resource 不存在。");
        }

        BitmapFrame icon = BitmapFrame.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        icon.Freeze();
        return icon;
    }

    bool ISettingsWindowHost.IsMinimized => WindowState == WindowState.Minimized;
    void ISettingsWindowHost.Activate() => Activate();
    void ISettingsWindowHost.Restore() => WindowState = WindowState.Normal;
}
