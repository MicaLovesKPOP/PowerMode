# Building Power Mode

## Requirements

- Windows 10/11 x64
- Visual Studio 2022+ with .NET desktop and WinUI 3 support
- Inno Setup 6

## Build the release installer

Run from the repository root:

```bat
Build-Release-Installer.cmd
```

Expected outputs:

```text
dist\PowerModeSetup-v2.7.15.exe
dist\PowerModeSetup-v2.7.15.exe.sha256
dist\build-release-installer.log
```

## Inno Setup custom install path

If Inno Setup is installed somewhere custom and the build script cannot find it, set `ISCC_EXE` before building:

```bat
set ISCC_EXE=D:\Programs\Inno Setup 6\ISCC.exe
Build-Release-Installer.cmd
```

## Troubleshooting

Open this file first if the build fails:

```text
dist\build-release-installer.log
```

A clean release build should end with:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
```
