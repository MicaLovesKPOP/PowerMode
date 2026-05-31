# Power Mode [![Download](https://img.shields.io/badge/Download-Latest_Release-brightgreen)](https://github.com/MicaLovesKPOP/PowerMode/releases/latest)

Power Mode is a Windows tray utility for switching between custom performance, quiet, and energy-saving power profiles from a polished Windows 11-style flyout.

It is designed for desktop PCs where the user wants quick control over responsiveness, fan noise, and background power use without digging through classic Control Panel power-plan pages.

## Download

Download the latest installer from the [latest release](https://github.com/MicaLovesKPOP/PowerMode/releases/latest).

For normal users, download:

```text
PowerModeSetup-v2.7.15.exe
```

The `.sha256` file is included for checksum verification.

## Highlights

- Windows 11-style left-click flyout for everyday power profile switching.
- Compact right-click menu for screen saver, display timeout, sleep timeout, startup, Power history, and exit.
- Five custom desktop-oriented power profiles:
  - Unrestrained Performance
  - Optimized Performance
  - Balanced Performance
  - Cool & Quiet
  - Extreme Energy Saver
- Optional Automatic Mode that switches to a lower-power Away profile when the user is inactive.
- Fullscreen apps pause Away mode by default, so games and fullscreen video are not interrupted.
- Power history with readable summary, event log, and compact stats storage.
- Release installer with dependency checks for .NET 8 Desktop Runtime and Windows App Runtime 1.6.
- Clean release build: 0 warnings, 0 errors in the current v2.7.15 release build.

## Screenshots

Screenshots are planned. Suggested files:

```text
assets/screenshots/main-flyout-manual.png
assets/screenshots/main-flyout-automatic.png
assets/screenshots/right-click-menu.png
assets/screenshots/installer.png
```

## Profiles

| Profile | Purpose |
| --- | --- |
| Unrestrained Performance | Maximum responsiveness, with higher power use, heat, and fan noise. |
| Optimized Performance | Recommended default for a responsive desktop PC. |
| Balanced Performance | Good balance of responsiveness, noise, and efficiency. |
| Cool & Quiet | Lower heat and noise by preventing CPU boost. |
| Extreme Energy Saver | Best for background tasks or when away from the PC. |

The profiles are intentionally desktop-focused first. Laptop/battery behavior is not the primary design target yet.

## Automatic Mode

Automatic Mode is optional and disabled by default. When enabled, it uses two states:

- **Using PC**: the profile used while the user is active.
- **Away**: a lower-power profile used after the configured idle delay.

Away mode is blocked by fullscreen foreground apps by default. Returning to the PC immediately exits Away mode.

## Power history

Power history is available from the right-click tray menu.

Opening Power history refreshes the readable summary before opening the folder, so the summary reflects the current session up to that moment without adding fake event-log entries.

It opens a local folder containing:

```text
power-mode-summary.txt
power-mode-events.log
power-mode-stats.json
```

The history tracks Manual time, Automatic active time, Away time, fullscreen-paused time, profile time, switches, and profile changes. The event log is capped to stay small.

## Installer behavior

The release installer:

- installs Power Mode to Program Files;
- checks/downloads required Microsoft runtimes if missing;
- creates or repairs the five Power Mode profiles;
- sets Optimized Performance active on first setup/repair;
- installs the current-user Startup folder shortcut;
- starts Power Mode after setup;
- best-effort promotes the tray icon into the visible tray area.

Uninstall removes the app and startup entry. Restoring standard Windows power plans is optional because it removes all custom power plans on the PC, not only Power Mode profiles.

## Requirements

Runtime requirements:

- Windows 10/11 x64
- .NET 8 Desktop Runtime x64
- Microsoft Windows App Runtime 1.6 x64

Build requirements:

- Windows 10/11 x64
- Visual Studio 2022+ with .NET desktop and WinUI 3 support
- Inno Setup 6

## Building

Run:

```bat
Build-Release-Installer.cmd
```

Expected outputs:

```text
dist\PowerModeSetup-v2.7.15.exe
dist\PowerModeSetup-v2.7.15.exe.sha256
dist\build-release-installer.log
```

More details are in [`docs/building.md`](docs/building.md).

## Status

Current release candidate: **v2.7.15**

Release build status from local testing:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
```

## Known limitations

- Desktop-first behavior; laptop/battery-specific logic is future work.
- Estimated energy savings are intentionally not shown yet, because accurate savings require measured wattage or hardware-specific calibration.
- The app currently focuses on the five built-in Power Mode profiles.

## License

MIT License. See [`LICENSE`](LICENSE).
