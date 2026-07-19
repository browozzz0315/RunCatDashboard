using System.Collections.Concurrent;
using RunCatDashboard.App.Interop;
using RunCatDashboard.App.Models;
using RunCatDashboard.App.Services;
using RunCatDashboard.App.ViewModels;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    private static readonly DateTimeOffset SampleTime =
        new(2026, 7, 18, 12, 34, 56, TimeSpan.Zero);

    [Fact]
    public async Task Constructor_SetsClearInitialState()
    {
        await using MainWindowViewModel viewModel = CreateViewModel(
            new SequenceMetricsService(),
            new ControlledDelay());

        Assert.Equal("--", viewModel.CpuUsageText);
        Assert.Equal("--", viewModel.MemoryUsageText);
        Assert.Equal("-- / --", viewModel.UsedAndTotalMemoryText);
        Assert.Equal("--", viewModel.LastUpdatedText);
        Assert.Empty(viewModel.CpuHistory);
        Assert.Empty(viewModel.CpuHistoryNewestFirst);
        Assert.False(viewModel.IsSampling);
        Assert.Equal("Stopped", viewModel.SamplingStatus);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal(OverlayInteractionMode.ClickThrough, viewModel.OverlayMode);
        Assert.Equal("Click-through (pending)", viewModel.OverlayModeText);
        Assert.False(viewModel.HasAppliedOverlayMode);
        Assert.True(viewModel.IsInteractive);
        Assert.False(viewModel.IsOverlayFaulted);
        Assert.Null(viewModel.OverlayErrorMessage);
        Assert.Equal(
            OverlayDisplayPolicy.HideOverFullscreenApps,
            viewModel.RequestedDisplayPolicy);
        Assert.True(viewModel.IsOverlayVisible);
        Assert.True(viewModel.IsOverlayTopmost);
        Assert.Null(viewModel.DisplayPolicyFault);
    }

    [Fact]
    public async Task DisplayPolicySelectionAndAppliedState_AreKeptSeparate()
    {
        await using MainWindowViewModel viewModel = CreateViewModel(
            new SequenceMetricsService(),
            new ControlledDelay());
        OverlayDisplayPolicy? requested = null;
        viewModel.DisplayPolicyRequested += policy => requested = policy;

        viewModel.RequestedDisplayPolicy = OverlayDisplayPolicy.NeverTopmost;
        viewModel.ApplyDisplayPolicyState(new OverlayDisplayPolicyState(
            OverlayDisplayPolicy.NeverTopmost,
            true,
            false,
            true,
            false,
            "foreground diagnostic",
            "overlay monitor diagnostic",
            null));

        Assert.Equal(OverlayDisplayPolicy.NeverTopmost, requested);
        Assert.Equal(OverlayDisplayPolicy.NeverTopmost, viewModel.RequestedDisplayPolicy);
        Assert.True(viewModel.IsOverlayVisible);
        Assert.False(viewModel.IsOverlayTopmost);
        Assert.Equal("Visible / Not topmost", viewModel.AppliedDisplayPolicyText);
        Assert.Equal("Fullscreen detected on another monitor", viewModel.FullscreenDisplayStatusText);
    }

    [Fact]
    public async Task GlobalHotKeyModeTransitions_DoNotChangeSamplingLifecycle()
    {
        var service = new SequenceMetricsService(Snapshot(10d));
        var delay = new ControlledDelay();
        var hotKeyController = new GlobalHotKeyController(new NoOpNativeGlobalHotKeyApi());
        hotKeyController.Register(new nint(1234));
        var overlayController = new FakeOverlayWindowController();
        var coordinator = new OverlayModeCoordinator(overlayController);
        var messageHandler = new OverlayHotKeyMessageHandler(hotKeyController, coordinator);
        await using MainWindowViewModel viewModel = CreateViewModel(
            service,
            delay);
        viewModel.Start();
        await delay.WaitUntilDelayStartsAsync();

        Assert.True(messageHandler.TryHandleMessage(
            GlobalHotKeyController.WindowMessageHotKey,
            new nint(GlobalHotKeyController.HotKeyIdentifier),
            out OverlayWindowState interactiveState));
        viewModel.ApplyOverlayState(interactiveState);
        Assert.True(viewModel.IsSampling);
        Assert.Equal("Sampling", viewModel.SamplingStatus);
        Assert.Equal(1, service.SampleCount);
        Assert.Equal("Interactive", viewModel.OverlayModeText);

        Assert.True(messageHandler.TryHandleMessage(
            GlobalHotKeyController.WindowMessageHotKey,
            new nint(GlobalHotKeyController.HotKeyIdentifier),
            out OverlayWindowState clickThroughState));
        viewModel.ApplyOverlayState(clickThroughState);
        Assert.True(viewModel.IsSampling);
        Assert.Equal(1, service.SampleCount);
        Assert.Equal("Click-through", viewModel.OverlayModeText);
        Assert.Equal(2, overlayController.SetModeCount);
    }

    [Fact]
    public async Task FirstSample_WithNullCpu_ShowsWaitingStateAndDoesNotAddHistory()
    {
        var service = new SequenceMetricsService(
            Snapshot(cpuUsagePercent: null, memoryUsagePercent: 25d));
        var delay = new ControlledDelay();
        await using MainWindowViewModel viewModel = CreateViewModel(service, delay);

        viewModel.Start();
        await delay.WaitUntilDelayStartsAsync();

        Assert.Equal("--", viewModel.CpuUsageText);
        Assert.Equal("25.0%", viewModel.MemoryUsageText);
        Assert.Equal("Waiting for the next CPU sample", viewModel.SamplingStatus);
        Assert.Empty(viewModel.CpuHistory);
    }

    [Fact]
    public async Task ValidSample_UpdatesAllDisplayedValues()
    {
        var snapshot = Snapshot(
            cpuUsagePercent: 42.26d,
            memoryUsagePercent: 75.5d,
            usedBytes: 6UL * 1024 * 1024 * 1024,
            totalBytes: 8UL * 1024 * 1024 * 1024);
        var delay = new ControlledDelay();
        await using MainWindowViewModel viewModel = CreateViewModel(
            new SequenceMetricsService(snapshot),
            delay);

        viewModel.Start();
        await delay.WaitUntilDelayStartsAsync();

        Assert.Equal("42.3%", viewModel.CpuUsageText);
        Assert.Equal("75.5%", viewModel.MemoryUsageText);
        Assert.Equal("6.00 GiB / 8.00 GiB", viewModel.UsedAndTotalMemoryText);
        Assert.Equal(
            SampleTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", System.Globalization.CultureInfo.InvariantCulture),
            viewModel.LastUpdatedText);
        Assert.Equal(snapshot, Assert.Single(viewModel.CpuHistory));
        Assert.Equal("Sampling", viewModel.SamplingStatus);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task CpuHistory_AddsOnlySamplesWithValidCpuInOldestToNewestOrder()
    {
        var firstValid = Snapshot(10d);
        var secondValid = Snapshot(20d);
        var delay = new ControlledDelay();
        await using MainWindowViewModel viewModel = CreateViewModel(
            new SequenceMetricsService(
                Snapshot(null),
                firstValid,
                Snapshot(null),
                secondValid),
            delay);
        int cpuHistoryChangeCount = 0;
        int newestFirstChangeCount = 0;
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(MainWindowViewModel.CpuHistory))
            {
                cpuHistoryChangeCount++;
            }

            if (eventArgs.PropertyName == nameof(MainWindowViewModel.CpuHistoryNewestFirst))
            {
                newestFirstChangeCount++;
            }
        };

        viewModel.Start();
        await AdvanceToNextSampleAsync(delay);
        await AdvanceToNextSampleAsync(delay);
        await AdvanceToNextSampleAsync(delay);
        await delay.WaitUntilDelayStartsAsync();

        Assert.Equal([firstValid, secondValid], viewModel.CpuHistory);
        Assert.Equal([secondValid, firstValid], viewModel.CpuHistoryNewestFirst);
        Assert.Equal(2, cpuHistoryChangeCount);
        Assert.Equal(cpuHistoryChangeCount, newestFirstChangeCount);
    }

    [Fact]
    public async Task CpuHistoryNewestFirst_AboveTwenty_ShowsOnlyNewestTwenty()
    {
        SystemMetricsSnapshot[] snapshots = Enumerable.Range(1, 25)
            .Select(value => Snapshot(value))
            .ToArray();
        var delay = new ControlledDelay();
        await using MainWindowViewModel viewModel = CreateViewModel(
            new SequenceMetricsService(snapshots.Cast<object>().ToArray()),
            delay,
            cpuHistoryCapacity: MainWindowViewModel.DefaultCpuHistoryCapacity);

        viewModel.Start();
        for (int index = 1; index < snapshots.Length; index++)
        {
            await AdvanceToNextSampleAsync(delay);
        }

        await delay.WaitUntilDelayStartsAsync();

        Assert.Equal(
            Enumerable.Range(1, 25).Select(value => (double?)value),
            viewModel.CpuHistory.Select(item => item.CpuUsagePercent));
        Assert.Equal(MainWindowViewModel.DisplayedCpuHistoryCapacity, viewModel.CpuHistoryNewestFirst.Count);
        Assert.Equal(
            Enumerable.Range(6, 20).Reverse().Select(value => (double?)value),
            viewModel.CpuHistoryNewestFirst.Select(item => item.CpuUsagePercent));
    }

    [Fact]
    public async Task CpuHistory_AboveCapacity_RetainsOnlyNewestSamples()
    {
        var delay = new ControlledDelay();
        await using MainWindowViewModel viewModel = CreateViewModel(
            new SequenceMetricsService(Snapshot(10d), Snapshot(20d), Snapshot(30d)),
            delay,
            cpuHistoryCapacity: 2);

        viewModel.Start();
        await AdvanceToNextSampleAsync(delay);
        await AdvanceToNextSampleAsync(delay);
        await delay.WaitUntilDelayStartsAsync();

        Assert.Equal([20d, 30d], viewModel.CpuHistory.Select(item => item.CpuUsagePercent));
    }

    [Theory]
    [InlineData(0UL, "0.00 GiB")]
    [InlineData(536870912UL, "0.50 GiB")]
    [InlineData(1073741824UL, "1.00 GiB")]
    [InlineData(1610612736UL, "1.50 GiB")]
    public void FormatGibibytes_UsesBinaryUnitsAndInvariantTwoDecimalFormat(
        ulong bytes,
        string expected)
    {
        Assert.Equal(expected, MainWindowViewModel.FormatGibibytes(bytes));
    }

    [Fact]
    public async Task Start_WhileAlreadyRunning_DoesNotCreateDuplicateLoop()
    {
        var service = new SequenceMetricsService(Snapshot(10d));
        var delay = new ControlledDelay();
        await using MainWindowViewModel viewModel = CreateViewModel(service, delay);

        Assert.True(viewModel.Start());
        Assert.False(viewModel.Start());
        await delay.WaitUntilDelayStartsAsync();

        Assert.Equal(1, service.SampleCount);
    }

    [Fact]
    public async Task StopAsync_CancelsPendingDelayAndStopsSampling()
    {
        var service = new SequenceMetricsService(Snapshot(10d));
        var delay = new ControlledDelay();
        await using MainWindowViewModel viewModel = CreateViewModel(service, delay);
        viewModel.Start();
        await delay.WaitUntilDelayStartsAsync();

        await viewModel.StopAsync();

        Assert.False(viewModel.IsSampling);
        Assert.Equal("Stopped", viewModel.SamplingStatus);
        Assert.Equal(1, delay.CancellationCount);
        Assert.Equal(1, service.SampleCount);
    }

    [Fact]
    public async Task Cancellation_DuringSample_DoesNotUpdatePropertiesAfterStop()
    {
        var service = new BlockingMetricsService();
        var delay = new ControlledDelay();
        await using MainWindowViewModel viewModel = CreateViewModel(service, delay);
        viewModel.Start();
        await service.WaitUntilSampleStartsAsync();

        await viewModel.StopAsync();

        Assert.Equal("--", viewModel.CpuUsageText);
        Assert.Equal("--", viewModel.MemoryUsageText);
        Assert.Empty(viewModel.CpuHistory);
        Assert.False(viewModel.IsSampling);
        Assert.Equal(1, service.CancellationCount);
    }

    [Fact]
    public async Task SamplingException_SetsUnderstandableErrorAndKeepsLoopRunning()
    {
        var delay = new ControlledDelay();
        await using MainWindowViewModel viewModel = CreateViewModel(
            new SequenceMetricsService(new InvalidOperationException("native read failed")),
            delay);

        viewModel.Start();
        await delay.WaitUntilDelayStartsAsync();

        Assert.True(viewModel.IsSampling);
        Assert.Equal("Sampling error; retrying", viewModel.SamplingStatus);
        Assert.Equal("Sampling failed: native read failed", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task SamplingException_PreservesLastValidValuesAndHistory()
    {
        var validSnapshot = Snapshot(64d, 50d);
        var delay = new ControlledDelay();
        await using MainWindowViewModel viewModel = CreateViewModel(
            new SequenceMetricsService(
                validSnapshot,
                new InvalidOperationException("temporary failure")),
            delay);
        viewModel.Start();
        await AdvanceToNextSampleAsync(delay);
        await delay.WaitUntilDelayStartsAsync();

        Assert.Equal("64.0%", viewModel.CpuUsageText);
        Assert.Equal("50.0%", viewModel.MemoryUsageText);
        Assert.Equal(validSnapshot, Assert.Single(viewModel.CpuHistory));
        Assert.Equal("Sampling failed: temporary failure", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task SamplingException_OnLaterSuccess_RecoversAndClearsError()
    {
        var recoveredSnapshot = Snapshot(35d, 40d);
        var delay = new ControlledDelay();
        await using MainWindowViewModel viewModel = CreateViewModel(
            new SequenceMetricsService(
                new InvalidOperationException("temporary failure"),
                recoveredSnapshot),
            delay);
        viewModel.Start();
        await AdvanceToNextSampleAsync(delay);
        await delay.WaitUntilDelayStartsAsync();

        Assert.Equal("35.0%", viewModel.CpuUsageText);
        Assert.Equal("Sampling", viewModel.SamplingStatus);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal(recoveredSnapshot, Assert.Single(viewModel.CpuHistory));
    }

    [Fact]
    public async Task StopAndDispose_CanBeCalledRepeatedly()
    {
        var delay = new ControlledDelay();
        var viewModel = CreateViewModel(
            new SequenceMetricsService(Snapshot(10d)),
            delay);
        viewModel.Start();
        await delay.WaitUntilDelayStartsAsync();

        await viewModel.StopAsync();
        await viewModel.StopAsync();
        await viewModel.DisposeAsync();
        await viewModel.DisposeAsync();

        Assert.False(viewModel.IsSampling);
        Assert.Throws<ObjectDisposedException>(() => viewModel.Start());
    }

    private static MainWindowViewModel CreateViewModel(
        ISystemMetricsService service,
        ControlledDelay delay,
        int cpuHistoryCapacity = 3)
    {
        return new MainWindowViewModel(
            service,
            new ImmediateUiDispatcher(),
            cpuHistoryCapacity,
            TimeSpan.FromSeconds(1),
            delay.DelayAsync);
    }

    private static SystemMetricsSnapshot Snapshot(
        double? cpuUsagePercent,
        double memoryUsagePercent = 50d,
        ulong usedBytes = 4UL * 1024 * 1024 * 1024,
        ulong totalBytes = 8UL * 1024 * 1024 * 1024)
    {
        return new SystemMetricsSnapshot(
            SampleTime,
            cpuUsagePercent,
            memoryUsagePercent,
            usedBytes,
            totalBytes);
    }

    private static async Task AdvanceToNextSampleAsync(ControlledDelay delay)
    {
        await delay.WaitUntilDelayStartsAsync();
        delay.ReleaseNextDelay();
    }

    private sealed class ImmediateUiDispatcher : IUiDispatcher
    {
        public ValueTask InvokeAsync(
            Action action,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoOpNativeGlobalHotKeyApi : INativeGlobalHotKeyApi
    {
        public void Register(nint windowHandle, int identifier, uint modifiers, uint virtualKey)
        {
        }

        public void Unregister(nint windowHandle, int identifier)
        {
        }
    }

    private sealed class FakeOverlayWindowController : IOverlayWindowController
    {
        public OverlayWindowState State { get; private set; } = new(
            OverlayInteractionMode.ClickThrough,
            OverlayInteractionMode.ClickThrough,
            true,
            false,
            null);

        public bool IsInitialized => true;

        internal int SetModeCount { get; private set; }

        public void Initialize(nint windowHandle)
        {
        }

        public bool SetMode(OverlayInteractionMode mode)
        {
            SetModeCount++;
            State = new OverlayWindowState(mode, mode, true, false, null);
            return true;
        }

        public void Close()
        {
        }
    }

    private sealed class SequenceMetricsService : ISystemMetricsService
    {
        private readonly ConcurrentQueue<object> _results;

        internal SequenceMetricsService(params object[] results)
        {
            _results = new ConcurrentQueue<object>(results);
        }

        internal int SampleCount { get; private set; }

        public ValueTask<SystemMetricsSnapshot> SampleAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SampleCount++;

            if (!_results.TryDequeue(out object? result))
            {
                throw new InvalidOperationException("No configured sample remains.");
            }

            return result switch
            {
                SystemMetricsSnapshot snapshot => ValueTask.FromResult(snapshot),
                Exception exception => ValueTask.FromException<SystemMetricsSnapshot>(exception),
                _ => throw new InvalidOperationException("Unsupported fake result.")
            };
        }
    }

    private sealed class BlockingMetricsService : ISystemMetricsService
    {
        private readonly TaskCompletionSource _sampleStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int CancellationCount { get; private set; }

        public async ValueTask<SystemMetricsSnapshot> SampleAsync(
            CancellationToken cancellationToken = default)
        {
            _sampleStarted.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationCount++;
                throw;
            }

            throw new InvalidOperationException("The blocking sample should be cancelled.");
        }

        internal Task WaitUntilSampleStartsAsync()
        {
            return _sampleStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class ControlledDelay
    {
        private readonly ConcurrentQueue<TaskCompletionSource> _pendingDelays = new();
        private readonly SemaphoreSlim _delayStarted = new(0);

        internal int CancellationCount { get; private set; }

        internal async Task DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingDelays.Enqueue(completion);
            _delayStarted.Release();

            try
            {
                await completion.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationCount++;
                throw;
            }
        }

        internal Task WaitUntilDelayStartsAsync()
        {
            return _delayStarted.WaitAsync(TimeSpan.FromSeconds(5));
        }

        internal void ReleaseNextDelay()
        {
            Assert.True(_pendingDelays.TryDequeue(out TaskCompletionSource? completion));
            completion.SetResult();
        }
    }
}
