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

Dashboard visibility uses one coordinator with separate user-requested and
fullscreen-policy inputs. Tray actions, the visibility hotkey, Window Closing,
and fullscreen observations update that coordinator instead of independently
calling `Show()` or `Hide()`. The existing `MainWindow` and ViewModel instances
remain alive while hidden.

The system tray is implemented by an infrastructure-owned
`System.Windows.Forms.NotifyIcon`; neither the concrete tray type nor HWND
message handling enters the ViewModel. The shared Window message hook dispatches
both registered hotkeys and the Windows `TaskbarCreated` message. Win32
declarations remain under `Interop`.

`TrayAnimationCoordinator` is the tray-specific presenter. It subscribes to the
same `IRunCatAnimationController.FrameChanged` event used by the Dashboard and
maps that shared frame index directly to a preloaded tray icon frame. It does not
own a timer, sample CPU, or duplicate the CPU-to-interval mapping. Animated mode
is the per-process default; the tray menu can switch only the tray presentation
to the static icon without changing Dashboard animation state or persisting a
setting. Dashboard hiding does not stop the shared animation controller, so the
animated tray remains synchronized while the Window instance is hidden.

The tray adapter owns the static fallback icon and all preloaded animation icon
frames until true application exit. Frame-assignment failures keep the previous
valid icon. Failure to load the complete animation set falls back to
`RunCatDashboard.Tray.ico` and remains diagnosable. Explorer recovery reuses the
same tray service and presenter and reapplies the current animated/static mode.

Fullscreen display-policy detection and lifecycle responsibilities are documented in
[`OVERLAY_FULLSCREEN_POLICY.md`](OVERLAY_FULLSCREEN_POLICY.md).

System tray, hotkey registration, visibility precedence, Explorer recovery,
and exit cleanup are documented in
[`SYSTEM_TRAY_AND_HOTKEYS.md`](SYSTEM_TRAY_AND_HOTKEYS.md).

### Settings services

Responsible for loading and saving local settings, defaults, validation, and future settings migration.

### Interop

Responsible for P/Invoke declarations, native constants, safe wrappers around Windows handles, and native error conversion.

### Single application instance

RunCatDashboard uses the fixed session-local named Mutex `Local\RunCatDashboard.SingleInstance` so only one instance runs in the current Windows user session. Ownership is checked immediately without waiting; an abandoned Mutex is treated as successfully acquired so an abnormal previous termination cannot permanently block startup. A second launch is rejected before the service provider or `MainWindow` is created; it shows a short message and exits normally. This does not wake, show, or focus the existing Overlay. Any future second-launch control of the existing instance requires a separately designed IPC mechanism.

### Animation

Responsible for mapping CPU load to animation speed, frame selection, and animation policy independent of the concrete View.

The eight-frame resource, CPU averaging, linear speed mapping, single timer, and application lifecycle decisions are documented in
[`RUN_CAT_ANIMATION.md`](RUN_CAT_ANIMATION.md).

## Future extraction criteria

Create additional projects only when one or more conditions become true:

- non-Windows UI is planned
- multiple independent data-source plugins are introduced
- a reusable core library has real consumers
- Windows integration becomes independently substantial
- application logic becomes complex enough to need stronger boundaries
