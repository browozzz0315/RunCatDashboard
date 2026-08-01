using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;
using RunCatDashboard.App.Animation;
using RunCatDashboard.App.Diagnostics;
using RunCatDashboard.App.Services;
using RunCatDashboard.App.ViewModels;
using RunCatDashboard.App.Views;
using RunCatDashboard.App.Interop;
using RunCatDashboard.App.Settings;
using RunCatDashboard.App.Startup;
using RunCatDashboard.App.Windowing;
using MessageBox = System.Windows.MessageBox;

namespace RunCatDashboard.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private const string AlreadyRunningMessage = "RunCatDashboard 已在執行中。";

    private readonly IApplicationInstanceGuard _instanceGuard;
    private readonly ApplicationStartupCoordinator _startupCoordinator;
    private ServiceProvider? _serviceProvider;
    private IApplicationPaths? _applicationPaths;
    private IApplicationLoggingRuntime? _loggingRuntime;
    private ILogger? _lifecycleLogger;
    private IReadOnlyList<string> _startupArguments = Array.Empty<string>();

    public App()
        : this(new WindowsApplicationInstanceGuard())
    {
    }

    internal App(IApplicationInstanceGuard instanceGuard)
    {
        ArgumentNullException.ThrowIfNull(instanceGuard);
        _instanceGuard = instanceGuard;
        _startupCoordinator = new ApplicationStartupCoordinator(instanceGuard);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _startupArguments = e.Args;

        try
        {
            ApplicationStartupDecision decision = _startupCoordinator.Coordinate(
                StartPrimaryInstance,
                ShowAlreadyRunningMessage);
            if (decision == ApplicationStartupDecision.ExitSecondaryInstance)
            {
                Shutdown(0);
            }
        }
        catch (ApplicationInstanceException exception)
        {
            MessageBox.Show(
                $"RunCatDashboard 啟動失敗：{exception.Message}",
                "RunCatDashboard",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
        catch (Exception exception)
        {
            _lifecycleLogger?.LogCritical(
                exception,
                "Application startup failed. {Operation} {Subsystem} {ApplicationVersion}",
                "StartPrimaryInstance",
                "Startup",
                GetApplicationVersion());
            MessageBox.Show(
                $"RunCatDashboard 啟動失敗：{exception.Message}",
                "RunCatDashboard",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        List<Exception>? cleanupFailures = null;
        try
        {
            _serviceProvider?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _lifecycleLogger?.LogError(
                exception,
                "Dependency injection cleanup failed. {Operation} {Subsystem}",
                "DisposeServiceProvider",
                "Shutdown");
            (cleanupFailures ??= []).Add(exception);
        }

        try
        {
            _instanceGuard.Dispose();
        }
        catch (Exception exception)
        {
            _lifecycleLogger?.LogError(
                exception,
                "Single-instance cleanup failed. {Operation} {Subsystem}",
                "ReleaseMutex",
                "SingleInstance");
            (cleanupFailures ??= []).Add(exception);
        }

        try
        {
            _lifecycleLogger?.LogInformation(
                "Application exit completed. {Operation} {Subsystem}",
                "OnExit",
                "Shutdown");
            _loggingRuntime?.FlushAsync(TimeSpan.FromSeconds(2))
                .GetAwaiter().GetResult();
            _loggingRuntime?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.WriteLine(
                $"RunCatDashboard final logging cleanup failed: {exception.Message}");
        }

        if (cleanupFailures is not null)
        {
            e.ApplicationExitCode = 1;
            MessageBox.Show(
                $"RunCatDashboard 結束清理失敗：{string.Join(" ", cleanupFailures.Select(failure => failure.Message))}",
                "RunCatDashboard",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        base.OnExit(e);
    }

    private void StartPrimaryInstance()
    {
        _applicationPaths = ApplicationPaths.CreateDefault();
        string buildConfiguration = typeof(App).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?
            .Configuration ?? "Release";
        LoggingPolicy loggingPolicy = LoggingPolicy.Create(
            buildConfiguration,
            _startupArguments);
        _loggingRuntime = ApplicationLoggingRuntime.TryCreate(
            _applicationPaths,
            loggingPolicy);
        _lifecycleLogger = _loggingRuntime.LoggerFactory.CreateLogger(
            LoggingPolicy.LifecycleCategory);
        _lifecycleLogger.LogInformation(
            "Primary application startup began. {Operation} {Subsystem} {ApplicationVersion} {BuildConfiguration} {WindowsSessionId}",
            "StartPrimaryInstance",
            "Startup",
            GetApplicationVersion(),
            buildConfiguration,
            _applicationPaths.WindowsSessionId);

        var services = new ServiceCollection();
        ConfigureServices(
            services,
            _applicationPaths,
            _loggingRuntime);
        _serviceProvider = services.BuildServiceProvider();

        ISettingsService settings = _serviceProvider.GetRequiredService<ISettingsService>();
        settings.LoadAsync().GetAwaiter().GetResult();
        AppSettings initial = settings.Current;
        IRunAtLoginService runAtLogin = _serviceProvider.GetRequiredService<IRunAtLoginService>();
        runAtLogin.ReconcileAsync(initial.Startup.RunAtLoginRequested).GetAwaiter().GetResult();
        _serviceProvider.GetRequiredService<IWindowVisibilityCoordinator>()
            .SetUserRequestedVisibility(initial.Window.IsDashboardVisible);
        _serviceProvider.GetRequiredService<IOverlayModeCoordinator>()
            .TrySetMode(initial.Overlay.InteractionMode);
        _serviceProvider.GetRequiredService<MainWindowViewModel>()
            .UpdateSamplingInterval(TimeSpan.FromMilliseconds(
                initial.Metrics.SamplingIntervalMilliseconds));

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.PrepareForStartup();
        _lifecycleLogger.LogInformation(
            "Primary application startup completed. {Operation} {Subsystem}",
            "PrepareForStartup",
            "Startup");
    }

    private static void ShowAlreadyRunningMessage()
    {
        MessageBox.Show(
            AlreadyRunningMessage,
            "RunCatDashboard",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static void ConfigureServices(
        IServiceCollection services,
        IApplicationPaths applicationPaths,
        IApplicationLoggingRuntime loggingRuntime)
    {
        services.AddLogging();
        services.AddSingleton(loggingRuntime.LoggerFactory);
        services.AddSingleton(applicationPaths);
        services.AddSingleton(loggingRuntime);
        services.AddSingleton<ISettingsStore>(
            _ => new JsonSettingsStore(
                applicationPaths.DataDirectory,
                new PhysicalSettingsFileSystem()));
        services.AddSingleton<ISettingsService>(provider =>
            new SettingsService(
                provider.GetRequiredService<ISettingsStore>(),
                logger: provider.GetRequiredService<ILogger<SettingsService>>()));
        services.AddSingleton<IRunAtLoginService>(provider =>
            new RunAtLoginService(
                new CurrentUserRunRegistry(),
                () => Environment.ProcessPath,
                provider.GetRequiredService<ILogger<RunAtLoginService>>()));
        services.AddSingleton<ISystemMetricsService, WindowsSystemMetricsService>();
        services.AddSingleton<IUiDispatcher>(
            _ => new WpfUiDispatcher(Current.Dispatcher));
        services.AddSingleton<IAnimationTimer>(
            _ => new DispatcherAnimationTimer(Current.Dispatcher));
        services.AddSingleton<IRunCatAnimationController>(provider =>
            new RunCatAnimationController(
                provider.GetRequiredService<IAnimationTimer>(),
                logger: provider.GetRequiredService<ILogger<RunCatAnimationController>>()));
        services.AddSingleton<IOverlayWindowController>(provider =>
            new OverlayWindowController(
                new Win32NativeWindowStyleApi(),
                provider.GetRequiredService<ILogger<OverlayWindowController>>()));
        services.AddSingleton<IOverlayModeCoordinator>(provider =>
            new OverlayModeCoordinator(
                provider.GetRequiredService<IOverlayWindowController>()));
        services.AddSingleton<IInteractionModeToggleAction>(provider =>
            new InteractionModeToggleAction(
                provider.GetRequiredService<IUiDispatcher>(),
                provider.GetRequiredService<IOverlayModeCoordinator>()));
        services.AddSingleton<IWindowVisibilityCoordinator>(
            _ => new WindowVisibilityCoordinator());
        services.AddSingleton<IApplicationExitCoordinator>(
            _ => new ApplicationExitCoordinator());
        services.AddSingleton(provider => new ExplicitShutdownCoordinator(
            provider.GetRequiredService<IWindowVisibilityCoordinator>(),
            provider.GetRequiredService<ISettingsService>(),
            provider.GetRequiredService<IApplicationLoggingRuntime>(),
            provider.GetRequiredService<ILoggerFactory>().CreateLogger(
                LoggingPolicy.LifecycleCategory)));
        services.AddSingleton<IGlobalHotKeyController>(provider =>
            new GlobalHotKeyController(
                new Win32GlobalHotKeyApi(),
                provider.GetRequiredService<ILogger<GlobalHotKeyController>>(),
                provider.GetRequiredService<ISettingsService>()
                    .Current.Overlay.InteractionHotKey,
                provider.GetRequiredService<ISettingsService>()
                    .Current.Window.VisibilityHotKey));
        services.AddSingleton<IOverlayHotKeyMessageHandler>(provider =>
            new OverlayHotKeyMessageHandler(
                provider.GetRequiredService<IGlobalHotKeyController>(),
                provider.GetRequiredService<IInteractionModeToggleAction>(),
                provider.GetRequiredService<IWindowVisibilityCoordinator>()));
        services.AddSingleton<ITrayIconAdapter>(
            _ => new NotifyIconTrayAdapter(
                new AssemblyTrayIconResourceLoader(),
                new AssemblyTrayAnimationIconResourceLoader()));
        services.AddSingleton<ITrayAnimationCoordinator>(provider =>
            new TrayAnimationCoordinator(
                provider.GetRequiredService<ITrayIconAdapter>(),
                provider.GetRequiredService<IRunCatAnimationController>(),
                provider.GetRequiredService<ILogger<TrayAnimationCoordinator>>()));
        services.AddSingleton<ISystemTrayService>(provider =>
            new SystemTrayService(
                provider.GetRequiredService<ITrayIconAdapter>(),
                new Win32RegisteredWindowMessageApi(),
                provider.GetRequiredService<IWindowVisibilityCoordinator>(),
                provider.GetRequiredService<IInteractionModeToggleAction>(),
                provider.GetRequiredService<IApplicationExitCoordinator>(),
                provider.GetRequiredService<ITrayAnimationCoordinator>(),
                provider.GetRequiredService<ILogger<SystemTrayService>>()));
        services.AddSingleton<IWindowWorkAreaProvider, Win32WindowWorkAreaProvider>();
        services.AddSingleton<IOverlayDisplayMonitor>(
            provider => new OverlayDisplayMonitor(
                new FullscreenObservationSource(new Win32FullscreenApi()),
                new Win32ForegroundWindowEventHook(),
                new ReconciliationTimer(),
                logger: provider.GetRequiredService<ILogger<OverlayDisplayMonitor>>(),
                highFrequencyLogger: provider.GetRequiredService<ILoggerFactory>().CreateLogger(
                    $"{LoggingPolicy.HighFrequencyCategoryPrefix}.Fullscreen")));
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<ISettingsApplicationService>(provider =>
            new SettingsApplicationService(
                provider.GetRequiredService<ISettingsService>(),
                provider.GetRequiredService<IWindowVisibilityCoordinator>(),
                provider.GetRequiredService<IInteractionModeToggleAction>(),
                provider.GetRequiredService<IGlobalHotKeyController>(),
                provider.GetRequiredService<MainWindowViewModel>(),
                provider.GetRequiredService<IRunAtLoginService>()));
        services.AddTransient<SettingsWindowViewModel>();
        services.AddTransient<SettingsWindow>();
        services.AddSingleton<ISettingsWindowService>(provider =>
            new SettingsWindowService(() => provider.GetRequiredService<SettingsWindow>()));
        services.AddSingleton<MainWindow>();
    }

    private static string GetApplicationVersion() =>
        typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown";
}

