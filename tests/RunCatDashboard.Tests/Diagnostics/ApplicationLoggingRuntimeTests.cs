using System.IO;
using Microsoft.Extensions.Logging;
using RunCatDashboard.App.Diagnostics;
using RunCatDashboard.App.Services;

namespace RunCatDashboard.Tests.Diagnostics;

public sealed class ApplicationLoggingRuntimeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"RunCatDashboard.LoggingTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task WriteFlushAndDispose_ProducesUtf8FileAndReleasesHandle()
    {
        var paths = new ApplicationPaths(_root, 7);
        IApplicationLoggingRuntime runtime = CreateRuntime(paths);
        ILogger logger = runtime.LoggerFactory.CreateLogger("RunCatDashboard.Tests");
        logger.LogWarning(
            "設定診斷 {Operation} {Subsystem}",
            "VerifyUtf8",
            "Tests");

        await runtime.FlushAsync(TimeSpan.FromSeconds(2));
        await runtime.DisposeAsync();

        string activeFile = Assert.Single(Directory.GetFiles(paths.LogsDirectory, "*.log"));
        string content = await File.ReadAllTextAsync(activeFile);
        Assert.Contains("設定診斷", content);
        string moved = activeFile + ".moved";
        File.Move(activeFile, moved);
        File.Delete(moved);
    }

    [Fact]
    public async Task SizeLimit_CreatesRealSequenceArchives()
    {
        var paths = new ApplicationPaths(_root, 8);
        IApplicationLoggingRuntime runtime = CreateRuntime(
            paths,
            new LoggingFileOptions(512, 7, 14));
        ILogger logger = runtime.LoggerFactory.CreateLogger("RunCatDashboard.Tests");

        for (int index = 0; index < 40; index++)
        {
            logger.LogWarning("Size rotation {Index} {Payload}", index, new string('x', 180));
        }

        await runtime.DisposeAsync();

        Assert.True(Directory.GetFiles(paths.LogsDirectory, "*.log").Length > 1);
    }

    [Fact]
    public async Task DailyRotation_ArchivesARealPreviousDayActiveFile()
    {
        var paths = new ApplicationPaths(_root, 9);
        Directory.CreateDirectory(paths.LogsDirectory);
        var controlledTime = new ControlledTimeSource(DateTime.Today.AddHours(12));
        var factory = new NLog.LogFactory();
        factory.Setup(setup =>
            NLog.SetupBuilderExtensions.SetupLogFactory(
                setup,
                factoryBuilder => NLog.SetupLogFactoryBuilderExtensions.SetTimeSource(
                    factoryBuilder,
                    controlledTime)));
        factory.Configuration = NLogApplicationLoggingRuntime.CreateConfiguration(
                paths,
                LoggingFileOptions.Defaults);
        try
        {
            NLog.Logger logger = factory.GetLogger("RunCatDashboard.Tests");
            logger.Warn("first day");
            controlledTime.Advance(TimeSpan.FromDays(1));
            logger.Warn("second day");
            factory.Flush(TimeSpan.FromSeconds(2));

            Assert.True(Directory.GetFiles(paths.LogsDirectory, "*.log").Length > 1);
        }
        finally
        {
            factory.Shutdown();
            factory.Dispose();
        }

        await Task.CompletedTask;
    }

    [Fact]
    public async Task Retention_UsesAtMostFourteenRealArchives()
    {
        Assert.Equal(7, LoggingFileOptions.Defaults.RetentionDays);
        Assert.Equal(14, LoggingFileOptions.Defaults.MaximumFileCount);

        var paths = new ApplicationPaths(_root, 10);
        IApplicationLoggingRuntime runtime = CreateRuntime(
            paths,
            new LoggingFileOptions(256, 7, 14));
        ILogger logger = runtime.LoggerFactory.CreateLogger("RunCatDashboard.Tests");
        for (int index = 0; index < 100; index++)
        {
            logger.LogWarning("Retention rotation {Index} {Payload}", index, new string('y', 200));
        }

        await runtime.DisposeAsync();

        string[] allFiles = Directory.GetFiles(paths.LogsDirectory, "RunCatDashboard-s10*.log");
        Assert.InRange(allFiles.Length, 2, 14);
    }

    [Fact]
    public async Task Retention_DeletesARealArchiveOlderThanSevenDays()
    {
        var paths = new ApplicationPaths(_root, 12);
        Directory.CreateDirectory(paths.LogsDirectory);
        string expiredArchive = Path.Combine(
            paths.LogsDirectory,
            "RunCatDashboard-s12-20000101.0.log");
        await File.WriteAllTextAsync(expiredArchive, "expired");
        File.SetLastWriteTimeUtc(expiredArchive, DateTime.UtcNow.AddDays(-8));
        IApplicationLoggingRuntime runtime = CreateRuntime(
            paths,
            new LoggingFileOptions(256, 7, 100));
        ILogger logger = runtime.LoggerFactory.CreateLogger("RunCatDashboard.Tests");
        for (int index = 0; index < 10; index++)
        {
            logger.LogWarning("Age retention {Index} {Payload}", index, new string('z', 200));
        }

        await runtime.DisposeAsync();

        Assert.False(File.Exists(expiredArchive));
    }

    [Fact]
    public async Task InvalidLogDirectory_FallsBackOnceWithoutThrowingOrRecursing()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "RunCatDashboard"), "blocks directory creation");
        var paths = new ApplicationPaths(_root, 11);
        int diagnostics = 0;

        IApplicationLoggingRuntime runtime = ApplicationLoggingRuntime.TryCreate(
            paths,
            LoggingPolicy.Create("Debug", []),
            _ => diagnostics++);
        runtime.LoggerFactory.CreateLogger("RunCatDashboard.Tests").LogError("ignored safely");
        await runtime.FlushAsync(TimeSpan.FromSeconds(2));
        await runtime.DisposeAsync();

        Assert.IsType<NullApplicationLoggingRuntime>(runtime);
        Assert.Equal(1, diagnostics);
    }

    [Fact]
    public async Task WriteFailure_IsContainedAndDoesNotCrashOrRecurse()
    {
        var paths = new ApplicationPaths(_root, 13);
        IApplicationLoggingRuntime runtime = CreateRuntime(paths);
        Directory.Delete(paths.LogsDirectory);
        File.WriteAllText(paths.LogsDirectory, "blocks lazy file creation");
        ILogger logger = runtime.LoggerFactory.CreateLogger("RunCatDashboard.Tests");

        Exception? exception = Record.Exception(() =>
            logger.LogError(
                new IOException("configured application failure"),
                "Write containment {Operation} {Subsystem}",
                "WriteLog",
                "Tests"));
        await runtime.FlushAsync(TimeSpan.FromSeconds(2));
        await runtime.DisposeAsync();

        Assert.Null(exception);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static IApplicationLoggingRuntime CreateRuntime(
        ApplicationPaths paths,
        LoggingFileOptions? options = null) =>
        ApplicationLoggingRuntime.TryCreate(
            paths,
            LoggingPolicy.Create("Debug", []),
            message => throw new Xunit.Sdk.XunitException(message),
            options);

    private sealed class ControlledTimeSource(DateTime current) : NLog.Time.TimeSource
    {
        public override DateTime Time => current;

        public override DateTime FromSystemTime(DateTime systemTime) => systemTime;

        internal void Advance(TimeSpan elapsed) => current += elapsed;
    }

}
