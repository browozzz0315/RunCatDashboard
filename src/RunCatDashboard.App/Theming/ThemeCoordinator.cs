using System.Windows;
using Microsoft.Extensions.Logging;
using RunCatDashboard.App.Services;
using RunCatDashboard.App.Settings;
using RunCatDashboard.App.Views;

namespace RunCatDashboard.App.Theming;

using WpfApplication = System.Windows.Application;

public sealed class ThemeCoordinator : IThemeCoordinator
{
    private const string LightDictionaryPath =
        "pack://application:,,,/RunCatDashboard;component/Themes/Light.xaml";
    private const string DarkDictionaryPath =
        "pack://application:,,,/RunCatDashboard;component/Themes/Dark.xaml";

    private readonly WpfApplication _application;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IWindowsAppThemeDetector _detector;
    private readonly ResourceDictionary _lightResources;
    private readonly ResourceDictionary _darkResources;
    private readonly ILogger<ThemeCoordinator> _logger;
    private readonly object _gate = new();
    private ThemePreference _themePreference = ThemePreference.System;
    private ResolvedTheme _resolvedTheme = ResolvedTheme.Light;
    private bool _isDisposed;

    public ThemeCoordinator(
        WpfApplication application,
        IUiDispatcher uiDispatcher,
        IWindowsAppThemeDetector detector,
        ILogger<ThemeCoordinator>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(detector);

        _application = application;
        _uiDispatcher = uiDispatcher;
        _detector = detector;
        _logger = logger ??
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ThemeCoordinator>.Instance;
        _lightResources = LoadResources(LightDictionaryPath);
        _darkResources = LoadResources(DarkDictionaryPath);
        _detector.ThemeChanged += OnWindowsThemeChanged;
    }

    public ThemePreference ThemePreference
    {
        get { lock (_gate) return _themePreference; }
    }

    public ResolvedTheme ResolvedTheme
    {
        get { lock (_gate) return _resolvedTheme; }
    }

    public event Action<ResolvedTheme>? ResolvedThemeChanged;

    public async Task ApplyPreferenceAsync(
        ThemePreference preference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ResolvedTheme resolved = ThemeResolver.Resolve(preference, _detector.Current);
        try
        {
            await _uiDispatcher.InvokeAsync(
                () => ApplyOnUi(preference, resolved),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not ThemeConfigurationException)
        {
            throw new ThemeConfigurationException(
                "主題資源無法套用，設定尚未完成。",
                exception);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
        }

        _detector.ThemeChanged -= OnWindowsThemeChanged;
        ResolvedThemeChanged = null;
        _detector.Dispose();
    }

    private void OnWindowsThemeChanged(WindowsAppTheme windowsTheme)
    {
        try
        {
            _uiDispatcher.InvokeAsync(
                () =>
                {
                    lock (_gate)
                    {
                        if (_isDisposed || _themePreference != ThemePreference.System)
                        {
                            return;
                        }
                    }

                    ResolvedTheme resolved = ThemeResolver.Resolve(
                        ThemePreference.System,
                        _detector.Current);
                    ApplyOnUi(ThemePreference.System, resolved);
                },
                CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            try
            {
                _logger.LogError(
                    exception,
                    "System theme notification could not be applied. {Operation} {Subsystem}",
                    "ApplySystemTheme",
                    "Theming");
            }
            catch
            {
                // Logging must not terminate the SystemEvents thread.
            }
        }
    }

    private void ApplyOnUi(ThemePreference preference, ResolvedTheme resolved)
    {
        ThemePreference previousPreference;
        ResolvedTheme previousResolved;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_resolvedTheme == resolved && _themePreference == preference)
            {
                return;
            }

            previousPreference = _themePreference;
            previousResolved = _resolvedTheme;
        }

        bool resourcesReplaced = false;
        try
        {
            ReplaceResources(resolved);
            resourcesReplaced = true;

            Action<ResolvedTheme>? handlers;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                _themePreference = preference;
                bool changed = _resolvedTheme != resolved;
                _resolvedTheme = resolved;
                handlers = changed ? ResolvedThemeChanged : null;
            }

            handlers?.Invoke(resolved);
        }
        catch (Exception exception)
        {
            if (resourcesReplaced)
            {
                Exception? rollbackFailure = RestoreAfterApplyFailure(
                    previousPreference,
                    previousResolved);
                if (rollbackFailure is not null)
                {
                    throw new ThemeConfigurationException(
                        "Theme runtime rollback failed.",
                        new AggregateException(exception, rollbackFailure));
                }
            }

            throw;
        }
    }

    private Exception? RestoreAfterApplyFailure(
        ThemePreference previousPreference,
        ResolvedTheme previousResolved)
    {
        Exception? resourceRollbackFailure = null;
        try
        {
            ReplaceResources(previousResolved);
        }
        catch (Exception rollbackException)
        {
            resourceRollbackFailure = rollbackException;
            try
            {
                _logger.LogError(
                    rollbackException,
                    "Theme resource rollback failed. {Operation} {Subsystem}",
                    "RollbackThemeResources",
                    "Theming");
            }
            catch
            {
                // Logging must not terminate the UI thread.
            }
        }

        lock (_gate)
        {
            if (!_isDisposed)
            {
                _themePreference = previousPreference;
                _resolvedTheme = previousResolved;
            }
        }

        return resourceRollbackFailure;
    }

    private void ReplaceResources(ResolvedTheme resolved)
    {
        ResourceDictionary next = resolved == ResolvedTheme.Dark
            ? _darkResources
            : _lightResources;
        ResourceDictionary? previous = null;
        int index = -1;
        var dictionaries = _application.Resources.MergedDictionaries;
        for (int candidate = 0; candidate < dictionaries.Count; candidate++)
        {
            Uri? source = dictionaries[candidate].Source;
            if (source is null ||
                (!string.Equals(source.ToString(), LightDictionaryPath, StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(source.ToString(), DarkDictionaryPath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            index = candidate;
            previous = dictionaries[candidate];
            break;
        }

        try
        {
            if (index >= 0)
            {
                dictionaries[index] = next;
            }
            else
            {
                dictionaries.Insert(0, next);
            }
        }
        catch
        {
            if (index >= 0 && previous is not null)
            {
                dictionaries[index] = previous;
            }
            else if (index < 0 && dictionaries.Count > 0 && ReferenceEquals(dictionaries[0], next))
            {
                dictionaries.RemoveAt(0);
            }

            throw;
        }
    }

    private static ResourceDictionary LoadResources(string source)
    {
        return new ResourceDictionary
        {
            Source = new Uri(source, UriKind.Absolute)
        };
    }
}
