using Forms = System.Windows.Forms;

namespace RunCatDashboard.App.Windowing;

internal sealed class NotifyIconTrayAdapter : ITrayIconAdapter
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _visibilityItem;
    private readonly Forms.ToolStripMenuItem _interactionItem;
    private readonly Forms.ToolStripMenuItem _animationItem;
    private readonly ITrayIconResource? _iconResource;
    private readonly IReadOnlyList<ITrayIconResource> _animationIconResources =
        Array.Empty<ITrayIconResource>();
    private readonly string? _iconLoadFailure;
    private readonly string? _animationIconLoadFailure;
    private bool _isDisposed;

    internal NotifyIconTrayAdapter(
        ITrayIconResourceLoader iconLoader,
        ITrayAnimationIconResourceLoader animationIconLoader)
    {
        ArgumentNullException.ThrowIfNull(iconLoader);
        ArgumentNullException.ThrowIfNull(animationIconLoader);
        _visibilityItem = new Forms.ToolStripMenuItem();
        _interactionItem = new Forms.ToolStripMenuItem();
        _animationItem = new Forms.ToolStripMenuItem();
        var exitItem = new Forms.ToolStripMenuItem("退出");
        _menu = new Forms.ContextMenuStrip();
        _menu.Items.AddRange([
            _visibilityItem,
            _interactionItem,
            _animationItem,
            new Forms.ToolStripSeparator(),
            exitItem
        ]);

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "RunCatDashboard",
            ContextMenuStrip = _menu
        };

        try
        {
            _iconResource = iconLoader.Load();
            _notifyIcon.Icon = _iconResource.Icon;
        }
        catch (Exception exception)
        {
            _iconLoadFailure = $"載入 RunCatDashboard 系統匣圖示失敗：{exception.Message}";
        }

        if (_iconResource is not null)
        {
            try
            {
                IReadOnlyList<ITrayIconResource> animationFrames =
                    animationIconLoader.LoadFrames();
                if (animationFrames.Count !=
                    AssemblyTrayAnimationIconResourceLoader.FrameCount)
                {
                    foreach (ITrayIconResource animationFrame in animationFrames)
                    {
                        animationFrame.Dispose();
                    }

                    throw new InvalidOperationException(
                        $"系統匣動畫必須包含 {AssemblyTrayAnimationIconResourceLoader.FrameCount} 幀，" +
                        $"實際為 {animationFrames.Count} 幀。");
                }

                _animationIconResources = animationFrames;
            }
            catch (Exception exception)
            {
                _animationIconLoadFailure = exception.Message;
            }
        }

        _notifyIcon.MouseDoubleClick += OnMouseDoubleClick;
        _visibilityItem.Click += OnVisibilityItemClick;
        _interactionItem.Click += OnInteractionItemClick;
        _animationItem.Click += OnAnimationItemClick;
        exitItem.Click += OnExitItemClick;
    }

    public event Action? DoubleClicked;
    public event Action? VisibilityToggleRequested;
    public event Action? InteractionToggleRequested;
    public event Action? AnimationToggleRequested;
    public event Action? ExitRequested;

    public bool CanUseAnimatedIcons => _animationIconResources.Count > 0;

    public string? AnimationIconLoadError => _animationIconLoadFailure;

    internal bool HasAssignedIcon => _notifyIcon.Icon is not null;

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ThrowIfIconUnavailable();
        _notifyIcon.Visible = true;
    }

    public void SetMenuText(
        string visibilityText,
        string interactionText,
        string animationText)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _visibilityItem.Text = visibilityText;
        _interactionItem.Text = interactionText;
        _animationItem.Text = animationText;
    }

    public void SetAnimatedFrame(int frameIndex)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (!CanUseAnimatedIcons)
        {
            throw new InvalidOperationException(
                _animationIconLoadFailure ?? "系統匣動畫圖示資源無法使用。");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(frameIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            frameIndex,
            _animationIconResources.Count);
        AssignIcon(_animationIconResources[frameIndex].Icon);
    }

    public void SetStaticIcon()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ThrowIfIconUnavailable();
        AssignIcon(_iconResource!.Icon);
    }

    public void RecoverAfterExplorerRestart()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ThrowIfIconUnavailable();
        if (_notifyIcon.Icon is null)
        {
            _notifyIcon.Icon = _iconResource!.Icon;
        }
        _notifyIcon.Visible = false;
        _notifyIcon.Visible = true;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        foreach (ITrayIconResource animationIconResource in _animationIconResources)
        {
            animationIconResource.Dispose();
        }
        _iconResource?.Dispose();
        DoubleClicked = null;
        VisibilityToggleRequested = null;
        InteractionToggleRequested = null;
        AnimationToggleRequested = null;
        ExitRequested = null;
    }

    private void OnMouseDoubleClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
        {
            DoubleClicked?.Invoke();
        }
    }

    private void OnVisibilityItemClick(object? sender, EventArgs e) =>
        VisibilityToggleRequested?.Invoke();

    private void OnInteractionItemClick(object? sender, EventArgs e) =>
        InteractionToggleRequested?.Invoke();

    private void OnAnimationItemClick(object? sender, EventArgs e) =>
        AnimationToggleRequested?.Invoke();

    private void OnExitItemClick(object? sender, EventArgs e) =>
        ExitRequested?.Invoke();

    private void ThrowIfIconUnavailable()
    {
        if (_iconResource is null || _notifyIcon.Icon is null)
        {
            throw new InvalidOperationException(
                _iconLoadFailure ?? "RunCatDashboard 系統匣圖示無法使用。");
        }
    }

    private void AssignIcon(System.Drawing.Icon icon)
    {
        System.Drawing.Icon? previousIcon = _notifyIcon.Icon;
        try
        {
            _notifyIcon.Icon = icon;
        }
        catch (Exception assignmentException)
        {
            if (previousIcon is null || ReferenceEquals(previousIcon, icon))
            {
                throw;
            }

            try
            {
                _notifyIcon.Icon = previousIcon;
            }
            catch (Exception rollbackException)
            {
                throw new InvalidOperationException(
                    $"指定系統匣圖示失敗，回復上一個圖示也失敗：{rollbackException.Message}",
                    assignmentException);
            }

            throw;
        }
    }
}
