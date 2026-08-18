using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace RunCatDashboard.App.Diagnostics;

internal static class UnhandledUiExceptionBoundary
{
    internal const string Operation = "DispatcherUnhandledException";
    internal const string Subsystem = "WpfDispatcher";

    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(2);

    internal static void Handle(
        Exception? exception,
        ILogger? logger,
        IApplicationLoggingRuntime? loggingRuntime,
        Action<string>? fallback = null)
    {
        try
        {
            if (exception is null)
            {
                return;
            }

            bool fallbackRequired = !CanUseNormalLogger(logger, loggingRuntime);
            if (!fallbackRequired)
            {
                try
                {
                    logger!.LogCritical(
                        exception,
                        "Unhandled WPF UI exception. {Operation} {Subsystem}",
                        Operation,
                        Subsystem);
                }
                catch
                {
                    fallbackRequired = true;
                }
            }

            try
            {
                loggingRuntime?.FlushAsync(FlushTimeout)
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                fallbackRequired = true;
            }

            if (fallbackRequired)
            {
                TryWriteFallback(
                    exception,
                    fallback ?? (message => Trace.WriteLine(message)));
            }
        }
        catch
        {
            // The original Dispatcher exception must remain the only fatal
            // exception observed by WPF.
        }
    }

    private static bool CanUseNormalLogger(
        ILogger? logger,
        IApplicationLoggingRuntime? loggingRuntime) =>
        logger is not null &&
        logger is not Microsoft.Extensions.Logging.Abstractions.NullLogger &&
        loggingRuntime is not NullApplicationLoggingRuntime;

    private static void TryWriteFallback(
        Exception exception,
        Action<string> fallback)
    {
        try
        {
            fallback(
                $"RunCatDashboard unhandled WPF UI exception. " +
                $"Operation={Operation} Subsystem={Subsystem}{Environment.NewLine}" +
                GetExceptionText(exception));
        }
        catch
        {
            // The independent fallback is the last failure boundary.
        }
    }

    private static string GetExceptionText(Exception exception)
    {
        try
        {
            return exception.ToString();
        }
        catch
        {
            string type = GetSafeValue(
                () => exception.GetType().FullName ?? exception.GetType().Name,
                "unknown");
            string message = GetSafeValue(() => exception.Message, "unavailable");
            string stackTrace = GetSafeValue(
                () => exception.StackTrace ?? "unavailable",
                "unavailable");
            return $"ExceptionType={type} ExceptionMessage={message} " +
                   $"StackTrace={stackTrace}";
        }
    }

    private static string GetSafeValue(
        Func<string> valueFactory,
        string fallback)
    {
        try
        {
            return valueFactory();
        }
        catch
        {
            return fallback;
        }
    }
}
