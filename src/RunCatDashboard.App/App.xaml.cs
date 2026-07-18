using System.Windows;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly ServiceProvider _serviceProvider;

    public App()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ISystemMetricsService, WindowsSystemMetricsService>();
        services.AddSingleton<IUiDispatcher>(
            _ => new WpfUiDispatcher(Current.Dispatcher));
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
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
    }
}

