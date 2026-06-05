Power Mode release installer build
==================================

Requirements on your build PC
-----------------------------
- Windows 10/11 x64
- Visual Studio 2022+ with the workloads/components required for WinUI 3, .NET desktop, and MSBuild
- Inno Setup 6

Build
-----
Run:

  Build-Release-Installer.cmd

The build script will:
1. Publish the WinUI tray app to winui\publish
2. Remove *.pdb debug symbols from the release payload
3. Compile installer\PowerMode.iss with Inno Setup
4. Output the installer to dist\PowerModeSetup-v2.7.16-beta.1.exe
5. Write a SHA256 checksum to dist\PowerModeSetup-v2.7.16-beta.1.exe.sha256

Installer behavior
------------------
The release installer:
- installs Power Mode to Program Files
- appears in Windows Settings > Apps > Installed apps as Power Mode
- checks/downloads required Microsoft runtimes if missing:
  - .NET 8 Desktop Runtime x64
  - Microsoft Windows App Runtime 1.6 x64
- stops an already-running Power Mode tray process before copying files
- creates/repairs the five Power Mode profiles
- sets Optimized Performance active on first setup/repair
- sets the matching native Windows Power mode overlay
- installs the current-user Startup folder shortcut
- starts Power Mode after setup
- best-effort promotes the tray icon into the visible tray area

Beta note
---------
v2.7.16-beta.1 adds startup/shutdown safety behavior. Before publishing it as stable, validate restart, cancelled shutdown/restart, Automatic Mode Away restart, and normal Automatic Mode startup in a Windows VM.

Uninstall behavior
------------------
Uninstalling from Windows Settings:
- stops Power Mode
- removes the startup entry
- restores Power Mode global behavior changes where applicable
- removes app files
- asks whether to restore standard Windows power plans

Restoring standard Windows power plans is optional because it removes all custom power plans on the PC, not only Power Mode profiles.

Build troubleshooting
---------------------
The release build writes a log to:

  dist\build-release-installer.log

If the CMD window closes or no installer appears, open that log first.

Inno Setup custom install path
------------------------------
If Inno Setup is installed somewhere custom and the build script cannot find it, set ISCC_EXE before building, for example:

  set ISCC_EXE=D:\Programs\Inno Setup 6\ISCC.exe
  Build-Release-Installer.cmd

Output files
------------
Expected beta outputs on this branch:

  dist\PowerModeSetup-v2.7.16-beta.1.exe
  dist\PowerModeSetup-v2.7.16-beta.1.exe.sha256
  dist\build-release-installer.log

Notes
-----
- The setup EXE includes Power Mode version metadata.
- The setup EXE uses the Power Mode app icon.
- Installer PowerShell work runs hidden so users do not see raw PowerShell consoles.
- Dependency/setup logs are written under:
  C:\ProgramData\MicaLovesKPOP\PowerMode\logs
- User-facing Power history is stored under:
  %LOCALAPPDATA%\MicaLovesKPOP\PowerMode\logs
- Startup/shutdown safety diagnostics are written to:
  %LOCALAPPDATA%\MicaLovesKPOP\PowerMode\PowerModeTray-diagnostic.log
