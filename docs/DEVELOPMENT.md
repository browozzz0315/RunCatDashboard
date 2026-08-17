# Development workflow

## Open project

```powershell
Set-Location "D:\.repos\RunCatDashboard"
code .
```

## Restore and verify

```powershell
dotnet restore
dotnet build
dotnet test
```

## Run application

```powershell
dotnet run --project ".\src\RunCatDashboard.App"
```

Diagnostic files are written only to `%LocalAppData%\RunCatDashboard\Logs`.
Use `-- --log-level Trace` for one process, and add
`--enable-high-frequency-trace` only when polling diagnostics are explicitly
required. These switches are not persisted. See
[`DIAGNOSTIC_LOGGING.md`](DIAGNOSTIC_LOGGING.md) for rotation, retention,
privacy, failure handling, and manual verification.

## Native Codex session

```powershell
Set-Location "D:\.repos\RunCatDashboard"
codex
```

Equivalent explicit working directory:

```powershell
codex -C "D:\.repos\RunCatDashboard"
```

## Headroom-wrapped Codex session

```powershell
Set-Location "D:\.repos\RunCatDashboard"
headroom wrap codex
```

## Initial A/B measurement

Record task description, start and end time, whether Headroom was enabled, Codex model and reasoning level, Codex token usage, Headroom token savings, files changed, build and test results, and manual correction time.

Do not run the exact same implementation task twice against the same working tree. Use comparable tasks or separate Git branches for controlled comparisons.

## Definition of done

A V1 task is complete only when requested behavior is implemented, build succeeds, relevant tests pass, manual checks are listed, resource implications are considered, no unrelated changes are included, and a suitable commit message is provided.

## System tray and hotkey verification

The tray and global-hotkey unit tests use adapters and do not prove Windows
shell integration. Before release, follow the manual checklist in
[`SYSTEM_TRAY_AND_HOTKEYS.md`](SYSTEM_TRAY_AND_HOTKEYS.md), including an Explorer
restart, hotkey conflicts, Close-to-Hide, fullscreen visibility precedence, and
repeated exit/relaunch checks.

## Settings and startup verification

Settings tests use injectable file and Registry adapters and never modify the
real HKCU Run key. Before release, follow
[`SETTINGS_AND_STARTUP.md`](SETTINGS_AND_STARTUP.md): inspect schema v1 under
LocalAppData and its schema v6 migration, animation catalog fallback, malformed/unsupported backups, hidden startup without Show/Hide
flicker, multi-monitor placement, runtime sampling changes, Settings Save/Cancel,
quoted executable reconciliation, and explicit shutdown flush. Debug output may
be locked by a running app; do not terminate a user process, and verify with
`dotnet build -c Release` plus `dotnet test -c Release` instead.

Overlay presentation changes also require manual verification of all four size
modes, field combinations, content-driven Height, small-work-area scrolling,
post-layout clamp, CatOnly Interactive controls and ClickThrough hotkey／tray
recovery. Unit tests do not prove WPF layout or DPI behavior.
