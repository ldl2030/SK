using System;
using System.IO.Ports;
using System.Linq;

namespace TestPlatform
{
    /// <summary>
    /// 安耐斯 (ANS) 4位5位电源输出 MODBUS-RTU 控制驱动
    /// 波特率: 9600, 数据位: 8, 停止位: 1, 校验: None
    /// </summary>
    public class AnsPowerSupply : IDisposable
    {
        private SerialPort _serialPort;
        private byte _address = 0x01;
        private readonly object _lock = new object();

        public void Connect(string portName, int baudRate = 9600, byte address = 1)
        {
            if (string.IsNullOrWhiteSpace(portName))
                throw new ArgumentException("[AnsPowerSupply] COM口号不能为空，请在设置中配置正确的串口。", nameof(portName));
            Disconnect();
            _address = address;
            _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
            _serialPort.ReadTimeout = 1000;
            _serialPort.WriteTimeout = 1000;
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

        private byte[] FloatToModbusBytes(float val)
        {
            byte[] b = BitConverter.GetBytes(val);
            // C# 默认是小端 (DCBA)，协议要求 CDAB 顺序 (Word Swap)
            // b[0]=D, b[1]=C, b[2]=B, b[3]=A
            return new byte[] { b[1], b[0], b[3], b[2] };
        }

        private float ModbusBytesToFloat(byte[] data, int startIndex)
        {
            // data 收到的顺序是 CDAB
            // C# BitConverter 需要 DCBA
            byte[] b = new byte[] 
            { 
                data[startIndex + 1], 
                data[startIndex + 0], 
                data[startIndex + 3], 
                data[startIndex + 2] 
            };
            return BitConverter.ToSingle(b, 0);
        }

        private byte[] SendCommand(byte[] cmdBytes, int expectedRespLen)
        {
            lock (_lock)
            {
                if (!IsConnected)
                    throw new InvalidOperationException("ANS电源未连接 (ANS Power Supply not connected).");

                byte[] crc = CalculateCrc(cmdBytes);
                byte[] fullCmd = cmdBytes.Concat(crc).ToArray();

                _serialPort.DiscardInBuffer();
                _serialPort.Write(fullCmd, 0, fullCmd.Length);

                byte[] resp = new byte[expectedRespLen];
                int bytesRead = 0;
                
                // 由于可能分包，采用循环读取直到满足长度
                while (bytesRead < expectedRespLen)
                {
                    int read = _serialPort.Read(resp, bytesRead, expectedRespLen - bytesRead);
                    if (read == 0) break;
                    bytesRead += read;
                }

                if (bytesRead < expectedRespLen)
                    throw new TimeoutException($"Modbus 响应超时。期望 {expectedRespLen} 字节，实际接收 {bytesRead} 字节。");

                byte[] receivedData = resp.Take(resp.Length - 2).ToArray();
                byte[] receivedCrc = resp.Skip(resp.Length - 2).ToArray();
                byte[] expectedCrc = CalculateCrc(receivedData);

                if (receivedCrc[0] != expectedCrc[0] || receivedCrc[1] != expectedCrc[1])
                    throw new Exception("Modbus CRC 校验失败。");

                return resp;
            }
        }

        /// <summary>
        /// 设置电源的过压保护值 (OVP)。
        /// </summary>
        public void SetOvp(float ovp)
        {
            byte[] ovpBytes = FloatToModbusBytes(ovp);
            byte[] cmd = new byte[] { _address, 0x10, 0x01, 0x02, 0x00, 0x02, 0x04 };
            cmd = cmd.Concat(ovpBytes).ToArray();
            SendCommand(cmd, 8);
        }

        /// <summary>
        /// 设置电源的过流保护值 (OCP)。
        /// </summary>
        public void SetOcp(float ocp)
        {
            byte[] ocpBytes = FloatToModbusBytes(ocp);
            byte[] cmd = new byte[] { _address, 0x10, 0x01, 0x04, 0x00, 0x02, 0x04 };
            cmd = cmd.Concat(ocpBytes).ToArray();
            SendCommand(cmd, 8);
        }

        /// <summary>
        /// 设定电压和电流。
        /// </summary>
        public void SetVoltageCurrent(float voltage, float current)
        {
            byte[] vBytes = FloatToModbusBytes(voltage);
            byte[] iBytes = FloatToModbusBytes(current);
            byte[] data = vBytes.Concat(iBytes).ToArray();
            
            byte[] cmd = new byte[] { _address, 0x10, 0x00, 0x0A, 0x00, 0x04, 0x08 };
            cmd = cmd.Concat(data).ToArray();
            SendCommand(cmd, 8);
        }

        /// <summary>
        /// 设置电源开关 (ON/OFF)。
        /// </summary>
        public void SetPower(bool turnOn)
        {
            ushort actionVal = (ushort)(turnOn ? 0x0003 : 0x0002);
            byte[] cmd = new byte[] { 
                _address, 0x10, 0x00, 0x09, 0x00, 0x01, 0x02,
                (byte)(actionVal >> 8), (byte)(actionVal & 0xFF)
            };
            SendCommand(cmd, 8);
        }

        /// <summary>
        /// 读取实际输出的电压和电流。
        /// </summary>
        public void ReadVoltageCurrent(out float voltage, out float current)
        {
            byte[] cmd = new byte[] { _address, 0x03, 0x00, 0x04, 0x00, 0x04 };
            byte[] resp = SendCommand(cmd, 13);
            
            voltage = ModbusBytesToFloat(resp, 3);
            current = ModbusBytesToFloat(resp, 7);
        }

        /// <summary>
        /// 读取电源状态 (地址 0000H 和 0001H)
        /// status1 包含：Bit0(启动状态), Bit1(远程), Bit4(允许输出), Bit5(0恒流/1恒压), Bit8-11(欠压/过压/过流/过温)
        /// </summary>
        public void ReadStatus(out ushort status1, out ushort status2)
        {
            byte[] cmd = new byte[] { _address, 0x03, 0x00, 0x00, 0x00, 0x02 };
            byte[] resp = SendCommand(cmd, 9); // 1(Addr)+1(Func)+1(Len)+4(Data)+2(CRC) = 9
            
            status1 = (ushort)((resp[3] << 8) | resp[4]);
            status2 = (ushort)((resp[5] << 8) | resp[6]);
        }

        /// <summary>
        /// 读取电源温度 (地址 0002H)
        /// </summary>
        public float ReadTemperature()
        {
            byte[] cmd = new byte[] { _address, 0x03, 0x00, 0x02, 0x00, 0x02 };
            byte[] resp = SendCommand(cmd, 9); // 1+1+1+4+2 = 9
            
            return ModbusBytesToFloat(resp, 3);
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
