[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Dist = Join-Path $Root 'dist'
$Publish = Join-Path $Root 'winui\publish'
$Project = Join-Path $Root 'winui\PowerModeTray.WinUI.csproj'
$Iss = Join-Path $Root 'installer\PowerMode.iss'
$Log = Join-Path $Dist 'build-release-installer.log'

New-Item -ItemType Directory -Path $Dist -Force | Out-Null
if (Test-Path -LiteralPath $Log) { Remove-Item -LiteralPath $Log -Force }

function Write-Log {
    param([string]$Message)
    $line = '[{0}] {1}' -f (Get-Date -Format 'HH:mm:ss'), $Message
    Write-Host $line
    Add-Content -LiteralPath $Log -Value $line
}

function Invoke-LoggedProcess {
    param(
        [Parameter(Mandatory=$true)][string]$FilePath,
        [Parameter(Mandatory=$true)][string[]]$Arguments,
        [string]$WorkingDirectory = $Root
    )

    Write-Log ('Running: "{0}" {1}' -f $FilePath, ($Arguments -join ' '))

    Push-Location -LiteralPath $WorkingDirectory
    try {
        # Windows PowerShell 5.1 runs on .NET Framework, where
        # System.Diagnostics.ProcessStartInfo.ArgumentList does not exist.
        # Use PowerShell's native call operator with an argument array instead;
        # this preserves paths with spaces and still lets us capture output.
        $output = & $FilePath @Arguments 2>&1
        $exit = $LASTEXITCODE
        if ($null -eq $exit) { $exit = 0 }

        if ($output) {
            $output | ForEach-Object { Write-Host $_ }
            $output | Out-String | Add-Content -LiteralPath $Log
        }

        Write-Log ('Exit code: {0}' -f $exit)

        if ($exit -ne 0) {
            throw ('Process failed with exit code {0}: {1}' -f $exit, $FilePath)
        }
    } finally {
        Pop-Location
    }
}

function Find-MSBuild {
    $candidates = New-Object System.Collections.Generic.List[string]

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        try {
            $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null
            foreach ($item in $found) {
                if ($item -and (Test-Path -LiteralPath $item)) { [void]$candidates.Add($item) }
            }
        } catch {}
    }

    foreach ($candidate in @(
        "$env:ProgramFiles\Microsoft Visual Studio\2026\Community\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2026\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2026\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    )) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) { [void]$candidates.Add($candidate) }
    }

    $first = $candidates | Select-Object -First 1
    if (-not $first) {
        throw 'Visual Studio MSBuild was not found. Install/repair Visual Studio 2022+ with WinUI application development and .NET desktop development workloads.'
    }

    return [string]$first
}

function Find-ISCC {
    $candidates = New-Object System.Collections.Generic.List[string]

    function Add-IsccCandidate([string]$Path) {
        if ($Path -and (Test-Path -LiteralPath $Path)) {
            [void]$candidates.Add((Resolve-Path -LiteralPath $Path).Path)
        }
    }

    # Explicit override for custom installs:
    #   set ISCC_EXE=D:\Programs\Inno Setup 6\ISCC.exe
    Add-IsccCandidate $env:ISCC_EXE

    foreach ($candidate in @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )) {
        Add-IsccCandidate $candidate
    }

    # Many of my build tools are on D:\Programs, so also check a Programs folder
    # on every fixed drive without doing a slow full-disk recursive search.
    try {
        foreach ($drive in @(Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue)) {
            if (-not $drive.Root) { continue }
            Add-IsccCandidate (Join-Path $drive.Root 'Programs\Inno Setup 6\ISCC.exe')
            Add-IsccCandidate (Join-Path $drive.Root 'Program Files\Inno Setup 6\ISCC.exe')
            Add-IsccCandidate (Join-Path $drive.Root 'Program Files (x86)\Inno Setup 6\ISCC.exe')
        }
    } catch {}

    # Registry install locations used by Inno Setup's own installer.
    foreach ($regPath in @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'
    )) {
        try {
            $p = Get-ItemProperty -LiteralPath $regPath -ErrorAction SilentlyContinue
            foreach ($prop in @('InstallLocation', 'Inno Setup: App Path')) {
                if ($p -and ($p.PSObject.Properties.Name -contains $prop) -and $p.$prop) {
                    Add-IsccCandidate (Join-Path ([string]$p.$prop) 'ISCC.exe')
                }
            }
        } catch {}
    }

    # App Paths registration, if present.
    foreach ($regPath in @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\ISCC.exe',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\ISCC.exe'
    )) {
        try {
            $p = Get-ItemProperty -LiteralPath $regPath -ErrorAction SilentlyContinue
            if ($p) {
                $default = $p.PSObject.Properties | Where-Object { $_.Name -eq '(default)' } | Select-Object -First 1
                if ($default -and $default.Value) { Add-IsccCandidate ([string]$default.Value) }
                if ($p.Path) { Add-IsccCandidate (Join-Path ([string]$p.Path) 'ISCC.exe') }
            }
        } catch {}
    }

    try {
        $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($cmd -and $cmd.Source) { Add-IsccCandidate $cmd.Source }
    } catch {}

    $unique = @($candidates | Where-Object { $_ } | Select-Object -Unique)
    $first = $unique | Select-Object -First 1
    if (-not $first) {
        throw ("Inno Setup 6 compiler was not found. Install Inno Setup 6, or set ISCC_EXE to the exact compiler path, for example:`n" +
               "  set ISCC_EXE=D:\Programs\Inno Setup 6\ISCC.exe`n" +
               "Then run Build-Release-Installer.cmd again.")
    }

    return [string]$first
}

try {
    Write-Log 'Power Mode release installer build started.'
    Write-Log "Root=$Root"
    Write-Log "Log=$Log"

    if (-not (Test-Path -LiteralPath $Project)) { throw "WinUI project not found: $Project" }
    if (-not (Test-Path -LiteralPath $Iss)) { throw "Inno Setup script not found: $Iss" }

    $appVersionMatch = Select-String -LiteralPath $Iss -Pattern '^\s*#define\s+AppVersion\s+"([^"]+)"' | Select-Object -First 1
    if (-not $appVersionMatch -or $appVersionMatch.Matches.Count -lt 1) {
        throw "Could not read AppVersion from Inno Setup script: $Iss"
    }
    $installerVersion = $appVersionMatch.Matches[0].Groups[1].Value
    Write-Log "Installer version=$installerVersion"

    if (Test-Path -LiteralPath $Publish) { Remove-Item -LiteralPath $Publish -Recurse -Force }
    New-Item -ItemType Directory -Path $Publish -Force | Out-Null

    $msbuild = Find-MSBuild
    Write-Log "MSBuild=$msbuild"

    Invoke-LoggedProcess -FilePath $msbuild -WorkingDirectory $Root -Arguments @(
        $Project,
        '/restore',
        '/t:Publish',
        '/p:Configuration=Release',
        '/p:Platform=x64',
        '/p:RuntimeIdentifier=win-x64',
        '/p:SelfContained=false',
        '/p:PublishSelfContained=false',
        '/p:WindowsAppSDKSelfContained=false',
        ('/p:PublishDir={0}' -f $Publish),
        '/p:WindowsPackageType=None',
        '/m'
    )

    $exe = Join-Path $Publish 'PowerModeTray.exe'
    if (-not (Test-Path -LiteralPath $exe)) {
        $contents = Get-ChildItem -LiteralPath $Publish -Recurse -Force -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName
        throw "Build completed, but PowerModeTray.exe was not found in $Publish. Contents:`n$($contents -join [Environment]::NewLine)"
    }

    Write-Log "Published app found: $exe"

    $pdbFiles = @(Get-ChildItem -LiteralPath $Publish -Recurse -Force -File -Filter '*.pdb' -ErrorAction SilentlyContinue)
    foreach ($pdb in $pdbFiles) {
        Write-Log ("Removing debug symbol from release publish output: {0}" -f $pdb.FullName)
        Remove-Item -LiteralPath $pdb.FullName -Force -ErrorAction SilentlyContinue
    }

    $iscc = Find-ISCC
    Write-Log "ISCC=$iscc"

    Invoke-LoggedProcess -FilePath $iscc -WorkingDirectory (Split-Path -Parent $Iss) -Arguments @($Iss)

    $expectedInstaller = Join-Path $Dist ('PowerModeSetup-v{0}.exe' -f $installerVersion)
    if (-not (Test-Path -LiteralPath $expectedInstaller)) {
        $distContents = Get-ChildItem -LiteralPath $Dist -Force -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName
        throw "Inno Setup finished, but expected installer was not found: $expectedInstaller. Dist contents:`n$($distContents -join [Environment]::NewLine)"
    }

    $sizeMb = [math]::Round((Get-Item -LiteralPath $expectedInstaller).Length / 1MB, 2)
    $hash = Get-FileHash -LiteralPath $expectedInstaller -Algorithm SHA256
    $checksumPath = "$expectedInstaller.sha256"
    ('{0}  {1}' -f $hash.Hash.ToLowerInvariant(), (Split-Path -Leaf $expectedInstaller)) | Set-Content -LiteralPath $checksumPath -Encoding ASCII

    Write-Log "Created installer: $expectedInstaller"
    Write-Log "Installer size: $sizeMb MB"
    Write-Log "SHA256: $($hash.Hash.ToLowerInvariant())"
    Write-Log "Checksum file: $checksumPath"
    Write-Log 'Power Mode release installer build completed successfully.'
    exit 0
} catch {
    Write-Log ('ERROR: {0}' -f $_.Exception.Message)
    Write-Host ''
    Write-Host 'Build failed.'
    Write-Host "Log: $Log"
    exit 1
}
