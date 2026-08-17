using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RunCatDashboard.App.Animation;
using RunCatDashboard.App.Settings;

namespace RunCatDashboard.Tests.Animation;

public sealed class AnimationLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "RunCatDashboard.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Import_WritesManifestAndNormalizedFramesAndRefreshesCatalog()
    {
        Directory.CreateDirectory(_root);
        string sourcePath = CreatePng("source.png", 8, 4);
        var storage = new AnimationLibraryStorage(Path.Combine(_root, "Animations"));
        var catalog = new AnimationCatalog(storage);
        var importer = new AnimationImportService(new SpriteSheetParser(), storage, catalog);

        AnimationCatalogEntry entry = importer.Import(sourcePath, 2, "My Cat");

        Assert.True(entry.IsValid);
        Assert.False(entry.IsBuiltIn);
        Assert.Equal("My Cat", entry.DisplayName);
        Assert.Equal(2, entry.FrameCount);
        Assert.Equal("custom-", entry.Id[..7]);
        string directory = Assert.IsType<string>(entry.PhysicalDirectory);
        Assert.True(File.Exists(Path.Combine(directory, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(directory, "frame-000.png")));
        Assert.True(File.Exists(Path.Combine(directory, "frame-001.png")));
        Assert.Empty(Directory.EnumerateDirectories(storage.AnimationsDirectory, ".import-*.tmp"));
        Assert.Contains(catalog.Entries, item => item.Id == entry.Id && item.IsValid);
    }

    [Fact]
    public void Import_DuplicateDisplayNameIsRejectedWithoutAddingSecondEntry()
    {
        Directory.CreateDirectory(_root);
        string sourcePath = CreatePng("source.png", 8, 4);
        var storage = new AnimationLibraryStorage(Path.Combine(_root, "Animations"));
        var catalog = new AnimationCatalog(storage);
        var importer = new AnimationImportService(new SpriteSheetParser(), storage, catalog);
        importer.Import(sourcePath, 2, "My Cat");

        Assert.Throws<AnimationValidationException>(
            () => importer.Import(sourcePath, 2, " my cat "));
        Assert.Single(catalog.Entries, entry =>
            !entry.IsBuiltIn && entry.IsValid && entry.DisplayName == "My Cat");
    }

    [Fact]
    public void ImportFailure_CleansTemporaryDirectory()
    {
        Directory.CreateDirectory(_root);
        string sourcePath = Path.Combine(_root, "invalid.png");
        File.WriteAllText(sourcePath, "invalid");
        var storage = new AnimationLibraryStorage(Path.Combine(_root, "Animations"));
        var catalog = new AnimationCatalog(storage);
        var importer = new AnimationImportService(new SpriteSheetParser(), storage, catalog);

        Assert.Throws<AnimationValidationException>(
            () => importer.Import(sourcePath, 1, "Broken"));
        Assert.False(Directory.Exists(storage.AnimationsDirectory));
    }

    [Fact]
    public void Catalog_MarksMissingFrameCorruptAndResolveFallsBackToBuiltIn()
    {
        Directory.CreateDirectory(_root);
        var storage = new AnimationLibraryStorage(Path.Combine(_root, "Animations"));
        string directory = Path.Combine(storage.AnimationsDirectory, "custom-0123456789abcdef0123456789abcdef");
        Directory.CreateDirectory(directory);
        var manifest = new AnimationManifest(
            1,
            Path.GetFileName(directory),
            "Broken",
            "png",
            2,
            4,
            4,
            250);
        File.WriteAllText(
            Path.Combine(directory, "manifest.json"),
            JsonSerializer.Serialize(manifest));
        WritePng(Path.Combine(directory, "frame-000.png"), 4, 4);

        var catalog = new AnimationCatalog(storage);
        catalog.Refresh();
        AnimationCatalogEntry corrupt = Assert.Single(
            catalog.Entries, entry => entry.Id == manifest.AnimationId);
        AnimationResolution resolution = catalog.Resolve(manifest.AnimationId);

        Assert.False(corrupt.IsValid);
        Assert.True(resolution.UsedFallback);
        Assert.Equal(AnimationSettings.BuiltInDefaultAnimationId, resolution.Entry.Id);
        Assert.Equal("CorruptSelectedAnimation", resolution.DiagnosticCategory);
        Assert.Contains(catalog.Entries, entry => entry.IsBuiltIn);
    }

    [Fact]
    public void Publish_DestinationCollisionDoesNotOverwriteOrDeleteTemporaryData()
    {
        Directory.CreateDirectory(_root);
        var storage = new AnimationLibraryStorage(Path.Combine(_root, "Animations"));
        string id = "custom-0123456789abcdef0123456789abcdef";
        Directory.CreateDirectory(Path.Combine(storage.AnimationsDirectory, id));
        string temporary = storage.CreateTemporaryDirectory();

        Assert.Throws<IOException>(() => storage.Publish(temporary, id));
        Assert.True(Directory.Exists(temporary));
        Assert.True(Directory.Exists(Path.Combine(storage.AnimationsDirectory, id)));
    }

    private string CreatePng(string fileName, int width, int height)
    {
        string path = Path.Combine(_root, fileName);
        WritePng(path, width, height);
        return path;
    }

    private static void WritePng(string path, int width, int height)
    {
        byte[] pixels = Enumerable.Repeat((byte)255, width * height * 4).ToArray();
        BitmapSource source = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        source.Freeze();
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(stream);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
