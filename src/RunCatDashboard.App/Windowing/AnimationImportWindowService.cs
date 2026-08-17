using RunCatDashboard.App.Animation;
using RunCatDashboard.App.ViewModels;
using RunCatDashboard.App.Views;

namespace RunCatDashboard.App.Windowing;

internal sealed class AnimationImportWindowService : IAnimationImportWindowService
{
    private readonly Func<AnimationImportWindow> _factory;
    private AnimationImportWindow? _window;
    private AnimationImportWindowViewModel? _viewModel;
    private Action<AnimationCatalogEntry>? _imported;

    internal AnimationImportWindowService(Func<AnimationImportWindow> factory)
    {
        _factory = factory;
    }

    public void Open(Action<AnimationCatalogEntry> imported)
    {
        ArgumentNullException.ThrowIfNull(imported);
        if (_window is not null)
        {
            if (_window.WindowState == System.Windows.WindowState.Minimized)
                _window.WindowState = System.Windows.WindowState.Normal;
            _window.Activate();
            return;
        }

        _window = _factory();
        _viewModel =
            (AnimationImportWindowViewModel)_window.DataContext;
        _imported = imported;
        _viewModel.Imported += imported;
        _window.Closed += OnClosed;
        _window.Show();
        _window.Activate();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_window is null)
            return;

        if (_viewModel is not null && _imported is not null)
            _viewModel.Imported -= _imported;
        _window.Closed -= OnClosed;
        _window = null;
        _viewModel = null;
        _imported = null;
    }
}
