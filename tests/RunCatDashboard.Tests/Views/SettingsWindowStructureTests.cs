using System.IO;
using System.Xml.Linq;

namespace RunCatDashboard.Tests.Views;

public sealed class SettingsWindowStructureTests
{
    [Fact]
    public async Task ActionButtons_HaveRequiredOrderBindingsAndKeyboardBehavior()
    {
        string xaml = await File.ReadAllTextAsync(GetSettingsWindowPath("SettingsWindow.xaml"));
        XDocument document = XDocument.Parse(xaml);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XElement actions = Assert.Single(
            document.Descendants(presentation + "StackPanel"),
            element => (string?)element.Attribute("Grid.Row") == "2");
        XElement[] buttons = actions.Elements(presentation + "Button").ToArray();

        Assert.Equal(["儲存", "取消", "套用"],
            buttons.Select(button => (string?)button.Attribute("Content")));
        Assert.Equal("{Binding SaveCommand}", (string?)buttons[0].Attribute("Command"));
        Assert.Equal("True", (string?)buttons[0].Attribute("IsDefault"));
        Assert.Equal("{Binding CancelCommand}", (string?)buttons[1].Attribute("Command"));
        Assert.Equal("True", (string?)buttons[1].Attribute("IsCancel"));
        Assert.Equal("{Binding ApplyCommand}", (string?)buttons[2].Attribute("Command"));
    }

    [Fact]
    public async Task TitleBarClose_HasNoApplicationOrRollbackPath()
    {
        string code = await File.ReadAllTextAsync(GetSettingsWindowPath("SettingsWindow.xaml.cs"));

        Assert.DoesNotContain("ApplyDraftAsync", code);
        Assert.DoesNotContain("SaveCommand", code);
        Assert.DoesNotContain("ApplyCommand", code);
        Assert.Contains("protected override void OnClosing(CancelEventArgs e)", code);
        Assert.Contains("if (_viewModel.IsApplying)", code);
        Assert.Contains("e.Cancel = true;", code);
        Assert.Contains("_viewModel.EndHotKeyCapture();", code);
    }

    private static string GetSettingsWindowPath(string fileName) => Path.Combine(
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

        throw new DirectoryNotFoundException(
            "Could not locate the RunCatDashboard repository root.");
    }
}
