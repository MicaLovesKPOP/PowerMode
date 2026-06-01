[CmdletBinding()]
param(
    [switch]$SetOptimizedActive,
    [switch]$SkipCleanup
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ConfigPath = Join-Path $PSScriptRoot 'power-plans.json'
$config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json

$BuiltInGuids = @{
    Balanced = '381b4222-f694-41f0-9685-ff5bb260df2e'
    HighPerformance = '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c'
    PowerSaver = 'a1841308-3541-4fab-bc81-f71556f20b4a'
}

$NativePowerModeOverlays = @{
    BestPerformance = @{
        DisplayName = 'Best performance'
        Guid = 'ded574b5-45a0-4f42-8737-46345c09c238'
        Candidates = @('ded574b5-45a0-4f42-8737-46345c09c238', 'OVERLAY_SCHEME_HIGH')
    }
    Balanced = @{
        DisplayName = 'Balanced'
        Guid = '00000000-0000-0000-0000-000000000000'
        Candidates = @('00000000-0000-0000-0000-000000000000', 'OVERLAY_SCHEME_NONE')
    }
    BestPowerEfficiency = @{
        DisplayName = 'Best power efficiency'
        Guid = '961cc777-2547-4f9d-8174-7d86181b8a7a'
        Candidates = @('961cc777-2547-4f9d-8174-7d86181b8a7a', 'OVERLAY_SCHEME_LOW')
    }
}

function Normalize-GuidText([string]$Guid) {
    return ([string]$Guid).Trim('{}').ToLowerInvariant()
}

function Normalize-PlanName([string]$Name) {
    $s = ([string]$Name).Trim().ToLowerInvariant()
    $s = $s -replace '&&', '&'
    $s = $s -replace '_', ''
    $s = $s -replace '\bcool\s+and\s+quiet\b', 'cool & quiet'
    $s = $s -replace '\s+', ' '
    return $s.Trim()
}

function Test-PlanNameEquivalent([string]$A, [string]$B) {
    return (Normalize-PlanName $A) -eq (Normalize-PlanName $B)
}

function Get-DesiredPlans {
    return @($config.builtinPlans) + @($config.customPlans)
}

function Get-PowerSchemes {
    $list = @()
    try {
        $lines = & powercfg.exe /list 2>$null
        foreach ($line in $lines) {
            if ($line -match '([0-9a-fA-F-]{36}).*\((.*?)\)(\s*\*)?') {
                $list += [pscustomobject]@{
                    Guid = (Normalize-GuidText $matches[1])
                    Name = [string]$matches[2]
                    Active = ($line -match '\*')
                    Raw = [string]$line
                }
            }
        }
    } catch {}
    return @($list)
}

function Get-PowerSchemeByGuid([string]$Guid) {
    $g = Normalize-GuidText $Guid
    return @(Get-PowerSchemes | Where-Object { $_.Guid -eq $g } | Select-Object -First 1)
}

function Get-PowerSchemeByDesiredName($Plan) {
    $target = [string]$Plan.name
    return @(Get-PowerSchemes | Where-Object { Test-PlanNameEquivalent ([string]$_.Name) $target })
}

function Invoke-PowerCfg {
    param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)

    $output = @()
    try {
        $output = & powercfg.exe @Arguments 2>&1
        $exit = $LASTEXITCODE
        if ($null -eq $exit) { $exit = 0 }
        return [pscustomobject]@{
            Success = ($exit -eq 0)
            ExitCode = [int]$exit
            Output = (($output | Out-String).Trim())
            Arguments = ($Arguments -join ' ')
        }
    } catch {
        return [pscustomobject]@{
            Success = $false
            ExitCode = -1
            Output = $_.Exception.Message
            Arguments = ($Arguments -join ' ')
        }
    }
}

function Invoke-PowerCfgBestEffort {
    param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
    $r = Invoke-PowerCfg @Arguments
    if (-not $r.Success) {
        Write-Warning "powercfg $($r.Arguments) failed with exit $($r.ExitCode): $($r.Output)"
    }
    return $r.Success
}

function Rename-PowerPlan {
    param($Plan, [string]$Guid)

    $name = [string]$Plan.name
    $description = [string]$Plan.description
    [bool](Invoke-PowerCfgBestEffort /changename $Guid $name $description)
}

function Set-PowerPlanValues {
    param($Plan, [string]$Guid)

    foreach ($mode in @('/setacvalueindex','/setdcvalueindex')) {
        Invoke-PowerCfgBestEffort $mode $Guid SUB_PROCESSOR PROCTHROTTLEMIN ([string][int]$Plan.minCpu) | Out-Null
        Invoke-PowerCfgBestEffort $mode $Guid SUB_PROCESSOR PROCTHROTTLEMAX ([string][int]$Plan.maxCpu) | Out-Null
    }
}

function New-DuplicatePlanFromSource {
    param([string]$SourceGuid, [string]$Reason)

    $source = Normalize-GuidText $SourceGuid
    $out = & powercfg.exe /duplicatescheme $source 2>&1
    $joined = ($out | Out-String)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not duplicate source power plan $source for $Reason. powercfg output: $joined"
    }

    $guid = $null
    if ($joined -match '([0-9a-fA-F-]{36})') { $guid = Normalize-GuidText $matches[1] }
    if (-not $guid) { throw "Could not parse duplicated power plan GUID for $Reason. powercfg output: $joined" }
    return $guid
}

function Get-FallbackSourceGuid {
    param($Plan)

    if ($Plan.PSObject.Properties.Name -contains 'sourceGuid') {
        $source = Normalize-GuidText ([string]$Plan.sourceGuid)
        if (@(Get-PowerSchemeByGuid $source).Count -gt 0) { return $source }
    }

    if ($Plan.PSObject.Properties.Name -contains 'builtinGuid') {
        $source = Normalize-GuidText ([string]$Plan.builtinGuid)
        if (@(Get-PowerSchemeByGuid $source).Count -gt 0) { return $source }
    }

    foreach ($candidate in @($BuiltInGuids.Balanced, $BuiltInGuids.HighPerformance, $BuiltInGuids.PowerSaver)) {
        $candidate = Normalize-GuidText $candidate
        if (@(Get-PowerSchemeByGuid $candidate).Count -gt 0) { return $candidate }
    }

    $any = @(Get-PowerSchemes | Select-Object -First 1)
    if ($any.Count -gt 0) { return [string]$any[0].Guid }

    throw "No source power scheme is available to create $($Plan.name)."
}

function Ensure-ManagedPlan {
    param($Plan, [System.Collections.Generic.HashSet[string]]$UsedGuids)

    $targetName = [string]$Plan.name
    $guid = $null
    $fromBuiltInGuid = $false

    foreach ($candidate in @(Get-PowerSchemeByDesiredName $Plan)) {
        $cg = Normalize-GuidText ([string]$candidate.Guid)
        if (-not $UsedGuids.Contains($cg)) {
            $guid = $cg
            break
        }
    }

    if (-not $guid -and ($Plan.PSObject.Properties.Name -contains 'builtinGuid')) {
        $bg = Normalize-GuidText ([string]$Plan.builtinGuid)
        $candidate = @(Get-PowerSchemeByGuid $bg | Select-Object -First 1)
        if ($candidate.Count -gt 0 -and -not $UsedGuids.Contains($bg)) {
            $guid = $bg
            $fromBuiltInGuid = $true
        }
    }

    if (-not $guid) {
        $source = Get-FallbackSourceGuid -Plan $Plan
        $guid = New-DuplicatePlanFromSource -SourceGuid $source -Reason $targetName
    }

    [void](Rename-PowerPlan -Plan $Plan -Guid $guid)
    Set-PowerPlanValues -Plan $Plan -Guid $guid

    $refreshed = @(Get-PowerSchemeByGuid $guid | Select-Object -First 1)
    if ($fromBuiltInGuid -and $refreshed.Count -gt 0 -and -not (Test-PlanNameEquivalent ([string]$refreshed[0].Name) $targetName)) {
        Write-Warning "Built-in scheme $guid still appears as '$($refreshed[0].Name)' after rename to '$targetName'. Creating managed replacement instead."
        $source = $guid
        $guid = New-DuplicatePlanFromSource -SourceGuid $source -Reason $targetName
        [void](Rename-PowerPlan -Plan $Plan -Guid $guid)
        Set-PowerPlanValues -Plan $Plan -Guid $guid
    }

    [void]$UsedGuids.Add((Normalize-GuidText $guid))
    Write-Host "Ensured power plan: $targetName [$guid]"
    return $guid
}

function Get-NativePowerModeForPlan {
    param($Plan)

    if ($Plan.PSObject.Properties.Name -contains 'nativePowerMode' -and -not [string]::IsNullOrWhiteSpace([string]$Plan.nativePowerMode)) {
        return [string]$Plan.nativePowerMode
    }

    $name = [string]$Plan.name
    if (Test-PlanNameEquivalent $name 'Unrestrained Performance' -or Test-PlanNameEquivalent $name 'Optimized Performance') { return 'BestPerformance' }
    if (Test-PlanNameEquivalent $name 'Extreme Energy Saver') { return 'BestPowerEfficiency' }
    if (Test-PlanNameEquivalent $name 'Balanced Performance' -or Test-PlanNameEquivalent $name 'Cool and Quiet') { return 'Balanced' }

    return 'Balanced'
}

function Set-NativePowerModeOverlay {
    param([string]$Mode)

    if ([string]::IsNullOrWhiteSpace($Mode) -or -not $NativePowerModeOverlays.ContainsKey($Mode)) {
        Write-Warning "Unknown native Windows Power mode mapping: $Mode"
        return $false
    }

    $overlay = $NativePowerModeOverlays[$Mode]
    $display = [string]$overlay.DisplayName

    foreach ($candidate in @($overlay.Candidates)) {
        $r = Invoke-PowerCfg /setactiveoverlay ([string]$candidate)
        if ($r.Success) {
            Write-Host "Native Windows Power mode overlay set to $display via powercfg ($candidate)."
            return $true
        }
    }

    try {
        $regPath = 'HKLM:\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes'
        if (Test-Path -LiteralPath $regPath) {
            New-ItemProperty -Path $regPath -Name ActiveOverlayAcPowerScheme -PropertyType String -Value ([string]$overlay.Guid) -Force | Out-Null
            New-ItemProperty -Path $regPath -Name ActiveOverlayDcPowerScheme -PropertyType String -Value ([string]$overlay.Guid) -Force | Out-Null
            Write-Host "Native Windows Power mode overlay set to $display via registry fallback."
            return $true
        }
    } catch {
        Write-Warning "Native Windows Power mode registry fallback failed: $($_.Exception.Message)"
    }

    Write-Warning "Could not set native Windows Power mode overlay to $display. This Windows build may not expose overlay switching through powercfg."
    return $false
}

Write-Host 'Power Mode power plan repair'
Write-Host '----------------------------'
Write-Host 'Ensuring the five current release power profiles.'
Write-Host 'No deprecated prototype/test profile names are cleaned or migrated in this release path.'
Write-Host ''

$usedGuids = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::OrdinalIgnoreCase)
$defaultGuid = $null

foreach ($plan in @(Get-DesiredPlans)) {
    $g = Ensure-ManagedPlan -Plan $plan -UsedGuids $usedGuids
    if ([string]$plan.name -ieq [string]$config.defaultPlan) { $defaultGuid = $g }
}

if ($SetOptimizedActive -and $defaultGuid) {
    Invoke-PowerCfgBestEffort /setactive $defaultGuid | Out-Null
    Write-Host "Active power plan set to $($config.defaultPlan)."

    $defaultPlanObject = @(Get-DesiredPlans | Where-Object { [string]$_.name -ieq [string]$config.defaultPlan } | Select-Object -First 1)
    if ($defaultPlanObject.Count -gt 0) {
        [void](Set-NativePowerModeOverlay -Mode (Get-NativePowerModeForPlan -Plan $defaultPlanObject[0]))
    }
}

Write-Host ''
Write-Host 'Final managed plan status:'
$schemes = Get-PowerSchemes
foreach ($plan in @(Get-DesiredPlans)) {
    $name = [string]$plan.name
    $match = @($schemes | Where-Object { Test-PlanNameEquivalent ([string]$_.Name) $name } | Select-Object -First 1)

    if ($match.Count -gt 0) {
        Write-Host "  [OK] $name [$($match[0].Guid)]"
    } else {
        Write-Warning "  [MISSING] $name"
    }
}
