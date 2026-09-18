using System;
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;

namespace TestPlatform
{
    /// <summary>
    /// Optional Ainuo AN50(V3) backup source B using the documented serial SCPI
    /// protocol. It is intentionally separate from HengHuiPowerSupply so adding
    /// the backup instrument cannot change the installed source-B behavior.
    /// </summary>
    public sealed class AinuoAn50PowerSupply : IProgrammablePowerSupply
    {
        private readonly object _sync = new object();
        private SerialPort _serialPort;

        private string _ovpSetCommandFormat;
        private string _ovpQueryCommand;
        private string _ocpSetCommandFormat;
        private string _ocpQueryCommand;

        public AinuoAn50PowerSupply(float vmax = 300.0f, float imax = 10.0f)
        {
            Vmax = vmax;
            Imax = imax;
        }

        public float Vmax { get; set; }
        public float Imax { get; set; }
        public string Identity { get; private set; }
        public int ReadTimeoutMs { get; set; } = 1500;
        public int WriteTimeoutMs { get; set; } = 1000;
        public string LineTerminator { get; set; } = "\n";

        public bool IsConnected
        {
            get
            {
                lock (_sync)
                    return _serialPort != null && _serialPort.IsOpen;
            }
        }

        public bool SupportsRemoteOvp =>
            !string.IsNullOrWhiteSpace(_ovpSetCommandFormat) &&
            !string.IsNullOrWhiteSpace(_ovpQueryCommand);

        public bool SupportsRemoteOcp =>
            !string.IsNullOrWhiteSpace(_ocpSetCommandFormat) &&
            !string.IsNullOrWhiteSpace(_ocpQueryCommand);

        /// <summary>
        /// Supplies vendor-confirmed protection commands without hard-coding an
        /// undocumented command. Each set format must contain a {0} placeholder.
        /// </summary>
        public void ConfigureProtectionCommands(
            string ovpSetCommandFormat,
            string ovpQueryCommand,
            string ocpSetCommandFormat = null,
            string ocpQueryCommand = null)
        {
            ValidateOptionalCommandFormat(ovpSetCommandFormat, nameof(ovpSetCommandFormat));
            ValidateOptionalCommandFormat(ocpSetCommandFormat, nameof(ocpSetCommandFormat));
            _ovpSetCommandFormat = NullIfWhiteSpace(ovpSetCommandFormat);
            _ovpQueryCommand = NullIfWhiteSpace(ovpQueryCommand);
            _ocpSetCommandFormat = NullIfWhiteSpace(ocpSetCommandFormat);
            _ocpQueryCommand = NullIfWhiteSpace(ocpQueryCommand);
        }

        // address is retained for API compatibility. SCPI over the dedicated
        // serial port does not include a slave address.
        public void Connect(string portName, int baudRate = 38400, byte address = 0)
        {
            if (string.IsNullOrWhiteSpace(portName))
                throw new ArgumentException("Ainuo AN50 backup-B COM port cannot be empty.", nameof(portName));
            if (string.IsNullOrEmpty(LineTerminator))
                throw new InvalidOperationException("Ainuo AN50 line terminator cannot be empty.");

            Disconnect();
            try
            {
                var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = Math.Max(50, ReadTimeoutMs),
                    WriteTimeout = Math.Max(50, WriteTimeoutMs),
                    NewLine = LineTerminator,
                    DtrEnable = false,
                    RtsEnable = false,
                    Handshake = Handshake.None
                };
                port.Open();
                lock (_sync)
                    _serialPort = port;

                bool outputOn = QueryOutputState();
                float voltageSetpoint = QueryFloat("SOUR:VOLT?");
                float currentSetpoint = QueryFloat("SOUR:CURR?");
                Identity = string.Format(
                    CultureInfo.InvariantCulture,
                    "AINUO AN50 SCPI; OUTP={0}; VSET={1:0.###}V; ISET={2:0.###}A",
                    outputOn ? "ON" : "OFF",
                    voltageSetpoint,
                    currentSetpoint);
            }
            catch
            {
                Disconnect();
                throw;
            }
        }

        public void Disconnect()
        {
            lock (_sync)
            {
                if (_serialPort != null)
                {
                    try
                    {
                        if (_serialPort.IsOpen)
                            _serialPort.Close();
                    }
                    finally
                    {
                        _serialPort.Dispose();
                    }
                }

                _serialPort = null;
                Identity = null;
            }
        }

        public void SetPower(bool turnOn)
        {
            Send(turnOn ? "OUTP ON" : "OUTP OFF");
            bool actual = QueryOutputState();
            if (actual != turnOn)
            {
                throw new InvalidOperationException(
                    $"Ainuo AN50 output verification failed: expected {(turnOn ? "ON" : "OFF")}, " +
                    $"got {(actual ? "ON" : "OFF")}.");
            }
        }

        public void SetVoltageCurrent(float voltage, float current)
        {
            ValidateSetpoint(voltage, Vmax, nameof(voltage));
            ValidateSetpoint(current, Imax, nameof(current));

            // AN50's documented SCPI list has no APPL command, so set and verify
            // the two values independently.
            Send("SOUR:VOLT " + Number(voltage));
            VerifySetpoint("SOUR:VOLT?", voltage, "voltage", VoltageResolution());
            Send("SOUR:CURR " + Number(current));
            VerifySetpoint("SOUR:CURR?", current, "current", CurrentResolution());
        }

        public void SetVoltage(float voltage)
        {
            ValidateSetpoint(voltage, Vmax, nameof(voltage));
            Send("SOUR:VOLT " + Number(voltage));
            VerifySetpoint("SOUR:VOLT?", voltage, "voltage", VoltageResolution());
        }

        public void SetCurrent(float current)
        {
            ValidateSetpoint(current, Imax, nameof(current));
            Send("SOUR:CURR " + Number(current));
            VerifySetpoint("SOUR:CURR?", current, "current", CurrentResolution());
        }

        public void ReadVoltageCurrent(out float voltage, out float current)
        {
            voltage = QueryFloat("MEAS:VOLT?");
            current = QueryFloat("MEAS:CURR?");
        }

        public void ReadOutputTelemetry(
            int readTimeoutMs,
            out bool outputOn,
            out float voltage,
            out float current)
        {
            int timeoutMs = Math.Max(50, readTimeoutMs);
            voltage = QueryFloat("MEAS:VOLT?", timeoutMs);
            current = QueryFloat("MEAS:CURR?", timeoutMs);
            outputOn = QueryOutputState(timeoutMs);
        }

        public void SetOvp(float ovp)
        {
            ValidateSetpoint(ovp, Vmax * 1.1f, nameof(ovp));
            SetConfiguredProtection(
                "OVP",
                ovp,
                _ovpSetCommandFormat,
                _ovpQueryCommand,
                VoltageResolution());
        }

        public void SetOcp(float ocp)
        {
            ValidateSetpoint(ocp, Imax * 1.1f, nameof(ocp));
            SetConfiguredProtection(
                "OCP",
                ocp,
                _ocpSetCommandFormat,
                _ocpQueryCommand,
                CurrentResolution());
        }

        public ushort ReadStatus()
        {
            return QueryOutputState() ? (ushort)0x0100 : (ushort)0;
        }

        public void SetWorkingMode(int mode)
        {
            throw Unsupported("working-mode control");
        }

        public void SetPowerLimit(float powerWatt, bool isMaxPowerGreaterThan100 = true)
        {
            if (float.IsNaN(powerWatt) || float.IsInfinity(powerWatt) || powerWatt < 0)
                throw new ArgumentOutOfRangeException(nameof(powerWatt));
            throw Unsupported("power-limit setting");
        }

        public float ReadPower(bool isMaxPowerGreaterThan100 = true)
        {
            float voltage;
            float current;
            ReadVoltageCurrent(out voltage, out current);
            return voltage * current;
        }

        public void ReadStatistics(
            out float maxV,
            out float maxI,
            out float minV,
            out float minI,
            out float avgV,
            out float avgI)
        {
            float voltage;
            float current;
            ReadVoltageCurrent(out voltage, out current);
            maxV = minV = avgV = voltage;
            maxI = minI = avgI = current;
        }

        public void ClearLimits()
        {
            // The AN50 manual documents this command as clearing the protection
            // alarm and returning the instrument to standby.
            Send("OUTP:PROT:CLE");
        }

        public void Dispose()
        {
            Disconnect();
        }

        private void SetConfiguredProtection(
            string name,
            float value,
            string setCommandFormat,
            string queryCommand,
            float resolution)
        {
            if (string.IsNullOrWhiteSpace(setCommandFormat) ||
                string.IsNullOrWhiteSpace(queryCommand))
            {
                throw new NotSupportedException(
                    $"Ainuo AN50 remote {name} control is not listed in the supplied manual. " +
                    $"Configure vendor-confirmed {name} set/query commands before using this function.");
            }

            Send(string.Format(CultureInfo.InvariantCulture, setCommandFormat, Number(value)));
            VerifySetpoint(queryCommand, value, name, resolution);
        }

        private bool QueryOutputState()
        {
            return QueryOutputState(null);
        }

        private bool QueryOutputState(int? readTimeoutMs)
        {
            string response = Query("OUTP?", readTimeoutMs);
            if (string.Equals(response, "ON", StringComparison.OrdinalIgnoreCase) || response == "1")
                return true;
            if (string.Equals(response, "OFF", StringComparison.OrdinalIgnoreCase) || response == "0")
                return false;
            throw new FormatException($"Invalid Ainuo AN50 response to OUTP?: {response}");
        }

        private void Send(string command)
        {
            lock (_sync)
            {
                EnsureConnected();
                _serialPort.DiscardInBuffer();
                _serialPort.Write(NormalizeCommand(command) + _serialPort.NewLine);
            }
        }

        private string Query(string command)
        {
            return Query(command, null);
        }

        private string Query(string command, int? readTimeoutMs)
        {
            lock (_sync)
            {
                EnsureConnected();
                string normalizedCommand = NormalizeCommand(command);
                int originalTimeout = _serialPort.ReadTimeout;
                int timeoutMs = Math.Max(50, readTimeoutMs ?? originalTimeout);
                var timer = Stopwatch.StartNew();
                try
                {
                    _serialPort.ReadTimeout = timeoutMs;
                    _serialPort.DiscardInBuffer();
                    _serialPort.Write(normalizedCommand + _serialPort.NewLine);

                    while (timer.ElapsedMilliseconds < timeoutMs)
                    {
                        int remaining = Math.Max(50, timeoutMs - (int)timer.ElapsedMilliseconds);
                        _serialPort.ReadTimeout = remaining;
                        string response = (_serialPort.ReadLine() ?? string.Empty).Trim();
                        if (response.Length == 0 ||
                            string.Equals(response, normalizedCommand, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        return response;
                    }

                    throw new TimeoutException(
                        $"Ainuo AN50 returned no data for {normalizedCommand} within {timeoutMs}ms.");
                }
                catch (TimeoutException)
                {
                    throw;
                }
                finally
                {
                    if (_serialPort != null && _serialPort.IsOpen)
                        _serialPort.ReadTimeout = originalTimeout;
                }
            }
        }

        private float QueryFloat(string command)
        {
            return QueryFloat(command, null);
        }

        private float QueryFloat(string command, int? readTimeoutMs)
        {
            string response = Query(command, readTimeoutMs);
            float value;
            if (!float.TryParse(response, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new FormatException($"Invalid Ainuo AN50 response to {command}: {response}");
            return value;
        }

        private void VerifySetpoint(
            string query,
            float expected,
            string name,
            float resolution)
        {
            float actual = QueryFloat(query);
            float tolerance = Math.Max(
                resolution * 0.51f,
                Math.Max(0.001f, Math.Abs(expected) * 0.001f));
            if (Math.Abs(actual - expected) > tolerance)
            {
                throw new InvalidOperationException(
                    $"Ainuo AN50 {name} setpoint verification failed: " +
                    $"expected {expected}, got {actual}.");
            }
        }

        private float VoltageResolution()
        {
            return Vmax >= 100.0f ? 0.1f : 0.01f;
        }

        private float CurrentResolution()
        {
            return Imax >= 10.0f ? 0.1f : 0.01f;
        }

        private void EnsureConnected()
        {
            if (_serialPort == null || !_serialPort.IsOpen)
                throw new InvalidOperationException("Ainuo AN50 backup source B is not connected.");
        }

        private static void ValidateSetpoint(float value, float maximum, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > maximum)
                throw new ArgumentOutOfRangeException(name, value, $"Allowed range is 0..{maximum}.");
        }

        private static void ValidateOptionalCommandFormat(string value, string name)
        {
            if (!string.IsNullOrWhiteSpace(value) && value.IndexOf("{0}", StringComparison.Ordinal) < 0)
                throw new ArgumentException("The command format must contain a {0} value placeholder.", name);
        }

        private static string NormalizeCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                throw new ArgumentException("SCPI command cannot be empty.", nameof(command));
            return command.Trim().TrimEnd('\r', '\n');
        }

        private static string NullIfWhiteSpace(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string Number(float value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static NotSupportedException Unsupported(string operation)
        {
            return new NotSupportedException(
                $"Ainuo AN50 {operation} is not defined by the supplied SCPI manual.");
        }
    }
}
