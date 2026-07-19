using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using RunCatDashboard.App.Animation;

namespace RunCatDashboard.App.Views;

public sealed class RunCatFrameConverter : IValueConverter
{
    private static readonly IReadOnlyList<BitmapSource> Frames = LoadFrames();

    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        return value is int frameIndex && frameIndex >= 0 && frameIndex < Frames.Count
            ? Frames[frameIndex]
            : DependencyProperty.UnsetValue;
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    internal static IReadOnlyList<BitmapSource> LoadFrames()
    {
        var frames = new BitmapSource[RunCatAnimationController.DefaultFrameCount];
        for (int index = 0; index < frames.Length; index++)
        {
            var frame = new BitmapImage();
            frame.BeginInit();
            frame.CacheOption = BitmapCacheOption.OnLoad;
            frame.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            frame.UriSource = new Uri(
                $"pack://application:,,,/RunCatDashboard.App;component/Assets/RunCat/cat-frame-{index + 1:D2}.png",
                UriKind.Absolute);
            frame.EndInit();
            frame.Freeze();
            frames[index] = frame;
        }

        return Array.AsReadOnly(frames);
    }
}
