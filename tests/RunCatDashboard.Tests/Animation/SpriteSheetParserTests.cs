using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RunCatDashboard.App.Animation;

namespace RunCatDashboard.Tests.Animation;

public sealed class SpriteSheetParserTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "RunCatDashboard.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ValidPng_ProducesFrozenEqualWidthFramesWithNaturalNames()
    {
        Directory.CreateDirectory(_directory);
        string path = CreatePng("source.PNG", 12, 4);

        AnimationImportPreview preview = new SpriteSheetParser().Parse(path, 3);

        Assert.Equal(12, preview.SourceWidth);
        Assert.Equal(4, preview.SourceHeight);
        Assert.Equal(4, preview.FrameWidth);
        Assert.Equal(4, preview.FrameHeight);
        Assert.Equal(3, preview.Frames.Count);
        Assert.All(preview.Frames, frame => Assert.True(frame.IsFrozen));
        Assert.Equal(["frame-000.png", "frame-001.png", "frame-002.png"],
            Enumerable.Range(0, preview.FrameCount)
                .Select(AnimationLibraryStorage.GetFrameFileName));
    }

    [Fact]
    public void InvalidExtensionAndInvalidPngContentAreValidationErrors()
    {
        Directory.CreateDirectory(_directory);
        string textPath = Path.Combine(_directory, "not-image.txt");
        File.WriteAllText(textPath, "not a png");
        string invalidPngPath = Path.Combine(_directory, "invalid.png");
        File.WriteAllText(invalidPngPath, "not a png");
        var parser = new SpriteSheetParser();

        Assert.Throws<AnimationValidationException>(() => parser.Parse(textPath, 1));
        Assert.Throws<AnimationValidationException>(() => parser.Parse(invalidPngPath, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65)]
    public void InvalidFrameCount_IsRejected(int frameCount)
    {
        Directory.CreateDirectory(_directory);
        string path = CreatePng("source.png", 8, 8);

        Assert.Throws<AnimationValidationException>(
            () => new SpriteSheetParser().Parse(path, frameCount));
    }

    [Fact]
    public void WidthNotDivisibleByFrameCount_IsRejected()
    {
        Directory.CreateDirectory(_directory);
        string path = CreatePng("source.png", 10, 4);

        Assert.Throws<AnimationValidationException>(
            () => new SpriteSheetParser().Parse(path, 3));
    }

    [Fact]
    public void AlphaChannel_IsPreservedInCroppedFrame()
    {
        Directory.CreateDirectory(_directory);
        string path = CreatePng(
            "alpha.png",
            2,
            1,
            [0, 0, 255, 0, 0, 255, 0, 255]);

        AnimationImportPreview preview = new SpriteSheetParser().Parse(path, 1);
        byte[] pixels = new byte[8];
        preview.Frames[0].CopyPixels(pixels, 8, 0);

        Assert.Equal(0, pixels[3]);
        Assert.Equal(255, pixels[7]);
    }

    [Fact]
    public void DimensionAndPixelBudgetLimitsAreRejected()
    {
        Directory.CreateDirectory(_directory);
        string frameDimensionPath = CreatePng("large-frame.png", 1025, 1);
        string pixelBudgetPath = CreatePng("large-budget.png", 4096, 2049);
        var parser = new SpriteSheetParser();

        Assert.Throws<AnimationValidationException>(
            () => parser.Parse(frameDimensionPath, 1));
        Assert.Throws<AnimationValidationException>(
            () => parser.Parse(pixelBudgetPath, 1));
    }

    private string CreatePng(
        string fileName,
        int width,
        int height,
        byte[]? pixels = null)
    {
        byte[] data = pixels ?? Enumerable.Repeat((byte)255, width * height * 4).ToArray();
        var source = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            data,
            width * 4);
        source.Freeze();
        string path = Path.Combine(_directory, fileName);
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(stream);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
