using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using RunCatDashboard.App.Animation;
using RunCatDashboard.App.Services;
using RunCatDashboard.App.ViewModels;
using RunCatDashboard.App.Views;
using RunCatDashboard.App.Interop;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private const string AlreadyRunningMessage = "RunCatDashboard 已在執行中。";

    private readonly IApplicationInstanceGuard _instanceGuard;
    private readonly ApplicationStartupCoordinator _startupCoordinator;
    private ServiceProvider? _serviceProvider;

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
            (cleanupFailures ??= []).Add(exception);
        }

        try
        {
            _instanceGuard.Dispose();
        }
        catch (Exception exception)
        {
            (cleanupFailures ??= []).Add(exception);
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
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private static void ShowAlreadyRunningMessage()
    {
        MessageBox.Show(
            AlreadyRunningMessage,
            "RunCatDashboard",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ISystemMetricsService, WindowsSystemMetricsService>();
        services.AddSingleton<IUiDispatcher>(
            _ => new WpfUiDispatcher(Current.Dispatcher));
        services.AddSingleton<IAnimationTimer>(
            _ => new DispatcherAnimationTimer(Current.Dispatcher));
        services.AddSingleton<IRunCatAnimationController>(provider =>
            new RunCatAnimationController(
                provider.GetRequiredService<IAnimationTimer>()));
        services.AddSingleton<IOverlayWindowController>(
            _ => new OverlayWindowController(new Win32NativeWindowStyleApi()));
        services.AddSingleton<IOverlayModeCoordinator>(provider =>
            new OverlayModeCoordinator(
                provider.GetRequiredService<IOverlayWindowController>()));
        services.AddSingleton<IGlobalHotKeyController>(
            _ => new GlobalHotKeyController(new Win32GlobalHotKeyApi()));
        services.AddSingleton<IOverlayHotKeyMessageHandler>(provider =>
            new OverlayHotKeyMessageHandler(
                provider.GetRequiredService<IGlobalHotKeyController>(),
                provider.GetRequiredService<IOverlayModeCoordinator>()));
        services.AddSingleton<IWindowWorkAreaProvider, Win32WindowWorkAreaProvider>();
        services.AddSingleton<IOverlayDisplayMonitor>(
            _ => new OverlayDisplayMonitor(
                new FullscreenObservationSource(new Win32FullscreenApi()),
                new Win32ForegroundWindowEventHook(),
                new ReconciliationTimer()));
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
    }
}

