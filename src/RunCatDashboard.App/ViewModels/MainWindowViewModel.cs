using System.Globalization;
using System.ComponentModel;
using System.Threading.Channels;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using RunCatDashboard.App.Animation;
using RunCatDashboard.App.Collections;
using RunCatDashboard.App.Diagnostics;
using RunCatDashboard.App.Models;
using RunCatDashboard.App.Services;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    internal const int DefaultCpuHistoryCapacity = 30;
    internal const int DisplayedCpuHistoryCapacity = 20;
    internal static readonly TimeSpan DefaultSamplingInterval = TimeSpan.FromSeconds(1);

    private readonly ISystemMetricsService _systemMetricsService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IRunCatAnimationController _animationController;
    private readonly BoundedHistory<SystemMetricsSnapshot> _cpuHistoryBuffer;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly FaultEpisodeTracker _samplingFaultEpisode = new();
    private long _samplingIntervalTicks;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly Channel<bool> _intervalChanges = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _samplingCancellationSource;
    private Task? _samplingTask;
    private bool _isDisposed;

    [ObservableProperty]
    private string _cpuUsageText = "--";

    [ObservableProperty]
    private string _memoryUsageText = "--";

    [ObservableProperty]
    private string _usedAndTotalMemoryText = "-- / --";

    [ObservableProperty]
    private string _lastUpdatedText = "--";

    [ObservableProperty]
    private IReadOnlyList<SystemMetricsSnapshot> _cpuHistory =
        Array.Empty<SystemMetricsSnapshot>();

    private IReadOnlyList<SystemMetricsSnapshot> _cpuHistoryNewestFirst =
        Array.Empty<SystemMetricsSnapshot>();

    [ObservableProperty]
    private bool _isSampling;

    [ObservableProperty]
    private string _samplingStatus = "Stopped";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private int _animationFrameIndex;

    [ObservableProperty]
    private bool _isAnimationRunning;

    [ObservableProperty]
    private string? _animationErrorMessage;

    [ObservableProperty]
    private OverlayInteractionMode _overlayMode = OverlayInteractionMode.ClickThrough;

    [ObservableProperty]
    private bool _hasAppliedOverlayMode;

    [ObservableProperty]
    private bool _isOverlayFaulted;

    [ObservableProperty]
    private string? _overlayErrorMessage;

    [ObservableProperty]
    private string? _hotKeyErrorMessage;

    [ObservableProperty]
    private string? _trayErrorMessage;

    [ObservableProperty]
    private OverlayDisplayPolicy _requestedDisplayPolicy =
        OverlayDisplayPolicy.HideOverFullscreenApps;

    [ObservableProperty]
    private string? _displayPolicyFault;

    public event Action<OverlayDisplayPolicy>? DisplayPolicyRequested;

    public IReadOnlyList<OverlayDisplayPolicy> DisplayPolicies { get; } =
        Enum.GetValues<OverlayDisplayPolicy>();

    public string OverlayModeText => IsInteractive
        ? "Interactive"
        : "Click-through";

    public bool IsInteractive =>
        !HasAppliedOverlayMode ||
        IsOverlayFaulted ||
        OverlayMode == OverlayInteractionMode.Interactive;

    public IReadOnlyList<SystemMetricsSnapshot> CpuHistoryNewestFirst =>
        _cpuHistoryNewestFirst;

    public MainWindowViewModel(
        ISystemMetricsService systemMetricsService,
        IUiDispatcher uiDispatcher,
        IRunCatAnimationController animationController,
        ILogger<MainWindowViewModel>? logger = null)
        : this(
            systemMetricsService,
            uiDispatcher,
            animationController,
            DefaultCpuHistoryCapacity,
            DefaultSamplingInterval,
            Task.Delay,
            logger)
    {
    }

    internal MainWindowViewModel(
        ISystemMetricsService systemMetricsService,
        IUiDispatcher uiDispatcher,
        IRunCatAnimationController animationController,
        int cpuHistoryCapacity,
        TimeSpan samplingInterval,
        Func<TimeSpan, CancellationToken, Task> delayAsync,
        ILogger<MainWindowViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(systemMetricsService);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(animationController);
        ArgumentNullException.ThrowIfNull(delayAsync);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(samplingInterval, TimeSpan.Zero);

        _systemMetricsService = systemMetricsService;
        _uiDispatcher = uiDispatcher;
        _animationController = animationController;
        _cpuHistoryBuffer = new BoundedHistory<SystemMetricsSnapshot>(cpuHistoryCapacity);
        _samplingIntervalTicks = samplingInterval.Ticks;
        _delayAsync = delayAsync;
        _logger = logger ??
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MainWindowViewModel>.Instance;
        _animationController.FrameChanged += OnAnimationFrameChanged;
        _animationController.Faulted += OnAnimationFaulted;
        _animationController.UpdateInterval(CpuAnimationSpeedMapper.SlowestInterval);
    }

    internal void ApplyOverlayState(
        OverlayWindowState state,
        string? additionalError = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        OverlayMode = state.AppliedMode ?? state.RequestedMode;
        HasAppliedOverlayMode = state.AppliedMode.HasValue;
        IsOverlayFaulted = state.IsFaulted;
        OverlayErrorMessage = additionalError ?? (state.LastError is null
            ? null
            : "互動模式套用失敗，已保留目前可用狀態。");
    }

    internal void ReportOverlayError(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        OverlayErrorMessage = message;
    }

    internal void ApplyHotKeyRegistrations(
        IReadOnlyList<GlobalHotKeyRegistrationState> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        HotKeyErrorMessage = string.Join(
            " ",
            registrations
                .Where(registration => registration.Fault is not null)
                .Select(registration => registration.Fault)
                .Distinct(StringComparer.Ordinal));
        if (HotKeyErrorMessage.Length == 0)
        {
            HotKeyErrorMessage = null;
        }
    }

    internal void ReportTrayError(string? message)
    {
        TrayErrorMessage = message;
    }

    internal void ApplyDisplayPolicyState(OverlayDisplayPolicyState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        DisplayPolicyFault = state.Fault is null
            ? null
            : "全螢幕顯示政策暫時無法判斷，Dashboard 將保持顯示。";
    }

    public bool Start()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (_samplingTask is { IsCompleted: false })
            {
                return false;
            }

            _samplingCancellationSource?.Dispose();
            _samplingCancellationSource = new CancellationTokenSource();
            while (_intervalChanges.Reader.TryRead(out _)) { }
            CancellationToken cancellationToken = _samplingCancellationSource.Token;

            IsSampling = true;
            SamplingStatus = "Sampling";
            ErrorMessage = null;
            _samplingTask = Task.Run(
                () => RunSamplingLoopAsync(cancellationToken),
                CancellationToken.None);
            TryLog(() => _logger.LogDebug(
                "Metrics sampling started. {Operation} {Subsystem}",
                "StartSampling",
                "Metrics"));

            return true;
        }
    }

    public async Task StopAsync()
    {
        Task? samplingTask;
        CancellationTokenSource? cancellationSource;

        lock (_lifecycleLock)
        {
            samplingTask = _samplingTask;
            cancellationSource = _samplingCancellationSource;
            cancellationSource?.Cancel();

            IsSampling = false;
            SamplingStatus = "Stopped";
        }
        TryLog(() => _logger.LogDebug(
            "Metrics sampling stopped. {Operation} {Subsystem}",
            "StopSampling",
            "Metrics"));

        if (samplingTask is not null)
        {
            try
            {
                await samplingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationSource?.IsCancellationRequested == true)
            {
            }
        }

        lock (_lifecycleLock)
        {
            if (ReferenceEquals(_samplingTask, samplingTask))
            {
                _samplingTask = null;
                _samplingCancellationSource = null;
                cancellationSource?.Dispose();
            }
        }
    }

    public bool UpdateSamplingInterval(TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        long previous = Interlocked.Exchange(ref _samplingIntervalTicks, interval.Ticks);
        if (previous == interval.Ticks)
        {
            return false;
        }

        lock (_lifecycleLock)
        {
            if (!_isDisposed && _samplingTask is { IsCompleted: false })
            {
                _intervalChanges.Writer.TryWrite(true);
            }
        }
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        lock (_lifecycleLock)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
        }

        _animationController.FrameChanged -= OnAnimationFrameChanged;
        _animationController.Faulted -= OnAnimationFaulted;
        _animationController.Dispose();
        await StopAsync().ConfigureAwait(false);
    }

    internal void SetAnimationVisibility(bool isVisible)
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            if (isVisible)
            {
                _animationController.Start();
            }
            else
            {
                _animationController.Stop();
            }

            IsAnimationRunning = _animationController.IsRunning;
        }
        catch (Exception exception)
        {
            TryLog(() => _logger.LogError(
                exception,
                "Run-cat animation lifecycle failed. {Operation} {Subsystem} {FaultState} {HResult}",
                isVisible ? "StartAnimation" : "StopAnimation",
                "Animation",
                "Faulted",
                exception.HResult));
            AnimationErrorMessage = "Run Cat 動畫暫時發生錯誤。";
            IsAnimationRunning = _animationController.IsRunning;
        }
    }

    private async Task RunSamplingLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await SampleOnceAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                TimeSpan interval = TimeSpan.FromTicks(
                    Interlocked.Read(ref _samplingIntervalTicks));
                using var wakeCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                Task delayTask = _delayAsync(interval, wakeCancellation.Token);
                Task<bool> intervalChangeTask = _intervalChanges.Reader
                    .WaitToReadAsync(wakeCancellation.Token).AsTask();
                Task completed = await Task.WhenAny(delayTask, intervalChangeTask)
                    .ConfigureAwait(false);
                if (completed == intervalChangeTask &&
                    await intervalChangeTask.ConfigureAwait(false))
                {
                    while (_intervalChanges.Reader.TryRead(out _)) { }
                    wakeCancellation.Cancel();
                    try { await delayTask.ConfigureAwait(false); }
                    catch (OperationCanceledException) when (wakeCancellation.IsCancellationRequested) { }
                }
                else
                {
                    await delayTask.ConfigureAwait(false);
                    wakeCancellation.Cancel();
                    try { await intervalChangeTask.ConfigureAwait(false); }
                    catch (OperationCanceledException) when (wakeCancellation.IsCancellationRequested) { }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task SampleOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            SystemMetricsSnapshot snapshot =
                await _systemMetricsService.SampleAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            await _uiDispatcher.InvokeAsync(
                () => ApplySuccessfulSample(snapshot),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            try
            {
                await _uiDispatcher.InvokeAsync(
                    () => ApplySamplingError(exception),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private void ApplySuccessfulSample(SystemMetricsSnapshot snapshot)
    {
        CpuUsageText = snapshot.CpuUsagePercent is double cpuUsage && double.IsFinite(cpuUsage)
            ? string.Create(CultureInfo.InvariantCulture, $"{cpuUsage:F1}%")
            : "--";
        MemoryUsageText = string.Create(
            CultureInfo.InvariantCulture,
            $"{snapshot.MemoryUsagePercent:F1}%");
        UsedAndTotalMemoryText = string.Create(
            CultureInfo.InvariantCulture,
            $"{FormatGibibytes(snapshot.UsedPhysicalMemoryBytes)} / {FormatGibibytes(snapshot.TotalPhysicalMemoryBytes)}");
        LastUpdatedText = snapshot.SampledAt.ToLocalTime().ToString(
            "yyyy-MM-dd HH:mm:ss zzz",
            CultureInfo.InvariantCulture);

        if (snapshot.CpuUsagePercent is double finiteCpuUsage &&
            double.IsFinite(finiteCpuUsage))
        {
            _cpuHistoryBuffer.Add(snapshot);
            CpuHistory = _cpuHistoryBuffer.GetSnapshot();
        }

        UpdateAnimationSpeed();

        if (_samplingFaultEpisode.Observe(isFaulted: false) == FaultEpisodeTransition.Recovered)
        {
            TryLog(() => _logger.LogInformation(
                "Metrics sampling recovered. {Operation} {Subsystem} {FaultState}",
                "SampleSystemMetrics",
                "Metrics",
                "Recovered"));
        }

        ErrorMessage = null;
        SamplingStatus = snapshot.CpuUsagePercent is double currentCpuUsage &&
                         double.IsFinite(currentCpuUsage)
            ? "Sampling"
            : "Waiting for the next CPU sample";
    }

    private void UpdateAnimationSpeed()
    {
        double? averageCpu = RecentCpuSampleAverager.Average(
            _cpuHistoryBuffer
                .GetSnapshot()
                .Select(snapshot => snapshot.CpuUsagePercent));
        TimeSpan interval = CpuAnimationSpeedMapper.Map(averageCpu);

        _animationController.UpdateInterval(interval);
    }

    private void OnAnimationFrameChanged(int frameIndex)
    {
        AnimationFrameIndex = frameIndex;
    }

    private void OnAnimationFaulted(string message)
    {
        AnimationErrorMessage = "Run Cat 動畫暫時發生錯誤。";
    }

    private void ApplySamplingError(Exception exception)
    {
        if (_samplingFaultEpisode.Observe(isFaulted: true) == FaultEpisodeTransition.Failed)
        {
            int? nativeErrorCode = (exception as Win32Exception)?.NativeErrorCode;
            TryLog(() => _logger.LogError(
                exception,
                "Metrics sampling failed. {Operation} {Subsystem} {FaultState} {NativeErrorCode}",
                "SampleSystemMetrics",
                "Metrics",
                "Faulted",
                nativeErrorCode));
        }
        ErrorMessage = "系統資訊取樣失敗，將自動重試。";
        SamplingStatus = "Sampling error; retrying";
    }

    internal static string FormatGibibytes(ulong bytes)
    {
        const double bytesPerGibibyte = 1024d * 1024d * 1024d;
        double gibibytes = bytes / bytesPerGibibyte;
        return string.Create(CultureInfo.InvariantCulture, $"{gibibytes:F2} GiB");
    }

    private static void TryLog(Action log)
    {
        try
        {
            log();
        }
        catch
        {
            // Logging must not alter sampling or presentation state.
        }
    }

    partial void OnOverlayModeChanged(OverlayInteractionMode value)
    {
        OnPropertyChanged(nameof(OverlayModeText));
        OnPropertyChanged(nameof(IsInteractive));
    }

    partial void OnCpuHistoryChanged(IReadOnlyList<SystemMetricsSnapshot> value)
    {
        _cpuHistoryNewestFirst = Array.AsReadOnly(
            value
                .Reverse()
                .Take(DisplayedCpuHistoryCapacity)
                .ToArray());
        OnPropertyChanged(nameof(CpuHistoryNewestFirst));
    }

    partial void OnHasAppliedOverlayModeChanged(bool value)
    {
        OnPropertyChanged(nameof(OverlayModeText));
        OnPropertyChanged(nameof(IsInteractive));
    }

    partial void OnIsOverlayFaultedChanged(bool value)
    {
        OnPropertyChanged(nameof(OverlayModeText));
        OnPropertyChanged(nameof(IsInteractive));
    }

    partial void OnOverlayErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(OverlayModeText));
    }

    partial void OnRequestedDisplayPolicyChanged(OverlayDisplayPolicy value)
    {
        DisplayPolicyRequested?.Invoke(value);
    }

}
