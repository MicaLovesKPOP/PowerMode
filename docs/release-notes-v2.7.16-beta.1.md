# Power Mode v2.7.16-beta.1

Experimental beta release candidate for startup, shutdown, Extreme Energy Saver safety behavior, and the clean logging layout.

This beta intentionally lives on the PR branch while the behavior is tested. The stable public release remains v2.7.15 until this naturally becomes ready for normal users.

## What changed

- Added a startup safety guard so Power Mode can avoid leaving the PC in Extreme Energy Saver during app startup.
- Added a shutdown/restart safety guard so the next boot does not start in an unexpectedly low-power state.
- Added post-login restore behavior for users who intentionally left Manual mode in Extreme Energy Saver.
- Added CPU/disk settled-system monitoring before restoring manual Extreme Energy Saver.
- Added an experimental Automatic Mode EES startup gate.
- Added a clean logging layout under `PowerMode\logs`.
- Added one-time legacy log-folder preservation: old data can be moved to `PowerMode.bak` before the new clean layout starts.
- Added concise diagnostics for safety-guard decisions, restore cancellation, restore fallback, delayed Away mode, and final restore timing.

## Behavior details

Automatic Mode:

- If Automatic Mode is enabled and the Away profile is Extreme Energy Saver, Power Mode pauses Automatic Mode briefly during startup/sign-in.
- While the startup gate is active, Automatic Mode may stay in or return to the configured Using PC profile instead of entering Away/EES immediately.
- The experimental Automatic Mode gate waits at least 30 seconds, then allows Away/EES once CPU stays <= 15% for 12 seconds.
- If the CPU counter is unavailable, the gate falls back after 120 seconds.
- If the CPU never appears settled, the gate falls back after 5 minutes.
- The gate is disposed on Windows session ending so it should not keep doing power-plan work during shutdown/restart.

Manual Mode:

- If Manual mode was intentionally left in Extreme Energy Saver, Power Mode temporarily uses Cool & Quiet during startup/shutdown safety.
- After sign-in, Power Mode restores Extreme Energy Saver once the system has settled.
- The restore waits at least 20 seconds, then requires CPU <= 15% and disk <= 20% for 12 seconds.
- If the system never appears settled, the restore uses a 5-minute fallback timeout.
- The restore is cancelled if the user changes profiles, Automatic Mode is enabled, or the expected temporary safe profile is no longer active.

Logging:

- New logs are written to:

```text
%LOCALAPPDATA%\MicaLovesKPOP\PowerMode\logs\power-mode-events.log
%LOCALAPPDATA%\MicaLovesKPOP\PowerMode\logs\power-mode-diagnostic.log
%LOCALAPPDATA%\MicaLovesKPOP\PowerMode\logs\power-mode-crash.log
%LOCALAPPDATA%\MicaLovesKPOP\PowerMode\logs\power-mode-stats.json
```

- Existing legacy log data is preserved by moving the old `PowerMode` folder to `PowerMode.bak` when needed.
- Fresh installs only create the clean `PowerMode\logs` layout.
- Event, diagnostic, and crash logs use size-based rotation.

## Recommended beta validation

Before publishing this as stable, test:

- Manual mode -> Extreme Energy Saver -> restart.
- Manual mode -> Extreme Energy Saver -> shutdown/restart request -> cancelled shutdown if possible.
- Automatic Mode enabled -> Away profile Extreme Energy Saver -> normal restart.
- Automatic Mode enabled -> Away profile Extreme Energy Saver -> simulated power loss / forced VM reset only from a disposable VM snapshot.
- Automatic Mode enabled -> normal startup while user becomes active before Away mode.
- Confirm no `powercfg` popups appear during shutdown/restart.
- Confirm legacy logs are preserved in `PowerMode.bak` when upgrading from an older layout.
- Confirm new event/diagnostic/crash logs appear only under `PowerMode\logs`.

## Download

Download:

```text
PowerModeSetup-v2.7.16-beta.1.exe
```

The `.sha256` file is included for checksum verification.
