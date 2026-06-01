[CmdletBinding()]
param(
    [switch]$Silent,
    [switch]$VerifyOnly,
    [switch]$CheckDotNetOnly,
    [switch]$CheckWindowsAppRuntimeOnly,
    [switch]$InstallDotNetOnly,
    [switch]$InstallWindowsAppRuntimeOnly,
    [string]$DotNetInstallerPath,
    [string]$WindowsAppRuntimeInstallerPath,
    [string]$LogPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:PowerModeDependencyTranscriptStarted = $false
if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
    try {
        $logDir = Split-Path -Parent $LogPath
        if ($logDir) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
        Start-Transcript -Path $LogPath -Append | Out-Null
        $script:PowerModeDependencyTranscriptStarted = $true
    } catch {
        Write-Warning "Could not start dependency-install transcript: $($_.Exception.Message)"
    }
}

trap {
    if ($script:PowerModeDependencyTranscriptStarted) {
        try { Stop-Transcript | Out-Null } catch {}
    }
    throw
}

function Test-IsAdministrator {
    try {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    } catch {
        return $false
    }
}

$RequiredDotNetMajor = 8
$RequiredWindowsAppRuntimeMajorMinor = '1.6'
$RequiredWindowsAppRuntimePackageVersion = [Version]'6000.401.2352.0'

function Get-DotNetExeCandidates {
    $candidates = @()

    try {
        $cmd = Get-Command dotnet.exe -ErrorAction SilentlyContinue
        if ($cmd -and $cmd.Source) { $candidates += $cmd.Source }
    } catch {}

    $pf = [Environment]::GetFolderPath('ProgramFiles')
    $pf86 = [Environment]::GetFolderPath('ProgramFilesX86')

    if ($pf) { $candidates += (Join-Path $pf 'dotnet\dotnet.exe') }
    if ($pf86) { $candidates += (Join-Path $pf86 'dotnet\dotnet.exe') }

    return @($candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique)
}

function Get-DotNetDesktopRuntimeFolders {
    $folders = @()

    $pf = [Environment]::GetFolderPath('ProgramFiles')
    $pf86 = [Environment]::GetFolderPath('ProgramFilesX86')

    if ($pf) { $folders += (Join-Path $pf 'dotnet\shared\Microsoft.WindowsDesktop.App') }
    if ($pf86) { $folders += (Join-Path $pf86 'dotnet\shared\Microsoft.WindowsDesktop.App') }

    return @($folders | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique)
}

function Test-DotNetDesktopRuntime8 {
    foreach ($exe in @(Get-DotNetExeCandidates)) {
        try {
            $runtimes = & $exe --list-runtimes 2>$null
            if ($runtimes | Select-String -Quiet 'Microsoft\.WindowsDesktop\.App\s+8\.') {
                return $true
            }
        } catch {}
    }

    foreach ($folder in @(Get-DotNetDesktopRuntimeFolders)) {
        try {
            $versions = @(Get-ChildItem -LiteralPath $folder -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -like '8.*' })
            if ($versions.Count -gt 0) { return $true }
        } catch {}
    }

    return $false
}

function Get-DotNetRuntimeDiagnostic {
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('dotnet.exe candidates:')
    $exes = @(Get-DotNetExeCandidates)
    if ($exes.Count -eq 0) {
        $lines.Add('  (none found)')
    } else {
        foreach ($exe in $exes) {
            $lines.Add("  $exe")
            try {
                $runtimeLines = & $exe --list-runtimes 2>$null
                foreach ($line in $runtimeLines) { $lines.Add("    $line") }
            } catch {
                $lines.Add("    failed to query: $($_.Exception.Message)")
            }
        }
    }

    $lines.Add('')
    $lines.Add('Microsoft.WindowsDesktop.App folders:')
    $folders = @(Get-DotNetDesktopRuntimeFolders)
    if ($folders.Count -eq 0) {
        $lines.Add('  (none found)')
    } else {
        foreach ($folder in $folders) {
            $lines.Add("  $folder")
            try {
                $children = @(Get-ChildItem -LiteralPath $folder -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name)
                if ($children.Count -eq 0) {
                    $lines.Add('    (no version folders found)')
                } else {
                    foreach ($child in $children) { $lines.Add("    $child") }
                }
            } catch {
                $lines.Add("    failed to list: $($_.Exception.Message)")
            }
        }
    }

    return ($lines -join [Environment]::NewLine)
}

function Get-WindowsAppRuntimePackages {
    param([switch]$AllUsers)

    $all = @()

    try {
        if ($AllUsers) {
            $all += @(Get-AppxPackage -AllUsers -ErrorAction SilentlyContinue | Where-Object {
                $_.Name -like 'Microsoft.WindowsAppRuntime*' -or
                $_.Name -like 'Microsoft.WinAppRuntime*' -or
                $_.Name -like 'MicrosoftCorporationII.WinAppRuntime*' -or
                $_.PackageFamilyName -like 'Microsoft.WindowsAppRuntime*' -or
                $_.PackageFamilyName -like 'Microsoft.WinAppRuntime*' -or
                $_.PackageFamilyName -like 'MicrosoftCorporationII.WinAppRuntime*'
            })
        } else {
            $all += @(Get-AppxPackage -ErrorAction SilentlyContinue | Where-Object {
                $_.Name -like 'Microsoft.WindowsAppRuntime*' -or
                $_.Name -like 'Microsoft.WinAppRuntime*' -or
                $_.Name -like 'MicrosoftCorporationII.WinAppRuntime*' -or
                $_.PackageFamilyName -like 'Microsoft.WindowsAppRuntime*' -or
                $_.PackageFamilyName -like 'Microsoft.WinAppRuntime*' -or
                $_.PackageFamilyName -like 'MicrosoftCorporationII.WinAppRuntime*'
            })
        }
    } catch {}

    return @($all | Sort-Object PackageFullName -Unique)
}

function Get-WindowsAppRuntime16State {
    $packages = @(Get-WindowsAppRuntimePackages)

    $state = [ordered]@{
        FrameworkX64 = $false
        MainX64      = $false
        SingletonX64 = $false
        DdlmX64      = $false
    }

    foreach ($pkg in $packages) {
        $name = [string]$pkg.Name
        $family = [string]$pkg.PackageFamilyName
        $fullName = [string]$pkg.PackageFullName
        $version = [Version]$pkg.Version
        $arch = [string]$pkg.Architecture
        $isX64 = ($arch -eq 'X64' -or $fullName -match '_x64__')

        if (-not $isX64 -or $version -lt $RequiredWindowsAppRuntimePackageVersion) {
            continue
        }

        # Framework package, e.g.:
        # Microsoft.WindowsAppRuntime.1.6_6000.519.329.0_x64__8wekyb3d8bbwe
        if ($name -eq 'Microsoft.WindowsAppRuntime.1.6') {
            $state.FrameworkX64 = $true
            continue
        }

        # Main package, e.g.:
        # MicrosoftCorporationII.WinAppRuntime.Main.1.6_6000.519.329.0_x64__8wekyb3d8bbwe
        if ($name -eq 'MicrosoftCorporationII.WinAppRuntime.Main.1.6') {
            $state.MainX64 = $true
            continue
        }

        # Singleton package does not include 1.6 in the name.
        # Newer Windows App Runtime lines can install a newer Singleton package
        # (for example 8002.*) and still satisfy apps using Windows App Runtime 1.6.
        # Requiring exactly 6000.* caused false negatives on systems that already
        # had a newer Singleton package registered for the current user.
        if ($name -eq 'MicrosoftCorporationII.WinAppRuntime.Singleton' -and $version.Major -ge 6000) {
            $state.SingletonX64 = $true
            continue
        }

        # DDLM package name includes the 6000.* runtime line and architecture suffix, e.g.:
        # Microsoft.WinAppRuntime.DDLM.6000.519.329.0-x6_6000.519.329.0_x64__8wekyb3d8bbwe
        if ($name -like 'Microsoft.WinAppRuntime.DDLM.6000.*' -and $version.Major -eq 6000) {
            $state.DdlmX64 = $true
            continue
        }
    }

    $state['Complete'] = ($state.FrameworkX64 -and $state.MainX64 -and $state.SingletonX64 -and $state.DdlmX64)
    return [pscustomobject]$state
}

function Test-WindowsAppRuntime16Complete {
    $state = Get-WindowsAppRuntime16State
    return [bool]$state.Complete
}

function Get-WindowsAppRuntimeDiagnostic {
    $packages = @(Get-WindowsAppRuntimePackages)
    $allUserPackages = @(Get-WindowsAppRuntimePackages -AllUsers)
    $state = Get-WindowsAppRuntime16State

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("Required: complete Windows App Runtime 1.6 x64 set, package version >= $RequiredWindowsAppRuntimePackageVersion")
    $lines.Add("Required set:")
    $lines.Add("  Framework: Microsoft.WindowsAppRuntime.1.6 x64")
    $lines.Add("  Main:      MicrosoftCorporationII.WinAppRuntime.Main.1.6 x64")
    $lines.Add("  Singleton: MicrosoftCorporationII.WinAppRuntime.Singleton x64, 6000.* or newer")
    $lines.Add("  DDLM:      Microsoft.WinAppRuntime.DDLM.6000.* x64")
    $lines.Add('')
    $lines.Add('Detected required-set state:')
    $lines.Add("  FrameworkX64=$($state.FrameworkX64)")
    $lines.Add("  MainX64=$($state.MainX64)")
    $lines.Add("  SingletonX64=$($state.SingletonX64)")
    $lines.Add("  DdlmX64=$($state.DdlmX64)")
    $lines.Add("  Complete=$($state.Complete)")
    $lines.Add('')
    $lines.Add('Detected Windows App Runtime related packages for the installing/current user:')

    if ($packages.Count -eq 0) {
        $lines.Add('  (none found)')
    } else {
        foreach ($pkg in $packages) {
            $lines.Add("  Name=$($pkg.Name)")
            $lines.Add("    Version=$($pkg.Version)")
            $lines.Add("    Architecture=$($pkg.Architecture)")
            $lines.Add("    PackageFullName=$($pkg.PackageFullName)")
            $lines.Add("    PackageFamilyName=$($pkg.PackageFamilyName)")
        }
    }

    $lines.Add('')
    $lines.Add('Detected Windows App Runtime related packages across all users (diagnostic only, not used for readiness):')
    if ($allUserPackages.Count -eq 0) {
        $lines.Add('  (none found)')
    } else {
        foreach ($pkg in $allUserPackages) {
            $lines.Add("  Name=$($pkg.Name)")
            $lines.Add("    Version=$($pkg.Version)")
            $lines.Add("    Architecture=$($pkg.Architecture)")
            $lines.Add("    PackageFullName=$($pkg.PackageFullName)")
            $lines.Add("    PackageFamilyName=$($pkg.PackageFamilyName)")
        }
    }

    return ($lines -join [Environment]::NewLine)
}

function Wait-ForInstallerTest {
    param(
        [Parameter(Mandatory=$true)][string]$Name,
        [Parameter(Mandatory=$true)][scriptblock]$Test,
        [int]$TimeoutSeconds = 90
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (& $Test) { return $true }
        Start-Sleep -Seconds 3
    }

    return [bool](& $Test)
}

function Invoke-InstallerIfMissing {
    param(
        [Parameter(Mandatory=$true)][string]$Name,
        [Parameter(Mandatory=$true)][scriptblock]$Test,
        [Parameter(Mandatory=$true)][string]$DownloadUrl,
        [Parameter(Mandatory=$true)][string]$FileName,
        [string]$InstallerPath,
        [string[]]$InstallerArguments = @(),
        [scriptblock]$Diagnostic
    )

    if (& $Test) {
        Write-Host "$Name already appears to be installed for the installing/current user."
        return
    }

    if ($VerifyOnly) {
        Write-Host ""
        Write-Host "$Name was not detected for the installing/current user."
        if ($Diagnostic) {
            Write-Host ""
            Write-Host "Current diagnostic:"
            Write-Host (& $Diagnostic)
            Write-Host ""
        }
        throw "$Name is required but is not installed for the installing/current user."
    }

    $tempPath = if (-not [string]::IsNullOrWhiteSpace($InstallerPath)) { $InstallerPath } else { Join-Path $env:TEMP $FileName }
    Write-Host ""
    Write-Host "$Name was not detected for the installing/current user."

    if ($Diagnostic) {
        Write-Host ""
        Write-Host "Current diagnostic:"
        Write-Host (& $Diagnostic)
        Write-Host ""
    }

    if (-not [string]::IsNullOrWhiteSpace($InstallerPath)) {
        if (-not (Test-Path -LiteralPath $InstallerPath)) {
            throw "$Name installer path was provided but does not exist: $InstallerPath"
        }
        Write-Host "Using installer already downloaded by setup: $InstallerPath"
    } else {
        Write-Host "Downloading installer: $DownloadUrl"
        Invoke-WebRequest -Uri $DownloadUrl -OutFile $tempPath
    }

    Write-Host "Launching $Name installer..."
    if ($InstallerArguments.Count -gt 0) {
        Write-Host "Installer arguments: $($InstallerArguments -join ' ')"
    }
    Write-Host ""
    Write-Host "IMPORTANT:"
    Write-Host "A Windows administrator/UAC prompt may appear now."
    Write-Host "Please click Yes/Ja. If this prompt is cancelled, setup cannot install the missing runtime."
    Write-Host ""

    try {
        $alreadyElevated = Test-IsAdministrator
        $startArgs = @{
            FilePath = $tempPath
            Wait = $true
            PassThru = $true
            ErrorAction = 'Stop'
        }

        if ($InstallerArguments.Count -gt 0) {
            $startArgs.ArgumentList = $InstallerArguments
        }

        if ($Silent) {
            $startArgs.WindowStyle = 'Hidden'
        }

        if (-not $alreadyElevated) {
            $startArgs.Verb = 'RunAs'
        }

        $process = Start-Process @startArgs
    } catch [System.ComponentModel.Win32Exception] {
        if ($_.Exception.NativeErrorCode -eq 1223) {
            throw "$Name installer was cancelled at the Windows administrator/UAC prompt. Run Install-For-Testing.cmd again and click Yes/Ja when Windows asks for permission."
        }
        throw "$Name installer could not be launched: $($_.Exception.Message)"
    } catch {
        throw "$Name installer could not be launched: $($_.Exception.Message)"
    }

    if ($null -ne $process.ExitCode) {
        Write-Host "$Name installer exit code: $($process.ExitCode)"
        if ($process.ExitCode -eq 3010) {
            Write-Host "$Name installer reports that a restart may be required."
        }
    }

    Write-Host "Waiting for $Name to become visible to the current Windows user..."
    $isNowInstalled = Wait-ForInstallerTest -Name $Name -Test $Test -TimeoutSeconds 90

    if (-not $isNowInstalled) {
        Write-Host ""
        Write-Warning "$Name still was not detected for the installing/current user for the installing/current user after the installer completed."

        if ($Diagnostic) {
            Write-Host ""
            Write-Host "Diagnostic:"
            Write-Host (& $Diagnostic)
        }

        throw "$Name still does not appear to be installed for the installing/current user after the installer completed."
    }

    Write-Host "$Name installation looks good."
}

$dotNetInstallerArguments = if ($Silent) { @('/install', '/quiet', '/norestart') } else { @() }
$windowsAppRuntimeInstallerArguments = if ($Silent) { @('--quiet', '--force') } else { @('--force') }

function Complete-DependencyScript {
    param([int]$ExitCode = 0)
    if ($script:PowerModeDependencyTranscriptStarted) {
        try {
            Stop-Transcript | Out-Null
            $script:PowerModeDependencyTranscriptStarted = $false
        } catch {}
    }
    exit $ExitCode
}

if ($CheckDotNetOnly) {
    if (Test-DotNetDesktopRuntime8) { Complete-DependencyScript 0 } else { Complete-DependencyScript 1 }
}

if ($CheckWindowsAppRuntimeOnly) {
    if (Test-WindowsAppRuntime16Complete) { Complete-DependencyScript 0 } else { Complete-DependencyScript 1 }
}

if ($InstallDotNetOnly) {
    Invoke-InstallerIfMissing -Name '.NET 8 Desktop Runtime x64' `
        -Test ${function:Test-DotNetDesktopRuntime8} `
        -DownloadUrl 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe' `
        -FileName 'windowsdesktop-runtime-8-x64.exe' `
        -InstallerPath $DotNetInstallerPath `
        -InstallerArguments $dotNetInstallerArguments `
        -Diagnostic ${function:Get-DotNetRuntimeDiagnostic}

    Write-Host ''
    Write-Host '.NET 8 Desktop Runtime x64 appears to be installed.'
    Complete-DependencyScript 0
}

if ($InstallWindowsAppRuntimeOnly) {
    Invoke-InstallerIfMissing -Name 'complete Windows App Runtime 1.6 x64 package set' `
        -Test ${function:Test-WindowsAppRuntime16Complete} `
        -DownloadUrl 'https://aka.ms/windowsappsdk/1.6/latest/windowsappruntimeinstall-x64.exe' `
        -FileName 'windowsappruntimeinstall-1.6-x64.exe' `
        -InstallerPath $WindowsAppRuntimeInstallerPath `
        -InstallerArguments $windowsAppRuntimeInstallerArguments `
        -Diagnostic ${function:Get-WindowsAppRuntimeDiagnostic}

    Write-Host ''
    Write-Host 'Windows App Runtime 1.6 x64 appears to be installed for the installing/current user.'
    Complete-DependencyScript 0
}

Invoke-InstallerIfMissing -Name '.NET 8 Desktop Runtime x64' `
    -Test ${function:Test-DotNetDesktopRuntime8} `
    -DownloadUrl 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe' `
    -FileName 'windowsdesktop-runtime-8-x64.exe' `
    -InstallerPath $DotNetInstallerPath `
    -InstallerArguments $dotNetInstallerArguments `
    -Diagnostic ${function:Get-DotNetRuntimeDiagnostic}

Invoke-InstallerIfMissing -Name 'complete Windows App Runtime 1.6 x64 package set' `
    -Test ${function:Test-WindowsAppRuntime16Complete} `
    -DownloadUrl 'https://aka.ms/windowsappsdk/1.6/latest/windowsappruntimeinstall-x64.exe' `
    -FileName 'windowsappruntimeinstall-1.6-x64.exe' `
    -InstallerPath $WindowsAppRuntimeInstallerPath `
    -InstallerArguments $windowsAppRuntimeInstallerArguments `
    -Diagnostic ${function:Get-WindowsAppRuntimeDiagnostic}

Write-Host ''
Write-Host 'All required runtimes appear to be installed.'

Complete-DependencyScript 0
