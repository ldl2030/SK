using System;
using System.IO.Ports;
using System.Threading;
using System.Globalization;
using System.Collections.Generic;

namespace TestPlatform
{
    public sealed class DaqInvalidMeasurementException : FormatException
    {
        public DaqInvalidMeasurementException(string rawValue)
            : base("DAQ contains an invalid/overrange measurement: " + rawValue)
        {
        }
    }

    public sealed class DaqVoltageSampleSeries
    {
        public double[] Voltages { get; }
        public double[] RelativeTimesSeconds { get; }
        public bool IsSimulated { get; }

        public DaqVoltageSampleSeries(double[] voltages, double[] relativeTimesSeconds, bool isSimulated = false)
        {
            if (voltages == null || relativeTimesSeconds == null ||
                voltages.Length < 2 || voltages.Length != relativeTimesSeconds.Length)
                throw new ArgumentException("DAQ sample values and timestamps must have the same length, at least two.");

            for (int index = 0; index < voltages.Length; index++)
            {
                // SCPI overload values (typically 9.9E37) are not measurements.
                if (double.IsNaN(voltages[index]) || double.IsInfinity(voltages[index]) ||
                    Math.Abs(voltages[index]) >= 9E36)
                    throw new InvalidOperationException("DAQ scan contains an invalid/overrange voltage.");
                double time = relativeTimesSeconds[index];
                if (double.IsNaN(time) || double.IsInfinity(time) || time < 0 ||
                    (index > 0 && time <= relativeTimesSeconds[index - 1]))
                    throw new InvalidOperationException("DAQ scan timestamps must be finite and strictly increasing.");
            }
            Voltages = (double[])voltages.Clone();
            RelativeTimesSeconds = (double[])relativeTimesSeconds.Clone();
            IsSimulated = isSimulated;
        }

        public void ValidateInterval(int intervalMs, bool allowStartupInterval = false)
        {
            if (intervalMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(intervalMs));
            double toleranceMs = Math.Max(2.0, intervalMs * 0.1);
            for (int index = 1; index < RelativeTimesSeconds.Length; index++)
            {
                double actualMs = (RelativeTimesSeconds[index] - RelativeTimesSeconds[index - 1]) * 1000.0;
                // DAQ timestamps mark completed readings. The first reading
                // can include initial settling; retain it, with a separately
                // bounded startup tolerance, rather than discard its value.
                double allowedMs = allowStartupInterval && index == 1
                    ? Math.Max(20.0, intervalMs * 0.25)
                    : toleranceMs;
                if (Math.Abs(actualMs - intervalMs) > allowedMs + 0.000001)
                    throw new InvalidOperationException(
                        $"DAQ sample {index + 1} interval={actualMs:0.###}ms; expected {intervalMs}ms " +
                        $"within ±{allowedMs:0.###}ms. The fixed-window measurement is invalid.");
            }
            // Small per-point errors must not accumulate into a different
            // acquisition window. Allow at most one nominal interval overall.
            double expectedSpanMs = (RelativeTimesSeconds.Length - 1) * (double)intervalMs;
            double actualSpanMs = (RelativeTimesSeconds[RelativeTimesSeconds.Length - 1] -
                RelativeTimesSeconds[0]) * 1000.0;
            if (Math.Abs(actualSpanMs - expectedSpanMs) > intervalMs + 0.000001)
                throw new InvalidOperationException(
                    $"DAQ scan span={actualSpanMs:0.###}ms; expected {expectedSpanMs:0.###}ms " +
                    $"within ±{intervalMs}ms. The full window is invalid.");
        }
    }

    /// <summary>
    /// 数字测量万用表 / DAQ 数据采集器 (SCPI协议)
    /// 支持电压、电流测量通道配置，及继电器通道控制
    /// </summary>
    public class DaqMultimeter : IDisposable
    {
        private SerialPort _serialPort;
        private readonly object _lock = new object();
        private bool _bufferedAutoZeroDisabled;

        public void Connect(string portName, int baudRate = 9600, int readTimeoutMs = 5000)
        {
            if (string.IsNullOrWhiteSpace(portName))
                throw new ArgumentException("[DaqMultimeter] COM口号不能为空，请在设置中配置正确的串口。", nameof(portName));
            Disconnect();
            _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
            _serialPort.NewLine = "\n"; // SCPI 标准换行
            _serialPort.ReadTimeout = Math.Max(1000, readTimeoutMs);
            _serialPort.WriteTimeout = 2000;
            _serialPort.Open();
            
            // 可选：清空一下旧状态
            try { ClearStatus(); } catch { }
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

        protected virtual void SendCommand(string cmd)
        {
            lock (_lock)
            {
                if (!IsConnected) throw new InvalidOperationException("DAQ Multimeter 未连接。");
                _serialPort.DiscardInBuffer();
                _serialPort.WriteLine(cmd);
                Thread.Sleep(20); // 防堆叠延时
            }
        }

        protected virtual string Query(string queryCmd, int readTimeoutMs = 0)
        {
            lock (_lock)
            {
                if (!IsConnected) throw new InvalidOperationException("DAQ Multimeter 未连接。");
                int originalTimeout = _serialPort.ReadTimeout;
                try
                {
                    if (readTimeoutMs > originalTimeout)
                        _serialPort.ReadTimeout = readTimeoutMs;
                    _serialPort.DiscardInBuffer();
                    _serialPort.WriteLine(queryCmd);
                    return _serialPort.ReadLine().Trim();
                }
                finally
                {
                    _serialPort.ReadTimeout = originalTimeout;
                }
            }
        }

        /// <summary>
        /// 查询设备ID (返回机器名称，序列号版本号等信息)
        /// </summary>
        public string GetIdn()
        {
            return Query("*IDN?");
        }

        public void Reset() => SendCommand("*RST");
        
        public void ClearStatus() => SendCommand("*CLS");

        // ================== 测量配置 ==================

        /// <summary>
        /// 设置直流电压测量通道。如 channelList = "@101:120"
        /// </summary>
        public void ConfigVoltageDc(string channelList, float range = 10.0f)
        {
            SendCommand($"CONF:VOLT:DC {range.ToString(CultureInfo.InvariantCulture)},MIN,({channelList})");
        }

        /// <summary>
        /// 设置交流电压测量通道
        /// </summary>
        public void ConfigVoltageAc(string channelList)
        {
            SendCommand($"CONF:VOLT:AC AUTO,MIN,({channelList})");
        }

        /// <summary>
        /// 设置直流电流测量通道。如 channelList = "@121,122"
        /// </summary>
        public void ConfigCurrentDc(string channelList)
        {
            SendCommand($"CONF:CURR:DC AUTO,MIN,({channelList})");
        }

        /// <summary>
        /// 配置扫描速度 (NPLC)
        /// </summary>
        public void SetSpeedNplc(float nplc, string channelList)
        {
            SendCommand($"VOLT:NPLC {nplc.ToString(CultureInfo.InvariantCulture)},({channelList})");
        }

        /// <summary>
        /// 设置交流电流测量通道。
        /// </summary>
        public void ConfigCurrentAc(string channelList)
        {
            SendCommand($"CONF:CURR:AC AUTO,MIN,({channelList})");
        }

        /// <summary>
        /// 设置二线制电阻测量通道。
        /// </summary>
        public void ConfigResistance(string channelList, float range = 1000f)
        {
            SendCommand($"CONF:RES {range.ToString(CultureInfo.InvariantCulture)},MIN,({channelList})");
        }

        /// <summary>
        /// 设置二线制自动量程电阻测量，适用于测试前实测不同量级的分流电阻。
        /// </summary>
        public void ConfigResistanceAuto(string channelList)
        {
            SendCommand($"CONF:RES AUTO,DEF,({channelList})");
        }

        /// <summary>
        /// 设置四线制电阻测量通道。
        /// </summary>
        public void ConfigFResistance(string channelList, float range = 1000f)
        {
            SendCommand($"CONF:FRES {range.ToString(CultureInfo.InvariantCulture)},MIN,({channelList})");
        }

        /// <summary>
        /// 设置频率测量通道。
        /// </summary>
        public void ConfigFrequency(string channelList)
        {
            SendCommand($"CONF:FREQ AUTO,MIN,({channelList})");
        }

        /// <summary>
        /// 设置温度测量通道 (热电偶，默认K型)。
        /// </summary>
        public void ConfigTemperatureTC(string channelList, string tcType = "K")
        {
            SendCommand($"CONF:TEMP TC,{tcType},({channelList})");
        }
        
        /// <summary>
        /// 设置温度测量通道 (热电阻，如PT100)。
        /// </summary>
        public void ConfigTemperatureRTD(string channelList, string rtdType = "PT100")
        {
            SendCommand($"CONF:TEMP RTD,{rtdType},({channelList})");
        }

        /// <summary>
        /// 设置二极管测量通道。
        /// </summary>
        public void ConfigDiode(string channelList)
        {
            SendCommand($"CONF:DIOD ({channelList})");
        }

        /// <summary>
        /// 设置电容测量通道。
        /// </summary>
        public void ConfigCapacitance(string channelList)
        {
            SendCommand($"CONF:CAP AUTO,MIN,({channelList})");
        }

        /// <summary>
        /// 设置周期测量通道。
        /// </summary>
        public void ConfigPeriod(string channelList)
        {
            SendCommand($"CONF:PER AUTO,MIN,({channelList})");
        }

        /// <summary>
        /// 设置温度测量通道 (热敏电阻)。
        /// </summary>
        public void ConfigTemperatureThermistor(string channelList, string type = "5000")
        {
            SendCommand($"CONF:TEMP THER,{type},({channelList})");
        }

        /// <summary>
        /// 设置应变测量通道。
        /// </summary>
        public void ConfigStrain(string channelList)
        {
            SendCommand($"CONF:STR:DIR 120,2,({channelList})");
        }

        // ================== 扫描、触发与延时 ==================

        /// <summary>
        /// 设置扫描通道
        /// </summary>
        public void SetScanList(string channelList)
        {
            SendCommand($"ROUT:SCAN ({channelList})");
        }

        /// <summary>
        /// 设置触发扫描次数 (0代表 inf 无限)
        /// </summary>
        public void SetTriggerCount(int count)
        {
            SendCommand(count <= 0 ? "TRIG:COUN INF" : $"TRIG:COUN {count}");
        }

        /// <summary>
        /// 设置通道扫描延时 (秒)
        /// </summary>
        public void SetChannelDelay(string channelList, float seconds)
        {
            SendCommand($"ROUT:CHAN:DEL {seconds.ToString(CultureInfo.InvariantCulture)},({channelList})");
        }

        /// <summary>
        /// 设置触发源 (IMM: 连续, EXT: 外部, BUS: 软件指令, TIM: 定时)
        /// </summary>
        public void SetTriggerSource(string source)
        {
            SendCommand($"TRIG:SOUR {source}");
        }

        /// <summary>
        /// 设置定时触发的间隔 (秒)
        /// </summary>
        public void SetTriggerTimer(float seconds)
        {
            SendCommand($"TRIG:TIM {seconds.ToString(CultureInfo.InvariantCulture)}");
        }

        /// <summary>
        /// 开始扫描 (使设备进入 Wait-for-Trigger 状态)
        /// </summary>
        public void StartScan()
        {
            SendCommand("INIT");
        }

        /// <summary>
        /// Configure and start a buffered, internally timed DC-voltage scan.
        /// The DAQ performs all trigger timing internally; the serial link is
        /// used only for setup and for one bulk fetch after acquisition.
        /// </summary>
        public void StartTimedVoltageDcScan(
            string channelList,
            double range,
            double nplc,
            double intervalSeconds,
            int triggerCount,
            bool includeRelativeTimes = false,
            bool disableAutoZero = false)
        {
            if (string.IsNullOrWhiteSpace(channelList))
                throw new ArgumentException("DAQ timed scan channel list is required.", nameof(channelList));
            if (range <= 0)
                throw new ArgumentOutOfRangeException(nameof(range));
            if (nplc <= 0)
                throw new ArgumentOutOfRangeException(nameof(nplc));
            if (intervalSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
            if (triggerCount <= 1)
                throw new ArgumentOutOfRangeException(nameof(triggerCount));

            RecoverVoltageDcMeasurement();
            SendCommand(
                $"CONF:VOLT:DC {range.ToString(CultureInfo.InvariantCulture)},DEF,({channelList})");
            SendCommand(
                $"VOLT:NPLC {nplc.ToString(CultureInfo.InvariantCulture)},({channelList})");

            _bufferedAutoZeroDisabled = disableAutoZero;
            if (disableAutoZero)
                SendCommand($"VOLT:DC:ZERO:AUTO OFF,({channelList})");

            // Keep FETCH? compact enough for the 9600-baud link. The scan
            // timing itself remains inside the DAQ and is not affected by the
            // later serial transfer.
            SendCommand("FORM:READ:UNIT OFF");
            SendCommand(includeRelativeTimes ? "FORM:READ:TIME ON" : "FORM:READ:TIME OFF");
            if (includeRelativeTimes)
                SendCommand("FORM:READ:TIME:TYPE REL");
            SendCommand("FORM:READ:CHAN OFF");
            SendCommand("FORM:READ:ALAR OFF");
            SetScanList(channelList);
            SetChannelDelay(channelList, 0);
            SetTriggerSource("TIM");
            SetTriggerTimer((float)intervalSeconds);
            SetTriggerCount(triggerCount);
            StartScan();
        }

        /// <summary>
        /// Starts a buffered, immediately repeated acquisition using autorange.
        /// TRIG:COUN lets the DAQ acquire every point without one READ? serial
        /// round-trip per sample; INIT starts acquisition and FETCH? reads the
        /// complete buffer once.
        /// </summary>
        public void StartBufferedVoltageDcAutoRangeScan(
            string channelList,
            double nplc,
            double intervalSeconds,
            int triggerCount)
        {
            StartBufferedVoltageDcScanCore(
                channelList,
                null,
                nplc,
                intervalSeconds,
                triggerCount,
                false);
        }

        /// <summary>
        /// Starts a DAQ-buffered repeated acquisition with a fixed range.
        /// Fixed range avoids an autorange decision on every point. Auto-zero
        /// can be disabled during the burst and is restored afterwards.
        /// </summary>
        public void StartBufferedVoltageDcScan(
            string channelList,
            double range,
            double nplc,
            double intervalSeconds,
            int triggerCount,
            bool disableAutoZero)
        {
            if (range <= 0)
                throw new ArgumentOutOfRangeException(nameof(range));

            StartBufferedVoltageDcScanCore(
                channelList,
                range,
                nplc,
                intervalSeconds,
                triggerCount,
                disableAutoZero);
        }

        private void StartBufferedVoltageDcScanCore(
            string channelList,
            double? range,
            double nplc,
            double intervalSeconds,
            int triggerCount,
            bool disableAutoZero)
        {
            if (string.IsNullOrWhiteSpace(channelList))
                throw new ArgumentException("DAQ buffered scan channel list is required.", nameof(channelList));
            if (nplc <= 0)
                throw new ArgumentOutOfRangeException(nameof(nplc));
            if (intervalSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
            if (triggerCount <= 1)
                throw new ArgumentOutOfRangeException(nameof(triggerCount));

            RecoverVoltageDcMeasurement();
            string rangeText = range.HasValue
                ? range.Value.ToString(CultureInfo.InvariantCulture)
                : "AUTO";
            SendCommand($"CONF:VOLT:DC {rangeText},DEF,({channelList})");
            SendCommand(
                $"VOLT:NPLC {nplc.ToString(CultureInfo.InvariantCulture)},({channelList})");
            _bufferedAutoZeroDisabled = disableAutoZero;
            if (disableAutoZero)
                SendCommand($"VOLT:DC:ZERO:AUTO OFF,({channelList})");
            SendCommand("FORM:READ:UNIT OFF");
            SendCommand("FORM:READ:TIME OFF");
            SendCommand("FORM:READ:CHAN OFF");
            SendCommand("FORM:READ:ALAR OFF");
            SetScanList(channelList);
            // NPLC already consumes acquisition time. Add only the remainder
            // needed to maintain the requested start-to-start interval.
            double measurementSecondsAt50Hz = nplc / 50.0;
            float additionalDelaySeconds = (float)Math.Max(
                0.0,
                intervalSeconds - measurementSecondsAt50Hz);
            SetChannelDelay(channelList, additionalDelaySeconds);
            SetTriggerSource("IMM");
            SetTriggerCount(triggerCount);
            StartScan();
        }

        public double[] FetchTimedVoltageDcScan(int expectedCount, out string rawResponse)
        {
            rawResponse = FetchAllData();
            return ParseMeasurementValues(rawResponse, expectedCount);
        }

        public DaqVoltageSampleSeries FetchTimedVoltageDcSampleSeries(int expectedCount)
        {
            return FetchTimedVoltageDcSampleSeries(expectedCount, out _);
        }

        public DaqVoltageSampleSeries FetchTimedVoltageDcSampleSeries(int expectedCount, out string rawResponse)
        {
            if (expectedCount < 2)
                throw new ArgumentOutOfRangeException(nameof(expectedCount));
            rawResponse = FetchTimedVoltageDcScanRawData(expectedCount);
            return ParseTimedVoltageDcSampleSeries(rawResponse, expectedCount);
        }

        public void AbortTimedVoltageDcScan()
        {
            // ABOR stops acquisition without starting a new scan or clearing
            // the reading memory. FETCH? can then preserve a partial window.
            SendCommand("ABOR");
        }

        public string FetchTimedVoltageDcScanRawData(int expectedCount)
        {
            if (expectedCount < 1)
                throw new ArgumentOutOfRangeException(nameof(expectedCount));
            // Allow the voltage/time pairs to cross the 9600-baud serial link.
            // This transfer timeout does not change acquisition timing.
            int transferTimeoutMs = (int)Math.Ceiling(expectedCount * 64.0 * 10.0 /
                _serialPort.BaudRate * 1000.0) + 1000;
            return Query("FETCH?", transferTimeoutMs);
        }

        private static DaqVoltageSampleSeries ParseTimedVoltageDcSampleSeries(string response, int expectedCount)
        {
            if (string.IsNullOrWhiteSpace(response))
                throw new InvalidOperationException("DAQ timed scan returned no readings.");
            string[] fields = response.Split(',');
            if (fields.Length != expectedCount * 2)
                throw new InvalidOperationException(
                    $"DAQ timed scan expected {expectedCount} voltage/time pairs, returned {fields.Length} fields.");
            var voltages = new double[expectedCount];
            var times = new double[expectedCount];
            for (int index = 0; index < expectedCount; index++)
            {
                if (!double.TryParse(fields[index * 2].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out voltages[index]) ||
                    !double.TryParse(fields[index * 2 + 1].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out times[index]))
                    throw new FormatException($"DAQ timed scan voltage/time pair {index + 1} is malformed.");
            }
            return new DaqVoltageSampleSeries(voltages, times);
        }

        public void EnsureNoScanError()
        {
            string response = Query("SYST:ERR?");
            int separator = response.IndexOf(',');
            string codeText = separator < 0 ? response : response.Substring(0, separator);
            if (!int.TryParse(codeText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
                throw new FormatException("DAQ error response is malformed: " + response);
            if (code != 0)
                throw new InvalidOperationException("DAQ scan error: " + response);
        }

        public void RestoreAfterTimedVoltageDcScan()
        {
            RecoverVoltageDcMeasurement();
            if (_bufferedAutoZeroDisabled)
            {
                SendCommand("VOLT:DC:ZERO:AUTO ON");
                _bufferedAutoZeroDisabled = false;
            }
            SetTriggerSource("IMM");
            SetTriggerCount(1);
            SendCommand("FORM:READ:TIME OFF");
        }

        // ================== 数据回读与统计 ==================

        /// <summary>
        /// 触发一次读取并回读数据
        /// </summary>
        public string ReadRawData()
        {
            return Query("READ?");
        }

        /// <summary>
        /// 提取缓冲内的所有扫描数据
        /// </summary>
        public string FetchAllData()
        {
            return Query("FETCH?");
        }

        /// <summary>
        /// 获取当前内存中的读数总数
        /// </summary>
        public int GetPointsCount()
        {
            string resp = Query("DATA:POIN?");
            return int.TryParse(resp, out int count) ? count : 0;
        }

        /// <summary>
        /// 读取单个数值（自动解析第一段数字）
        /// 格式如: "4.589E-02 VDC,00000000.069,101,0" -> 返回 0.04589
        /// </summary>
        public double ReadSingleValue()
        {
            return ParseSingleValue(Query("READ?"));
        }

        /// <summary>
        /// Configure the requested channel explicitly, read once, then return
        /// to idle. Do not use MEAS? here: repeated MEAS? calls on the station's
        /// DAQ-9600 alternated between successful readings and timeouts.
        /// </summary>
        public double MeasureVoltageDc(string channelList, out string rawResponse, bool validateSample = true, double? rangeVolts = null)
        {
            lock (_lock)
            {
                try
                {
                    PrepareVoltageDcMeasurementSeries(channelList, rangeVolts);
                    rawResponse = Query("READ?");
                    return validateSample ? ParseMeasurementValues(rawResponse, 1)[0] : ParseSingleValue(rawResponse);
                }
                finally { RecoverVoltageDcMeasurement(); }
            }
        }

        /// <summary>
        /// Prepare one channel list for a series of READ? operations.  CONF
        /// establishes the measurement function once and ROUT:SCAN replaces
        /// any stale scan list, so subsequent samples do not need to repeat
        /// MEAS configuration.
        /// </summary>
        public void PrepareVoltageDcMeasurementSeries(string channelList, double? rangeVolts = null)
        {
            if (rangeVolts.HasValue &&
                (rangeVolts.Value <= 0 || double.IsNaN(rangeVolts.Value) || double.IsInfinity(rangeVolts.Value)))
                throw new ArgumentOutOfRangeException(nameof(rangeVolts));
            string range = rangeVolts.HasValue
                ? rangeVolts.Value.ToString("G", CultureInfo.InvariantCulture)
                : "AUTO";
            RecoverVoltageDcMeasurement();
            // DAQ-9600 manual pp. 265/281: AUTO range requires DEF resolution.
            string resolution = rangeVolts.HasValue ? "MIN" : "DEF";
            SendCommand($"CONF:VOLT:DC {range},{resolution},({channelList})");
            SetScanList(channelList);
            PrepareSingleReadingState(channelList);
        }

        private void PrepareSingleReadingState(string channelList)
        {
            // ABOR/*CLS do not reset the trigger/format settings left by an
            // 81-point timed scan or an eight-point buffered average.
            SetTriggerSource("IMM");
            SetTriggerCount(1);
            SendCommand($"ROUT:CHAN:DEL:AUTO ON,({channelList})");
            if (_bufferedAutoZeroDisabled)
            {
                SendCommand("VOLT:DC:ZERO:AUTO ON");
                _bufferedAutoZeroDisabled = false;
            }
            SendCommand("FORM:READ:UNIT OFF");
            SendCommand("FORM:READ:TIME OFF");
            SendCommand("FORM:READ:CHAN OFF");
            SendCommand("FORM:READ:ALAR OFF");
        }

        public double MeasureResistanceAuto(string channelList, out string rawResponse)
        {
            lock (_lock)
            {
                try
                {
                    RecoverVoltageDcMeasurement();
                    ConfigResistanceAuto(channelList);
                    SetScanList(channelList);
                    PrepareSingleReadingState(channelList);
                    rawResponse = Query("READ?");
                    double value;
                    if (!double.TryParse(rawResponse.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                        double.IsNaN(value) || double.IsInfinity(value))
                        throw new FormatException("DAQ resistance response is invalid: " + rawResponse);
                    // Positive resistance overload is valid for open-contact
                    // tests. Voltage readings use the stricter parser above.
                    return value;
                }
                finally { RecoverVoltageDcMeasurement(); }
            }
        }

        /// <summary>
        /// Read one sample using the channel configuration established by
        /// PrepareVoltageDcMeasurementSeries.
        /// </summary>
        public double ReadVoltageDcMeasurementSeriesSample(out string rawResponse)
        {
            rawResponse = Query("READ?");
            return ParseMeasurementValues(rawResponse, 1)[0];
        }

        /// <summary>
        /// Return the DAQ to idle and clear its status/error queue.  Call this
        /// once before a measurement series and again only when that series
        /// fails.  This is not a full instrument reset (*RST).
        /// </summary>
        public void RecoverVoltageDcMeasurement()
        {
            SendCommand("ABOR");
            ClearStatus();
        }

        /// <summary>
        /// 读取扫描统计：平均值
        /// </summary>
        public double GetAverage(string channelList)
        {
            return ParseSingleValue(Query($"CALC:AVER:AVER? ({channelList})"));
        }

        /// <summary>
        /// 读取扫描统计：最大值
        /// </summary>
        public double GetMax(string channelList)
        {
            return ParseSingleValue(Query($"CALC:AVER:MAX? ({channelList})"));
        }

        /// <summary>
        /// 读取扫描统计：最小值
        /// </summary>
        public double GetMin(string channelList)
        {
            return ParseSingleValue(Query($"CALC:AVER:MIN? ({channelList})"));
        }

        /// <summary>
        /// 清除统计数据
        /// </summary>
        public void ClearStats()
        {
            SendCommand("CALC:AVER:CLE");
        }

        // ================== 报警限制 ==================

        /// <summary>
        /// 设置通道报警上限
        /// </summary>
        public void SetAlarmUpperLimit(string channelList, float limit)
        {
            SendCommand($"CALC:LIM:UPP {limit.ToString(CultureInfo.InvariantCulture)},({channelList})");
            SendCommand($"CALC:LIM:UPP:STAT ON,({channelList})");
        }

        /// <summary>
        /// 设置通道报警下限
        /// </summary>
        public void SetAlarmLowerLimit(string channelList, float limit)
        {
            SendCommand($"CALC:LIM:LOW {limit.ToString(CultureInfo.InvariantCulture)},({channelList})");
            SendCommand($"CALC:LIM:LOW:STAT ON,({channelList})");
        }

        private double ParseSingleValue(string resp)
        {
            if (string.IsNullOrEmpty(resp)) return 0.0;
            var parts = resp.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
            {
                return val;
            }
            return 0.0;
        }

        private static double[] ParseMeasurementValues(string response, int expectedCount)
        {
            if (string.IsNullOrWhiteSpace(response))
                throw new InvalidOperationException("DAQ buffered scan returned no measurements.");

            string[] fields = response.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var values = new List<double>(fields.Length);
            foreach (string field in fields)
            {
                double value;
                if (!double.TryParse(
                        field.Trim(),
                        NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture,
                        out value))
                {
                    throw new FormatException(
                        "DAQ buffered scan contains an invalid measurement: " + field.Trim());
                }
                if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) >= 1e30)
                    throw new DaqInvalidMeasurementException(field.Trim());
                values.Add(value);
            }

            if (values.Count != expectedCount)
            {
                throw new InvalidOperationException(
                    $"DAQ buffered scan expected {expectedCount} measurements but returned {values.Count}.");
            }

            return values.ToArray();
        }

        // ================== 继电器控制 (301-320) ==================

        /// <summary>
        /// 闭合继电器。如 channelList = "@301:310"
        /// </summary>
        public void CloseRelay(string channelList)
        {
            // 注意：具体 SCPI 指令可能是 ROUT:CLOS 或 ROUT:CHAN:CLOS
            // 通用 Agilent/Keysight 语法通常是 ROUT:CLOS (@xxx)
            SendCommand($"ROUT:CLOS ({channelList})");
        }

        /// <summary>
        /// 断开继电器
        /// </summary>
        public void OpenRelay(string channelList)
        {
            SendCommand($"ROUT:OPEN ({channelList})");
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
