using System.Collections.Concurrent;
using System.IO;
using RunCatDashboard.App.Settings;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Settings;

public sealed class SettingsServiceTests
{
    [Fact]
    public async Task Debounce_CoalescesRapidUpdatesIntoOneWrite()
    {
        var store = new RecordingStore();
        var delay = new ControlledDelay();
        await using var service = new SettingsService(store, delay.DelayAsync);
        await service.LoadAsync();

        service.Update(s => s with { Metrics = new MetricsSettings(250) });
        service.Update(s => s with { Metrics = new MetricsSettings(500) });
        service.Update(s => s with { Metrics = new MetricsSettings(2000) });
        delay.ReleaseLatest();
        await store.WaitForSaveAsync();

        Assert.Single(store.Saves);
        Assert.Equal(2000, store.Saves.Single().Metrics.SamplingIntervalMilliseconds);
    }

    [Fact]
    public async Task Flush_CancelsDebounceAndPersistsLatestRevision()
    {
        var store = new RecordingStore();
        var delay = new ControlledDelay();
        await using var service = new SettingsService(store, delay.DelayAsync);
        await service.LoadAsync();
        service.Update(s => s with { Window = s.Window with { IsDashboardVisible = false } });

        await service.FlushAsync();

        Assert.False(Assert.Single(store.Saves).Window.IsDashboardVisible);
    }

    [Fact]
    public async Task LaterSettingsUpdate_PreservesSavedInteractionHotKey()
    {
        var store = new RecordingStore();
        await using var service = new SettingsService(store);
        await service.LoadAsync();
        var gesture = new OverlayHotKeyGesture(
            true, false, true, true, OverlayHotKeyKey.F10);
        service.Update(settings => settings with
        {
            Overlay = settings.Overlay with { InteractionHotKey = gesture }
        });
        await service.FlushAsync();

        service.Update(settings => settings with
        {
            Metrics = new MetricsSettings(500)
        });
        await service.FlushAsync();

        Assert.Equal(gesture, service.Current.Overlay.InteractionHotKey);
        Assert.Equal(gesture, store.Saves.Last().Overlay.InteractionHotKey);
    }

    [Fact]
    public async Task TryReplaceCurrent_SaveFails_DoesNotPublishRuntimeSnapshot()
    {
        await using var service = new SettingsService(new ThrowingStore());
        await service.LoadAsync();
        AppSettings original = service.Current;
        AppSettings replacement = original with
        {
            Window = original.Window with { IsDashboardVisible = false }
        };

        bool saved = await service.TryReplaceCurrentAsync(_ => replacement);

        Assert.False(saved);
        Assert.Equal(original, service.Current);
        Assert.Equal("設定無法保存，設定未變更。", service.LastDiagnostic);
    }

    [Fact]
    public async Task ConcurrentUpdatesAndFlushes_ProduceWholeSnapshotsWithoutInterleaving()
    {
        var store = new RecordingStore();
        await using var service = new SettingsService(store);
        await service.LoadAsync();

        await Task.WhenAll(Enumerable.Range(0, 25).Select(async index =>
        {
            service.Update(s => s with
            {
                Window = new WindowSettings(index, -index, index % 2 == 0)
            });
            await service.FlushAsync();
        }));

        Assert.NotEmpty(store.Saves);
        Assert.All(store.Saves, saved =>
        {
            Assert.True(saved.Window.Left.HasValue);
            Assert.Equal(-saved.Window.Left!.Value, saved.Window.Top);
        });
        Assert.Equal(service.Current, store.Saves.Last());
    }

    [Fact]
    public Task TransactionalReplacement_WaitsForWriteGateAndPreservesWindowPosition() =>
        VerifyUpdateWhileWaitingForWriteGateAsync(
            settings => settings with
            {
                Window = settings.Window with { Left = 321.5, Top = -42.25 }
            },
            settings =>
            {
                Assert.Equal(321.5, settings.Window.Left);
                Assert.Equal(-42.25, settings.Window.Top);
            });

    [Fact]
    public Task TransactionalReplacement_WaitsForWriteGateAndPreservesVisibility() =>
        VerifyUpdateWhileWaitingForWriteGateAsync(
            settings => settings with
            {
                Window = settings.Window with { IsDashboardVisible = false }
            },
            settings => Assert.False(settings.Window.IsDashboardVisible));

    [Fact]
    public Task TransactionalReplacement_WaitsForWriteGateAndPreservesInteractionMode() =>
        VerifyUpdateWhileWaitingForWriteGateAsync(
            settings => settings with
            {
                Overlay = settings.Overlay with
                {
                    InteractionMode = OverlayInteractionMode.Interactive
                }
            },
            settings => Assert.Equal(
                OverlayInteractionMode.Interactive,
                settings.Overlay.InteractionMode));

    [Fact]
    public async Task TransactionalReplacement_RevisionChangesDuringPrepare_RetriesWithoutCommittingStaleCandidate()
    {
        var store = new ControlledStore();
        var delay = new ControlledDelay();
        await using var service = new SettingsService(store, delay.DelayAsync);
        await service.LoadAsync();
        var visibilityGesture = new OverlayHotKeyGesture(
            true, false, true, true, OverlayHotKeyKey.F9);
        store.BlockNextPrepare();

        Task<bool> replacement = service.TryReplaceCurrentAsync(settings => settings with
        {
            Window = settings.Window with { VisibilityHotKey = visibilityGesture }
        });
        await store.WaitForPrepareAsync();
        service.Update(settings => settings with
        {
            Window = settings.Window with { Left = 88.5, Top = -17.25 }
        });
        store.ReleasePrepare();

        Assert.True(await replacement);
        await service.FlushAsync();

        Assert.Equal(2, store.Prepared.Count);
        Assert.Single(store.Committed);
        Assert.Equal(service.Current, store.Committed.Single());
        Assert.Equal(visibilityGesture, service.Current.Window.VisibilityHotKey);
        Assert.Equal(88.5, service.Current.Window.Left);
        Assert.Equal(-17.25, service.Current.Window.Top);
    }

    [Fact]
    public async Task TransactionalReplacement_IncludesPendingDebounceRevisionBeforeMarkingSaved()
    {
        var store = new ControlledStore();
        var delay = new ControlledDelay();
        await using var service = new SettingsService(store, delay.DelayAsync);
        await service.LoadAsync();
        store.BlockNextPrepare();

        Task<bool> replacement = service.TryReplaceCurrentAsync(settings => settings with
        {
            Overlay = settings.Overlay with
            {
                InteractionHotKey = new OverlayHotKeyGesture(
                    true, false, true, false, OverlayHotKeyKey.F8)
            }
        });
        await store.WaitForPrepareAsync();
        service.Update(settings => settings with
        {
            Window = settings.Window with { IsDashboardVisible = false }
        });
        store.ReleasePrepare();

        Assert.True(await replacement);
        await service.FlushAsync();

        Assert.Single(store.Committed);
        Assert.Equal(service.Current, store.Committed.Single());
        Assert.False(store.Committed.Single().Window.IsDashboardVisible);
        Assert.Equal(OverlayHotKeyKey.F8,
            store.Committed.Single().Overlay.InteractionHotKey!.Key);
    }

    [Fact]
    public async Task DebouncePersistenceFailure_ReportsRuntimeAppliedButNotPersisted()
    {
        await using var service = new SettingsService(new ThrowingStore());
        await service.LoadAsync();

        service.Update(settings => settings with
        {
            Window = settings.Window with { IsDashboardVisible = false }
        });
        await service.FlushAsync();

        Assert.False(service.Current.Window.IsDashboardVisible);
        Assert.Equal(
            "設定已在目前執行期間套用，但無法寫入設定檔，重新啟動後可能不會保留。",
            service.LastDiagnostic);
    }

    private static async Task VerifyUpdateWhileWaitingForWriteGateAsync(
        Func<AppSettings, AppSettings> concurrentUpdate,
        Action<AppSettings> assertConcurrentUpdate)
    {
        var store = new ControlledStore();
        var delay = new ControlledDelay();
        await using var service = new SettingsService(store, delay.DelayAsync);
        await service.LoadAsync();
        service.Update(settings => settings with { Metrics = new MetricsSettings(500) });
        store.BlockNextSave();
        Task flush = service.FlushAsync();
        await store.WaitForSaveAsync();

        var visibilityGesture = new OverlayHotKeyGesture(
            true, false, true, true, OverlayHotKeyKey.F9);
        Task<bool> replacement = service.TryReplaceCurrentAsync(settings => settings with
        {
            Window = settings.Window with { VisibilityHotKey = visibilityGesture }
        });
        service.Update(concurrentUpdate);
        store.ReleaseSave();

        await flush;
        Assert.True(await replacement);
        await service.FlushAsync();

        assertConcurrentUpdate(service.Current);
        Assert.Equal(visibilityGesture, service.Current.Window.VisibilityHotKey);
        Assert.Equal(service.Current, store.Committed.Last());
    }

    private sealed class RecordingStore : ISettingsStore
    {
        private readonly SemaphoreSlim _saved = new(0);
        internal ConcurrentQueue<AppSettings> Saves { get; } = new();
        public Task<SettingsLoadResult> LoadAsync(CancellationToken token = default) =>
            Task.FromResult(new SettingsLoadResult(AppSettings.Defaults, null));
        public Task SaveAsync(AppSettings settings, CancellationToken token = default)
        {
            Saves.Enqueue(settings);
            _saved.Release();
            return Task.CompletedTask;
        }
        public Task<IPreparedSettingsWrite> PrepareSaveAsync(
            AppSettings settings,
            CancellationToken token = default) =>
            Task.FromResult<IPreparedSettingsWrite>(new PreparedWrite(() =>
            {
                Saves.Enqueue(settings);
                _saved.Release();
            }));
        internal Task WaitForSaveAsync() => _saved.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class ThrowingStore : ISettingsStore
    {
        public Task<SettingsLoadResult> LoadAsync(CancellationToken token = default) =>
            Task.FromResult(new SettingsLoadResult(AppSettings.Defaults, null));

        public Task SaveAsync(AppSettings settings, CancellationToken token = default) =>
            throw new IOException("configured save failure");

        public Task<IPreparedSettingsWrite> PrepareSaveAsync(
            AppSettings settings,
            CancellationToken token = default) =>
            throw new IOException("configured save failure");
    }

    private sealed class ControlledStore : ISettingsStore
    {
        private readonly TaskCompletionSource _saveEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseSave =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _prepareEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releasePrepare =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _blockSave;
        private int _blockPrepare;

        internal ConcurrentQueue<AppSettings> Prepared { get; } = new();
        internal ConcurrentQueue<AppSettings> Committed { get; } = new();

        public Task<SettingsLoadResult> LoadAsync(CancellationToken token = default) =>
            Task.FromResult(new SettingsLoadResult(AppSettings.Defaults, null));

        public async Task SaveAsync(AppSettings settings, CancellationToken token = default)
        {
            if (Interlocked.Exchange(ref _blockSave, 0) == 1)
            {
                _saveEntered.TrySetResult();
                await _releaseSave.Task.WaitAsync(token);
            }
            Committed.Enqueue(settings);
        }

        public async Task<IPreparedSettingsWrite> PrepareSaveAsync(
            AppSettings settings,
            CancellationToken token = default)
        {
            Prepared.Enqueue(settings);
            if (Interlocked.Exchange(ref _blockPrepare, 0) == 1)
            {
                _prepareEntered.TrySetResult();
                await _releasePrepare.Task.WaitAsync(token);
            }
            return new PreparedWrite(() => Committed.Enqueue(settings));
        }

        internal void BlockNextSave() => Interlocked.Exchange(ref _blockSave, 1);
        internal Task WaitForSaveAsync() => _saveEntered.Task;
        internal void ReleaseSave() => _releaseSave.TrySetResult();
        internal void BlockNextPrepare() => Interlocked.Exchange(ref _blockPrepare, 1);
        internal Task WaitForPrepareAsync() => _prepareEntered.Task;
        internal void ReleasePrepare() => _releasePrepare.TrySetResult();
    }

    private sealed class PreparedWrite(Action commit) : IPreparedSettingsWrite
    {
        public void Commit() => commit();
        public void Dispose() { }
    }

    private sealed class ControlledDelay
    {
        private readonly object _gate = new();
        private TaskCompletionSource? _latest;
        internal Task DelayAsync(TimeSpan delay, CancellationToken token)
        {
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate) _latest = source;
            return source.Task.WaitAsync(token);
        }
        internal void ReleaseLatest()
        {
            TaskCompletionSource? source;
            SpinWait.SpinUntil(() => { lock (_gate) return _latest is not null; }, 5000);
            lock (_gate) source = _latest;
            source!.TrySetResult();
        }
    }
}
