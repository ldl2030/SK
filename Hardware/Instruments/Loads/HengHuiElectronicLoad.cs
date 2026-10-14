using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;
using System.Globalization;

namespace TestPlatform
{
    /// <summary>
    /// 恒惠(HengHui) 电子负载 SCPI 控制驱动 (如 300V 12A 3600W 型号)
    /// </summary>
    public class HengHuiElectronicLoad : IDisposable, IDeviceLogSource
    {
        private const float MinimumProgrammableSetpoint = 0.001f;
        private const float MaximumProgrammableSetpoint = 999.999f;
        private SerialPort _serialPort;
        private readonly object _lock = new object();
        private bool _protectionClearSkipLogged;

        public event Action<string> LogInfo;
        public event Action<string> LogError;

        public int CommandDelayMs { get; set; } = 50;
        public int ReadTimeoutMs { get; set; } = 2500;
        public int FastQueryTimeoutMs { get; set; } = 700;
        public int RecoveryQueryTimeoutMs { get; set; } = 1500;
        public int WriteTimeoutMs { get; set; } = 2000;
        public int QueryRetryDelayMs { get; set; } = 200;
        public int QueryAttempts { get; set; } = 2;
        public string ProtectionClearCommand { get; set; }

        public void Connect(string portName, int baudRate = 9600)
        {
            if (string.IsNullOrWhiteSpace(portName))
                throw new ArgumentException("[HengHuiElectronicLoad] COM口号不能为空，请在设置中配置正确的串口。", nameof(portName));
            Disconnect();
            _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
            // 大多数 SCPI 设备以 \n 结尾
            _serialPort.NewLine = "\n";
            _serialPort.ReadTimeout = Math.Max(100, ReadTimeoutMs);
            _serialPort.WriteTimeout = Math.Max(100, WriteTimeoutMs);
            _serialPort.Handshake = Handshake.None;
            _serialPort.DtrEnable = false;
            _serialPort.RtsEnable = false;
            _serialPort.Open();
        }

        public void Disconnect()
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
            }
            _serialPort?.Dispose();
            _serialPort = null;
        }

        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;

        private void SendCommand(string cmd)
        {
            lock (_lock)
            {
                if (!IsConnected) throw new InvalidOperationException("HengHui 电子负载未连接。");
                string portName = _serialPort.PortName;
                try
                {
                    _serialPort.DiscardInBuffer();
                    LogInfo?.Invoke($"电子负载 TX [{portName}] {cmd}");
                    _serialPort.WriteLine(cmd);
                    Thread.Sleep(Math.Max(0, CommandDelayMs));
                }
                catch (Exception ex)
                {
                    LogError?.Invoke($"电子负载 TX ERROR [{portName}] {cmd}: {ex.Message}");
                    throw;
                }
            }
        }

        private string Query(string queryCmd, int? readTimeoutMs = null)
        {
            lock (_lock)
            {
                if (!IsConnected) throw new InvalidOperationException("HengHui 电子负载未连接。");
                string portName = _serialPort.PortName;
                var timer = Stopwatch.StartNew();
                int originalReadTimeout = _serialPort.ReadTimeout;
                try
                {
                    if (readTimeoutMs.HasValue)
                        _serialPort.ReadTimeout = Math.Max(100, readTimeoutMs.Value);
                    _serialPort.DiscardInBuffer();
                    LogInfo?.Invoke($"电子负载 TX [{portName}] {queryCmd}");
                    _serialPort.WriteLine(queryCmd);
                    string response = _serialPort.ReadLine().Trim();
                    timer.Stop();
                    LogInfo?.Invoke(
                        $"电子负载 RX [{portName}] {queryCmd} ({timer.ElapsedMilliseconds} ms): {response}");
                    return response;
                }
                catch (Exception ex)
                {
                    timer.Stop();
                    LogError?.Invoke(
                        $"电子负载 QUERY ERROR [{portName}] {queryCmd} " +
                        $"({timer.ElapsedMilliseconds} ms): {ex.Message}");
                    throw;
                }
                finally
                {
                    if (_serialPort != null && _serialPort.IsOpen)
                        _serialPort.ReadTimeout = originalReadTimeout;
                }
            }
        }

        private void ResynchronizeAfterQueryFailure()
        {
            lock (_lock)
            {
                if (!IsConnected)
                    return;

                if (QueryRetryDelayMs > 0)
                    Thread.Sleep(QueryRetryDelayMs);

                try
                {
                    // A reply that arrives just after a timeout must not be
                    // mistaken for the response to the next query.
                    _serialPort.DiscardInBuffer();
                    _serialPort.DiscardOutBuffer();
                }
                catch (Exception ex)
                {
                    LogInfo?.Invoke($"电子负载查询重同步失败：{ex.Message}");
                }
            }
        }

        private string QueryWithRetry(string queryCmd, int maxAttempts = 0)
        {
            Exception lastError = null;
            int attempts = Math.Max(1, maxAttempts > 0 ? maxAttempts : QueryAttempts);
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    int timeoutMs = attempt == 1
                        ? Math.Max(100, FastQueryTimeoutMs)
                        : Math.Max(100, RecoveryQueryTimeoutMs);
                    string response = Query(queryCmd, timeoutMs);
                    if (string.IsNullOrWhiteSpace(response))
                        throw new FormatException("empty response");
                    return response;
                }
                catch (Exception ex) when (ex is TimeoutException || ex is FormatException)
                {
                    lastError = ex;
                    if (attempt < attempts)
                    {
                        LogInfo?.Invoke(
                            $"电子负载查询重试 {queryCmd}: {attempt}/{attempts}，" +
                            $"本次超时={Math.Max(100, FastQueryTimeoutMs)}ms，原因={ex.Message}");
                        ResynchronizeAfterQueryFailure();
                    }
                }
            }

            throw new InvalidOperationException(
                $"KUNKIN 7303 query '{queryCmd}' failed after {attempts} attempts.",
                lastError);
        }

        private static float ParseMeasurement(string command, string response)
        {
            float value;
            if (!float.TryParse(
                    response,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out value))
            {
                throw new FormatException(
                    $"KUNKIN 7303 returned invalid numeric data for '{command}': '{response}'.");
            }
            return value;
        }

        /// <summary>
        /// 控制负载拉载开关
        /// </summary>
        public virtual void SetInputState(bool on)
        {
            SendCommand(on ? "INP 1" : "INP 0");
        }

        /// <summary>
        /// Clear latched input protection (OCP/OPP/OVP) before a new load attempt.
        /// </summary>
        public void ClearInputProtection()
        {
            string command = (ProtectionClearCommand ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(command))
            {
                if (!_protectionClearSkipLogged)
                {
                    _protectionClearSkipLogged = true;
                    LogInfo?.Invoke(
                        "电子负载SCPI手册未定义保护清除命令；本次连接不下发INP:PROT:CLE。");
                }
                return;
            }

            SendCommand(command);
        }

        public virtual bool GetInputState()
        {
            string response = QueryWithRetry("INP?").Trim().ToUpperInvariant();
            if (response == "1" || response == "ON" || response.EndsWith(" 1") || response.EndsWith(":ON"))
                return true;
            if (response == "0" || response == "OFF" || response.EndsWith(" 0") || response.EndsWith(":OFF"))
                return false;
            throw new FormatException($"KUNKIN 7303 returned invalid input state: '{response}'.");
        }

        /// <summary>
        /// 设置负载工作模式: CURR(定电流), VOLT(定电压), RES(定电阻), POW(定功率), DYN(动态)
        /// </summary>
        public void SetMode(string mode)
        {
            string requestedMode = (mode ?? string.Empty).Trim().ToUpperInvariant();
            string deviceMode;
            switch (requestedMode)
            {
                case "CV":
                case "VOLT":
                    deviceMode = "VOLT";
                    break;
                case "CC":
                case "CURR":
                    deviceMode = "CURR";
                    break;
                case "CR":
                case "RES":
                    deviceMode = "RES";
                    break;
                case "CW":
                case "CP":
                case "POW":
                    deviceMode = "POW";
                    break;
                case "DYN":
                    deviceMode = "DYN";
                    break;
                default:
                    throw new ArgumentException(
                        $"不支持的电子负载模式：'{mode}'。可用模式为CV/CC/CR/CW/DYN。",
                        nameof(mode));
            }

            SendCommand($"MODE {deviceMode}");
        }

        public string GetMode()
        {
            string response = QueryWithRetry("MODE?").Trim().ToUpperInvariant();
            if (response.Contains("CV") || response.Contains("VOLT"))
                return "CV";
            if (response.Contains("CW") || response.Contains("CP") || response.Contains("POW"))
                return "CW";
            if (response.Contains("CC") || response.Contains("CURR"))
                return "CC";
            if (response.Contains("CR") || response.Contains("RES"))
                return "CR";
            throw new FormatException($"KUNKIN 7303 returned invalid operating mode: '{response}'.");
        }

        // ===================== 基本带载参数设置 =====================

        public virtual void SetCurrent(float current)
        {
            float deviceValue = NormalizeProgrammableSetpoint(current, nameof(current));
            LogMinimumSetpointSubstitution("CURR", current, deviceValue, "A");
            SendCommand($"CURR {FormatProgrammableSetpoint(deviceValue)}");
        }
        
        public void SetCurrentSlewRate(float slewRate)
        {
            SendCommand($"CURR:SLEW {slewRate.ToString(CultureInfo.InvariantCulture)}");
        }

        public void SetCurrentProtection(float currentLimit)
        {
            SendCommand($"CURR:PROT {currentLimit.ToString("0.###", CultureInfo.InvariantCulture)}");
        }

        public virtual void SetVoltage(float voltage)
        {
            float deviceValue = NormalizeProgrammableSetpoint(voltage, nameof(voltage));
            LogMinimumSetpointSubstitution("VOLT", voltage, deviceValue, "V");
            SendCommand($"VOLT {FormatProgrammableSetpoint(deviceValue)}");
        }

        private static float NormalizeProgrammableSetpoint(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 ||
                value > MaximumProgrammableSetpoint)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    $"电子负载设定范围为0至{MaximumProgrammableSetpoint:0.000}；" +
                    $"设备SCPI最小可编程值为{MinimumProgrammableSetpoint:0.000}。");
            }

            return value < MinimumProgrammableSetpoint
                ? MinimumProgrammableSetpoint
                : value;
        }

        private static string FormatProgrammableSetpoint(float value)
        {
            return value.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private void LogMinimumSetpointSubstitution(
            string command,
            float requestedValue,
            float deviceValue,
            string unit)
        {
            if (Math.Abs(requestedValue - deviceValue) < 0.0000001f)
                return;

            LogInfo?.Invoke(
                $"电子负载逻辑设定{command}={requestedValue:0.###}{unit}；" +
                $"按SCPI合法范围下发{deviceValue:0.000}{unit}，输入保持OFF时等效为安全零输出。");
        }
        
        public void SetVon(float von)
        {
            SendCommand($"VOLT:ON {von.ToString(CultureInfo.InvariantCulture)}");
        }
        
        public void SetVoff(float voff)
        {
            SendCommand($"VOLT:OFF {voff.ToString(CultureInfo.InvariantCulture)}");
        }
        
        public void SetVoltageRange(bool isMax)
        {
            SendCommand(isMax ? "VOLT:RANG MAX" : "VOLT:RANG MIN");
        }

        public void SetPower(float power)
        {
            SendCommand($"POW {power.ToString(CultureInfo.InvariantCulture)}");
        }

        public void SetPowerProtection(float powerLimit)
        {
            SendCommand($"POW:PROT {powerLimit.ToString("0.###", CultureInfo.InvariantCulture)}");
        }

        public void SetResistance(float resistance)
        {
            SendCommand($"RES {resistance.ToString(CultureInfo.InvariantCulture)}");
        }

        // ===================== 测量指令 =====================

        public float MeasureVoltage()
        {
            string resp = QueryWithRetry("MEAS:VOLT?");
            return ParseMeasurement("MEAS:VOLT?", resp);
        }

        /// <summary>
        /// Performs one bounded query without the normal recovery retry. This is
        /// used while the load is powering up so readiness can be polled without
        /// letting one early timeout consume the complete startup allowance.
        /// </summary>
        public float MeasureVoltageOnce(int readTimeoutMs)
        {
            string resp = Query("MEAS:VOLT?", readTimeoutMs);
            return ParseMeasurement("MEAS:VOLT?", resp);
        }

        public float MeasureCurrent()
        {
            string resp = QueryWithRetry("MEAS:CURR?");
            return ParseMeasurement("MEAS:CURR?", resp);
        }

        public float MeasureCurrentOnce(int readTimeoutMs)
        {
            string resp = Query("MEAS:CURR?", readTimeoutMs);
            return ParseMeasurement("MEAS:CURR?", resp);
        }

        public float MeasurePower()
        {
            string resp = QueryWithRetry("MEAS:POW?");
            return ParseMeasurement("MEAS:POW?", resp);
        }
        
        public float MeasureVpp()
        {
            string resp = Query("MEAS:VOLT:PTP?");
            return float.TryParse(resp, NumberStyles.Any, CultureInfo.InvariantCulture, out float val) ? val : 0f;
        }

        // ===================== OCP 自动测试 =====================

        public void ConfigOcpTest(float triggerVoltage, float startCurrent, float endCurrent, int stepCount, float dwellTime)
        {
            SendCommand($"OCP:VTR {triggerVoltage.ToString(CultureInfo.InvariantCulture)}");
            SendCommand($"OCP:IST {startCurrent.ToString(CultureInfo.InvariantCulture)}");
            SendCommand($"OCP:IEND {endCurrent.ToString(CultureInfo.InvariantCulture)}");
            SendCommand($"OCP:STEP {stepCount}");
            SendCommand($"OCP:DWEL {dwellTime.ToString(CultureInfo.InvariantCulture)}");
        }

        public void StartOcpTest(bool start)
        {
            SendCommand(start ? "OCP 1" : "OCP 0");
        }

        /// <summary>
        /// 查询 OCP 结果。
        /// 若返回 -1(测试未完成), -2(未跌至触发电平), -3(启动失败) 则失败，保护点均输出为对应错误码。
        /// </summary>
        public bool QueryOcpResult(out float pMax, out float v, out float a)
        {
            pMax = -1; v = -1; a = -1;
            string resp = Query("OCP:RES:PMAX?");
            var parts = resp.Split(',');
            
            if (parts.Length == 1)
            {
                // -1, -2, or -3
                if (float.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out float err))
                {
                    a = err;
                }
                return false;
            }
            else if (parts.Length == 3)
            {
                float.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out pMax);
                float.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out v);
                float.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out a);
                return true;
            }
            return false;
        }

        // ===================== 时序 (Timing) 测试 =====================
        
        public void ConfigTimingLoad(string mode, float value)
        {
            SendCommand($"TIM:LOAD:MODE {mode}"); // CURR, VOLT, RES, POW
            SendCommand($"TIM:LOAD:VAL {value.ToString(CultureInfo.InvariantCulture)}");
        }

        /// <summary>
        /// 设置时序测试启动条件 (sour: VOLT/CURR/EXT, edge: RISE/FALL)
        /// </summary>
        public void ConfigTimingStart(string sour, string edge, float level)
        {
            SendCommand($"TIM:TST:SOUR {sour}");
            SendCommand($"TIM:TST:EDGE {edge}");
            if (sour != "EXT") SendCommand($"TIM:TST:LEV {level.ToString(CultureInfo.InvariantCulture)}");
        }

        /// <summary>
        /// 设置时序测试停止条件 (sour: VOLT/CURR/EXT, edge: RISE/FALL)
        /// </summary>
        public void ConfigTimingEnd(string sour, string edge, float level)
        {
            SendCommand($"TIM:TEND:SOUR {sour}");
            SendCommand($"TIM:TEND:EDGE {edge}");
            if (sour != "EXT") SendCommand($"TIM:TEND:LEV {level.ToString(CultureInfo.InvariantCulture)}");
        }

        public void StartTimingTest(bool start)
        {
            SendCommand(start ? "TIM 1" : "TIM 0");
        }

        public float QueryTimingResult()
        {
            string resp = Query("TIM:RES?");
            return float.TryParse(resp, NumberStyles.Any, CultureInfo.InvariantCulture, out float val) ? val : -1f;
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
