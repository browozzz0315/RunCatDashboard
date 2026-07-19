using System.Drawing;
using System.IO;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Windowing;

public sealed class TrayIconResourceTests
{
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
                var adapter = new NotifyIconTrayAdapter(
                    new FakeTrayIconResourceLoader(resource));

                Assert.True(adapter.HasAssignedIcon);
                Assert.Equal(0, resource.DisposeCount);

                adapter.RecoverAfterExplorerRestart();

                Assert.True(adapter.HasAssignedIcon);
                Assert.Equal(0, resource.DisposeCount);

                adapter.Dispose();
                adapter.Dispose();

                Assert.Equal(1, resource.DisposeCount);
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
        var adapter = new NotifyIconTrayAdapter(new ThrowingTrayIconResourceLoader());

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
