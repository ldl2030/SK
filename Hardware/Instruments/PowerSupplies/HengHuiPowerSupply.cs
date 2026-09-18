using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;

namespace TestPlatform
{
    /// <summary>
    /// HengHui 8602 programmable DC supply using its SCPI serial protocol.
    /// Station-confirmed settings: 9600 baud, 8N1, LF terminator.
    /// </summary>
    public class HengHuiPowerSupply : IDisposable, IProgrammablePowerSupply
    {
        private static readonly Dictionary<string, SerialPort> SharedPorts =
            new Dictionary<string, SerialPort>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> PortRefCounts =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly object BusLock = new object();

        private SerialPort _serialPort;
        private string _portName;

        public float Vmax { get; set; }
        public float Imax { get; set; }
        public string Identity { get; private set; }

        public HengHuiPowerSupply(float vmax = 300.0f, float imax = 10.0f)
        {
            Vmax = vmax;
            Imax = imax;
        }

        // address is retained for source compatibility.  The two station supplies use
        // independent COM ports and the confirmed SCPI protocol has no slave address.
        public void Connect(string portName, int baudRate = 9600, byte address = 0)
        {
            if (string.IsNullOrWhiteSpace(portName))
                throw new ArgumentException("HengHui 8602 COM port cannot be empty.", nameof(portName));

            Disconnect();
            _portName = portName;

            try
            {
                lock (BusLock)
                {
                    SerialPort port;
                    if (!SharedPorts.TryGetValue(portName, out port) || !port.IsOpen)
                    {
                        port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
                        {
                            ReadTimeout = 1500,
                            WriteTimeout = 1000,
                            NewLine = "\n"
                        };
                        port.Open();
                        SharedPorts[portName] = port;
                        PortRefCounts[portName] = 0;
                    }

                    _serialPort = port;
                    PortRefCounts[portName]++;
                }

                Identity = Query("*IDN?");
                if (Identity.IndexOf("DC POWER", StringComparison.OrdinalIgnoreCase) < 0 ||
                    Identity.IndexOf("8602", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    throw new InvalidOperationException(
                        $"HengHui 8602 identity mismatch on {portName}: {Identity}");
                }
            }
            catch
            {
                Disconnect();
                throw;
            }
        }

        public void Disconnect()
        {
            lock (BusLock)
            {
                if (_serialPort != null && _portName != null && PortRefCounts.ContainsKey(_portName))
                {
                    PortRefCounts[_portName]--;
                    if (PortRefCounts[_portName] <= 0)
                    {
                        if (_serialPort.IsOpen)
                            _serialPort.Close();
                        _serialPort.Dispose();
                        SharedPorts.Remove(_portName);
                        PortRefCounts.Remove(_portName);
                    }
                }

                _serialPort = null;
                _portName = null;
                Identity = null;
            }
        }

        public bool IsConnected
        {
            get
            {
                lock (BusLock)
                    return _serialPort != null && _serialPort.IsOpen;
            }
        }

        private void EnsureConnected()
        {
            if (_serialPort == null || !_serialPort.IsOpen)
                throw new InvalidOperationException("HengHui 8602 is not connected.");
        }

        private void Send(string command)
        {
            lock (BusLock)
            {
                EnsureConnected();
                _serialPort.DiscardInBuffer();
                _serialPort.Write((command ?? string.Empty).TrimEnd('\r', '\n') + "\n");
            }
        }

        private string Query(string command)
        {
            return Query(command, null);
        }

        private string Query(string command, int? readTimeoutMs)
        {
            lock (BusLock)
            {
                EnsureConnected();
                int originalReadTimeout = _serialPort.ReadTimeout;
                try
                {
                    if (readTimeoutMs.HasValue)
                        _serialPort.ReadTimeout = Math.Max(50, readTimeoutMs.Value);
                    _serialPort.DiscardInBuffer();
                    _serialPort.Write((command ?? string.Empty).TrimEnd('\r', '\n') + "\n");
                    string response = _serialPort.ReadLine();
                    if (string.IsNullOrWhiteSpace(response))
                        throw new TimeoutException($"HengHui 8602 returned an empty response to {command}.");
                    return response.Trim();
                }
                finally
                {
                    if (_serialPort != null && _serialPort.IsOpen)
                        _serialPort.ReadTimeout = originalReadTimeout;
                }
            }
        }

        private float QueryFloat(string command)
        {
            float value;
            string response = Query(command);
            if (!float.TryParse(response, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new FormatException($"Invalid HengHui 8602 response to {command}: {response}");
            return value;
        }

        private float QueryFloat(string command, int readTimeoutMs)
        {
            float value;
            string response = Query(command, readTimeoutMs);
            if (!float.TryParse(response, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new FormatException($"Invalid HengHui 8602 response to {command}: {response}");
            return value;
        }

        private static string Number(float value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static void ValidateSetpoint(float value, float maximum, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > maximum)
                throw new ArgumentOutOfRangeException(name, value, $"Allowed range is 0..{maximum}.");
        }

        public void SetPower(bool turnOn)
        {
            Send(turnOn ? ":OUTP ON" : ":OUTP OFF");
            string actual = Query(":OUTP?");
            string expected = turnOn ? "ON" : "OFF";
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"HengHui output verification failed: expected {expected}, got {actual}.");
        }

        public void SetVoltageCurrent(float voltage, float current)
        {
            ValidateSetpoint(voltage, Vmax, nameof(voltage));
            ValidateSetpoint(current, Imax, nameof(current));
            Send($":APPL {Number(voltage)},{Number(current)}");
            VerifySetpoint(":VOLT?", voltage, "voltage");
            VerifySetpoint(":CURR?", current, "current");
        }

        public void SetVoltage(float voltage)
        {
            ValidateSetpoint(voltage, Vmax, nameof(voltage));
            Send($":VOLT {Number(voltage)}");
            VerifySetpoint(":VOLT?", voltage, "voltage");
        }

        public void SetCurrent(float current)
        {
            ValidateSetpoint(current, Imax, nameof(current));
            Send($":CURR {Number(current)}");
            VerifySetpoint(":CURR?", current, "current");
        }

        private void VerifySetpoint(string query, float expected, string name)
        {
            float actual = QueryFloat(query);
            float tolerance = Math.Max(0.001f, Math.Abs(expected) * 0.001f);
            if (Math.Abs(actual - expected) > tolerance)
            {
                throw new InvalidOperationException(
                    $"HengHui {name} setpoint verification failed: expected {expected}, got {actual}.");
            }
        }

        public void ReadVoltageCurrent(out float voltage, out float current)
        {
            voltage = QueryFloat(":MEAS:VOLT?");
            current = QueryFloat(":MEAS:CURR?");
        }

        public void ReadOutputTelemetry(
            int readTimeoutMs,
            out bool outputOn,
            out float voltage,
            out float current)
        {
            int timeoutMs = Math.Max(50, readTimeoutMs);
            voltage = QueryFloat(":MEAS:VOLT?", timeoutMs);
            current = QueryFloat(":MEAS:CURR?", timeoutMs);
            string outputState = Query(":OUTP?", timeoutMs);
            if (!string.Equals(outputState, "ON", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(outputState, "OFF", StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException(
                    $"Invalid HengHui 8602 response to :OUTP?: {outputState}");
            }
            outputOn = string.Equals(outputState, "ON", StringComparison.OrdinalIgnoreCase);
        }

        public void SetOvp(float ovp)
        {
            ValidateSetpoint(ovp, Vmax * 1.2f, nameof(ovp));
            Send($":VOLT:PROT {Number(ovp)}");
            VerifySetpoint(":VOLT:PROT?", ovp, "OVP");
        }

        public void SetOcp(float ocp)
        {
            ValidateSetpoint(ocp, Imax * 1.2f, nameof(ocp));
            Send($":CURR:PROT {Number(ocp)}");
            VerifySetpoint(":CURR:PROT?", ocp, "OCP");
        }

        public ushort ReadStatus()
        {
            return string.Equals(Query(":OUTP?"), "ON", StringComparison.OrdinalIgnoreCase)
                ? (ushort)0x0100
                : (ushort)0;
        }

        public void SetWorkingMode(int mode)
        {
            Send(mode == 0 ? ":POW:STAT OFF" : ":POW:STAT ON");
        }

        public void SetPowerLimit(float powerWatt, bool isMaxPowerGreaterThan100 = true)
        {
            if (powerWatt < 0) throw new ArgumentOutOfRangeException(nameof(powerWatt));
            Send($":POW {Number(powerWatt)}");
        }

        public float ReadPower(bool isMaxPowerGreaterThan100 = true)
        {
            return QueryFloat(":POW?");
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
            Send("*CLS");
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
