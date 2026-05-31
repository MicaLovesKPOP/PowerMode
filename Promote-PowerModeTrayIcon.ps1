[CmdletBinding()]
param()
$ErrorActionPreference = 'Continue'
$root = 'HKCU:\Control Panel\NotifyIconSettings'
if (-not (Test-Path -LiteralPath $root)) {
    Write-Host 'NotifyIconSettings registry path does not exist yet. Start Power Mode once, then run this again.'
    exit 0
}
$changed = 0
foreach ($k in Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue) {
    $p = Get-ItemProperty -LiteralPath $k.PSPath -ErrorAction SilentlyContinue
    $tip = ''
    foreach ($n in @('InitialTooltip','ToolTip','Tooltip')) {
        if ($p.PSObject.Properties.Name -contains $n -and $p.$n) { $tip = [string]$p.$n; break }
    }
    $exe = if ($p.PSObject.Properties.Name -contains 'ExecutablePath') { [string]$p.ExecutablePath } else { '' }
    if ($tip -like 'Power Mode*' -or $tip -like 'Power mode*' -or $exe -like '*PowerModeTray.exe*' -or ($exe -like '*powershell*' -and $tip -like '*Power*')) {
        New-ItemProperty -LiteralPath $k.PSPath -Name IsPromoted -PropertyType DWord -Value 1 -Force | Out-Null
        # Normalize the Settings display name where Windows stores it in NotifyIconSettings.
        foreach ($displayName in @('InitialTooltip','ToolTip','Tooltip')) {
            New-ItemProperty -LiteralPath $k.PSPath -Name $displayName -PropertyType String -Value 'Power Mode' -Force | Out-Null
        }
        $changed++
    }
}
if ($changed -gt 0) {
    Write-Host "Requested taskbar-corner visibility for $changed matching Power Mode entry/entries."
} else {
    Write-Host 'No matching Power Mode entry found yet. Start the tray app, wait a few seconds, then retry or open Settings > Personalization > Taskbar > Other system tray icons.'
}
