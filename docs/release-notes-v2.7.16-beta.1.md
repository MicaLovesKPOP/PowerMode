# Power Mode v2.7.16-beta.1

Beta release candidate for startup and shutdown safety behavior.

## What changed

- Added a startup safety guard so Power Mode can avoid leaving the PC in Extreme Energy Saver during app startup.
- Added a shutdown/restart safety guard so the next boot does not start in an unexpectedly low-power state.
- Added post-login restore behavior for users who intentionally left Manual mode in Extreme Energy Saver.
- Added CPU/disk settled-system monitoring before restoring manual Extreme Energy Saver.
- Added concise diagnostics for safety-guard decisions, restore cancellation, restore fallback, and final restore timing.

## Behavior details

Automatic Mode:

- If Automatic Mode is enabled, startup/shutdown safety prefers the configured Using PC profile when the active profile is lower-power than expected.
- Normal Automatic Mode behavior still resumes after the app starts.

Manual Mode:

- If Manual mode was intentionally left in Extreme Energy Saver, Power Mode temporarily uses Cool & Quiet during startup/shutdown safety.
- After sign-in, Power Mode restores Extreme Energy Saver once the system has settled.
- The restore waits at least 20 seconds, then requires CPU <= 15% and disk <= 20% for 12 seconds.
- If the system never appears settled, the restore uses a 5-minute fallback timeout.
- The restore is cancelled if the user changes profiles, Automatic Mode is enabled, or the expected temporary safe profile is no longer active.

## Diagnostics

Safety diagnostics are written to:

```text
%LOCALAPPDATA%\MicaLovesKPOP\PowerMode\PowerModeTray-diagnostic.log
```

The diagnostic log is capped to avoid unbounded growth.

## Recommended beta validation

Before publishing this as stable, test:

- Manual mode -> Extreme Energy Saver -> restart.
- Manual mode -> Extreme Energy Saver -> shutdown/restart request -> cancelled shutdown if possible.
- Automatic Mode enabled -> Away profile active -> restart.
- Automatic Mode enabled -> normal startup.
- Diagnostic log entries after each scenario.

## Download

Download:

```text
PowerModeSetup-v2.7.16-beta.1.exe
```

The `.sha256` file is included for checksum verification.
