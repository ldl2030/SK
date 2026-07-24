using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace TestPlatform
{
    /// <summary>
    /// 目标测试板通信类 (通过 USB-B / J2 接口)
    /// 内部代号: comm_usb
    /// 主要用于向被测主板 (Target DUT / MBD) 收发功能测试指令。
    /// </summary>
    public class TargetBoardComm : IDisposable
    {
        private SerialPort _serialPort;
        private readonly object _lock = new object();

        public event Action<string> LogInfo;
        public event Action<string> LogError;

        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;

        /// <summary>
        /// 连接到目标板的 USB-B 测试串口
        /// </summary>
        public void Connect(string portName, int baudRate = 115200)
        {
            if (string.IsNullOrWhiteSpace(portName))
                throw new ArgumentException("[TargetBoardComm] COM口号不能为空，请在设置中配置 USB-B 串口。", nameof(portName));
            Disconnect();
            try
            {
                _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
                _serialPort.ReadTimeout = 2000;
                _serialPort.WriteTimeout = 2000;
                _serialPort.NewLine = "\r\n";
                _serialPort.Open();
                LogInfo?.Invoke($"已成功连接目标测试板 (USB-B): {portName}");
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"连接目标测试板失败 ({portName}): {ex.Message}");
                throw;
            }
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

        /// <summary>
        /// 向目标板发送指令并等待特定回复
        /// </summary>
        public async Task<string> SendCommandAsync(string command, int timeoutMs = 2000)
        {
            if (!IsConnected)
            {
                LogError?.Invoke("测试板未连接 (USB-B)。");
                return null;
            }

            return await Task.Run(() =>
            {
                lock (_lock)
                {
                    try
                    {
                        _serialPort.DiscardInBuffer();
                        _serialPort.WriteLine(command);
                        LogInfo?.Invoke($"[USB-B发送] {command}");

                        // 简单的读取示例，根据实际通信协议（如 JSON, Hex, 或纯文本）调整
                        string response = _serialPort.ReadLine();
                        LogInfo?.Invoke($"[USB-B接收] {response}");
                        return response.Trim();
                    }
                    catch (TimeoutException)
                    {
                        LogError?.Invoke("[USB-B接收] 读取超时。");
                        return null;
                    }
                    catch (Exception ex)
                    {
                        LogError?.Invoke($"[USB-B接收] 错误: {ex.Message}");
                        return null;
                    }
                }
            });
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
