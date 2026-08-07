using System.IO;
using System.Xml.Linq;
using RunCatDashboard.App.ViewModels;

namespace RunCatDashboard.Tests.Views;

public sealed class MainWindowDiagnosticSurfaceTests
{
    [Fact]
    public async Task Xaml_UsesContentSizedProfilesAndCompleteFieldVisibilityContainers()
    {
        string xaml = await File.ReadAllTextAsync(GetMainWindowPath("MainWindow.xaml"));
        XDocument document = XDocument.Parse(xaml);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement window = document.Root!;

        const string overlayWidthBinding = "{Binding OverlayWidth, Mode=OneWay}";
        Assert.Equal(overlayWidthBinding, (string?)window.Attribute("Width"));
        Assert.Equal(overlayWidthBinding, (string?)window.Attribute("MinWidth"));
        Assert.Equal(overlayWidthBinding, (string?)window.Attribute("MaxWidth"));
        Assert.Equal("{Binding OverlayMaxHeight}", (string?)window.Attribute("MaxHeight"));
        Assert.Equal("Height", (string?)window.Attribute("SizeToContent"));
        Assert.Null(window.Attribute("Height"));

        XElement scrollViewer = Assert.Single(
            document.Descendants(presentation + "ScrollViewer"));
        Assert.Equal("Disabled",
            (string?)scrollViewer.Attribute("HorizontalScrollBarVisibility"));
        Assert.Equal("Hidden",
            (string?)scrollViewer.Attribute("VerticalScrollBarVisibility"));

        XElement content = Assert.Single(document.Descendants(presentation + "StackPanel"),
            element => (string?)element.Attribute(x + "Name") == "OverlayContent");
        XElement catViewport = Assert.Single(content.Elements(),
            element => (string?)element.Attribute(x + "Name") == "CatViewport");
        Assert.Equal("True", (string?)catViewport.Attribute("ClipToBounds"));
        Assert.Equal("{Binding CatViewportWidth}", (string?)catViewport.Attribute("Width"));
        Assert.Equal("{Binding CatViewportHeight}", (string?)catViewport.Attribute("Height"));
        XElement catImage = Assert.Single(catViewport.Descendants(presentation + "Image"));
        XElement animationCanvas = Assert.IsType<XElement>(catImage.Parent);
        Assert.Equal(presentation + "Canvas", animationCanvas.Name);
        Assert.Equal("CatAnimationCanvas", (string?)animationCanvas.Attribute(x + "Name"));
        Assert.Equal("{Binding CatViewportWidth}", (string?)animationCanvas.Attribute("Width"));
        Assert.Equal("{Binding CatViewportHeight}", (string?)animationCanvas.Attribute("Height"));
        Assert.Equal("{Binding CatRenderSize}", (string?)catImage.Attribute("Width"));
        Assert.Equal("{Binding CatRenderSize}", (string?)catImage.Attribute("Height"));
        Assert.Equal("{Binding CatRenderOffsetX}", (string?)catImage.Attribute("Canvas.Left"));
        Assert.Equal("{Binding CatRenderOffsetY}", (string?)catImage.Attribute("Canvas.Top"));
        Assert.Equal("Uniform", (string?)catImage.Attribute("Stretch"));
        Assert.Equal("NearestNeighbor",
            (string?)catImage.Attribute("RenderOptions.BitmapScalingMode"));
        Assert.Empty(animationCanvas.Descendants(presentation + "DataTrigger"));

        XElement catFloorLine = Assert.Single(catViewport.Elements(presentation + "Border"));
        Assert.Equal("CatFloorLine", (string?)catFloorLine.Attribute(x + "Name"));
        Assert.Equal("1", (string?)catFloorLine.Attribute("Height"));
        Assert.Equal("Bottom", (string?)catFloorLine.Attribute("VerticalAlignment"));
        Assert.Equal("0.55", (string?)catFloorLine.Attribute("Opacity"));
        Assert.Null(catFloorLine.Attribute("BorderBrush"));
        Assert.Null(catFloorLine.Attribute("BorderThickness"));
        Assert.Contains(catFloorLine, animationCanvas.ElementsAfterSelf());
        Assert.DoesNotContain("ScaleTransform", xaml);
        Assert.DoesNotContain("UniformToFill", xaml);
        Assert.DoesNotContain("Width=\"98\"", xaml);
        Assert.DoesNotContain("Height=\"66\"", xaml);

        string[] visibilityProperties =
        [
            "ShowDashboardContent",
            "ShowCpu",
            "ShowMemory",
            "ShowUsedAndTotalMemory",
            "ShowLastUpdated",
            "ShowSamplingStatus",
            "ShowRecentCpuHistory",
            "ShowInteractionMode",
            "ShowHotKeyHints",
            "HasDiagnostics"
        ];
        foreach (string property in visibilityProperties)
        {
            Assert.Contains($"Visibility=\"{{Binding {property},", xaml);
            Assert.NotNull(typeof(MainWindowViewModel).GetProperty(property));
        }

        Assert.Contains("Text=\"{Binding HotKeyHintsText}\"", xaml);
        Assert.DoesNotContain("Ctrl + Alt + Shift + R", xaml);
        Assert.DoesNotContain("Ctrl + Alt + Shift + D", xaml);
        Assert.DoesNotContain("Fullscreen display policy", xaml);
        Assert.DoesNotContain("ItemsSource=\"{Binding DisplayPolicies}\"", xaml);
        Assert.Contains("x:Name=\"DiagnosticsPanel\"", xaml);
        Assert.Contains("Text=\"{Binding PlacementErrorMessage}\"", xaml);
        Assert.Contains("Text=\"{Binding ErrorMessage}\"", xaml);
        Assert.Contains("Text=\"{Binding SamplingStatus}\"", xaml);

        XElement primaryValueStyle = Assert.Single(
            document.Descendants(presentation + "Style"),
            style => (string?)style.Attribute(x + "Key") == "PrimaryMetricValueStyle");
        XElement fontSizeSetter = Assert.Single(
            primaryValueStyle.Elements(presentation + "Setter"),
            setter => (string?)setter.Attribute("Property") == "FontSize");
        Assert.Equal("17", (string?)fontSizeSetter.Attribute("Value"));
        Assert.DoesNotContain("FontSize=\"20\"", xaml);
    }

    [Fact]
    public async Task CodeBehind_SetsDataContextBeforeInitializeComponent()
    {
        string code = await File.ReadAllTextAsync(GetMainWindowPath("MainWindow.xaml.cs"));

        int dataContextAssignment = code.IndexOf(
            "DataContext = viewModel;",
            StringComparison.Ordinal);
        int initializeComponent = code.IndexOf(
            "InitializeComponent();",
            StringComparison.Ordinal);

        Assert.True(dataContextAssignment >= 0);
        Assert.True(initializeComponent > dataContextAssignment);
        Assert.Equal(dataContextAssignment, code.LastIndexOf(
            "DataContext = viewModel;",
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task CodeBehind_SizeChangedDefersClampUntilNewActualSizeIsAvailable()
    {
        string code = await File.ReadAllTextAsync(GetMainWindowPath("MainWindow.xaml.cs"));

        Assert.Contains("SizeChanged += OnOverlaySizeChanged", code);
        Assert.Contains("DispatcherPriority.Loaded", code);
        Assert.Contains("TryClampToCurrentWorkArea();", code);
        Assert.Contains("ActualWidth > 0 ? ActualWidth : Width", code);
        Assert.Contains("ActualHeight > 0 ? ActualHeight : Height", code);
        Assert.Contains("!HasFiniteWindowSize()", code);
        Assert.Contains("TryCompleteInitialPlacement", code);
        Assert.Contains("ReportPlacementError(null)", code);
    }

    [Fact]
    public async Task FullscreenPolicyControl_LivesOnlyInSettingsWindow()
    {
        string mainXaml = await File.ReadAllTextAsync(GetMainWindowPath("MainWindow.xaml"));
        string settingsXaml = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "RunCatDashboard.App",
            "Views",
            "SettingsWindow.xaml"));

        Assert.DoesNotContain("ItemsSource=\"{Binding DisplayPolicies}\"", mainXaml);
        Assert.Contains("ItemsSource=\"{Binding DisplayPolicies}\"", settingsXaml);
        Assert.Contains("SelectedItem=\"{Binding RequestedDisplayPolicy}\"", settingsXaml);
    }

    private static string GetMainWindowPath(string fileName) => Path.Combine(
        FindRepositoryRoot(),
        "src",
        "RunCatDashboard.App",
        "Views",
        fileName);

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RunCatDashboard.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the RunCatDashboard repository root.");
    }
}
