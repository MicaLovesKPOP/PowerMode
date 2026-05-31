param(
    [string]$ExePath,
    [switch]$StartNow
)

$ErrorActionPreference = 'Stop'

$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ExePath) {
    $candidate = Join-Path $ScriptRoot 'PowerModeTray.exe'
    if (Test-Path $candidate) {
        $ExePath = $candidate
    } else {
        $candidate = Join-Path (Split-Path -Parent $ScriptRoot) 'PowerModeTray.exe'
        if (Test-Path $candidate) {
            $ExePath = $candidate
        }
    }
}

if (-not $ExePath -or -not (Test-Path $ExePath)) {
    throw "PowerModeTray.exe was not found. ExePath='$ExePath'"
}

$exe = (Resolve-Path $ExePath).Path

# Standard Win32 per-user startup mechanism:
# create a shortcut in the user's Startup folder. This is the same class of
# startup registration many normal desktop apps use and avoids fragile
# schtasks quoting/localization issues.
$startupFolder = [Environment]::GetFolderPath('Startup')
New-Item -Path $startupFolder -ItemType Directory -Force | Out-Null

$shortcutPath = Join-Path $startupFolder 'Power Mode.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.Arguments = '--startup'
$shortcut.WorkingDirectory = Split-Path -Parent $exe
$shortcut.Description = 'Start Power Mode'
$shortcut.IconLocation = "$exe,0"
$shortcut.Save()

# Clean old experiments/legacy entries so there is only one startup mechanism.
schtasks.exe /Delete /TN 'Power Mode' /F 2>$null | Out-Null

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty -Path $runKey -Name 'Power Mode' -Force -ErrorAction SilentlyContinue

$approvedKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
Remove-ItemProperty -Path $approvedKey -Name 'Power Mode' -Force -ErrorAction SilentlyContinue

Write-Host "Power Mode startup shortcut installed: $shortcutPath"
Write-Host "Power Mode target: $exe"

if ($StartNow) {
    Write-Host "Starting Power Mode..."
    Start-Process -FilePath $exe | Out-Null
}
