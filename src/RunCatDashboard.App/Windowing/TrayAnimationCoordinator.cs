using RunCatDashboard.App.Animation;

namespace RunCatDashboard.App.Windowing;

internal interface ITrayAnimationCoordinator : IDisposable
{
    bool IsAnimated { get; }

    string? LastError { get; }

    event Action<string?>? DiagnosticChanged;

    bool Initialize();

    bool ToggleMode();

    void RestoreCurrentModeIcon();
}

internal sealed class TrayAnimationCoordinator : ITrayAnimationCoordinator
{
    private readonly ITrayIconAdapter _adapter;
    private readonly IRunCatAnimationController _animationController;
    private bool _isInitialized;
    private bool _isDisposed;

    internal TrayAnimationCoordinator(
        ITrayIconAdapter adapter,
        IRunCatAnimationController animationController)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(animationController);
        _adapter = adapter;
        _animationController = animationController;
    }

    public bool IsAnimated { get; private set; } = true;

    public string? LastError { get; private set; }

    public event Action<string?>? DiagnosticChanged;

    public bool Initialize()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isInitialized)
        {
            return false;
        }

        _animationController.FrameChanged += OnFrameChanged;
        _isInitialized = true;
        ApplyCurrentModeIcon();
        return true;
    }

    public bool ToggleMode()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        IsAnimated = !IsAnimated;
        ApplyCurrentModeIcon();
        return IsAnimated;
    }

    public void RestoreCurrentModeIcon()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ApplyCurrentModeIcon();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_isInitialized)
        {
            _animationController.FrameChanged -= OnFrameChanged;
            _isInitialized = false;
        }

        DiagnosticChanged = null;
    }

    private void OnFrameChanged(int frameIndex)
    {
        if (!_isInitialized || _isDisposed || !IsAnimated)
        {
            return;
        }

        TrySetAnimatedFrame(frameIndex);
    }

    private void ApplyCurrentModeIcon()
    {
        if (!IsAnimated)
        {
            TrySetStaticIcon();
            return;
        }

        if (_adapter.CanUseAnimatedIcons)
        {
            TrySetAnimatedFrame(_animationController.FrameIndex);
            return;
        }

        try
        {
            _adapter.SetStaticIcon();
            SetDiagnostic(
                "載入系統匣動畫圖示失敗，已回退為靜態圖示：" +
                (_adapter.AnimationIconLoadError ?? "動畫圖示資源無法使用。"));
        }
        catch (Exception exception)
        {
            SetDiagnostic($"系統匣動畫與靜態 fallback 均無法使用：{exception.Message}");
        }
    }

    private void TrySetAnimatedFrame(int frameIndex)
    {
        try
        {
            _adapter.SetAnimatedFrame(frameIndex);
            SetDiagnostic(null);
        }
        catch (Exception exception)
        {
            SetDiagnostic(
                $"更新系統匣動畫第 {frameIndex + 1} 幀失敗，已保留上一個有效圖示：{exception.Message}");
        }
    }

    private void TrySetStaticIcon()
    {
        try
        {
            _adapter.SetStaticIcon();
            SetDiagnostic(null);
        }
        catch (Exception exception)
        {
            SetDiagnostic($"切換系統匣靜態圖示失敗，已保留上一個有效圖示：{exception.Message}");
        }
    }

    private void SetDiagnostic(string? message)
    {
        if (LastError == message)
        {
            return;
        }

        LastError = message;
        DiagnosticChanged?.Invoke(message);
    }
}
