using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TestPlatform
{
    /// <summary>
    /// MBD/目标板 USB-B 串口通信：9600 8N1、无流控、命令以 CR/0x0D 结束。
    /// </summary>
    public class TargetBoardComm : IDisposable, IDeviceLogSource
    {
        private const int DefaultTimeoutMs = 2000;
        private const int ResponseWithoutPromptQuietMs = 300;
        private SerialPort _serialPort;
        private readonly object _lock = new object();

        public event Action<string> LogInfo;
        public event Action<string> LogError;

        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;

        public void Connect(string portName, int baudRate = 9600, int openSettleMs = 500)
        {
            if (string.IsNullOrWhiteSpace(portName))
                throw new ArgumentException("MBD UART 端口不能为空。", nameof(portName));

            Disconnect();
            try
            {
                _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = DefaultTimeoutMs,
                    WriteTimeout = DefaultTimeoutMs,
                    NewLine = "\r",
                    Handshake = Handshake.None,
                    DtrEnable = false,
                    RtsEnable = false,
                    Encoding = Encoding.ASCII
                };
                _serialPort.Open();

                if (openSettleMs > 0)
                    Thread.Sleep(openSettleMs);

                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
                LogInfo?.Invoke(
                    $"MBD UART 已连接：{portName}，{baudRate} 8N1，无流控，命令结尾=CR");
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"MBD UART 连接失败：端口={portName}，错误={ex.Message}");
                Disconnect();
                throw;
            }
        }

        public void Disconnect()
        {
            if (_serialPort != null && _serialPort.IsOpen)
                _serialPort.Close();
            _serialPort?.Dispose();
            _serialPort = null;
        }

        public async Task<string> SendCommandAsync(string command, int timeoutMs = DefaultTimeoutMs)
        {
            if (!IsConnected)
            {
                LogError?.Invoke("MBD UART 未连接，无法发送命令。");
                return null;
            }

            return await Task.Run(() =>
            {
                lock (_lock)
                {
                    string payload = (command ?? string.Empty).TrimEnd('\r', '\n');
                    string portName = _serialPort.PortName;
                    try
                    {
                        int effectiveTimeoutMs = timeoutMs > 0 ? timeoutMs : DefaultTimeoutMs;
                        _serialPort.DiscardInBuffer();
                        _serialPort.DiscardOutBuffer();
                        _serialPort.Write(payload);
                        _serialPort.Write(new[] { (byte)'\r' }, 0, 1);

                        var timer = Stopwatch.StartNew();
                        LogInfo?.Invoke($"MBD TX [{portName}] {FormatCommandForLog(payload)}");
                        string response = ReadResponse(effectiveTimeoutMs);
                        timer.Stop();

                        if (string.IsNullOrWhiteSpace(response))
                        {
                            LogError?.Invoke(
                                $"MBD TIMEOUT [{portName}] 命令={FormatCommandForLog(payload)}，" +
                                $"等待={effectiveTimeoutMs} ms");
                            return null;
                        }

                        LogInfo?.Invoke(
                            $"MBD RX [{portName}] {FormatCommandForLog(payload)} " +
                            $"({timer.ElapsedMilliseconds} ms): {FormatResponseForLog(response, payload)}");
                        return response.Trim('\r', '\n', ' ', '\t', '\0');
                    }
                    catch (Exception ex)
                    {
                        LogError?.Invoke(
                            $"MBD ERROR [{portName}] 命令={FormatCommandForLog(payload)}，错误={ex.Message}");
                        return null;
                    }
                }
            });
        }

        private string ReadResponse(int timeoutMs)
        {
            var response = new StringBuilder();
            var totalTimer = Stopwatch.StartNew();
            long lastDataAtMs = -1;

            while (totalTimer.ElapsedMilliseconds < timeoutMs)
            {
                string chunk = _serialPort.ReadExisting();
                if (!string.IsNullOrEmpty(chunk))
                {
                    response.Append(chunk);
                    lastDataAtMs = totalTimer.ElapsedMilliseconds;
                }

                if (lastDataAtMs >= 0)
                {
                    string currentResponse = response.ToString();
                    if (HasTerminalPrompt(currentResponse))
                        break;

                    if (totalTimer.ElapsedMilliseconds - lastDataAtMs >=
                        ResponseWithoutPromptQuietMs)
                        break;
                }

                Thread.Sleep(10);
            }

            return response.ToString();
        }

        public static bool HasTerminalPrompt(string response)
        {
            if (string.IsNullOrEmpty(response))
                return false;

            return response.IndexOf(
                       "chars answered. Ready",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   response.IndexOf(
                       "Ready.",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   response.EndsWith(
                       "Ready",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatCommandForLog(string command)
        {
            return string.IsNullOrEmpty(command) ? "<空命令>" : command;
        }

        private static string FormatResponseForLog(string response, string command)
        {
            string[] lines = (response ?? string.Empty)
                .Split(new[] { '\r', '\n', '\0' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .Where(line => !string.Equals(line, command, StringComparison.OrdinalIgnoreCase))
                .Select(line => Regex.IsMatch(
                    line,
                    @"^\d+\s+chars answered\.\s*Ready\.?$",
                    RegexOptions.IgnoreCase) ? "Ready." : line)
                .ToArray();
            return lines.Length == 0 ? "<空响应>" : string.Join(" | ", lines);
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
