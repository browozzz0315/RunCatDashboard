using System.Collections;
using System.IO;
using System.Resources;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using RunCatDashboard.App.Animation;
using RunCatDashboard.App.Settings;
using RunCatDashboard.App.Views;

namespace RunCatDashboard.Tests.Resources;

public sealed class RunCatResourceTests
{
    private const int ExpectedFrameCount = RunCatAnimationController.DefaultFrameCount;
    private const double MinimumRenderedSafetyMargin = 4d;
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
    public void MainWindow_UsesFiniteAnimationCanvasAndUniformImageWithoutViewboxCropping()
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

        XElement animationCanvas = Assert.IsType<XElement>(image.Parent);
        XElement viewport = Assert.IsType<XElement>(animationCanvas.Parent);

        Assert.Equal(presentation + "Canvas", animationCanvas.Name);
        Assert.Equal("True", viewport.Attribute("ClipToBounds")?.Value);
        Assert.Equal("{Binding CatViewportWidth}", viewport.Attribute("Width")?.Value);
        Assert.Equal("{Binding CatViewportHeight}", viewport.Attribute("Height")?.Value);
        Assert.Equal("{Binding CatViewportWidth}", animationCanvas.Attribute("Width")?.Value);
        Assert.Equal("{Binding CatViewportHeight}", animationCanvas.Attribute("Height")?.Value);
        Assert.Equal("{Binding CatRenderSize}", image.Attribute("Width")?.Value);
        Assert.Equal("{Binding CatRenderSize}", image.Attribute("Height")?.Value);
        Assert.Equal("{Binding CatRenderOffsetX}", image.Attribute("Canvas.Left")?.Value);
        Assert.Equal("{Binding CatRenderOffsetY}", image.Attribute("Canvas.Top")?.Value);
        Assert.Equal("NearestNeighbor", image.Attribute("RenderOptions.BitmapScalingMode")?.Value);
        Assert.Equal("Uniform", image.Attribute("Stretch")?.Value);
        Assert.NotEqual("Fill", image.Attribute("Stretch")?.Value);
        Assert.Empty(image.Elements(presentation + "Image.RenderTransform"));
        Assert.Empty(viewport.Descendants(presentation + "Viewbox"));
    }

    [Fact]
    public void EveryFrame_VisiblePixelsRemainInsideEveryModeViewport()
    {
        IReadOnlyDictionary<string, byte[]> resources = ReadRunCatResources();

        foreach (OverlaySizeMode mode in Enum.GetValues<OverlaySizeMode>())
        {
            OverlaySizeProfile profile = OverlaySizeProfiles.Get(mode);
            double viewportWidth = GetCatViewportWidth(profile);

            foreach (string resourceName in FrameResourceNames)
            {
                AlphaBounds bounds = GetAlphaBounds(Decode(resources[resourceName]));
                PixelBounds rendered = RenderWithProfile(bounds, profile);

                Assert.True(rendered.Left >= MinimumRenderedSafetyMargin,
                    $"{mode} {resourceName} lacks the left safety margin.");
                Assert.True(rendered.Top >= MinimumRenderedSafetyMargin,
                    $"{mode} {resourceName} lacks the top safety margin.");
                Assert.True(rendered.Right <= viewportWidth - MinimumRenderedSafetyMargin,
                    $"{mode} {resourceName} lacks the right safety margin.");
                Assert.True(
                    rendered.Bottom <= profile.CatViewportHeight - MinimumRenderedSafetyMargin,
                    $"{mode} {resourceName} lacks the bottom safety margin.");
            }
        }
    }

    [Fact]
    public void FloorLine_StaysBelowEveryFramesIntendedVisibleRegion()
    {
        const double floorLineHeight = 1d;
        IReadOnlyDictionary<string, byte[]> resources = ReadRunCatResources();

        foreach (OverlaySizeMode mode in Enum.GetValues<OverlaySizeMode>())
        {
            OverlaySizeProfile profile = OverlaySizeProfiles.Get(mode);
            double floorLineTop = profile.CatViewportHeight - floorLineHeight;

            foreach (string resourceName in FrameResourceNames)
            {
                PixelBounds rendered = RenderWithProfile(
                    GetAlphaBounds(Decode(resources[resourceName])),
                    profile);

                Assert.True(rendered.Bottom + MinimumRenderedSafetyMargin <= floorLineTop,
                    $"{mode} {resourceName} enters the floor-line safety region.");
            }
        }
    }

    [Fact]
    public void CatOnlyCloseButton_DoesNotOverlapVisiblePixelsInAnyFrame()
    {
        string xamlPath = FindRepositoryFile("src/RunCatDashboard.App/Views/MainWindow.xaml");
        XDocument document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement closeButton = Assert.Single(
            document.Descendants(presentation + "Button"),
            element => element.Attribute(x + "Name")?.Value == "CatCloseButton");
        Assert.Equal("Right", closeButton.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("Top", closeButton.Attribute("VerticalAlignment")?.Value);

        double buttonWidth = double.Parse(closeButton.Attribute("Width")!.Value);
        double buttonHeight = double.Parse(closeButton.Attribute("Height")!.Value);
        double[] margin = closeButton.Attribute("Margin")!.Value
            .Split(',')
            .Select(double.Parse)
            .ToArray();
        OverlaySizeProfile profile = OverlaySizeProfiles.Get(OverlaySizeMode.CatOnly);
        double viewportWidth = GetCatViewportWidth(profile);
        var buttonBounds = new PixelBounds(
            viewportWidth - margin[2] - buttonWidth,
            margin[1],
            viewportWidth - margin[2],
            margin[1] + buttonHeight);
        IReadOnlyDictionary<string, byte[]> resources = ReadRunCatResources();

        foreach (string resourceName in FrameResourceNames)
        {
            PixelBounds rendered = RenderWithProfile(
                GetAlphaBounds(Decode(resources[resourceName])),
                profile);

            Assert.False(Overlaps(buttonBounds, rendered),
                $"CatOnly Close overlaps visible pixels in {resourceName}.");
        }
    }

    private static IReadOnlyDictionary<string, byte[]> ReadRunCatResources()
    {
        System.Reflection.Assembly appAssembly = typeof(RunCatDashboard.App.App).Assembly;
        Stream resourceStream = appAssembly.GetManifestResourceStream(
            $"{appAssembly.GetName().Name}.g.resources") ??
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

    private static AlphaBounds GetAlphaBounds(DecodedImage image)
    {
        int minX = image.Width;
        int minY = image.Height;
        int maxX = -1;
        int maxY = -1;
        int stride = image.Width * 4;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                if (image.Pixels[(y * stride) + (x * 4) + 3] == 0)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        Assert.True(maxX >= minX && maxY >= minY);
        return new AlphaBounds(minX, minY, maxX, maxY, image.Width, image.Height);
    }

    private static PixelBounds RenderWithProfile(
        AlphaBounds bounds,
        OverlaySizeProfile profile)
    {
        double scale = profile.CatRenderSize / bounds.ImageWidth;
        return new PixelBounds(
            profile.CatRenderOffsetX + (bounds.MinX * scale),
            profile.CatRenderOffsetY + (bounds.MinY * scale),
            profile.CatRenderOffsetX + ((bounds.MaxX + 1) * scale),
            profile.CatRenderOffsetY + ((bounds.MaxY + 1) * scale));
    }

    private static double GetCatViewportWidth(OverlaySizeProfile profile) =>
        profile.Width - 16d - 2d - (2d * profile.ContentPadding);

    private static bool Overlaps(PixelBounds first, PixelBounds second) =>
        first.Left < second.Right &&
        first.Right > second.Left &&
        first.Top < second.Bottom &&
        first.Bottom > second.Top;

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

    private sealed record AlphaBounds(
        int MinX,
        int MinY,
        int MaxX,
        int MaxY,
        int ImageWidth,
        int ImageHeight);

    private sealed record PixelBounds(
        double Left,
        double Top,
        double Right,
        double Bottom);
}
