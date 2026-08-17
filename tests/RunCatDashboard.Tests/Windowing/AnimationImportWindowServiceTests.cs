using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using RunCatDashboard.App.Animation;
using RunCatDashboard.App.ViewModels;
using RunCatDashboard.App.Views;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Windowing;

public sealed class AnimationImportWindowServiceTests
{
    [Fact]
    public void ImportWindow_ResolvesAllStableDependenciesOnSta()
    {
        string root = CreateTestDirectory();
        try
        {
            RunOnSta(() =>
            {
                using ServiceProvider provider = CreateProvider(root);

                Assert.NotNull(provider.GetRequiredService<IAnimationFilePicker>());
                Assert.NotNull(provider.GetRequiredService<AnimationImportService>());
                Assert.Null(provider.GetService<Action<AnimationCatalogEntry>>());

                AnimationImportWindow window =
                    provider.GetRequiredService<AnimationImportWindow>();
                Assert.IsType<AnimationImportWindowViewModel>(window.DataContext);

                window.Show();
                window.Close();
            });
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void Open_ReusesWindowUsesPerOpenCallbackAndStopsPreviewAfterClose()
    {
        string root = CreateTestDirectory();
        try
        {
            RunOnSta(() =>
            {
                string sourcePath = CreatePng(root);
                ServiceCollection services = CreateImportRegistrations(root, sourcePath);
                AnimationImportWindow? currentWindow = null;
                int createCount = 0;
                services.AddSingleton<IAnimationImportWindowService>(provider =>
                    new AnimationImportWindowService(() =>
                    {
                        createCount++;
                        currentWindow = provider.GetRequiredService<AnimationImportWindow>();
                        return currentWindow;
                    }));

                using ServiceProvider provider = services.BuildServiceProvider();
                IAnimationImportWindowService service =
                    provider.GetRequiredService<IAnimationImportWindowService>();
                int firstCallbackCount = 0;
                int ignoredCallbackCount = 0;
                int secondCallbackCount = 0;

                service.Open(_ => firstCallbackCount++);
                AnimationImportWindow firstWindow = Assert.IsType<AnimationImportWindow>(
                    currentWindow);
                AnimationImportWindowViewModel firstViewModel =
                    Assert.IsType<AnimationImportWindowViewModel>(firstWindow.DataContext);
                DispatcherTimer firstPreviewTimer = GetPreviewTimer(firstWindow);

                service.Open(_ => ignoredCallbackCount++);

                Assert.Same(firstWindow, currentWindow);
                Assert.Equal(1, createCount);
                firstWindow.Close();
                Assert.False(firstPreviewTimer.IsEnabled);

                firstViewModel.ChooseSourceCommand.Execute(null);
                firstViewModel.DisplayName = "Closed first window";
                firstViewModel.ConfirmImportCommand.Execute(null);
                Assert.Equal(0, firstCallbackCount);
                Assert.Equal(0, ignoredCallbackCount);

                service.Open(_ => secondCallbackCount++);
                AnimationImportWindow secondWindow = Assert.IsType<AnimationImportWindow>(
                    currentWindow);
                AnimationImportWindowViewModel secondViewModel =
                    Assert.IsType<AnimationImportWindowViewModel>(secondWindow.DataContext);
                DispatcherTimer secondPreviewTimer = GetPreviewTimer(secondWindow);

                secondViewModel.ChooseSourceCommand.Execute(null);
                secondViewModel.DisplayName = "Second window";
                secondViewModel.ConfirmImportCommand.Execute(null);

                Assert.Equal(2, createCount);
                Assert.Equal(1, secondCallbackCount);
                Assert.False(secondPreviewTimer.IsEnabled);
            });
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    private static ServiceProvider CreateProvider(string root)
    {
        return CreateImportRegistrations(root, null).BuildServiceProvider();
    }

    private static ServiceCollection CreateImportRegistrations(
        string root,
        string? sourcePath)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new AnimationLibraryStorage(Path.Combine(root, "Animations")));
        services.AddSingleton<AnimationCatalog>(provider =>
            new AnimationCatalog(
                provider.GetRequiredService<AnimationLibraryStorage>(),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AnimationCatalog>>()));
        services.AddSingleton<SpriteSheetParser>();
        services.AddSingleton<AnimationImportService>(provider =>
            new AnimationImportService(
                provider.GetRequiredService<SpriteSheetParser>(),
                provider.GetRequiredService<AnimationLibraryStorage>(),
                provider.GetRequiredService<AnimationCatalog>(),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AnimationImportService>>()));
        services.AddSingleton<IAnimationFilePicker>(_ =>
            new FixedAnimationFilePicker(sourcePath));
        services.AddTransient<AnimationImportWindowViewModel>(provider =>
            new AnimationImportWindowViewModel(
                provider.GetRequiredService<IAnimationFilePicker>(),
                provider.GetRequiredService<AnimationImportService>()));
        services.AddTransient<AnimationImportWindow>();
        return services;
    }

    private static DispatcherTimer GetPreviewTimer(AnimationImportWindow window) =>
        Assert.IsType<DispatcherTimer>(
            typeof(AnimationImportWindow).GetField(
                "_previewTimer",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));

    private static string CreateTestDirectory() => Path.Combine(
        Path.GetTempPath(),
        "RunCatDashboard.Tests",
        Guid.NewGuid().ToString("N"));

    private static string CreatePng(string root)
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.png");
        byte[] pixels = Enumerable.Repeat((byte)255, 8 * 4 * 4).ToArray();
        BitmapSource source = BitmapSource.Create(
            8,
            4,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            8 * 4);
        source.Freeze();
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(stream);
        return path;
    }

    private static void DeleteTestDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        using var completed = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                completed.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        completed.Wait();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class FixedAnimationFilePicker : IAnimationFilePicker
    {
        private readonly string? _sourcePath;

        internal FixedAnimationFilePicker(string? sourcePath)
        {
            _sourcePath = sourcePath;
        }

        public AnimationFilePickerResult? PickPng() =>
            _sourcePath is null ? null : new AnimationFilePickerResult(_sourcePath);
    }
}
