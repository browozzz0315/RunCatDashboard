using Microsoft.Win32;
using Microsoft.Extensions.Logging;
using System.IO;

namespace RunCatDashboard.App.Startup;

public sealed record RunAtLoginState(
    bool Requested,
    bool Applied,
    string? Fault);

public interface IRunAtLoginService
{
    RunAtLoginState State { get; }
    Task<RunAtLoginState> ReconcileAsync(
        bool requested,
        CancellationToken cancellationToken = default);
}

internal interface IRunRegistry
{
    string? Read(string valueName);
    void Write(string valueName, string value);
    void Delete(string valueName);
}

internal sealed class CurrentUserRunRegistry : IRunRegistry
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Read(string valueName)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
            as string;
    }

    public void Write(string valueName, string value)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    public void Delete(string valueName)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}

internal sealed class RunAtLoginService : IRunAtLoginService
{
    internal const string ValueName = "RunCatDashboard";
    internal const string ExecutableFileName = "RunCatDashboard.exe";
    private readonly IRunRegistry _registry;
    private readonly Func<string?> _processPathProvider;
    private readonly ILogger<RunAtLoginService> _logger;

    internal RunAtLoginService(
        IRunRegistry registry,
        Func<string?> processPathProvider,
        ILogger<RunAtLoginService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(processPathProvider);
        _registry = registry;
        _processPathProvider = processPathProvider;
        _logger = logger ??
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RunAtLoginService>.Instance;
    }

    public RunAtLoginState State { get; private set; } = new(false, false, null);

    public Task<RunAtLoginState> ReconcileAsync(
        bool requested,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!requested)
            {
                _registry.Delete(ValueName);
                string? remaining = _registry.Read(ValueName);
                State = new(false, remaining is not null, remaining is null
                    ? null
                    : "停用 Windows 登入啟動後，Registry value 仍然存在。");
                LogState("DisableRunAtLogin", State);
                return Task.FromResult(State);
            }

            string? processPath = _processPathProvider();
            if (!IsValidExecutablePath(processPath))
            {
                State = new(
                    true,
                    false,
                    $"目前 process path 不是有效的 {ExecutableFileName}，未寫入開機啟動命令。");
                LogState("ValidateRunAtLoginExecutable", State);
                return Task.FromResult(State);
            }

            string command = QuoteExecutablePath(processPath!);
            if (!string.Equals(_registry.Read(ValueName), command, StringComparison.Ordinal))
            {
                _registry.Write(ValueName, command);
            }

            string? confirmed = _registry.Read(ValueName);
            bool applied = string.Equals(confirmed, command, StringComparison.Ordinal);
            State = new(
                true,
                applied,
                applied ? null : "Windows 登入啟動命令寫入後未能確認套用狀態。");
            LogState("EnableRunAtLogin", State);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            bool applied = false;
            try { applied = _registry.Read(ValueName) is not null; } catch { }
            State = new(requested, applied, "套用 Windows 登入啟動設定失敗，請稍後重試。");
            try
            {
                _logger.LogError(
                    exception,
                    "Run-at-login reconciliation failed. {Operation} {Subsystem} {RequestedState} {AppliedState} {FaultState} {HResult}",
                    "ReconcileRunAtLogin",
                    "Startup",
                    requested,
                    applied,
                    "Faulted",
                    exception.HResult);
            }
            catch
            {
                // Logging must not alter requested/applied/fault state.
            }
        }

        return Task.FromResult(State);
    }

    internal static bool IsValidExecutablePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Path.IsPathFullyQualified(path) &&
        string.Equals(Path.GetFileName(path), ExecutableFileName, StringComparison.OrdinalIgnoreCase);

    internal static string QuoteExecutablePath(string path) => $"\"{path}\"";

    private void LogState(string operation, RunAtLoginState state)
    {
        try
        {
            if (state.Fault is null)
            {
                _logger.LogInformation(
                    "Run-at-login state reconciled. {Operation} {Subsystem} {RequestedState} {AppliedState} {FaultState}",
                    operation,
                    "Startup",
                    state.Requested,
                    state.Applied,
                    "Healthy");
            }
            else
            {
                _logger.LogWarning(
                    "Run-at-login state could not be fully applied. {Operation} {Subsystem} {RequestedState} {AppliedState} {FaultState}",
                    operation,
                    "Startup",
                    state.Requested,
                    state.Applied,
                    "Faulted");
            }
        }
        catch
        {
            // Logging must not alter requested/applied/fault state.
        }
    }
}
