using System;
using System.IO.Ports;
using System.Threading;
using System.Globalization;

namespace TestPlatform
{
    /// <summary>
    /// 恒惠(HengHui) 电子负载 SCPI 控制驱动 (如 300V 12A 3600W 型号)
    /// </summary>
    public class HengHuiElectronicLoad : IDisposable
    {
        private SerialPort _serialPort;
        private readonly object _lock = new object();

        public void Connect(string portName, int baudRate = 9600)
        {
            if (string.IsNullOrWhiteSpace(portName))
                throw new ArgumentException("[HengHuiElectronicLoad] COM口号不能为空，请在设置中配置正确的串口。", nameof(portName));
            Disconnect();
            _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
            // 大多数 SCPI 设备以 \n 结尾
            _serialPort.NewLine = "\n"; 
            _serialPort.ReadTimeout = 2000;
            _serialPort.WriteTimeout = 2000;
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
                _serialPort.DiscardInBuffer();
                _serialPort.WriteLine(cmd);
                Thread.Sleep(20); // 稍微延时防止设备处理不过来
            }
        }

        private string Query(string queryCmd)
        {
            lock (_lock)
            {
                if (!IsConnected) throw new InvalidOperationException("HengHui 电子负载未连接。");
                _serialPort.DiscardInBuffer();
                _serialPort.WriteLine(queryCmd);
                return _serialPort.ReadLine().Trim();
            }
        }

        /// <summary>
        /// 控制负载拉载开关
        /// </summary>
        public void SetInputState(bool on)
        {
            SendCommand(on ? "INP 1" : "INP 0");
        }

        /// <summary>
        /// 设置负载工作模式: CURR(定电流), VOLT(定电压), RES(定电阻), POW(定功率), DYN(动态)
        /// </summary>
        public void SetMode(string mode)
        {
            SendCommand($"MODE {mode}");
        }

        // ===================== 基本带载参数设置 =====================

        public void SetCurrent(float current)
        {
            SendCommand($"CURR {current.ToString(CultureInfo.InvariantCulture)}");
        }
        
        public void SetCurrentSlewRate(float slewRate)
        {
            SendCommand($"CURR:SLEW {slewRate.ToString(CultureInfo.InvariantCulture)}");
        }

        public void SetCurrentProtection(float currentLimit)
        {
            SendCommand($"CURR:PROT {currentLimit.ToString("0000")}");
        }

        public void SetVoltage(float voltage)
        {
            SendCommand($"VOLT {voltage.ToString(CultureInfo.InvariantCulture)}");
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
            SendCommand($"POW:PROT {powerLimit.ToString("0000")}");
        }

        public void SetResistance(float resistance)
        {
            SendCommand($"RES {resistance.ToString(CultureInfo.InvariantCulture)}");
        }

        // ===================== 测量指令 =====================

        public float MeasureVoltage()
        {
            string resp = Query("MEAS:VOLT?");
            return float.TryParse(resp, NumberStyles.Any, CultureInfo.InvariantCulture, out float val) ? val : 0f;
        }

        public float MeasureCurrent()
        {
            string resp = Query("MEAS:CURR?");
            return float.TryParse(resp, NumberStyles.Any, CultureInfo.InvariantCulture, out float val) ? val : 0f;
        }

        public float MeasurePower()
        {
            string resp = Query("MEAS:POW?");
            return float.TryParse(resp, NumberStyles.Any, CultureInfo.InvariantCulture, out float val) ? val : 0f;
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
