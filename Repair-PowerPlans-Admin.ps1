[CmdletBinding()]
param(
    [switch]$SetOptimizedActive,
    [switch]$Elevated
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$EnsureScript = Join-Path $ScriptRoot 'Ensure-MicaPowerPlans.ps1'

$LogDir = Join-Path $env:ProgramData 'MicaLovesKPOP\PowerMode\logs'
New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
$LogPath = Join-Path $LogDir ('power-plan-repair-{0}.log' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))

function Test-IsAdministrator {
    try {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    } catch {
        return $false
    }
}

if (-not (Test-Path -LiteralPath $EnsureScript)) {
    throw "Ensure-MicaPowerPlans.ps1 was not found next to this script: $EnsureScript"
}

if (-not (Test-IsAdministrator)) {
    Write-Host 'Power plan repair needs administrator rights.'
    Write-Host 'A Windows administrator/UAC prompt should appear now. Click Yes/Ja.'
    Write-Host ''

    $args = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $PSCommandPath,
        '-Elevated'
    )
    if ($SetOptimizedActive) { $args += '-SetOptimizedActive' }

    try {
        $process = Start-Process -FilePath powershell.exe -ArgumentList $args -Verb RunAs -Wait -PassThru -ErrorAction Stop
        if ($null -ne $process.ExitCode) {
            exit $process.ExitCode
        }
        exit 0
    } catch [System.ComponentModel.Win32Exception] {
        if ($_.Exception.NativeErrorCode -eq 1223) {
            Write-Host 'Power plan repair was cancelled at the Windows administrator/UAC prompt.'
            Write-Host 'Run Install-For-Testing.cmd again and click Yes/Ja when Windows asks for permission.'
            exit 1
        }
        Write-Host "Power plan repair could not be launched as administrator: $($_.Exception.Message)"
        exit 1
    } catch {
        Write-Host "Power plan repair could not be launched as administrator: $($_.Exception.Message)"
        exit 1
    }
}

Write-Host 'Running power plan repair as administrator...'
Write-Host "Log: $LogPath"
Write-Host ''

try {
    # Call the repair script explicitly instead of forwarding a string-array switch.
    # Some Windows PowerShell parsing paths treated the forwarded '-SetOptimizedActive'
    # as a positional argument, which made repair fail even though the target script
    # supports that switch.
    if ($SetOptimizedActive) {
        $repairOutput = & $EnsureScript -SetOptimizedActive *>&1
    } else {
        $repairOutput = & $EnsureScript *>&1
    }

    $repairOutput | Tee-Object -FilePath $LogPath

    Write-Host ''
    Write-Host "Power plan repair completed. Log: $LogPath"
    exit 0
} catch {
    $msg = "Power plan repair failed: $($_.Exception.Message)"
    Write-Host ''
    Write-Host $msg
    Add-Content -LiteralPath $LogPath -Value ''
    Add-Content -LiteralPath $LogPath -Value $msg
    exit 1
}
