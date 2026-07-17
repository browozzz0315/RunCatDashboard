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
