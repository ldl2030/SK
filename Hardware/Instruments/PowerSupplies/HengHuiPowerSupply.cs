using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;

namespace TestPlatform
{
    /// <summary>
    /// 恒惠(HengHui) / HSP 直流电源 MODBUS-RTU 控制驱动
    /// 波特率: 9600, 数据位: 8, 停止位: 1, 校验: None
    /// 支持同RS485总线(同COM口)下多台设备共享连接
    /// </summary>
    public class HengHuiPowerSupply : IDisposable
    {
        private static readonly Dictionary<string, SerialPort> _sharedPorts = new Dictionary<string, SerialPort>();
        private static readonly Dictionary<string, int> _portRefCounts = new Dictionary<string, int>();
        private static readonly object _busLock = new object();

        private SerialPort _serialPort;
        private string _portName;
        private byte _address;
        
        public float Vmax { get; set; } = 300.0f; // 电压满量程，需根据机型设置
        public float Imax { get; set; } = 10.0f; // 电流满量程，需根据机型设置

        /// <summary>
        /// 实例化恒惠电源
        /// </summary>
        /// <param name="vmax">机器标称最大电压 (如 300V)</param>
        /// <param name="imax">机器标称最大电流 (如 10A)</param>
        public HengHuiPowerSupply(float vmax = 300.0f, float imax = 10.0f)
        {
            Vmax = vmax;
            Imax = imax;
        }

        public void Connect(string portName, int baudRate = 9600, byte address = 88)
        {
            if (string.IsNullOrWhiteSpace(portName))
                throw new ArgumentException("[HengHuiPowerSupply] COM口号不能为空，请在设置中配置正确的串口。", nameof(portName));
            Disconnect(); // 先断开旧连接
            
            _address = address;
            _portName = portName;

            lock (_busLock)
            {
                if (!_sharedPorts.ContainsKey(portName) || !_sharedPorts[portName].IsOpen)
                {
                    var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
                    port.ReadTimeout = 1000;
                    port.WriteTimeout = 1000;
                    port.Open();
                    _sharedPorts[portName] = port;
                    _portRefCounts[portName] = 0;
                }
                
                _serialPort = _sharedPorts[portName];
                _portRefCounts[portName]++;
            }
        }

        public void Disconnect()
        {
            lock (_busLock)
            {
                if (_serialPort != null && _portName != null && _portRefCounts.ContainsKey(_portName))
                {
                    _portRefCounts[_portName]--;
                    if (_portRefCounts[_portName] <= 0)
                    {
                        if (_serialPort.IsOpen)
                            _serialPort.Close();
                        _serialPort.Dispose();
                        _sharedPorts.Remove(_portName);
                        _portRefCounts.Remove(_portName);
                    }
                }
                _serialPort = null;
                _portName = null;
            }
        }

        public bool IsConnected
        {
            get
            {
                lock (_busLock)
                {
                    return _serialPort != null && _serialPort.IsOpen;
                }
            }
        }

        private byte[] CalculateCrc(byte[] data)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                    {
                        crc >>= 1;
                        crc ^= 0xA001;
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }
            return new byte[] { (byte)(crc & 0xFF), (byte)(crc >> 8) };
        }

        private byte[] SendCommand(byte[] cmdBytes, int expectedRespLen)
        {
            lock (_busLock)
            {
                if (!IsConnected)
                    throw new InvalidOperationException($"HengHui电源 (地址:{_address}) 未连接。");

                byte[] crc = CalculateCrc(cmdBytes);
                byte[] fullCmd = cmdBytes.Concat(crc).ToArray();

                _serialPort.DiscardInBuffer();
                _serialPort.Write(fullCmd, 0, fullCmd.Length);

                byte[] resp = new byte[expectedRespLen];
                int bytesRead = 0;
                while (bytesRead < expectedRespLen)
                {
                    int read = _serialPort.Read(resp, bytesRead, expectedRespLen - bytesRead);
                    if (read == 0) break;
                    bytesRead += read;
                }

                if (bytesRead < expectedRespLen)
                    throw new TimeoutException($"Modbus 响应超时(地址:{_address})。期望 {expectedRespLen} 字节，接收 {bytesRead} 字节。");

                byte[] receivedData = resp.Take(resp.Length - 2).ToArray();
                byte[] receivedCrc = resp.Skip(resp.Length - 2).ToArray();
                byte[] expectedCrc = CalculateCrc(receivedData);

                if (receivedCrc[0] != expectedCrc[0] || receivedCrc[1] != expectedCrc[1])
                    throw new Exception("Modbus CRC 校验失败。");

                return resp;
            }
        }

        /// <summary>
        /// 控制电源输出开关
        /// </summary>
        public void SetPower(bool turnOn)
        {
            // 寄存器 0x33, 写1开，写0关
            ushort val = (ushort)(turnOn ? 0x0001 : 0x0000);
            byte[] cmd = new byte[] { _address, 0x06, 0x00, 0x33, (byte)(val >> 8), (byte)(val & 0xFF) };
            SendCommand(cmd, 8);
        }

        /// <summary>
        /// 同时设定输出电压和电流 (功能码0x10，连续写2个寄存器)
        /// </summary>
        public void SetVoltageCurrent(float voltage, float current)
        {
            ushort vData = (ushort)(voltage * 65535.0 / Vmax);
            ushort iData = (ushort)(current * 65535.0 / Imax);
            
            // 0x10 功能码，寄存器 0x03 开始，2个寄存器，4字节
            byte[] cmd = new byte[] { 
                _address, 0x10, 0x00, 0x03, 0x00, 0x02, 0x04,
                (byte)(vData >> 8), (byte)(vData & 0xFF),
                (byte)(iData >> 8), (byte)(iData & 0xFF)
            };
            SendCommand(cmd, 8); // 返回也是8字节
        }
        
        /// <summary>
        /// 仅设定电压
        /// </summary>
        public void SetVoltage(float voltage)
        {
            ushort vData = (ushort)(voltage * 65535.0 / Vmax);
            byte[] cmd = new byte[] { _address, 0x06, 0x00, 0x03, (byte)(vData >> 8), (byte)(vData & 0xFF) };
            SendCommand(cmd, 8);
        }

        /// <summary>
        /// 仅设定电流
        /// </summary>
        public void SetCurrent(float current)
        {
            ushort iData = (ushort)(current * 65535.0 / Imax);
            byte[] cmd = new byte[] { _address, 0x06, 0x00, 0x04, (byte)(iData >> 8), (byte)(iData & 0xFF) };
            SendCommand(cmd, 8);
        }

        /// <summary>
        /// 读取实际输出的电压和电流
        /// </summary>
        public void ReadVoltageCurrent(out float voltage, out float current)
        {
            // 读寄存器 0x00(电压) 和 0x01(电流), 共2个
            byte[] cmd = new byte[] { _address, 0x03, 0x00, 0x00, 0x00, 0x02 };
            byte[] resp = SendCommand(cmd, 9);
            
            ushort vData = (ushort)((resp[3] << 8) | resp[4]);
            ushort iData = (ushort)((resp[5] << 8) | resp[6]);
            
            voltage = (float)(vData * Vmax / 65535.0);
            current = (float)(iData * Imax / 65535.0);
        }

        /// <summary>
        /// 设定过压保护 (OVP)
        /// </summary>
        public void SetOvp(float ovp)
        {
            ushort data = (ushort)(ovp * 65535.0 / Vmax);
            byte[] cmd = new byte[] { _address, 0x06, 0x00, 0x06, (byte)(data >> 8), (byte)(data & 0xFF) };
            SendCommand(cmd, 8);
        }

        /// <summary>
        /// 设定过流保护 (OCP)
        /// </summary>
        public void SetOcp(float ocp)
        {
            ushort data = (ushort)(ocp * 65535.0 / Imax);
            byte[] cmd = new byte[] { _address, 0x06, 0x00, 0x07, (byte)(data >> 8), (byte)(data & 0xFF) };
            SendCommand(cmd, 8);
        }

        /// <summary>
        /// 读取实时状态 A (包含保护状态和开关状态)
        /// Bit8=输出开关, Bit6=保护状态, Bit5=过温, Bit4=过流, Bit3=过压, Bit2=CC恒流, Bit1=CV恒压
        /// </summary>
        public ushort ReadStatus()
        {
            byte[] cmd = new byte[] { _address, 0x03, 0x00, 0x02, 0x00, 0x01 };
            byte[] resp = SendCommand(cmd, 7); // 1+1+1+2+2 = 7
            return (ushort)((resp[3] << 8) | resp[4]);
        }

        /// <summary>
        /// 设定工作模式 (0: 恒压恒流, 1: 恒功率)
        /// </summary>
        public void SetWorkingMode(int mode)
        {
            ushort data = (ushort)mode;
            byte[] cmd = new byte[] { _address, 0x06, 0x00, 0x32, (byte)(data >> 8), (byte)(data & 0xFF) };
            SendCommand(cmd, 8);
        }

        /// <summary>
        /// 设置功率值
        /// 注意: 最大功率>100时, data=功率值*10。否则 data=功率值*100。
        /// </summary>
        public void SetPowerLimit(float powerWatt, bool isMaxPowerGreaterThan100 = true)
        {
            ushort data = (ushort)(isMaxPowerGreaterThan100 ? powerWatt * 10 : powerWatt * 100);
            byte[] cmd = new byte[] { _address, 0x06, 0x00, 0x34, (byte)(data >> 8), (byte)(data & 0xFF) };
            SendCommand(cmd, 8);
        }

        /// <summary>
        /// 读取功率值
        /// </summary>
        public float ReadPower(bool isMaxPowerGreaterThan100 = true)
        {
            byte[] cmd = new byte[] { _address, 0x03, 0x00, 0x35, 0x00, 0x01 };
            byte[] resp = SendCommand(cmd, 7);
            ushort data = (ushort)((resp[3] << 8) | resp[4]);
            return isMaxPowerGreaterThan100 ? data / 10.0f : data / 100.0f;
        }

        /// <summary>
        /// 读取统计信息 (最大电压, 最大电流, 最小电压, 最小电流, 平均电压, 平均电流)
        /// </summary>
        public void ReadStatistics(out float maxV, out float maxI, out float minV, out float minI, out float avgV, out float avgI)
        {
            // 连读 6 个寄存器: 0x22 ~ 0x27
            byte[] cmd = new byte[] { _address, 0x03, 0x00, 0x22, 0x00, 0x06 };
            byte[] resp = SendCommand(cmd, 17); // 1+1+1+12+2 = 17 bytes
            
            maxV = (float)(((resp[3] << 8) | resp[4]) * Vmax / 65535.0);
            maxI = (float)(((resp[5] << 8) | resp[6]) * Imax / 65535.0);
            minV = (float)(((resp[7] << 8) | resp[8]) * Vmax / 65535.0);
            minI = (float)(((resp[9] << 8) | resp[10]) * Imax / 65535.0);
            avgV = (float)(((resp[11] << 8) | resp[12]) * Vmax / 65535.0);
            avgI = (float)(((resp[13] << 8) | resp[14]) * Imax / 65535.0);
        }

        /// <summary>
        /// 清除极限值 (最大/最小值)
        /// </summary>
        public void ClearLimits()
        {
            byte[] cmd = new byte[] { _address, 0x06, 0x00, 0x21, 0x00, 0x00 };
            SendCommand(cmd, 8);
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
