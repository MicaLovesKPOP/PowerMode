[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ExePath,
    [switch]$SkipDependencyCheck
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$AppRoot = Split-Path -Parent $ScriptRoot
$LogDir = Join-Path $env:ProgramData 'MicaLovesKPOP\PowerMode\logs'
New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
$LogPath = Join-Path $LogDir ('release-install-{0}.log' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))

function Write-Step([string]$Message) {
    $line = "[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $Message
    Write-Host $line
    Add-Content -LiteralPath $LogPath -Value $line
}

function Invoke-Step {
    param(
        [Parameter(Mandatory=$true)][string]$Title,
        [Parameter(Mandatory=$true)][scriptblock]$Script
    )

    Write-Step $Title
    try {
        & $Script *>&1 | Tee-Object -FilePath $LogPath -Append
        if (-not $?) { throw "$Title failed." }
    } catch {
        Write-Step "FAILED: $Title"
        throw
    }
}


function Invoke-OptionalStep {
    param(
        [Parameter(Mandatory=$true)][string]$Title,
        [Parameter(Mandatory=$true)][scriptblock]$Script
    )

    Write-Step $Title
    try {
        & $Script *>&1 | Tee-Object -FilePath $LogPath -Append
        if (-not $?) { Write-Step "WARNING: $Title reported failure."; return $false }
        return $true
    } catch {
        Write-Step "WARNING: $Title failed: $($_.Exception.Message)"
        return $false
    }
}

Write-Step 'Power Mode release setup started.'
Write-Step "AppRoot=$AppRoot"
Write-Step "ExePath=$ExePath"

if (-not $SkipDependencyCheck) {
    Invoke-Step 'Checking and installing required Microsoft runtimes' {
        & (Join-Path $ScriptRoot 'Install-Dependencies.ps1')
    }
} else {
    Write-Step 'Installer already ran the Microsoft runtime setup stage; verifying readiness before app launch.'
    Invoke-Step 'Verifying required Microsoft runtimes are ready for this Windows user' {
        & (Join-Path $ScriptRoot 'Install-Dependencies.ps1') -VerifyOnly -LogPath (Join-Path $LogDir 'dependency-verify.log')
    }
}

Invoke-Step 'Creating and repairing Power Mode power profiles' {
    & (Join-Path $ScriptRoot 'Ensure-MicaPowerPlans.ps1') -SetOptimizedActive
}

Invoke-OptionalStep 'Installing current-user Startup folder shortcut' {
    & (Join-Path $ScriptRoot 'Install-PowerModeTrayStartup.ps1') -ExePath $ExePath
} | Out-Null

Invoke-Step 'Stopping old running Power Mode process, if any' {
    & (Join-Path $ScriptRoot 'Stop-PowerModeTray.ps1')
}

Invoke-Step 'Starting Power Mode' {
    $process = Start-Process -FilePath $ExePath -PassThru
    Start-Sleep -Seconds 2
    if ($process.HasExited) {
        throw "Power Mode started but exited immediately. ExitCode=$($process.ExitCode). Check %LOCALAPPDATA%\MicaLovesKPOP\PowerMode\PowerModeTray-crash.log and Windows Event Viewer."
    }
}

Invoke-Step 'Requesting visible taskbar tray icon' {
    & (Join-Path $ScriptRoot 'Promote-PowerModeTrayIcon.ps1')
}

Write-Step 'Power Mode release setup completed.'
exit 0
