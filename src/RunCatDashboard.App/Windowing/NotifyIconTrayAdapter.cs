using Forms = System.Windows.Forms;
using RunCatDashboard.App.Theming;

namespace RunCatDashboard.App.Windowing;

internal sealed class NotifyIconTrayAdapter : ITrayIconAdapter
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _visibilityItem;
    private readonly Forms.ToolStripMenuItem _interactionItem;
    private readonly Forms.ToolStripMenuItem _animationItem;
    private readonly Forms.ToolStripMenuItem _openLogsDirectoryItem;
    private readonly ITrayIconResource? _lightIconResource;
    private readonly ITrayIconResource? _darkIconResource;
    private readonly IReadOnlyList<ITrayIconResource> _lightAnimationIconResources =
        Array.Empty<ITrayIconResource>();
    private readonly IReadOnlyList<ITrayIconResource> _darkAnimationIconResources =
        Array.Empty<ITrayIconResource>();
    private readonly string? _lightIconLoadFailure;
    private readonly string? _darkIconLoadFailure;
    private readonly string? _lightAnimationIconLoadFailure;
    private readonly string? _darkAnimationIconLoadFailure;
    private ResolvedTheme _resolvedTheme = ResolvedTheme.Light;
    private bool _isDisposed;

    internal NotifyIconTrayAdapter(
        ITrayIconResourceLoader iconLoader,
        ITrayAnimationIconResourceLoader animationIconLoader,
        ITrayIconResourceLoader? whiteIconLoader = null,
        ITrayAnimationIconResourceLoader? whiteAnimationIconLoader = null)
    {
        ArgumentNullException.ThrowIfNull(iconLoader);
        ArgumentNullException.ThrowIfNull(animationIconLoader);
        _visibilityItem = new Forms.ToolStripMenuItem();
        _interactionItem = new Forms.ToolStripMenuItem();
        _animationItem = new Forms.ToolStripMenuItem();
        var settingsItem = new Forms.ToolStripMenuItem("設定...");
        _openLogsDirectoryItem = new Forms.ToolStripMenuItem("開啟記錄資料夾");
        var exitItem = new Forms.ToolStripMenuItem("退出");
        _menu = new Forms.ContextMenuStrip();
        _menu.Items.AddRange([
            _visibilityItem,
            _interactionItem,
            _animationItem,
            new Forms.ToolStripSeparator(),
            settingsItem,
            _openLogsDirectoryItem,
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
            _lightIconResource = iconLoader.Load();
            _notifyIcon.Icon = _lightIconResource.Icon;
        }
        catch (Exception exception)
        {
            _lightIconLoadFailure = $"載入 RunCatDashboard 系統匣圖示失敗：{exception.Message}";
        }

        if (_lightIconResource is not null)
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

                _lightAnimationIconResources = animationFrames;
            }
            catch (Exception exception)
            {
                _lightAnimationIconLoadFailure = exception.Message;
            }
        }

        if (whiteIconLoader is null)
        {
            _darkIconResource = _lightIconResource;
            _darkIconLoadFailure = _lightIconLoadFailure;
        }
        else
        {
            try
            {
                _darkIconResource = whiteIconLoader.Load();
            }
            catch (Exception exception)
            {
                _darkIconLoadFailure = $"載入 RunCatDashboard 白色系統匣圖示失敗：{exception.Message}";
            }
        }

        if (whiteAnimationIconLoader is null)
        {
            _darkAnimationIconResources = _lightAnimationIconResources;
            _darkAnimationIconLoadFailure = _lightAnimationIconLoadFailure;
        }
        else
        {
            try
            {
                IReadOnlyList<ITrayIconResource> animationFrames =
                    whiteAnimationIconLoader.LoadFrames();
                if (animationFrames.Count !=
                    AssemblyTrayAnimationIconResourceLoader.FrameCount)
                {
                    foreach (ITrayIconResource animationFrame in animationFrames)
                    {
                        animationFrame.Dispose();
                    }

                    throw new InvalidOperationException(
                        $"白色系統匣動畫圖示應有 {AssemblyTrayAnimationIconResourceLoader.FrameCount} 幀，" +
                        $"實際為 {animationFrames.Count} 幀。");
                }

                _darkAnimationIconResources = animationFrames;
            }
            catch (Exception exception)
            {
                _darkAnimationIconLoadFailure = exception.Message;
            }
        }

        _notifyIcon.MouseDoubleClick += OnMouseDoubleClick;
        _visibilityItem.Click += OnVisibilityItemClick;
        _interactionItem.Click += OnInteractionItemClick;
        _animationItem.Click += OnAnimationItemClick;
        settingsItem.Click += OnSettingsItemClick;
        _openLogsDirectoryItem.Click += OnOpenLogsDirectoryItemClick;
        exitItem.Click += OnExitItemClick;
    }

    public event Action? DoubleClicked;
    public event Action? VisibilityToggleRequested;
    public event Action? InteractionToggleRequested;
    public event Action? AnimationToggleRequested;
    public event Action? SettingsRequested;
    public event Action? OpenLogsDirectoryRequested;
    public event Action? ExitRequested;

    public bool CanUseAnimatedIcons => CurrentAnimationIconResources.Count > 0;

    public string? AnimationIconLoadError => CurrentAnimationIconLoadFailure;

    internal bool HasAssignedIcon => _notifyIcon.Icon is not null;

    internal System.Drawing.Icon? AssignedIcon => _notifyIcon.Icon;

    internal string OpenLogsDirectoryMenuText => _openLogsDirectoryItem.Text ?? string.Empty;

    public void SetResolvedTheme(ResolvedTheme theme)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _resolvedTheme = theme;
    }

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
        IReadOnlyList<ITrayIconResource> animationIconResources =
            CurrentAnimationIconResources;
        if (animationIconResources.Count == 0)
        {
            throw new InvalidOperationException(
                CurrentAnimationIconLoadFailure ?? "系統匣動畫圖示資源無法使用。");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(frameIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            frameIndex,
            animationIconResources.Count);
        AssignIcon(animationIconResources[frameIndex].Icon);
    }

    public void SetStaticIcon()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ThrowIfIconUnavailable();
        AssignIcon(CurrentIconResource!.Icon);
    }

    public void RecoverAfterExplorerRestart()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ThrowIfIconUnavailable();
        if (_notifyIcon.Icon is null)
        {
            _notifyIcon.Icon = CurrentIconResource!.Icon;
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
        var ownedResources = new HashSet<ITrayIconResource>(ReferenceEqualityComparer.Instance);
        foreach (ITrayIconResource resource in _lightAnimationIconResources)
        {
            ownedResources.Add(resource);
        }

        foreach (ITrayIconResource resource in _darkAnimationIconResources)
        {
            ownedResources.Add(resource);
        }

        if (_lightIconResource is not null)
        {
            ownedResources.Add(_lightIconResource);
        }

        if (_darkIconResource is not null)
        {
            ownedResources.Add(_darkIconResource);
        }

        foreach (ITrayIconResource resource in ownedResources)
        {
            resource.Dispose();
        }
        DoubleClicked = null;
        VisibilityToggleRequested = null;
        InteractionToggleRequested = null;
        AnimationToggleRequested = null;
        SettingsRequested = null;
        OpenLogsDirectoryRequested = null;
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

    private void OnSettingsItemClick(object? sender, EventArgs e) =>
        SettingsRequested?.Invoke();

    private void OnOpenLogsDirectoryItemClick(object? sender, EventArgs e) =>
        OpenLogsDirectoryRequested?.Invoke();

    private void ThrowIfIconUnavailable()
    {
        if (CurrentIconResource is null || _notifyIcon.Icon is null)
        {
            throw new InvalidOperationException(
                CurrentIconLoadFailure ?? "RunCatDashboard 系統匣圖示無法使用。");
        }
    }

    private ITrayIconResource? CurrentIconResource =>
        _resolvedTheme == ResolvedTheme.Dark
            ? _darkIconResource
            : _lightIconResource;

    private string? CurrentIconLoadFailure =>
        _resolvedTheme == ResolvedTheme.Dark
            ? _darkIconLoadFailure
            : _lightIconLoadFailure;

    private IReadOnlyList<ITrayIconResource> CurrentAnimationIconResources =>
        _resolvedTheme == ResolvedTheme.Dark
            ? _darkAnimationIconResources
            : _lightAnimationIconResources;

    private string? CurrentAnimationIconLoadFailure =>
        _resolvedTheme == ResolvedTheme.Dark
            ? _darkAnimationIconLoadFailure
            : _lightAnimationIconLoadFailure;

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
