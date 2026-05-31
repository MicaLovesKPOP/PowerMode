# Release checklist

## Before building

- Confirm version in `VERSION.txt`.
- Confirm version in `winui/PowerModeTray.WinUI.csproj`.
- Confirm version in `installer/PowerMode.iss`.
- Confirm README expected output version.
- Confirm changelog entry.

## Build

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

## Verify

- Build succeeds with 0 warnings and 0 errors.
- Installer runs cleanly.
- Tray app starts after install.
- Left-click flyout opens.
- Right-click menu opens.
- Start with Windows works.
- Power history opens.
- Uninstall removes app files and startup entry.

## GitHub release

Tag:

```text
v2.7.15
```

Title:

```text
Power Mode v2.7.15
```

Upload:

```text
PowerModeSetup-v2.7.15.exe
PowerModeSetup-v2.7.15.exe.sha256
```
