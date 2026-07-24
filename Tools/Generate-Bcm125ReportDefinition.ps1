param(
    [Parameter(Mandatory = $true)]
    [string]$ReferenceReportPath,

    [string]$TestPlanPath = (Join-Path $PSScriptRoot '..\ProjectConfig\SK\BCM-125\BCM-125_autoTest.xml'),

    [string]$OutputPath = (Join-Path $PSScriptRoot '..\ProjectConfig\SK\BCM-125\BCM-125_report.psv')
)

$ErrorActionPreference = 'Stop'

function Read-ElementValue {
    param(
        [string]$Block,
        [string]$ElementName
    )

    $match = [regex]::Match(
        $Block,
        "<$ElementName>(.*?)</$ElementName>",
        [System.Text.RegularExpressions.RegexOptions]::Singleline)

    if ($match.Success) {
        return [System.Net.WebUtility]::HtmlDecode($match.Groups[1].Value.Trim())
    }

    return ''
}

function Parse-Number {
    param([string]$Text)

    $value = 0.0
    $normalized = $(if ($null -eq $Text) { '' } else { $Text }).Trim().Replace(',', '.')
    $ok = [double]::TryParse(
        $normalized,
        [System.Globalization.NumberStyles]::Float,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [ref]$value)

    return [pscustomobject]@{
        Success = $ok
        Value = $value
    }
}

function Escape-Field {
    param([string]$Text)
    $value = if ($null -eq $Text) { '' } else { $Text }
    return ($value.Replace('|', '/').Replace("`r", ' ').Replace("`n", ' ')).Trim()
}

$testPlanText = [System.IO.File]::ReadAllText((Resolve-Path $TestPlanPath))
$testItemBlocks = [regex]::Matches(
    $testPlanText,
    '<TestItem>.*?</TestItem>',
    [System.Text.RegularExpressions.RegexOptions]::Singleline)

# A report code letter maps directly to the procedure chapter:
# B -> CH08, C -> CH09, ... U -> CH27.
# For duplicated codes in CH23, the first row is the calculated result and the
# following row is the write-to-DUT action. The report must use the first row.
$stepByCode = @{}
foreach ($blockMatch in $testItemBlocks) {
    $block = $blockMatch.Value
    $stepId = Read-ElementValue $block 'StepId'
    $groupId = Read-ElementValue $block 'GroupId'

    if ($stepId -notmatch '\.([B-U][0-9]{2})$') {
        continue
    }

    $code = $Matches[1]
    $chapter = 8 + ([int][char]$code[0] - [int][char]'B')
    $expectedGroupId = 'BCM125.CH{0:D2}' -f $chapter
    if ($groupId -ne $expectedGroupId -or $stepByCode.ContainsKey($code)) {
        continue
    }

    $stepByCode[$code] = [pscustomobject]@{
        StepId = $stepId
        LowerLimit = Read-ElementValue $block 'LowerLimit'
        UpperLimit = Read-ElementValue $block 'UpperLimit'
        Unit = Read-ElementValue $block 'Unit'
    }
}

$outputLines = [System.Collections.Generic.List[string]]::new()
$outputLines.Add('# BCM-125 legacy report definition. Generated from the approved reference TXT.')
$outputLines.Add('# Code|StepId|ReportGroup|ProcedureRef|SymbolicName|Unit|LowerLimit|UpperLimit|ScaleFactor')

$currentGroup = ''
foreach ($line in [System.IO.File]::ReadAllLines((Resolve-Path $ReferenceReportPath))) {
    if ($line -match '^(.+):\s*$' -and
        $line -notmatch '^Test ' -and
        $line -notmatch '^BCM_') {
        $currentGroup = $Matches[1].Trim()
        continue
    }

    if ($line -notmatch '^([B-U][0-9]{2})\s+(\S+)\s+(.+)$') {
        continue
    }

    $code = $Matches[1]
    $procedureRef = $Matches[2]
    $remainder = $Matches[3]
    $separatorIndex = $remainder.LastIndexOf(' = ', [System.StringComparison]::Ordinal)
    if ($separatorIndex -lt 0) {
        throw "Cannot find the symbolic-name separator for report code $code."
    }

    $symbolicName = $remainder.Substring(0, $separatorIndex).Trim()
    $resultText = $remainder.Substring($separatorIndex + 3).Trim()
    $resultText = [regex]::Replace($resultText, '\s+(PASSED|NOT PASSED)\s*$', '').Trim()

    $lowerLimit = ''
    $upperLimit = ''
    $limitMatch = [regex]::Match($resultText, '\[([^;]+);\s*([^\]]+)\]')
    if ($limitMatch.Success) {
        $lowerLimit = $limitMatch.Groups[1].Value.Trim()
        $upperLimit = $limitMatch.Groups[2].Value.Trim()
        $resultText = $resultText.Remove($limitMatch.Index, $limitMatch.Length).Trim()
    }

    $unit = ''
    $unitMatch = [regex]::Match(
        $resultText,
        '(?i)(Count|mV|mA|V|A)(?=\s|\)|$)')
    if ($unitMatch.Success) {
        $unit = $unitMatch.Groups[1].Value
        if ($unit.Equals('count', [System.StringComparison]::OrdinalIgnoreCase)) {
            $unit = 'Count'
        }
    }

    if (-not $stepByCode.ContainsKey($code)) {
        throw "No BCM-125 StepId was found for report code $code."
    }

    $sourceStep = $stepByCode[$code]
    $scaleFactor = '1'
    $sourceLower = Parse-Number $sourceStep.LowerLimit
    $reportLower = Parse-Number $lowerLimit
    if ($sourceLower.Success -and $reportLower.Success -and [Math]::Abs($sourceLower.Value) -gt 0.0000001) {
        $ratio = $reportLower.Value / $sourceLower.Value
        if ([Math]::Abs($ratio - 0.001) -lt 0.0000001) {
            $scaleFactor = '0.001'
        }
    }

    $fields = @(
        $code,
        $sourceStep.StepId,
        $currentGroup,
        $procedureRef,
        $symbolicName,
        $unit,
        $lowerLimit,
        $upperLimit,
        $scaleFactor
    ) | ForEach-Object { Escape-Field $_ }

    $outputLines.Add(($fields -join '|'))
}

$reportItemCount = $outputLines.Count - 2
if ($reportItemCount -ne 176) {
    throw "Expected 176 BCM-125 report items, but generated $reportItemCount."
}

$duplicateCodes = $outputLines |
    Select-Object -Skip 2 |
    ForEach-Object { ($_ -split '\|', 2)[0] } |
    Group-Object |
    Where-Object Count -ne 1

if ($duplicateCodes) {
    throw "Duplicate report codes detected: $($duplicateCodes.Name -join ', ')."
}

$outputDirectory = Split-Path -Parent $OutputPath
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
[System.IO.File]::WriteAllLines(
    $OutputPath,
    $outputLines,
    [System.Text.UTF8Encoding]::new($false))

Write-Host "Generated $reportItemCount report items at $OutputPath"
