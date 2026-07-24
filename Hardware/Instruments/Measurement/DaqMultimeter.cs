using System;
using System.IO.Ports;
using System.Threading;
using System.Globalization;
using System.Collections.Generic;

namespace TestPlatform
{
    /// <summary>
    /// 数字测量万用表 / DAQ 数据采集器 (SCPI协议)
    /// 支持电压、电流测量通道配置，及继电器通道控制
    /// </summary>
    public class DaqMultimeter : IDisposable
    {
        private SerialPort _serialPort;
        private readonly object _lock = new object();

        public void Connect(string portName, int baudRate = 9600)
        {
            if (string.IsNullOrWhiteSpace(portName))
                throw new ArgumentException("[DaqMultimeter] COM口号不能为空，请在设置中配置正确的串口。", nameof(portName));
            Disconnect();
            _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
            _serialPort.NewLine = "\n"; // SCPI 标准换行
            _serialPort.ReadTimeout = 3000;
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

        private void SendCommand(string cmd)
        {
            lock (_lock)
            {
                if (!IsConnected) throw new InvalidOperationException("DAQ Multimeter 未连接。");
                _serialPort.DiscardInBuffer();
                _serialPort.WriteLine(cmd);
                Thread.Sleep(20); // 防堆叠延时
            }
        }

        private string Query(string queryCmd)
        {
            lock (_lock)
            {
                if (!IsConnected) throw new InvalidOperationException("DAQ Multimeter 未连接。");
                _serialPort.DiscardInBuffer();
                _serialPort.WriteLine(queryCmd);
                return _serialPort.ReadLine().Trim();
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

        // ================== 继电器控制 (201-220) ==================

        /// <summary>
        /// 闭合继电器。如 channelList = "@201:210"
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
