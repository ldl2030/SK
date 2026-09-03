[CmdletBinding()]
param(
    [string]$InputLog = (Join-Path $PSScriptRoot '..\bin\Debug\Logs\Live_{0}.log' (Get-Date -Format 'yyyyMMdd')),
    [datetime]$StartTime,
    [datetime]$EndTime,
    [string]$OutputPath
)

$resolvedInput = [IO.Path]::GetFullPath($InputLog)
if (-not (Test-Path -LiteralPath $resolvedInput)) {
    throw "Live log was not found: $resolvedInput"
}

$timestampPattern = '^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\s+\[(?<level>[^\]]+)\]\s+(?<message>.*)$'
$keyPattern = '(?i)\[(ERROR|WARN)\]|StepId=|BCM250\.RESULT\.(D02|J01|J04|L04|M01|M09|Q01|Q02|Q08|Q11|R01|R02|R04|R07|R11|R12|R13|R14|S02|T01|T02)|MBD (TX|RX).*R(15|42|43|44|50|51|56|68|92|93)(VERB)?|DAQ .* (105|106|109|113|114).*|TTL|Flash|Firmware|烧录|测试失败'

$items = foreach ($line in Get-Content -LiteralPath $resolvedInput -Encoding UTF8) {
    $match = [regex]::Match($line, $timestampPattern)
    if (-not $match.Success) { continue }

    $time = [datetime]::ParseExact(
        $match.Groups['time'].Value,
        'yyyy-MM-dd HH:mm:ss.fff',
        [Globalization.CultureInfo]::InvariantCulture)
    if ($PSBoundParameters.ContainsKey('StartTime') -and $time -lt $StartTime) { continue }
    if ($PSBoundParameters.ContainsKey('EndTime') -and $time -gt $EndTime) { continue }
    if ($line -match $keyPattern) { $line }
}

$startLabel = if ($PSBoundParameters.ContainsKey('StartTime')) { $StartTime.ToString('yyyy-MM-dd HH:mm:ss.fff') } else { '(all entries)' }
$endLabel = if ($PSBoundParameters.ContainsKey('EndTime')) { $EndTime.ToString('yyyy-MM-dd HH:mm:ss.fff') } else { '(all entries)' }

$header = @(
    'SK BCM-250 key-event extract',
    "Source: $resolvedInput",
    "Window: $startLabel to $endLabel",
    ''
)
$output = $header + $items

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $output
}
else {
    $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
    $directory = Split-Path -Parent $resolvedOutput
    if ($directory) { [IO.Directory]::CreateDirectory($directory) | Out-Null }
    [IO.File]::WriteAllLines($resolvedOutput, [string[]]$output, [Text.UTF8Encoding]::new($true))
    Write-Output "Key-event extract saved: $resolvedOutput"
}
