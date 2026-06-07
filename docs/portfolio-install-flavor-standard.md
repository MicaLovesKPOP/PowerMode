# KomCom portfolio install flavor standard v1

This is a portfolio-level design standard for future KomCom-aware apps, tools, scripts, and installers.

It is **not implemented in Power Mode yet**. Current Power Mode beta remains a normal public build and uses the standard app-data layout.

## Purpose

KomCom mode should mean one clear thing:

> This app/tool was installed or provisioned by KomCom before the PC was delivered to the customer.

KomCom mode should **not** mean:

> Every future app the user installs on a KomCom PC automatically becomes KomCom-branded.

This keeps normal user-installed software normal, while allowing KomCom-delivered software to have a clean shared identity.

## Flavors

Each installed product has exactly one install flavor:

```text
Standard
KomComProvisioned
```

One installed product instance means:

```text
one product identity
one install flavor
one storage root
one startup identity
one uninstall/upgrade identity
```

No app should dynamically switch flavor after install.

## Storage roots

For an app with `AppId = PowerMode`:

```text
Standard:
  %LOCALAPPDATA%\PowerMode
  C:\ProgramData\PowerMode

KomComProvisioned:
  %LOCALAPPDATA%\KomCom\PowerMode
  C:\ProgramData\KomCom\PowerMode
```

For other apps, replace `PowerMode` with the app's stable `AppId`.

The `AppId` should be stable, short, and filesystem-safe. Prefer names like:

```text
PowerMode
WindFlow
CrashdayCarInfoAssistant
```

## Install behavior

### Fresh public install

A normal public installer or public install script uses `Standard` unless it is explicitly told otherwise.

```text
PowerModeSetup-vX.exe
```

Result:

```text
InstallFlavor = Standard
```

### KomCom pre-delivery provisioning install

KomCom installs/preloads software through a clear wrapper or argument before shipping the PC.

Example wrapper:

```text
Install-KomCom-PowerMode.cmd
```

Example internal call:

```text
PowerModeSetup-vX.exe /KomComProvisioned
```

For script-only tools:

```text
powershell.exe -ExecutionPolicy Bypass -File .\Install-AppName.ps1 -Flavor KomComProvisioned
```

Result:

```text
InstallFlavor = KomComProvisioned
ProvisionedBy = KomCom
```

## Update behavior

Update source must not decide flavor.

If an existing install is found, the existing install flavor always wins:

```text
Existing Standard install + public installer:
  update in place as Standard

Existing KomComProvisioned install + public GitHub installer:
  update in place as KomComProvisioned

Existing KomComProvisioned install + KomCom wrapper:
  update in place as KomComProvisioned
```

A user downloading a newer version from GitHub should update the existing app, not create a second Standard install next to a KomCom-provisioned install.

## Product identity vs install flavor

Keep these separate:

```text
Product identity:
  This is Power Mode.
  Used for upgrade detection, uninstall identity, single-instance identity, and product repair.

Install flavor:
  Standard or KomComProvisioned.
  Used for storage roots, logs, provisioning identity, and optional KomCom labeling.
```

For the same underlying app, public and KomCom-provisioning installers should normally share the same product upgrade identity. The flavor should be stored as install state, not encoded as a separate product.

## Conflict behavior

Do not guess silently.

If the installer finds a conflicting state, it should stop, explain, and offer an explicit repair/migration path when available.

Examples:

```text
Standard data exists, KomComProvisioned install requested, no existing install identity:
  ask/stop; do not silently import

KomCom data exists, Standard install requested, no existing install identity:
  ask/stop; do not silently overwrite

Both Standard and KomCom data exist:
  ask/stop; do not guess

Existing install identity exists:
  preserve that flavor and update in place
```

## OEM detection rule

OEM/manufacturer/system info containing `KomCom` may be used only as a provisioning convenience.

Allowed:

```text
KomCom provisioning script sees OEM contains KomCom
  -> defaults to KomComProvisioned
```

Not allowed:

```text
App starts, sees OEM contains KomCom
  -> silently switches an already-installed Standard app into KomCom paths
```

OEM detection is an install/provisioning input, not a live runtime flavor switch.

## Single-instance rule

Tray/helper apps should use a single-instance guard.

For one installed product, there should not be multiple running instances competing for the same tray icon, startup entry, profile changes, or logs.

For Power Mode-style tray apps:

```text
One product mutex per installed product.
Existing install flavor does not create a second runtime identity.
```

## Stored install identity

Each app should store enough install identity for the installer and runtime app to agree.

Minimum fields:

```text
AppId = PowerMode
InstallFlavor = Standard | KomComProvisioned
InstallRoot = <installed program folder>
DataRoot = <resolved local data root>
ProvisionedBy = KomCom | <empty>
FlavorLockedAtUtc = <timestamp>
```

The exact storage location can be chosen per app, but installer and runtime must both read the same source of truth.

For installer-based apps, prefer a stable product-level registry key rather than deriving flavor from folders at runtime.

## Implementation checklist for future apps

Before implementing KomComProvisioned mode in an app, confirm:

```text
[ ] App has a stable AppId.
[ ] Installer has one product upgrade identity.
[ ] InstallFlavor is stored once and preserved on updates.
[ ] Fresh public install defaults to Standard.
[ ] KomCom wrapper/argument can request KomComProvisioned.
[ ] Existing install flavor overrides installer default.
[ ] Standard and KomCom data roots are never mixed.
[ ] Conflict states stop/explain instead of guessing.
[ ] Public installer updates KomComProvisioned install in place.
[ ] KomCom wrapper updates existing Standard install only through explicit migration/repair.
[ ] Tray/helper app has a single-instance guard.
[ ] Logs/config/cache all use the same resolved data root.
[ ] Uninstaller removes only the matching install's program files and handles data according to uninstall policy.
[ ] Documentation states that KomCom mode is for KomCom-provisioned software, not all software on a KomCom PC.
```

## Current Power Mode status

Power Mode currently remains `Standard` only.

Current standard paths:

```text
%LOCALAPPDATA%\PowerMode
C:\ProgramData\PowerMode
```

Do not implement KomComProvisioned behavior in Power Mode until this standard is intentionally adopted for that app.
