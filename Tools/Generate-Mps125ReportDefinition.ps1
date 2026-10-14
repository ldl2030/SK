param(
    [Parameter(Mandatory = $true)]
    [string]$ReferenceReportPath,
    [string]$TestPlanPath = (Join-Path $PSScriptRoot '..\ProjectConfig\SK\MPS-125\MPS-125_autoTest.xml'),
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\ProjectConfig\SK\MPS-125\MPS-125_report.psv')
)

$ErrorActionPreference = 'Stop'
$culture = [System.Globalization.CultureInfo]::InvariantCulture
[xml]$plan = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $TestPlanPath))
$stepByCode = @{}
foreach ($step in $plan.SelectNodes('//TestItem')) {
    if ([string]$step.StepId -match '^MPS125\.CH\d+\.S\d+\.([B-L]\d{2})$') {
        $code = $Matches[1]
        if ($stepByCode.ContainsKey($code)) { throw "Duplicate XML result code: $code" }
        $stepByCode[$code] = $step
    }
}

function Format-Limit([string]$raw, [double]$scale, [bool]$openUpper) {
    if ([string]::IsNullOrWhiteSpace($raw)) {
        if ($openUpper) { return 'any' }
        return ''
    }
    $number = 0.0
    if (-not [double]::TryParse($raw.Replace(',', '.'), [System.Globalization.NumberStyles]::Float, $culture, [ref]$number)) {
        throw "Invalid XML limit: $raw"
    }
    return ($number * $scale).ToString('0.########', $culture).Replace('.', ',')
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# MPS-125 customer report: names/order from reference TXT; ALL limits from current test XML.')
$lines.Add('# Code|StepId|ReportGroup|ProcedureRef|SymbolicName|Unit|LowerLimit|UpperLimit|ScaleFactor')
$seenCodes = @{}
$group = ''
foreach ($line in [System.IO.File]::ReadAllLines((Resolve-Path -LiteralPath $ReferenceReportPath))) {
    if ($line -match '^((?:FIRST|CHECK|ISOBATT).+):\s*$') {
        $group = $Matches[1].Trim()
        continue
    }
    if ($line -notmatch '^([B-L]\d{2})\s+(\S+)\s+(.+)$') { continue }
    $code = $Matches[1]
    $procedure = $Matches[2]
    $remainder = $Matches[3]
    if ($seenCodes.ContainsKey($code) -or -not $stepByCode.ContainsKey($code) -or $group.Length -eq 0) {
        throw "Duplicate, unmapped or ungrouped reference code: $code"
    }
    $seenCodes[$code] = $true
    # Read labels only. Reference values, limits and PASSED states are never imported.
    $symbol = ($remainder -split '=', 2)[0]
    $symbol = ([regex]::Replace($symbol, '\[.*$|\s+(?:NOT )?PASSED\s*$', '')).Trim()
    $step = $stepByCode[$code]
    $scale = 1.0
    $unit = [string]$step.Unit
    if ($code -eq 'D03' -or $unit -like 'Logic*') {
        $unit = ''
    }
    elseif ($code -in @('L01', 'L05', 'L09', 'L13')) {
        # R71 is an unscaled register count, despite the legacy XML display label mV.
        $unit = 'Count'
    }
    elseif ($unit -eq 'mV') { $unit = 'V'; $scale = 0.001 }
    elseif ($unit -eq 'mA') { $unit = 'A'; $scale = 0.001 }
    $lower = Format-Limit ([string]$step.LowerLimit) $scale $false
    $upper = Format-Limit ([string]$step.UpperLimit) $scale (-not [string]::IsNullOrWhiteSpace([string]$step.LowerLimit))
    $fields = @($code, [string]$step.StepId, $group, $procedure, $symbol, $unit, $lower, $upper, $scale.ToString($culture))
    if ($fields | Where-Object { $_ -match '[|\r\n]' }) { throw "Invalid report field for $code" }
    $lines.Add($fields -join '|')
}
if ($seenCodes.Count -ne 70 -or $seenCodes.Count -ne $stepByCode.Count) {
    throw "Expected all 70 MPS-125 results, got reference=$($seenCodes.Count), XML=$($stepByCode.Count)"
}
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
[System.IO.File]::WriteAllLines($resolvedOutput, $lines, [System.Text.UTF8Encoding]::new($false))
Write-Output "Generated 70 MPS-125 report rows with XML limits: $resolvedOutput"
