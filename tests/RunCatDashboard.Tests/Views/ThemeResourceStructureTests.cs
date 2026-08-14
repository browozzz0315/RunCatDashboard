using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

namespace RunCatDashboard.Tests.Views;

public sealed class ThemeResourceStructureTests
{
    private static readonly string[] RequiredKeys =
    [
        "AppWindowBackgroundBrush",
        "AppPanelBackgroundBrush",
        "AppBorderBrush",
        "AppPrimaryTextBrush",
        "AppSecondaryTextBrush",
        "AppMutedTextBrush",
        "AppAccentBrush",
        "AppWarningBrush",
        "AppErrorBrush",
        "AppModeBackgroundBrush",
        "AppHistoryBackgroundBrush",
        "AppInputBackgroundBrush",
        "AppInputForegroundBrush",
        "AppDiagnosticsBackgroundBrush",
        "ControlBackgroundBrush",
        "ControlForegroundBrush",
        "ControlBorderBrush",
        "ControlHoverBackgroundBrush",
        "ControlPressedBackgroundBrush",
        "ControlDisabledBackgroundBrush",
        "ControlDisabledForegroundBrush",
        "ControlSelectionBackgroundBrush",
        "ControlSelectionForegroundBrush",
        "ControlCheckedBackgroundBrush",
        "ControlFocusBorderBrush",
        "ControlCheckMarkBrush",
        "ControlDisabledCheckMarkBrush",
        "ControlScrollBarTrackBrush",
        "ControlScrollBarThumbBrush",
        "OverlayPanelBackgroundBrush",
        "OverlayBorderBrush",
        "OverlayPrimaryTextBrush",
        "OverlaySecondaryTextBrush",
        "OverlayErrorBrush",
        "OverlayModeBackgroundBrush",
        "OverlayHistoryBackgroundBrush"
    ];

    [Fact]
    public void LightAndDarkDictionaries_ExposeTheSameSemanticKeys()
    {
        HashSet<string> light = ReadKeys("Light.xaml");
        HashSet<string> dark = ReadKeys("Dark.xaml");

        Assert.Equal(light.Order(StringComparer.Ordinal), dark.Order(StringComparer.Ordinal));
        Assert.All(RequiredKeys, key => Assert.Contains(key, light));
    }

    [Fact]
    public void Windows_UseDynamicResourcesForThemeSensitiveValues()
    {
        string main = File.ReadAllText(GetSourcePath("Views", "MainWindow.xaml"));
        string settings = File.ReadAllText(GetSourcePath("Views", "SettingsWindow.xaml"));

        Assert.Contains("DynamicResource AppDiagnosticsBackgroundBrush", main);
        Assert.DoesNotContain("Background=\"#", main);
        Assert.DoesNotContain("DimGray", settings);
        Assert.DoesNotContain("DodgerBlue", settings);
        Assert.DoesNotContain("DarkRed", settings);
        Assert.DoesNotContain("DarkOrange", settings);
        Assert.Contains("DynamicResource ThemeWindowStyle", settings);
        Assert.Contains("BasedOn=\"{StaticResource ThemeComboBoxStyle}\"", settings);
        Assert.Contains("BasedOn=\"{StaticResource ThemeButtonStyle}\"", settings);
        Assert.Contains("BasedOn=\"{StaticResource ThemeTextBoxStyle}\"", settings);
        Assert.Contains("BasedOn=\"{StaticResource ThemeCheckBoxStyle}\"", settings);
        Assert.Contains("BasedOn=\"{StaticResource ThemeGroupBoxStyle}\"", settings);
        Assert.Contains("BasedOn=\"{StaticResource ThemeScrollBarStyle}\"", settings);
        Assert.DoesNotContain("Background=\"#", settings);
        Assert.DoesNotContain("Foreground=\"#", settings);
        Assert.DoesNotContain("BorderBrush=\"#", settings);
    }

    [Fact]
    public void ThemeDictionaries_DefineReadableControlStates()
    {
        foreach (string fileName in new[] { "Light.xaml", "Dark.xaml" })
        {
            string theme = File.ReadAllText(GetSourcePath("Themes", fileName));
            Assert.Contains("x:Key=\"ThemeComboBoxStyle\"", theme);
            Assert.Contains("x:Key=\"ThemeComboBoxItemStyle\"", theme);
            Assert.Contains("x:Key=\"ThemeButtonStyle\"", theme);
            Assert.Contains("x:Key=\"ThemeTextBoxStyle\"", theme);
            Assert.Contains("x:Key=\"ThemeCheckBoxStyle\"", theme);
            Assert.Contains("ControlSelectionBackgroundBrush", theme);
            Assert.Contains("ControlDisabledBackgroundBrush", theme);
            Assert.Contains("ControlDisabledForegroundBrush", theme);
            Assert.Contains("ControlHoverBackgroundBrush", theme);
            Assert.Contains("ControlPressedBackgroundBrush", theme);
        }
    }

    [Fact]
    public void CheckBoxTemplates_KeepCheckedGlyphVisibleOutsideHover()
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        foreach (string fileName in new[] { "Light.xaml", "Dark.xaml" })
        {
            XDocument document = XDocument.Load(GetSourcePath("Themes", fileName));
            XElement style = document.Descendants()
                .Single(element => (string?)element.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Key") == "ThemeCheckBoxStyle");
            XElement triggers = style.Descendants(presentation + "ControlTemplate.Triggers").Single();

            XElement checkedTrigger = triggers.Elements(presentation + "Trigger")
                .Single(trigger => (string?)trigger.Attribute("Property") == "IsChecked"
                    && (string?)trigger.Attribute("Value") == "True");
            XElement checkVisibility = checkedTrigger.Elements(presentation + "Setter")
                .Single(setter => (string?)setter.Attribute("TargetName") == "CheckMark"
                    && (string?)setter.Attribute("Property") == "Visibility");
            Assert.Equal("Visible", (string?)checkVisibility.Attribute("Value"));
            Assert.Contains(
                checkedTrigger.Elements(presentation + "Setter"),
                setter => (string?)setter.Attribute("Property") == "Background"
                    && ((string?)setter.Attribute("Value"))?.Contains("ControlCheckedBackgroundBrush", StringComparison.Ordinal) == true);

            Assert.Contains(
                triggers.Elements(presentation + "MultiTrigger"),
                trigger => HasCondition(trigger, "IsChecked", "False")
                    && HasCondition(trigger, "IsMouseOver", "True"));

            XElement disabledTrigger = triggers.Elements(presentation + "Trigger")
                .Single(trigger => (string?)trigger.Attribute("Property") == "IsEnabled"
                    && (string?)trigger.Attribute("Value") == "False");
            Assert.Contains(
                disabledTrigger.Elements(presentation + "Setter"),
                setter => (string?)setter.Attribute("TargetName") == "CheckMark"
                    && (string?)setter.Attribute("Property") == "Stroke"
                    && ((string?)setter.Attribute("Value"))?.Contains("ControlDisabledCheckMarkBrush", StringComparison.Ordinal) == true);
        }
    }

    [Fact]
    public void ThemeControlTemplates_CanBeLoadedAndAppliedOnSta()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                foreach (string fileName in new[] { "Light.xaml", "Dark.xaml" })
                {
                    ResourceDictionary resources = new()
                    {
                        Source = new Uri(GetSourcePath("Themes", fileName), UriKind.Absolute)
                    };

                    var checkBox = new CheckBox
                    {
                        Resources = resources,
                        Style = (Style)resources["ThemeCheckBoxStyle"],
                        IsChecked = true
                    };
                    Assert.True(checkBox.ApplyTemplate());

                    var comboBox = new ComboBox
                    {
                        Resources = resources,
                        Style = (Style)resources["ThemeComboBoxStyle"]
                    };
                    comboBox.Items.Add("HideOverFullscreenApps");
                    comboBox.SelectedIndex = 0;
                    Assert.True(comboBox.ApplyTemplate());
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }

    [Fact]
    public void MultiDataTriggerConditions_AlwaysUseBindings()
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        foreach (string fileName in new[] { "Light.xaml", "Dark.xaml" })
        {
            XDocument document = XDocument.Load(GetSourcePath("Themes", fileName));
            foreach (XElement condition in document.Descendants(presentation + "MultiDataTrigger")
                .Descendants(presentation + "Condition"))
            {
                Assert.False(string.IsNullOrWhiteSpace((string?)condition.Attribute("Binding")));
                Assert.Null(condition.Attribute("Property"));
            }
        }
    }

    [Fact]
    public void ComboBoxTemplates_ReserveArrowColumnAndTrimSelectedText()
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        foreach (string fileName in new[] { "Light.xaml", "Dark.xaml" })
        {
            XDocument document = XDocument.Load(GetSourcePath("Themes", fileName));
            XElement style = document.Descendants()
                .Single(element => (string?)element.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Key") == "ThemeComboBoxStyle");
            XElement layoutGrid = style.Descendants(presentation + "Grid")
                .Single(grid => grid.Element(presentation + "Grid.ColumnDefinitions") is not null
                    && grid.Elements(presentation + "ContentPresenter").Any());
            XElement selectedContent = layoutGrid.Elements(presentation + "ContentPresenter").Single();
            XElement arrow = layoutGrid.Elements(presentation + "Path").Single();

            Assert.Equal("0", (string?)selectedContent.Attribute("Grid.Column"));
            Assert.Equal("1", (string?)arrow.Attribute("Grid.Column"));
            Assert.Equal("Stretch", (string?)selectedContent.Attribute("HorizontalAlignment"));
            Assert.Contains(
                selectedContent.Descendants(presentation + "Setter"),
                setter => (string?)setter.Attribute("Property") == "TextTrimming"
                    && (string?)setter.Attribute("Value") == "CharacterEllipsis");
            Assert.Contains(
                selectedContent.Descendants(presentation + "Setter"),
                setter => (string?)setter.Attribute("Property") == "TextWrapping"
                    && (string?)setter.Attribute("Value") == "NoWrap");
        }
    }

    private static bool HasCondition(XElement trigger, string property, string value)
    {
        return trigger.Descendants(XName.Get("Condition", "http://schemas.microsoft.com/winfx/2006/xaml/presentation"))
            .Any(condition => (string?)condition.Attribute("Property") == property
                && (string?)condition.Attribute("Value") == value);
    }

    private static HashSet<string> ReadKeys(string fileName)
    {
        XDocument document = XDocument.Load(GetSourcePath("Themes", fileName));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return document.Descendants()
            .Select(element => (string?)element.Attribute(x + "Key"))
            .Where(key => key is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string GetSourcePath(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string root = Path.Combine([directory.FullName, "src", "RunCatDashboard.App"]);
            string candidate = Path.Combine([root, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            string.Join(Path.DirectorySeparatorChar, parts));
    }
}
