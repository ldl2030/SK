param([string]$Runtime = 'bin\Debug', [int]$WaitFixtureSeconds = 0)

# Run with SysWOW64 Windows PowerShell. No erase, firmware programming or FLASH save.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[void][Reflection.Assembly]::LoadFrom((Join-Path $root "$Runtime\TestPlatform.exe"))
$device = New-Object TestPlatform.SK441Device
$device.SkipComInit = $false
[TestPlatform.ComName]::fixtureRelayComName = 'COM12'
[TestPlatform.ComName]::rs485ComName = 'COM12'
$lines = [Collections.Generic.List[string]]::new()
$powered = $false
function Log([string]$message) {
    $line = '{0:HH:mm:ss.fff} {1}' -f [DateTime]::Now, $message
    $lines.Add($line)
    Write-Output $line
}
function Check-Safety {
    $inputs = $device.ReadDigitalInputsSafeAsync(2, 'COM12').GetAwaiter().GetResult()
    if (!$inputs.CommunicationOk -or $inputs.States[6] -or $inputs.States[7]) {
        throw 'Fixture down/emergency inputs do not permit powered probing.'
    }
}
function Check-Action($task, [string]$name) {
    if (!$task.GetAwaiter().GetResult()) { throw ('Failed: ' + $name) }
    Log $name
}
function Query([string]$command) {
    Check-Safety
    $response = $device.TargetBoard.SendCommandAsync($command, 2000, $false).GetAwaiter().GetResult()
    Log ($command + ' => ' + ($response -replace '[\r\n]+', ' | '))
    if ([string]::IsNullOrWhiteSpace($response)) { throw ('No MBD response: ' + $command) }
    if ($command -match '^(W|DIAG|REMOTE)' -and $response -notmatch 'OK|DIAGNOSTIC MODE is ON') {
        throw ('MBD command failed: ' + $command)
    }
}
try {
    if ($WaitFixtureSeconds -gt 0) {
        Log 'Waiting for physical two-hand start / DI7=0.'
        $deadline = [DateTime]::UtcNow.AddSeconds($WaitFixtureSeconds)
        do {
            $inputs = $device.ReadDigitalInputsSafeAsync(2, 'COM12').GetAwaiter().GetResult()
            if (!$inputs.CommunicationOk -or $inputs.States[7]) { throw 'Emergency/input communication failure.' }
            if (!$inputs.States[6]) { break }
            Start-Sleep -Milliseconds 1000
        } while ([DateTime]::UtcNow -lt $deadline)
    }
    Check-Safety
    $device.AnsPower.Connect('COM23', 9600, 1)
    $device.AinuoPowerA.Connect('COM6', 38400)
    $device.HengHuiPower2.Connect('COM22', 9600)
    $device.Daq.Connect('COM16', 9600, 2000)
    $device.Daq.OpenRelay('@305,306')
    Check-Action ($device.SetStringGeneratorAsync(0, 0, 150, $false)) 'String OFF'
    Check-Action ($device.SetVbusGeneratorAsync(0, 0, 150, $false)) 'VBUS OFF'
    Check-Action ($device.ControlRelaysAsync([int[]]@(1,4,6,8,9,10,11,12,13,14,15,16), $false)) 'Open charging routes'
    $powered = $true
    Check-Action ($device.CloseRl4ThenEnableAnsAsync(7, 140, 1)) 'ANS 140V ON'
    Start-Sleep -Milliseconds 3000
    $device.TargetBoard.Connect('COM24', 9600, 500)
    foreach ($mcu in 1..2) {
        Query 'EXIT'
        Query 'DIAG ON'
        Query ('REMOTE' + $mcu)
        Query 'DIAG ON'
        Query 'R0'
        foreach ($register in @(16,17,22,23,24)) { Query ('R' + $register) }
        Query 'W69=1'
        Query 'W12=6000'
        Query 'W75=1'
        $route = if ($mcu -eq 1) { [int[]]@(1,4,6,15,11,8) } else { [int[]]@(1,4,6,16,12,8) }
        Check-Action ($device.ControlRelaysAsync($route, $true)) ('Close ST' + $mcu + ' charging route')
        Check-Action ($device.SetStringGeneratorAsync(0, 0, 150, $true)) 'String 0V/0A ON'
        foreach ($voltage in @(1,15)) {
            Check-Action ($device.SetVbusGeneratorAsync($voltage, 1, 150, $true)) ('VBUS ' + $voltage + 'V')
            Start-Sleep -Milliseconds 2000
            foreach ($sample in 1..4) {
                Log ('ST' + $mcu + ' ' + $voltage + 'V 0A sample ' + $sample)
                foreach ($register in @(50,56,42,44)) { Query ('R' + $register) }
            }
        }
        Query 'W75=0'
        Check-Action ($device.SetStringGeneratorAsync(0, 0, 150, $false)) 'String OFF'
        Check-Action ($device.SetVbusGeneratorAsync(0, 0, 150, $false)) 'VBUS OFF'
        Check-Action ($device.ControlRelaysAsync($route, $false)) 'Open charging route'
    }
    Query 'EXIT'
}
catch {
    Log ('PROBE FAILED: ' + $_.Exception.Message)
    throw
}
finally {
    try {
        if ($powered) {
            foreach ($cleanup in @('String', 'VBUS', 'ANS', 'Routes', 'Fixture')) {
                try {
                    switch ($cleanup) {
                        'String' { Check-Action ($device.SetStringGeneratorAsync(0, 0, 150, $false)) 'Cleanup String OFF' }
                        'VBUS' { Check-Action ($device.SetVbusGeneratorAsync(0, 0, 150, $false)) 'Cleanup VBUS OFF' }
                        'ANS' { Check-Action ($device.DisableAnsThenOpenRl4Async(7)) 'Cleanup ANS discharged / RL4 open' }
                        'Routes' { Check-Action ($device.ControlRelaysAsync([int[]]@(1,4,6,8,9,10,11,12,13,14,15,16), $false)) 'Cleanup routes open' }
                        'Fixture' { Check-Action ($device.StopFixturePressDownAsync('BCM-125')) 'Cleanup fixture release' }
                    }
                }
                catch { Log ('CLEANUP FAILED: ' + $_.Exception.Message) }
            }
        }
    }
    finally {
        $device.Dispose()
        $logPath = Join-Path $root ('bin\Debug\Logs\Bcm125CurrentCalibrationProbe_{0:yyyyMMdd_HHmmss}.txt' -f [DateTime]::Now)
        $lines | Set-Content -LiteralPath $logPath -Encoding UTF8
        Write-Output ('Saved: ' + $logPath)
    }
}
