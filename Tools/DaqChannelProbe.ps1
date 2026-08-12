param(
    [string]$PortName = "COM16",
    [int]$FirstChannel = 101,
    [int]$LastChannel = 120
)

$ErrorActionPreference = "Stop"
$daq = New-Object System.IO.Ports.SerialPort $PortName, 9600, "None", 8, "One"
$daq.NewLine = "`n"
$daq.ReadTimeout = 3000
$daq.WriteTimeout = 2000

function Invoke-DaqQuery([string]$Command) {
    $daq.DiscardInBuffer()
    $daq.WriteLine($Command)
    return $daq.ReadLine().Trim()
}

function Send-DaqCommand([string]$Command) {
    $daq.DiscardInBuffer()
    $daq.WriteLine($Command)
    Start-Sleep -Milliseconds 30
}

try {
    $daq.Open()
    Write-Output ("IDN={0}" -f (Invoke-DaqQuery "*IDN?"))

    foreach ($channel in $FirstChannel..$LastChannel) {
        # MEAS selects the requested measurement input directly.  This script
        # does not operate fixture relays, DAQ relays 301-320, or power outputs.
        # Stop any old scan and clear its error queue before changing channel.
        Send-DaqCommand "ABOR"
        Send-DaqCommand "*CLS"
        $raw = Invoke-DaqQuery ("MEAS:VOLT:DC? AUTO,MIN,(@{0})" -f $channel)
        Write-Output ("CH{0}={1}" -f $channel, $raw)
    }
}
finally {
    if ($daq.IsOpen) {
        $daq.Close()
    }
    $daq.Dispose()
}
