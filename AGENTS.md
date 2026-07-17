# AGENTS.md

## Project purpose

Build a lightweight Windows desktop overlay using .NET 10, WPF, MVVM, and Win32 interop.

The application must remain unobtrusive during normal desktop use and must prioritize stable low resource usage over decorative complexity.

## Scope discipline

- Implement only the requirements explicitly requested for the current task.
- Do not introduce V2 features during V1 development.
- Do not add plugin systems, telemetry, cloud services, databases, MediatR, or unnecessary projects.
- Prefer the smallest design that preserves testability and clear dependencies.
- Ask before making a major architecture or technology change.

## Architecture

Use one executable application project and one test project for V1.

Dependency direction:

```text
Views
  -> ViewModels
  -> service abstractions
  -> Windows-specific service implementations
```

Rules:

- Views contain XAML and presentation-only behavior.
- ViewModels contain observable UI state and commands.
- ViewModels must not call Win32 APIs directly.
- Windows-specific APIs belong under `Interop` or Windows service implementations.
- System metric collection must be independent from WPF controls.
- Business and transformation logic should be testable without creating a Window.
- Code-behind is allowed only for genuinely view-specific WPF lifecycle or rendering behavior.
- Do not create a full Clean Architecture project split unless project complexity later justifies it.

## C# rules

- Enable nullable reference types.
- Use async APIs where work may block.
- Pass `CancellationToken` through background operations.
- Avoid `async void` except WPF event handlers.
- Avoid global mutable state.
- Avoid service locator patterns.
- Prefer constructor injection.
- Keep methods small and named by intent.
- Use records for immutable snapshots where appropriate.
- Do not suppress compiler warnings without documenting the reason.
- Do not add abstractions with only speculative value.

## WPF rules

- Keep UI updates on the dispatcher thread.
- Do not poll system metrics from the UI thread.
- Do not recreate visual trees unnecessarily on every sample.
- Use bounded history buffers.
- Dispose timers, hooks, tray icons, cancellation sources, and native handles.
- Account for DPI scaling and multiple monitors.
- Do not use large-area blur or permanent 60 FPS rendering without profiling evidence.
- Click-through and no-activate behavior must be switchable for interaction mode.

## Performance targets

- Collapsed average CPU target: below 0.3%
- Expanded average CPU target: below 0.8%
- Stable working set target: below 100 MB
- No continuous disk writes while idle
- No unbounded memory growth during long-running use
- Avoid unnecessary continuous GPU rendering

These are engineering targets, not guarantees.

## Testing

Before reporting completion:

```powershell
dotnet build
dotnet test
```

Unit-test at least:

- CPU-to-animation-speed mapping
- bounded metrics history
- settings serialization and validation
- non-visual state transitions

Do not claim that topmost, click-through, focus behavior, DPI, tray restoration, startup, or sleep-resume behavior is verified solely by unit tests.

## Change reporting

At the end of each task, report:

1. Implementation summary
2. Files changed
3. Build result
4. Test result
5. Manual verification still required
6. Known risks or follow-up work
7. Suggested Chinese Git commit message

## Git safety

- Do not commit build outputs, logs, local settings, credentials, or generated secrets.
- Do not rewrite Git history.
- Do not commit unless explicitly requested.
- Keep each task focused enough for one coherent commit.

## Environment

Expected commands:

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project ".\src\RunCatDashboard.App"
```

Codex runs with the repository root as its working directory.

Do not access or modify files outside the repository unless explicitly requested.
