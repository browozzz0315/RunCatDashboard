using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Layouts;
using NLog.Targets;
using RunCatDashboard.App.Services;

namespace RunCatDashboard.App.Diagnostics;

internal sealed record LoggingFileOptions(
    long MaximumFileSizeBytes,
    int RetentionDays,
    int MaximumFileCount)
{
    internal static LoggingFileOptions Defaults { get; } = new(
        5L * 1024L * 1024L,
        7,
        14);
}

internal interface IApplicationLoggingRuntime : IAsyncDisposable
{
    ILoggerFactory LoggerFactory { get; }

    Task FlushAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}

internal static class ApplicationLoggingRuntime
{
    internal static IApplicationLoggingRuntime TryCreate(
        IApplicationPaths paths,
        LoggingPolicy policy,
        Action<string>? selfDiagnostic = null,
        LoggingFileOptions? fileOptions = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(policy);
        selfDiagnostic ??= message => Trace.WriteLine(message);
        var oneShotDiagnostic = new OneShotSelfDiagnostic(selfDiagnostic);

        try
        {
            Directory.CreateDirectory(paths.LogsDirectory);
            return NLogApplicationLoggingRuntime.Create(
                paths,
                policy,
                fileOptions ?? LoggingFileOptions.Defaults,
                oneShotDiagnostic);
        }
        catch (Exception exception)
        {
            oneShotDiagnostic.Report(
                $"RunCatDashboard logging initialization failed; file logging is disabled: {exception.GetType().Name}: {exception.Message}");
            return new NullApplicationLoggingRuntime();
        }
    }
}

internal sealed class NLogApplicationLoggingRuntime : IApplicationLoggingRuntime
{
    private readonly object _gate = new();
    private readonly LogFactory _nlogFactory;
    private readonly OneShotSelfDiagnostic _selfDiagnostic;
    private bool _isDisposed;

    private NLogApplicationLoggingRuntime(
        LogFactory nlogFactory,
        ILoggerFactory loggerFactory,
        OneShotSelfDiagnostic selfDiagnostic)
    {
        _nlogFactory = nlogFactory;
        LoggerFactory = loggerFactory;
        _selfDiagnostic = selfDiagnostic;
    }

    public ILoggerFactory LoggerFactory { get; }

    internal static NLogApplicationLoggingRuntime Create(
        IApplicationPaths paths,
        LoggingPolicy policy,
        LoggingFileOptions fileOptions,
        OneShotSelfDiagnostic selfDiagnostic)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(fileOptions.MaximumFileSizeBytes, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(fileOptions.RetentionDays, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(fileOptions.MaximumFileCount, 2);

        LoggingConfiguration configuration = CreateConfiguration(paths, fileOptions);
        var nlogFactory = new LogFactory
        {
            ThrowConfigExceptions = true,
            ThrowExceptions = false
        };
        nlogFactory.Configuration = configuration;
        var provider = new NLogLoggerProvider(
            new NLogProviderOptions
            {
                CaptureMessageProperties = true,
                CaptureMessageTemplates = true,
                IncludeScopes = true,
                ShutdownOnDispose = false
            },
            nlogFactory);
        ILoggerFactory loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.AddFilter(policy.IsEnabled);
            builder.AddProvider(provider);
        });
        return new NLogApplicationLoggingRuntime(nlogFactory, loggerFactory, selfDiagnostic);
    }

    public Task FlushAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_isDisposed)
            {
                return Task.CompletedTask;
            }
        }

        try
        {
            _nlogFactory.Flush(timeout);
        }
        catch (Exception exception)
        {
            _selfDiagnostic.Report($"RunCatDashboard logging flush failed: {exception.Message}");
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
        }

        try
        {
            _nlogFactory.Flush(TimeSpan.FromSeconds(2));
        }
        catch (Exception exception)
        {
            _selfDiagnostic.Report($"RunCatDashboard final logging flush failed: {exception.Message}");
        }

        try
        {
            LoggerFactory.Dispose();
        }
        catch (Exception exception)
        {
            _selfDiagnostic.Report($"RunCatDashboard logger factory disposal failed: {exception.Message}");
        }

        try
        {
            _nlogFactory.Shutdown();
        }
        catch (Exception exception)
        {
            _selfDiagnostic.Report($"RunCatDashboard NLog shutdown failed: {exception.Message}");
        }

        try
        {
            _nlogFactory.Dispose();
        }
        catch (Exception exception)
        {
            _selfDiagnostic.Report($"RunCatDashboard NLog disposal failed: {exception.Message}");
        }

        await ValueTask.CompletedTask;
    }

    internal static LoggingConfiguration CreateConfiguration(
        IApplicationPaths paths,
        LoggingFileOptions fileOptions)
    {
        string activeFile = Path.Combine(
            paths.LogsDirectory,
            $"RunCatDashboard-s{paths.WindowsSessionId.ToString(CultureInfo.InvariantCulture)}.log");
        string archiveFile = Path.Combine(
            paths.LogsDirectory,
            $"RunCatDashboard-s{paths.WindowsSessionId.ToString(CultureInfo.InvariantCulture)}-{{#}}.log");

        var layout = new JsonLayout
        {
            IncludeEventProperties = true,
            SuppressSpaces = true,
            Attributes =
            {
                new JsonAttribute("timestamp", "${date:format=o:universalTime=true}"),
                new JsonAttribute("level", "${level:uppercase=true}"),
                new JsonAttribute("category", "${logger}"),
                new JsonAttribute("message", "${message:raw=true}"),
                new JsonAttribute("exception", "${exception:format=tostring}", false)
            }
        };
        var fileTarget = new FileTarget("diagnostic-file")
        {
            FileName = activeFile,
            ArchiveFileName = archiveFile,
            ArchiveEvery = FileArchivePeriod.Day,
            ArchiveAboveSize = fileOptions.MaximumFileSizeBytes,
            ArchiveNumbering = ArchiveNumberingMode.DateAndSequence,
            ArchiveDateFormat = "yyyyMMdd",
            MaxArchiveDays = fileOptions.RetentionDays,
            MaxArchiveFiles = fileOptions.MaximumFileCount - 1,
            KeepFileOpen = true,
            ConcurrentWrites = false,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Layout = layout
        };

        var configuration = new LoggingConfiguration();
        configuration.AddRuleForAllLevels(fileTarget);
        return configuration;
    }
}

internal sealed class NullApplicationLoggingRuntime : IApplicationLoggingRuntime
{
    public ILoggerFactory LoggerFactory { get; } =
        Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;

    public Task FlushAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class OneShotSelfDiagnostic(Action<string> write)
{
    private int _hasReported;

    internal void Report(string message)
    {
        if (Interlocked.Exchange(ref _hasReported, 1) == 0)
        {
            try
            {
                write(message);
            }
            catch
            {
                // The independent self-diagnostic is the final failure boundary.
            }
        }
    }
}
