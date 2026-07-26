using System.IO;
using System.Xml.Linq;
using RunCatDashboard.App.ViewModels;

namespace RunCatDashboard.Tests.Views;

public sealed class MainWindowDiagnosticSurfaceTests
{
    [Fact]
    public async Task Xaml_KeepsCompactInteractionBadgeAndFormalUserInformationOnly()
    {
        string xamlPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "RunCatDashboard.App",
            "Views",
            "MainWindow.xaml");
        string xaml = await File.ReadAllTextAsync(xamlPath);
        XDocument document = XDocument.Parse(xaml);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        string[] removedBindings =
        [
            "AnimationAverageCpuText",
            "AnimationIntervalText",
            "AppliedDisplayPolicyText",
            "FullscreenDisplayStatusText",
            "ForegroundDisplayDiagnostic",
            "OverlayMonitorDiagnostic"
        ];
        foreach (string binding in removedBindings)
        {
            Assert.DoesNotContain(binding, xaml);
            Assert.Null(typeof(MainWindowViewModel).GetProperty(binding));
        }

        string[] removedPresentation =
        [
            "Text=\"RunCatDashboard\"",
            "Drag this panel while Interactive",
            "Ctrl + Alt + Shift + R",
            "Ctrl + Alt + Shift + D",
            "toggle interaction mode",
            "show/hide Dashboard",
            "OverlayHotKeyText",
            "Text=\"Run Cat\""
        ];
        foreach (string text in removedPresentation)
        {
            Assert.DoesNotContain(text, xaml);
        }
        Assert.Null(typeof(MainWindowViewModel).GetProperty("OverlayHotKeyText"));

        XElement content = Assert.Single(document.Descendants(presentation + "StackPanel"),
            element => (string?)element.Attribute(x + "Name") == "OverlayContent");
        XElement topRow = Assert.Single(content.Elements(),
            element => (string?)element.Attribute(x + "Name") == "OverlayTopRow");
        Assert.Same(topRow, content.Elements().First());
        Assert.Contains(topRow.Descendants(presentation + "Image"), image =>
            ((string?)image.Attribute("Source"))?.Contains("AnimationFrameIndex") == true);
        Assert.Contains(topRow.Descendants(presentation + "TextBlock"), textBlock =>
            (string?)textBlock.Attribute("Text") == "{Binding OverlayModeText}");
        Assert.Contains(topRow.Descendants(presentation + "Button"), button =>
            (string?)button.Attribute("Content") == "Close");

        XElement fullscreenHeading = topRow.ElementsAfterSelf().First();
        Assert.Equal(presentation + "TextBlock", fullscreenHeading.Name);
        Assert.Equal("Fullscreen display policy", (string?)fullscreenHeading.Attribute("Text"));

        Assert.Contains("Text=\"Fullscreen display policy\"", xaml);
        Assert.Contains("ItemsSource=\"{Binding DisplayPolicies}\"", xaml);
        Assert.Contains("SelectedItem=\"{Binding RequestedDisplayPolicy, Mode=TwoWay}\"", xaml);
        Assert.Contains("Text=\"System Metrics Dashboard\"", xaml);
        Assert.Contains("ItemsSource=\"{Binding CpuHistoryNewestFirst}\"", xaml);
        Assert.Contains("Text=\"{Binding SamplingStatus}\"", xaml);
        Assert.Contains("Text=\"{Binding ErrorMessage}\"", xaml);
        Assert.Contains("Text=\"{Binding OverlayErrorMessage}\"", xaml);
        Assert.Contains("Text=\"{Binding HotKeyErrorMessage}\"", xaml);
        Assert.Contains("Text=\"{Binding TrayErrorMessage}\"", xaml);
        Assert.Contains("Text=\"{Binding DisplayPolicyFault}\"", xaml);
        Assert.NotNull(typeof(MainWindowViewModel).GetProperty("DisplayPolicyFault"));
        Assert.NotNull(typeof(MainWindowViewModel).GetProperty("HotKeyErrorMessage"));
    }

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
