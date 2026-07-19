# Architecture

## Decision

V1 uses:

```text
Single WPF application
+ MVVM
+ service abstractions
+ isolated Win32 interop
+ separate unit-test project
```

A full multi-project Clean Architecture is intentionally not used.

## Reason

The application has Windows integration and presentation complexity, but it does not yet contain complex business rules, transactions, persistence, or multiple external adapters.

Splitting Presentation, Application, Domain, and Infrastructure into separate assemblies would currently add more maintenance cost than protection.

## Dependency model

```text
View
  |
  v
ViewModel
  |
  v
Service abstraction
  |
  v
Windows-specific implementation
```

## Main components

### Views

Responsible for XAML layout, styles, visual states, and view-only lifecycle integration.

### ViewModels

Responsible for displayed metrics, collapsed and expanded state, interaction mode, commands, and user-facing state transitions. ViewModels must not call P/Invoke directly.

### Monitoring services

Responsible for CPU and memory sampling, bounded metric history, sampling intervals, and cancellation.

### Windowing services

Responsible for topmost behavior, click-through mode, no-activate behavior, global hotkeys, monitor placement, and DPI-aware positioning.

Fullscreen display-policy detection and lifecycle responsibilities are documented in
[`OVERLAY_FULLSCREEN_POLICY.md`](OVERLAY_FULLSCREEN_POLICY.md).

### Settings services

Responsible for loading and saving local settings, defaults, validation, and future settings migration.

### Interop

Responsible for P/Invoke declarations, native constants, safe wrappers around Windows handles, and native error conversion.

### Animation

Responsible for mapping CPU load to animation speed, frame selection, and animation policy independent of the concrete View.

## Future extraction criteria

Create additional projects only when one or more conditions become true:

- non-Windows UI is planned
- multiple independent data-source plugins are introduced
- a reusable core library has real consumers
- Windows integration becomes independently substantial
- application logic becomes complex enough to need stronger boundaries
