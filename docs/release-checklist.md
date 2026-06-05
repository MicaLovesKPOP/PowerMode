# Release checklist

## Before building

- Confirm version in `VERSION.txt`.
- Confirm version in `winui/PowerModeTray.WinUI.csproj`.
- Confirm version in `installer/PowerMode.iss`.
- Confirm README expected output version.
- Confirm changelog entry.
- Confirm beta release notes in `docs/release-notes-v2.7.16-beta.1.md`.

## Build

Run:

```bat
Build-Release-Installer.cmd
```

Expected beta outputs on this branch:

```text
dist\PowerModeSetup-v2.7.16-beta.1.exe
dist\PowerModeSetup-v2.7.16-beta.1.exe.sha256
dist\build-release-installer.log
```

## Verify

- Build succeeds with 0 warnings and 0 errors.
- Installer runs cleanly.
- Tray app starts after install.
- Left-click flyout opens.
- Right-click menu opens.
- Start with Windows works.
- Power history opens.
- Uninstall removes app files and startup entry.

## Beta-specific verification

- Manual mode -> Extreme Energy Saver -> restart.
- Manual mode -> Extreme Energy Saver -> shutdown/restart request -> cancelled shutdown/restart if possible.
- Automatic Mode enabled -> Away profile active -> restart.
- Automatic Mode enabled -> normal startup.
- Confirm startup/shutdown safety diagnostics under:

```text
%LOCALAPPDATA%\MicaLovesKPOP\PowerMode\PowerModeTray-diagnostic.log
```

## GitHub prerelease

Tag:

```text
v2.7.16-beta.1
```

Title:

```text
Power Mode v2.7.16-beta.1
```

Upload:

```text
PowerModeSetup-v2.7.16-beta.1.exe
PowerModeSetup-v2.7.16-beta.1.exe.sha256
```

Mark the GitHub release as a prerelease/beta.
