using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RunCatDashboard.App.Collections;
using RunCatDashboard.App.Models;
using RunCatDashboard.App.Services;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    internal const int DefaultCpuHistoryCapacity = 30;
    internal static readonly TimeSpan DefaultSamplingInterval = TimeSpan.FromSeconds(1);

    private readonly ISystemMetricsService _systemMetricsService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IOverlayWindowController _overlayWindowController;
    private readonly BoundedHistory<SystemMetricsSnapshot> _cpuHistoryBuffer;
    private readonly TimeSpan _samplingInterval;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
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

    [ObservableProperty]
    private bool _isSampling;

    [ObservableProperty]
    private string _samplingStatus = "Stopped";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private OverlayInteractionMode _overlayMode;

    [ObservableProperty]
    private string? _overlayErrorMessage;

    public string OverlayModeText => OverlayMode switch
    {
        OverlayInteractionMode.Interactive => "Interactive",
        OverlayInteractionMode.ClickThrough => "Click-through",
        _ => "Unknown"
    };

    public bool IsInteractive => OverlayMode == OverlayInteractionMode.Interactive;

    public MainWindowViewModel(
        ISystemMetricsService systemMetricsService,
        IUiDispatcher uiDispatcher,
        IOverlayWindowController overlayWindowController)
        : this(
            systemMetricsService,
            uiDispatcher,
            DefaultCpuHistoryCapacity,
            DefaultSamplingInterval,
            Task.Delay,
            overlayWindowController)
    {
    }

    internal MainWindowViewModel(
        ISystemMetricsService systemMetricsService,
        IUiDispatcher uiDispatcher,
        int cpuHistoryCapacity,
        TimeSpan samplingInterval,
        Func<TimeSpan, CancellationToken, Task> delayAsync,
        IOverlayWindowController overlayWindowController)
    {
        ArgumentNullException.ThrowIfNull(systemMetricsService);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(delayAsync);
        ArgumentNullException.ThrowIfNull(overlayWindowController);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(samplingInterval, TimeSpan.Zero);

        _systemMetricsService = systemMetricsService;
        _uiDispatcher = uiDispatcher;
        _overlayWindowController = overlayWindowController;
        _cpuHistoryBuffer = new BoundedHistory<SystemMetricsSnapshot>(cpuHistoryCapacity);
        _samplingInterval = samplingInterval;
        _delayAsync = delayAsync;
        _overlayMode = overlayWindowController.Mode;
    }

    public bool TrySetOverlayMode(OverlayInteractionMode mode)
    {
        try
        {
            bool changed = _overlayWindowController.SetMode(mode);
            OverlayMode = _overlayWindowController.Mode;
            OverlayErrorMessage = null;
            return changed;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
            OverlayErrorMessage = $"Overlay mode change failed: {exception.Message}";
            return false;
        }
    }

    [RelayCommand]
    private void EnableInteractive()
    {
        TrySetOverlayMode(OverlayInteractionMode.Interactive);
    }

    [RelayCommand]
    private void EnableClickThrough()
    {
        TrySetOverlayMode(OverlayInteractionMode.ClickThrough);
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
            CancellationToken cancellationToken = _samplingCancellationSource.Token;

            IsSampling = true;
            SamplingStatus = "Sampling";
            ErrorMessage = null;
            _samplingTask = Task.Run(
                () => RunSamplingLoopAsync(cancellationToken),
                CancellationToken.None);

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

        await StopAsync().ConfigureAwait(false);
    }

    private async Task RunSamplingLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await SampleOnceAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await _delayAsync(_samplingInterval, cancellationToken).ConfigureAwait(false);
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
        CpuUsageText = snapshot.CpuUsagePercent is double cpuUsage
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

        if (snapshot.CpuUsagePercent.HasValue)
        {
            _cpuHistoryBuffer.Add(snapshot);
            CpuHistory = _cpuHistoryBuffer.GetSnapshot();
        }

        ErrorMessage = null;
        SamplingStatus = snapshot.CpuUsagePercent.HasValue
            ? "Sampling"
            : "Waiting for the next CPU sample";
    }

    private void ApplySamplingError(Exception exception)
    {
        ErrorMessage = $"Sampling failed: {exception.Message}";
        SamplingStatus = "Sampling error; retrying";
    }

    internal static string FormatGibibytes(ulong bytes)
    {
        const double bytesPerGibibyte = 1024d * 1024d * 1024d;
        double gibibytes = bytes / bytesPerGibibyte;
        return string.Create(CultureInfo.InvariantCulture, $"{gibibytes:F2} GiB");
    }

    partial void OnOverlayModeChanged(OverlayInteractionMode value)
    {
        OnPropertyChanged(nameof(OverlayModeText));
        OnPropertyChanged(nameof(IsInteractive));
    }
}
