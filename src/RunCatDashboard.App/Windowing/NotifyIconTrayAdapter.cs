using Forms = System.Windows.Forms;

namespace RunCatDashboard.App.Windowing;

internal sealed class NotifyIconTrayAdapter : ITrayIconAdapter
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _visibilityItem;
    private readonly Forms.ToolStripMenuItem _interactionItem;
    private readonly ITrayIconResource? _iconResource;
    private readonly string? _iconLoadFailure;
    private bool _isDisposed;

    internal NotifyIconTrayAdapter(ITrayIconResourceLoader iconLoader)
    {
        ArgumentNullException.ThrowIfNull(iconLoader);
        _visibilityItem = new Forms.ToolStripMenuItem();
        _interactionItem = new Forms.ToolStripMenuItem();
        var exitItem = new Forms.ToolStripMenuItem("退出");
        _menu = new Forms.ContextMenuStrip();
        _menu.Items.AddRange([
            _visibilityItem,
            _interactionItem,
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

        _notifyIcon.MouseDoubleClick += OnMouseDoubleClick;
        _visibilityItem.Click += OnVisibilityItemClick;
        _interactionItem.Click += OnInteractionItemClick;
        exitItem.Click += OnExitItemClick;
    }

    public event Action? DoubleClicked;
    public event Action? VisibilityToggleRequested;
    public event Action? InteractionToggleRequested;
    public event Action? ExitRequested;

    internal bool HasAssignedIcon => _notifyIcon.Icon is not null;

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ThrowIfIconUnavailable();
        _notifyIcon.Visible = true;
    }

    public void SetMenuText(string visibilityText, string interactionText)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _visibilityItem.Text = visibilityText;
        _interactionItem.Text = interactionText;
    }

    public void RecoverAfterExplorerRestart()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ThrowIfIconUnavailable();
        _notifyIcon.Icon = _iconResource!.Icon;
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
        _iconResource?.Dispose();
        DoubleClicked = null;
        VisibilityToggleRequested = null;
        InteractionToggleRequested = null;
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
}
