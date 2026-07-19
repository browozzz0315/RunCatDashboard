using System.Collections;
using System.IO;
using System.Resources;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using RunCatDashboard.App.Views;

namespace RunCatDashboard.Tests.Resources;

public sealed class RunCatResourceTests
{
    private const int ExpectedFrameCount = 6;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly string[] FrameResourceNames =
        Enumerable.Range(1, ExpectedFrameCount)
            .Select(index => $"assets/runcat/cat-frame-{index:D2}.png")
            .ToArray();

    [Fact]
    public void Assembly_ContainsExactlySixStableOrderedFrameResources()
    {
        IReadOnlyDictionary<string, byte[]> resources = ReadRunCatResources();

        Assert.Equal(FrameResourceNames, resources.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Frames_Are48By32PngsWithAlphaAndVisibleContent()
    {
        IReadOnlyDictionary<string, byte[]> resources = ReadRunCatResources();

        foreach (string resourceName in FrameResourceNames)
        {
            byte[] png = resources[resourceName];
            Assert.True(png.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature));
            Assert.Contains(png[25], new byte[] { 4, 6 });

            DecodedImage frame = Decode(png);
            Assert.Equal(48, frame.Width);
            Assert.Equal(32, frame.Height);
            Assert.Equal(PixelFormats.Bgra32, frame.Format);

            IEnumerable<byte> alphaValues = frame.Pixels
                .Where((_, index) => index % 4 == 3);
            Assert.Contains(alphaValues, alpha => alpha < byte.MaxValue);
            Assert.Contains(alphaValues, alpha => alpha > byte.MinValue);
        }
    }

    [Fact]
    public void Frames_DoNotAllHaveIdenticalDecodedPixelContent()
    {
        IReadOnlyDictionary<string, byte[]> resources = ReadRunCatResources();

        int distinctFrameCount = FrameResourceNames
            .Select(name => Convert.ToBase64String(Decode(resources[name]).Pixels))
            .Distinct(StringComparer.Ordinal)
            .Count();

        Assert.True(distinctFrameCount > 1);
    }

    [Fact]
    public void FrameConverter_UsesOneFrozenCacheAndHandlesInvalidIndexes()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Application? application = null;
            try
            {
                application = new Application();
                var firstConverter = new RunCatFrameConverter();
                var secondConverter = new RunCatFrameConverter();

                for (int index = 0; index < ExpectedFrameCount; index++)
                {
                    object first = firstConverter.Convert(index, typeof(ImageSource), null!, null!);
                    object repeated = firstConverter.Convert(index, typeof(ImageSource), null!, null!);
                    object fromSecondConverter = secondConverter.Convert(index, typeof(ImageSource), null!, null!);

                    BitmapSource frame = Assert.IsAssignableFrom<BitmapSource>(first);
                    Assert.True(frame.IsFrozen);
                    Assert.Same(first, repeated);
                    Assert.Same(first, fromSecondConverter);
                }

                Assert.Same(
                    DependencyProperty.UnsetValue,
                    firstConverter.Convert(-1, typeof(ImageSource), null!, null!));
                Assert.Same(
                    DependencyProperty.UnsetValue,
                    firstConverter.Convert(ExpectedFrameCount, typeof(ImageSource), null!, null!));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                application?.Shutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));

        Assert.Null(failure);
    }

    [Fact]
    public void MainWindow_KeepsFixedRunCatImageLayoutAndNearestNeighborScaling()
    {
        string xamlPath = FindRepositoryFile("src/RunCatDashboard.App/Views/MainWindow.xaml");
        XDocument document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XElement image = Assert.Single(
            document.Descendants(presentation + "Image"),
            element =>
                element.Attribute("Source")?.Value.Contains(
                    "RunCatFrameConverter",
                    StringComparison.Ordinal) == true);

        Assert.Equal("96", image.Attribute("Width")?.Value);
        Assert.Equal("64", image.Attribute("Height")?.Value);
        Assert.Equal("NearestNeighbor", image.Attribute("RenderOptions.BitmapScalingMode")?.Value);
    }

    private static IReadOnlyDictionary<string, byte[]> ReadRunCatResources()
    {
        Stream resourceStream = typeof(RunCatDashboard.App.App).Assembly.GetManifestResourceStream(
            "RunCatDashboard.App.g.resources") ??
            throw new InvalidOperationException("The WPF generated resource stream is missing.");
        using (resourceStream)
        using (var reader = new ResourceReader(resourceStream))
        {
            var resources = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in reader)
            {
                string name = (string)entry.Key;
                if (!name.StartsWith("assets/runcat/", StringComparison.Ordinal))
                {
                    continue;
                }

                using var valueStream = (Stream)entry.Value!;
                using var copy = new MemoryStream();
                valueStream.CopyTo(copy);
                resources.Add(name, copy.ToArray());
            }

            return resources;
        }
    }

    private static DecodedImage Decode(byte[] png)
    {
        using var stream = new MemoryStream(png, writable: false);
        var decoder = new PngBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        BitmapSource source = decoder.Frames[0];
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0d);
        int stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return new DecodedImage(
            converted.PixelWidth,
            converted.PixelHeight,
            converted.Format,
            pixels);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        string normalizedPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, normalizedPath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Repository file was not found: {relativePath}");
    }

    private sealed record DecodedImage(
        int Width,
        int Height,
        PixelFormat Format,
        byte[] Pixels);
}
