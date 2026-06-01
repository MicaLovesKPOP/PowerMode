[CmdletBinding()]
param()

$ErrorActionPreference = 'SilentlyContinue'

$RegPath = 'HKCU:\Software\MicaLovesKPOP\PowerMode\GlobalBehavior'

function Restore-PowerTimeouts {
    param(
        [string]$ValueName,
        [string]$Subgroup,
        [string]$Setting
    )

    $stored = (Get-ItemProperty -LiteralPath $RegPath -Name $ValueName -ErrorAction SilentlyContinue).$ValueName
    if ([string]::IsNullOrWhiteSpace($stored)) { return }

    foreach ($line in ($stored -split "(`r`n|`n|`r)")) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line -split '\|'
        if ($parts.Count -ne 3) { continue }

        $scheme = $parts[0]
        $ac = $parts[1]
        $dc = $parts[2]

        & powercfg.exe /setacvalueindex $scheme $Subgroup $Setting $ac | Out-Null
        & powercfg.exe /setdcvalueindex $scheme $Subgroup $Setting $dc | Out-Null
    }
}

function Refresh-ActiveScheme {
    $list = & powercfg.exe /list 2>$null
    foreach ($line in $list) {
        if ($line -match '([0-9a-fA-F-]{36}).*\*') {
            & powercfg.exe /setactive $matches[1] | Out-Null
            break
        }
    }
}

function Restore-Screensaver {
    $props = Get-ItemProperty -LiteralPath $RegPath -ErrorAction SilentlyContinue
    $desktopPath = 'HKCU:\Control Panel\Desktop'

    if ($null -eq $props) { return }

    if ($null -ne $props.PreviousScreenSaveActive) {
        Set-ItemProperty -LiteralPath $desktopPath -Name 'ScreenSaveActive' -Value ([string]$props.PreviousScreenSaveActive)
    }

    if ($null -ne $props.PreviousScreenSaveTimeOut) {
        Set-ItemProperty -LiteralPath $desktopPath -Name 'ScreenSaveTimeOut' -Value ([string]$props.PreviousScreenSaveTimeOut)
    }

    if ($null -ne $props.PreviousScreenSaverExe -and -not [string]::IsNullOrWhiteSpace([string]$props.PreviousScreenSaverExe)) {
        Set-ItemProperty -LiteralPath $desktopPath -Name 'SCRNSAVE.EXE' -Value ([string]$props.PreviousScreenSaverExe)
    }

    try {
        Add-Type -Namespace Native -Name User32 -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)]
public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);
'@
        $active = (Get-ItemProperty -LiteralPath $desktopPath -Name 'ScreenSaveActive' -ErrorAction SilentlyContinue).ScreenSaveActive
        [Native.User32]::SystemParametersInfo(0x0011, $(if ($active -eq '0') { 0 } else { 1 }), $null, 0x0001 -bor 0x0002) | Out-Null
    } catch {}
}

if (Test-Path -LiteralPath $RegPath) {
    $props = Get-ItemProperty -LiteralPath $RegPath -ErrorAction SilentlyContinue

    if ($props.KeepPcAwake -eq 1) {
        Restore-PowerTimeouts -ValueName 'PreviousSleepTimeouts' -Subgroup 'SUB_SLEEP' -Setting 'STANDBYIDLE'
        Set-ItemProperty -LiteralPath $RegPath -Name 'KeepPcAwake' -Value 0 -Type DWord
    }

    if ($props.KeepScreenOn -eq 1) {
        Restore-PowerTimeouts -ValueName 'PreviousDisplayTimeouts' -Subgroup 'SUB_VIDEO' -Setting 'VIDEOIDLE'
        Set-ItemProperty -LiteralPath $RegPath -Name 'KeepScreenOn' -Value 0 -Type DWord
    }

    if ($props.DisableScreensaver -eq 1) {
        Restore-Screensaver
        Set-ItemProperty -LiteralPath $RegPath -Name 'DisableScreensaver' -Value 0 -Type DWord
    }

    Refresh-ActiveScheme
}
