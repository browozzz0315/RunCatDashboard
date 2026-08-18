using System.IO;
using RunCatDashboard.App.Diagnostics;
using Microsoft.Extensions.Logging;

namespace RunCatDashboard.Tests.Diagnostics;

public sealed class UnhandledUiExceptionBoundaryTests
{
    [Fact]
    public void Handle_LogsCriticalWithOriginalExceptionAndStructuredContext()
    {
        Exception original = CreateThrownException();
        var logger = new RecordingLogger<UnhandledUiExceptionBoundaryTests>();
        var runtime = new RecordingLoggingRuntime();

        UnhandledUiExceptionBoundary.Handle(original, logger, runtime);

        RecordedLog entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Critical, entry.Level);
        Assert.Same(original, entry.Exception);
        Assert.Contains(nameof(InvalidOperationException), entry.Exception!.ToString());
        Assert.Contains("fatal UI failure", entry.Exception.ToString());
        Assert.Contains(nameof(CreateThrownException), entry.Exception.ToString());
        Assert.Equal(
            UnhandledUiExceptionBoundary.Operation,
            entry.Properties[nameof(UnhandledUiExceptionBoundary.Operation)]);
        Assert.Equal(
            UnhandledUiExceptionBoundary.Subsystem,
            entry.Properties[nameof(UnhandledUiExceptionBoundary.Subsystem)]);
        Assert.True(runtime.FlushCalled);
    }

    [Fact]
    public void Handle_UsesIndependentFallbackWhenNormalLoggerIsUnavailable()
    {
        Exception original = CreateThrownException();
        var fallbackMessages = new List<string>();

        Exception? failure = Record.Exception(() =>
            UnhandledUiExceptionBoundary.Handle(
                original,
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                loggingRuntime: null,
                fallbackMessages.Add));

        Assert.Null(failure);
        string fallback = Assert.Single(fallbackMessages);
        Assert.Contains(UnhandledUiExceptionBoundary.Operation, fallback);
        Assert.Contains(UnhandledUiExceptionBoundary.Subsystem, fallback);
        Assert.Contains(nameof(InvalidOperationException), fallback);
        Assert.Contains("fatal UI failure", fallback);
        Assert.Contains(nameof(CreateThrownException), fallback);
    }

    [Fact]
    public void Handle_UsesIndependentFallbackWithNullLoggingRuntime()
    {
        Exception original = CreateThrownException();
        var fallbackMessages = new List<string>();

        Exception? failure = Record.Exception(() =>
            UnhandledUiExceptionBoundary.Handle(
                original,
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                new NullApplicationLoggingRuntime(),
                fallbackMessages.Add));

        Assert.Null(failure);
        Assert.Single(fallbackMessages);
        Assert.Contains("fatal UI failure", fallbackMessages[0]);
    }

    [Fact]
    public void Handle_ContainsNormalLoggerFailureWithoutThrowing()
    {
        Exception original = CreateThrownException();
        var fallbackMessages = new List<string>();
        var logger = new ThrowingLogger<UnhandledUiExceptionBoundaryTests>();

        Exception? failure = Record.Exception(() =>
            UnhandledUiExceptionBoundary.Handle(
                original,
                logger,
                loggingRuntime: null,
                fallbackMessages.Add));

        Assert.Null(failure);
        Assert.Single(fallbackMessages);
        Assert.Contains("fatal UI failure", fallbackMessages[0]);
    }

    [Fact]
    public void Handle_ContainsFlushFailureWithoutThrowing()
    {
        Exception original = CreateThrownException();
        var fallbackMessages = new List<string>();
        var logger = new RecordingLogger<UnhandledUiExceptionBoundaryTests>();
        var runtime = new RecordingLoggingRuntime(throwOnFlush: true);

        Exception? failure = Record.Exception(() =>
            UnhandledUiExceptionBoundary.Handle(
                original,
                logger,
                runtime,
                fallbackMessages.Add));

        Assert.Null(failure);
        Assert.Single(logger.Entries);
        Assert.Single(fallbackMessages);
        Assert.True(runtime.FlushCalled);
    }

    [Fact]
    public void Handle_ContainsIndependentFallbackFailureWithoutThrowing()
    {
        Exception original = CreateThrownException();
        var logger = new ThrowingLogger<UnhandledUiExceptionBoundaryTests>();

        Exception? failure = Record.Exception(() =>
            UnhandledUiExceptionBoundary.Handle(
                original,
                logger,
                loggingRuntime: null,
                _ => throw new InvalidOperationException("configured fallback failure")));

        Assert.Null(failure);
    }

    [Fact]
    public void Handle_DoesNotTriggerApplicationLifecycle()
    {
        Exception original = CreateThrownException();
        var logger = new RecordingLogger<UnhandledUiExceptionBoundaryTests>();
        var runtime = new RecordingLoggingRuntime();

        UnhandledUiExceptionBoundary.Handle(original, logger, runtime);

        Assert.False(runtime.DisposeCalled);
    }

    [Fact]
    public async Task AppHandler_IsRegisteredEarlyAndDoesNotControlFatalLifecycle()
    {
        string source = await File.ReadAllTextAsync(GetAppPath());
        Assert.Contains(
            "DispatcherUnhandledException += OnDispatcherUnhandledException",
            source);

        int handlerStart = source.IndexOf(
            "private void OnDispatcherUnhandledException",
            StringComparison.Ordinal);
        int handlerEnd = source.IndexOf(
            "    protected override void OnStartup",
            handlerStart,
            StringComparison.Ordinal);

        Assert.True(handlerStart >= 0);
        Assert.True(handlerEnd > handlerStart);
        string handler = source[handlerStart..handlerEnd];
        Assert.DoesNotContain("Handled =", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("Shutdown", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageBox", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService", handler, StringComparison.Ordinal);
    }

    private static Exception CreateThrownException()
    {
        try
        {
            throw new InvalidOperationException("fatal UI failure");
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static string GetAppPath() => Path.Combine(
        FindRepositoryRoot(),
        "src",
        "RunCatDashboard.App",
        "App.xaml.cs");

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RunCatDashboard.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the RunCatDashboard repository root.");
    }

    private sealed class RecordingLoggingRuntime(bool throwOnFlush = false)
        : IApplicationLoggingRuntime
    {
        public ILoggerFactory LoggerFactory { get; } =
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;

        public bool FlushCalled { get; private set; }

        public bool DisposeCalled { get; private set; }

        public Task FlushAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            FlushCalled = true;
            if (throwOnFlush)
            {
                throw new InvalidOperationException("configured flush failure");
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalled = true;
            return ValueTask.CompletedTask;
        }
    }
}
