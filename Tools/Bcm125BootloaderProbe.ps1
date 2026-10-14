param([switch]$Probe, [switch]$Compare, [switch]$ApplicationCompare)

# Run in Windows PowerShell (.NET Framework), matching the station application.
# Default is read-only: no fixture, power, reset, or programming commands.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$runtime = Join-Path $root 'bin\Debug'
$probeAssembly = [System.Reflection.Assembly]::LoadFrom((Join-Path $runtime 'TestPlatform.exe'))
$device = New-Object TestPlatform.SK441Device
$device.SkipComInit = $false
$log = [System.Collections.Generic.List[string]]::new()
function Write-ProbeLog([string]$message) {
    $line = '{0:yyyy-MM-dd HH:mm:ss.fff} {1}' -f [DateTime]::Now, $message
    $log.Add($line)
    Write-Output $line
}
function Query-Daq([string]$command) {
    $field = $device.Daq.GetType().GetField('_serialPort', [System.Reflection.BindingFlags]'Instance,NonPublic')
    $port = $field.GetValue($device.Daq)
    $port.DiscardInBuffer()
    $port.WriteLine($command)
    $port.ReadLine().Trim()
}
function Send-Mbd([string]$command) {
    $response = $device.TargetBoard.SendCommandAsync($command, 1500, $false).GetAwaiter().GetResult()
    Write-ProbeLog ('MBD {0} => {1}' -f $command, ($response -replace '[\r\n]+', ' | '))
    if ($response -notmatch '(?m)^OK\b|DIAGNOSTIC MODE is ON') { throw ('MBD command not acknowledged: ' + $command) }
}
function Read-MbdRaw([string]$command) {
    $response = $device.TargetBoard.SendCommandAsync($command, 2000, $false).GetAwaiter().GetResult()
    Write-ProbeLog ('QUERY {0} => {1}' -f $command, ($response -replace '[\r\n]+', ' | '))
}
function Read-Application([int]$mcu, [bool]$bootHigh) {
    Send-Mbd 'W86=1'
    $device.Daq.OpenRelay('@305,306')
    if ($bootHigh) { $device.Daq.CloseRelay('@' + (304 + $mcu)) }
    Start-Sleep -Milliseconds 300
    Send-Mbd 'W86=0'
    Start-Sleep -Milliseconds 2000
    Write-ProbeLog ('Application test MCU{0}, BOOT high={1}' -f $mcu,$bootHigh)
    Read-MbdRaw 'R86'
    Read-MbdRaw ('REMOTE' + $mcu)
    Read-MbdRaw 'R0'
    Read-MbdRaw 'R10'
    Read-MbdRaw 'EXIT'
}
function Set-Route([int]$mcu) {
    # Break both TTL paths before connecting the selected MCU.
    foreach ($relay in 3..8) {
        if (!$device.SetFixtureRelaysAsync([int[]]@($relay), $false, 'BCM125 probe route').GetAwaiter().GetResult()) {
            throw ('Fixture relay command failed: Y' + $relay)
        }
    }
    $selectedRelays = if ($mcu -eq 1) { 3..5 } else { 6..8 }
    foreach ($relay in $selectedRelays) {
        if (!$device.SetFixtureRelaysAsync([int[]]@($relay), $true, 'BCM125 probe route').GetAwaiter().GetResult()) {
            throw ('Fixture relay command failed: Y' + $relay)
        }
    }
    Write-ProbeLog ('Selected TTL route: MCU' + $mcu)
}
function Read-FixtureRegisters {
    $relayType = $probeAssembly.GetType('TestPlatform.RelayController')
    $method = $relayType.GetMethod('SendCommandWithCrcAsync', [System.Reflection.BindingFlags]'Static,Public')
    $task = $method.Invoke($null, [object[]]@([byte[]]@(1,3,0,0,0,8), 38400, 'COM12', 1000, $null))
    Write-ProbeLog ('Fixture registers Y1-Y8 raw: ' + $task.GetAwaiter().GetResult())
}
function Test-Bootloader([int]$mcu, [int]$bootChannel, [int]$baud, [int]$waitMs) {
    $safety = $device.ReadDigitalInputsSafeAsync(2, 'COM12').GetAwaiter().GetResult()
    if (!$safety.CommunicationOk -or $safety.States[6] -or $safety.States[7]) { throw 'Safety input changed; abort probe.' }
    $device.Flasher.Disconnect()
    Send-Mbd 'W86=1'
    $device.Daq.OpenRelay('@305,306')
    Set-Route $mcu
    $device.Daq.CloseRelay('@' + $bootChannel)
    Write-ProbeLog ('BOOT relay readback: ' + (Query-Daq 'ROUT:CLOS? (@305,306)'))
    Read-FixtureRegisters
    Start-Sleep -Milliseconds $waitMs
    Send-Mbd 'W86=0'
    Start-Sleep -Milliseconds $waitMs
    $device.Flasher.Connect('COM20', $baud)
    $script:lastProbeOk = $device.Flasher.ProbeBootloaderAsync('COM20', [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
    Write-ProbeLog ('RESULT MCU{0}, BOOT={1}, baud={2}, hold/settle={3}ms: {4}; {5}' -f $mcu,$bootChannel,$baud,$waitMs,$script:lastProbeOk,$device.Flasher.LastError)
    if ($script:lastProbeOk) {
        $serialField = $device.Flasher.GetType().GetField('_serialPort', [System.Reflection.BindingFlags]'Instance,NonPublic')
        $serial = $serialField.GetValue($device.Flasher)
        $serial.Write([byte[]]@(2,253),0,2)
        Start-Sleep -Milliseconds 100
        $bytes = [byte[]]::new($serial.BytesToRead)
        if ($bytes.Length) { [void]$serial.Read($bytes,0,$bytes.Length) }
        Write-ProbeLog ('GET ID raw bytes: ' + [BitConverter]::ToString($bytes))
    }
}
try {
    $inputs = $device.ReadDigitalInputsSafeAsync(2, 'COM12').GetAwaiter().GetResult()
    Write-ProbeLog ('DI communication={0}, error={1}' -f $inputs.CommunicationOk, $inputs.Error)
    if (!$inputs.CommunicationOk) { throw 'Cannot verify fixture inputs.' }
    1..10 | ForEach-Object { Write-ProbeLog ('DI{0}={1}' -f $_, [int]$inputs.States[$_ - 1]) }
    $device.Daq.Connect('COM16', 9600, 2000)
    Write-ProbeLog ('DAQ: ' + $device.Daq.GetIdn())
    foreach ($query in @('ROUT:CLOS? (@305,306)', 'SYST:ERR?')) {
        Write-ProbeLog ($query + ' => ' + (Query-Daq $query))
    }
    if ($Probe -or $Compare -or $ApplicationCompare) {
        if ($inputs.States[6] -or $inputs.States[7]) { throw 'Fixture down / emergency input does not allow probing.' }
        $device.AnsPower.Connect('COM23', 9600, 1)
        [single]$voltage = 0
        [single]$current = 0
        $device.AnsPower.ReadVoltageCurrent([ref]$voltage, [ref]$current)
        Write-ProbeLog ('ANS readback: {0} V, {1} A' -f $voltage, $current)
        if ($voltage -lt 130 -or $voltage -gt 150) { throw 'DUT supply is not at the expected 140 V working point.' }
        Write-ProbeLog 'MCU1 current paused state: probing COM20, 115200 8E1; GET/GET ID only, no erase or write.'
        $ok = $device.Flasher.ProbeBootloaderAsync('COM20', [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
        Write-ProbeLog ('MCU1 probe={0}, error={1}' -f $ok, $device.Flasher.LastError)
        if ($Compare -or $ApplicationCompare) {
            [TestPlatform.ComName]::fixtureRelayComName = 'COM12'
            [TestPlatform.ComName]::rs485ComName = 'COM12'
            $device.TargetBoard.Connect('COM24', 9600, 500)
            try {
                Send-Mbd 'DIAG ON'
                Read-FixtureRegisters
                if ($Compare) {
                    Test-Bootloader 1 305 115200 500
                    Test-Bootloader 2 306 115200 500
                    Test-Bootloader 1 305 9600 1000
                    Test-Bootloader 1 306 115200 500
                    Test-Bootloader 2 305 115200 500
                }
                if ($ApplicationCompare) {
                    $device.Flasher.Disconnect()
                    Read-Application 1 $true
                    Read-Application 1 $false
                    Read-Application 2 $false
                    Read-Application 2 $true
                }
            }
            finally {
                $device.Flasher.Disconnect()
                Send-Mbd 'W86=1'
                $device.Daq.OpenRelay('@305,306')
                Set-Route 1
                $device.Daq.CloseRelay('@305')
                Start-Sleep -Milliseconds 100
                Send-Mbd 'W86=0'
                Write-ProbeLog ('Restored MCU1 paused state. BOOT=' + (Query-Daq 'ROUT:CLOS? (@305,306)'))
                Read-FixtureRegisters
            }
        }
    }
}
finally {
    $device.Dispose()
    $logPath = Join-Path $runtime ('Logs\Bcm125BootloaderProbe_{0:yyyyMMdd_HHmmss}.txt' -f [DateTime]::Now)
    $log | Set-Content -LiteralPath $logPath -Encoding UTF8
    Write-Output ('Saved: ' + $logPath)
}
