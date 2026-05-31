# Troubleshooting

## Power Mode does not appear in the tray

1. Open Start and search for Power Mode.
2. Run Power Mode manually.
3. Right-click the tray icon and check **Start with Windows**.
4. If needed, reinstall or repair using the latest setup EXE.

## Profiles are missing or broken

Run the installer again. The installer creates or repairs the five Power Mode profiles.

## Power history looks outdated

Open **Power history** from the right-click tray menu. The app refreshes the readable summary before opening the folder.

## Installer dependency issues

The installer checks for:

- .NET 8 Desktop Runtime x64
- Microsoft Windows App Runtime 1.6 x64

Installer dependency logs are written under:

```text
C:\ProgramData\MicaLovesKPOP\PowerMode\logs
```

## Restoring standard Windows power plans

Uninstall can optionally restore standard Windows power plans.

Only use this option if you are okay with Windows removing all custom power plans on the PC, not only Power Mode profiles.
