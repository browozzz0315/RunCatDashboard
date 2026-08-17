using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using RunCatDashboard.App.Animation;
using RunCatDashboard.App.Theming;

namespace RunCatDashboard.App.Views;

public sealed class RunCatFrameConverter : IValueConverter, IMultiValueConverter
{
    private static readonly IReadOnlyList<BitmapSource> BlackFrames =
        LoadFrames(ResolvedTheme.Light);
    private static readonly IReadOnlyList<BitmapSource> WhiteFrames =
        LoadFrames(ResolvedTheme.Dark);

    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        return GetFrame(value, ResolvedTheme.Light);
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    public object Convert(
        object[] values,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        ResolvedTheme theme = values.Length > 1 && values[1] is ResolvedTheme resolved
            ? resolved
            : ResolvedTheme.Light;
        if (values.Length > 2 && values[2] is not null &&
            values[2] != DependencyProperty.UnsetValue)
        {
            return values[2];
        }
        return GetFrame(values.Length > 0 ? values[0] : null, theme);
    }

    public object[] ConvertBack(
        object value,
        Type[] targetTypes,
        object parameter,
        CultureInfo culture) => throw new NotSupportedException();

    internal static IReadOnlyList<BitmapSource> LoadFrames(ResolvedTheme theme)
        => theme == ResolvedTheme.Dark
            ? RunCatBuiltInFrameProvider.WhiteFrames
            : RunCatBuiltInFrameProvider.BlackFrames;

    internal static void EnsureFramesLoaded()
    {
        _ = BlackFrames;
        _ = WhiteFrames;
    }

    internal static IReadOnlyList<BitmapSource> GetFrames(ResolvedTheme theme) =>
        theme == ResolvedTheme.Dark ? WhiteFrames : BlackFrames;

    private static object GetFrame(object? value, ResolvedTheme theme)
    {
        IReadOnlyList<BitmapSource> frames = GetFrames(theme);
        return value is int frameIndex && frameIndex >= 0 && frameIndex < frames.Count
            ? frames[frameIndex]
            : DependencyProperty.UnsetValue;
    }
}
