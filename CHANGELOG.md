# Changelog

## v2.7.16-beta.1

Beta release candidate for startup and shutdown safety behavior.

### Added

- Added startup safety guard so the app can move out of Extreme Energy Saver during app startup when needed.
- Added shutdown/restart safety guard so the next boot does not start in an unexpectedly low-power state.
- Added post-login manual Extreme Energy Saver restore for users who intentionally left Manual mode in Extreme Energy Saver.
- Added CPU/disk settled-system monitoring before restoring manual Extreme Energy Saver.
- Added concise diagnostics for safety-guard decisions, restore cancellation, restore fallback, and final restore timing.

### Changed

- Automatic Mode startup/shutdown safety now prefers the configured Using PC profile when the active profile is lower-power than expected.
- Manual Extreme Energy Saver startup/shutdown safety uses Cool & Quiet as the temporary safe profile, then restores Extreme Energy Saver after the system settles or after a 5-minute fallback timeout.
- Diagnostic logging is capped to avoid unbounded local log growth.

### Notes

- This is a beta/prerelease build because it changes startup, shutdown, and sign-in behavior.
- Recommended validation: test manual Extreme Energy Saver restart, cancelled shutdown/restart, Automatic Mode Away restart, and normal Automatic Mode startup in a VM before publishing as stable.

### Release assets

- `PowerModeSetup-v2.7.16-beta.1.exe`
- `PowerModeSetup-v2.7.16-beta.1.exe.sha256`

## v2.7.15

First GitHub-ready public release candidate.

### Highlights

- Windows 11-style tray flyout.
- Five custom desktop-oriented power profiles.
- Optional Automatic Mode.
- Away mode with fullscreen-app pause behavior.
- Power history with summary, event log, and stats.
- Release installer with dependency checks.
- Clean release build: 0 warnings, 0 errors.

### Release assets

- `PowerModeSetup-v2.7.15.exe`
- `PowerModeSetup-v2.7.15.exe.sha256`
