using System.Drawing;
using System.IO;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Windowing;

public sealed class TrayIconResourceTests
{
    private static readonly byte[] ExpectedAnimationSizes = [16, 20, 24, 32];

    [Fact]
    public void AssemblyResource_LoadsAsMultiSizeIcon()
    {
        var loader = new AssemblyTrayIconResourceLoader();

        using Stream stream = typeof(AssemblyTrayIconResourceLoader).Assembly
            .GetManifestResourceStream(AssemblyTrayIconResourceLoader.ResourceName)!;
        using var reader = new BinaryReader(stream);
        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        Assert.Equal(2, reader.ReadUInt16());
        byte firstWidth = reader.ReadByte();
        byte firstHeight = reader.ReadByte();
        reader.BaseStream.Position += 14;
        byte secondWidth = reader.ReadByte();
        byte secondHeight = reader.ReadByte();

        using ITrayIconResource resource = loader.Load();
        using var small = new Icon(resource.Icon, new Size(16, 16));
        using var medium = new Icon(resource.Icon, new Size(32, 32));

        Assert.Equal(16, firstWidth);
        Assert.Equal(16, firstHeight);
        Assert.Equal(32, secondWidth);
        Assert.Equal(32, secondHeight);
        Assert.Equal(new Size(16, 16), small.Size);
        Assert.Equal(new Size(32, 32), medium.Size);
    }

    [Fact]
    public void NotifyIcon_KeepsOwnedIconThroughRecoveryAndDisposesItOnce()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var resource = new FakeTrayIconResource();
                var animationResources = Enumerable.Range(0, 8)
                    .Select(_ => new FakeTrayIconResource())
                    .ToArray();
                var adapter = new NotifyIconTrayAdapter(
                    new FakeTrayIconResourceLoader(resource),
                    new FakeTrayAnimationIconResourceLoader(animationResources));

                Assert.True(adapter.HasAssignedIcon);
                Assert.Equal(0, resource.DisposeCount);

                adapter.RecoverAfterExplorerRestart();

                Assert.True(adapter.HasAssignedIcon);
                Assert.Equal(0, resource.DisposeCount);

                adapter.Dispose();
                adapter.Dispose();

                Assert.Equal(1, resource.DisposeCount);
                Assert.All(
                    animationResources,
                    animationResource => Assert.Equal(1, animationResource.DisposeCount));
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
    public void NotifyIcon_WhenIconLoadFails_ReportsFailureInsteadOfShowingBlankIcon()
    {
        var adapter = new NotifyIconTrayAdapter(
            new ThrowingTrayIconResourceLoader(),
            new FakeTrayAnimationIconResourceLoader([]));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            adapter.Show);

        Assert.Contains("載入 RunCatDashboard 系統匣圖示失敗", exception.Message);
        adapter.Dispose();
    }

    private sealed class FakeTrayIconResourceLoader(ITrayIconResource resource)
        : ITrayIconResourceLoader
    {
        public ITrayIconResource Load() => resource;
    }

    private sealed class ThrowingTrayIconResourceLoader : ITrayIconResourceLoader
    {
        public ITrayIconResource Load() =>
            throw new InvalidOperationException("configured icon failure");
    }

    [Fact]
    public void AnimationAssemblyResources_ContainEightTransparentMultiSizeIcons()
    {
        var loader = new AssemblyTrayAnimationIconResourceLoader();
        using var resources = new DisposableResources(loader.LoadFrames());

        Assert.Equal(8, resources.Items.Count);
        for (int frameIndex = 0; frameIndex < resources.Items.Count; frameIndex++)
        {
            string resourceName =
                $"{AssemblyTrayAnimationIconResourceLoader.ResourceNamePrefix}{frameIndex + 1:D2}.ico";
            using Stream stream = typeof(AssemblyTrayAnimationIconResourceLoader).Assembly
                .GetManifestResourceStream(resourceName)!;
            using var reader = new BinaryReader(stream);
            Assert.Equal(0, reader.ReadUInt16());
            Assert.Equal(1, reader.ReadUInt16());
            Assert.Equal(ExpectedAnimationSizes.Length, reader.ReadUInt16());

            var entries = new List<(byte Size, uint Length, uint Offset)>();
            for (int sizeIndex = 0; sizeIndex < ExpectedAnimationSizes.Length; sizeIndex++)
            {
                byte size = reader.ReadByte();
                Assert.Equal(size, reader.ReadByte());
                reader.BaseStream.Position += 6;
                uint length = reader.ReadUInt32();
                uint offset = reader.ReadUInt32();
                entries.Add((size, length, offset));
            }

            Assert.Equal(ExpectedAnimationSizes, entries.Select(entry => entry.Size));
            foreach ((byte _, uint length, uint offset) in entries)
            {
                reader.BaseStream.Position = offset;
                byte[] pngBytes = reader.ReadBytes(checked((int)length));
                using var pngStream = new MemoryStream(pngBytes);
                using var bitmap = new Bitmap(pngStream);
                bool hasTransparentPixel = false;
                bool hasVisiblePixel = false;
                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        byte alpha = bitmap.GetPixel(x, y).A;
                        hasTransparentPixel |= alpha == 0;
                        hasVisiblePixel |= alpha > 0;
                    }
                }

                Assert.True(hasTransparentPixel);
                Assert.True(hasVisiblePixel);
            }

            using var icon16 = new Icon(resources.Items[frameIndex].Icon, new Size(16, 16));
            using var icon20 = new Icon(resources.Items[frameIndex].Icon, new Size(20, 20));
            using var icon24 = new Icon(resources.Items[frameIndex].Icon, new Size(24, 24));
            using var icon32 = new Icon(resources.Items[frameIndex].Icon, new Size(32, 32));
            Assert.Equal(new Size(16, 16), icon16.Size);
            Assert.Equal(new Size(20, 20), icon20.Size);
            Assert.Equal(new Size(24, 24), icon24.Size);
            Assert.Equal(new Size(32, 32), icon32.Size);
        }
    }

    [Fact]
    public void NotifyIcon_WhenAnimationLoadFails_KeepsStaticFallbackAvailable()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var staticResource = new FakeTrayIconResource();
                using var adapter = new NotifyIconTrayAdapter(
                    new FakeTrayIconResourceLoader(staticResource),
                    new ThrowingTrayAnimationIconResourceLoader());

                Assert.False(adapter.CanUseAnimatedIcons);
                Assert.Contains("configured animation failure", adapter.AnimationIconLoadError);
                adapter.SetStaticIcon();
                Assert.True(adapter.HasAssignedIcon);
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

    private sealed class FakeTrayAnimationIconResourceLoader(
        IReadOnlyList<ITrayIconResource> resources)
        : ITrayAnimationIconResourceLoader
    {
        public IReadOnlyList<ITrayIconResource> LoadFrames() => resources;
    }

    private sealed class ThrowingTrayAnimationIconResourceLoader
        : ITrayAnimationIconResourceLoader
    {
        public IReadOnlyList<ITrayIconResource> LoadFrames() =>
            throw new InvalidOperationException("configured animation failure");
    }

    private sealed class DisposableResources(IReadOnlyList<ITrayIconResource> items)
        : IDisposable
    {
        internal IReadOnlyList<ITrayIconResource> Items { get; } = items;

        public void Dispose()
        {
            foreach (ITrayIconResource item in Items)
            {
                item.Dispose();
            }
        }
    }

    private sealed class FakeTrayIconResource : ITrayIconResource
    {
        public Icon Icon { get; } = (Icon)SystemIcons.Warning.Clone();
        internal int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            if (DisposeCount == 1)
            {
                Icon.Dispose();
            }
        }
    }
}
