using System.Collections;
using System.IO;
using System.Resources;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using RunCatDashboard.App.Animation;
using RunCatDashboard.App.Views;

namespace RunCatDashboard.Tests.Resources;

public sealed class RunCatResourceTests
{
    private const int ExpectedFrameCount = RunCatAnimationController.DefaultFrameCount;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly string[] FrameResourceNames =
        Enumerable.Range(1, ExpectedFrameCount)
            .Select(index => $"assets/runcat/cat-frame-{index:D2}.png")
            .ToArray();

    [Fact]
    public void Assembly_ContainsExactlyEightStableOrderedFrameResources()
    {
        IReadOnlyDictionary<string, byte[]> resources = ReadRunCatResources();

        Assert.Equal(8, ExpectedFrameCount);
        Assert.Equal(FrameResourceNames, resources.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Frames_Are50By50RgbaPngsWithAlphaAndVisibleContent()
    {
        IReadOnlyDictionary<string, byte[]> resources = ReadRunCatResources();

        foreach (string resourceName in FrameResourceNames)
        {
            byte[] png = resources[resourceName];
            Assert.True(png.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature));
            Assert.Equal(6, png[25]);

            DecodedImage frame = Decode(png);
            Assert.Equal(50, frame.Width);
            Assert.Equal(50, frame.Height);
            Assert.Equal(PixelFormats.Bgra32, frame.Format);

            IEnumerable<byte> alphaValues = frame.Pixels
                .Where((_, index) => index % 4 == 3);
            Assert.Contains(alphaValues, alpha => alpha < byte.MaxValue);
            Assert.Contains(alphaValues, alpha => alpha > byte.MinValue);
        }
    }

    [Fact]
    public void Frames_MatchCorrespondingLocalSourceStripRegionsWhenSourceIsAvailable()
    {
        string? sourcePath = TryFindRepositoryFile(
            "assets/Pet Cats Pack/Cat-2/Cat-2-Run.png");
        if (sourcePath is null)
        {
            return;
        }

        byte[] sourcePng = File.ReadAllBytes(sourcePath);
        Assert.Equal(6, sourcePng[25]);
        DecodedImage source = Decode(sourcePng);
        Assert.Equal(400, source.Width);
        Assert.Equal(50, source.Height);

        IReadOnlyDictionary<string, byte[]> resources = ReadRunCatResources();
        const int frameWidth = 50;
        const int frameHeight = 50;
        const int bytesPerPixel = 4;
        int sourceStride = source.Width * bytesPerPixel;
        int frameStride = frameWidth * bytesPerPixel;

        for (int frameIndex = 0; frameIndex < ExpectedFrameCount; frameIndex++)
        {
            DecodedImage frame = Decode(resources[FrameResourceNames[frameIndex]]);
            var expectedPixels = new byte[frameStride * frameHeight];
            for (int row = 0; row < frameHeight; row++)
            {
                Array.Copy(
                    source.Pixels,
                    row * sourceStride + frameIndex * frameStride,
                    expectedPixels,
                    row * frameStride,
                    frameStride);
            }

            Assert.Equal(expectedPixels, frame.Pixels);
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
    public void MainWindow_UsesCenteredTwoTimesNearestNeighborScalingWithoutChangingPanelSize()
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

        XElement border = Assert.IsType<XElement>(image.Parent);
        XElement scaleTransform = Assert.Single(image.Elements(presentation + "Image.RenderTransform"))
            .Element(presentation + "ScaleTransform") ??
            throw new InvalidOperationException("Run Cat ScaleTransform is missing.");

        Assert.Equal("98", border.Attribute("Width")?.Value);
        Assert.Equal("66", border.Attribute("Height")?.Value);
        Assert.Equal("True", border.Attribute("ClipToBounds")?.Value);
        Assert.Equal("64", image.Attribute("Width")?.Value);
        Assert.Equal("64", image.Attribute("Height")?.Value);
        Assert.Equal("Center", image.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("Center", image.Attribute("VerticalAlignment")?.Value);
        Assert.Equal("NearestNeighbor", image.Attribute("RenderOptions.BitmapScalingMode")?.Value);
        Assert.Equal("Uniform", image.Attribute("Stretch")?.Value);
        Assert.NotEqual("Fill", image.Attribute("Stretch")?.Value);
        Assert.Equal("0.5,0.5", image.Attribute("RenderTransformOrigin")?.Value);
        Assert.Equal("2", scaleTransform.Attribute("ScaleX")?.Value);
        Assert.Equal("2", scaleTransform.Attribute("ScaleY")?.Value);
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
        return TryFindRepositoryFile(relativePath) ??
            throw new FileNotFoundException($"Repository file was not found: {relativePath}");
    }

    private static string? TryFindRepositoryFile(string relativePath)
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

        return null;
    }

    private sealed record DecodedImage(
        int Width,
        int Height,
        PixelFormat Format,
        byte[] Pixels);
}
