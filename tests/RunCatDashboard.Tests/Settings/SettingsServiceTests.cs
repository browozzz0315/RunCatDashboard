using System.Collections.Concurrent;
using RunCatDashboard.App.Settings;

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
        internal Task WaitForSaveAsync() => _saved.WaitAsync(TimeSpan.FromSeconds(5));
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
