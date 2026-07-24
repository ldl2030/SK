using System;
using System.IO;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Shapes;
using System.Linq;

namespace TestPlatform
{
    public static class SerialPortHelper
    {
        /// <summary>
        /// 识别RS485串口（异步）
        /// </summary>
        /// <param name="command">发送的命令（字节数组）</param>
        /// <param name="baudRate">波特率</param>
        /// <param name="expectedResponse">预期的响应（16进制字符串，可带空格或"-"）</param>
        /// <param name="logAction">日志回调</param>
        /// <param name="timeoutMs">超时时间（毫秒）</param>
        /// <param name="pollIntervalMs">轮询间隔（毫秒）</param>
        /// <returns>识别到的串口名称，未找到返回空字符串</returns>
        public static async Task<string> GetComNameAsync(
            byte[] command,
            int baudRate,
            string expectedResponse,
            Action<string> logAction = null,
            int timeoutMs = 200,
            int pollIntervalMs = 15)
        {
            // 参数验证
            if (command == null || command.Length == 0)
            {
                logAction?.Invoke("错误：命令数据为空");
                return string.Empty;
            }

            if (string.IsNullOrEmpty(expectedResponse))
            {
                logAction?.Invoke("错误：预期响应为空");
                return string.Empty;
            }

            // 规范化预期响应
            string normalizedExpectedResponse = expectedResponse
                .Replace(" ", "")
                .Replace("-", "")
                .ToUpperInvariant();

            int expectedResponseLength = normalizedExpectedResponse.Length;
            int expectedBytes = expectedResponseLength / 2;

            if (expectedResponseLength % 2 != 0)
            {
                logAction?.Invoke($"错误：预期响应长度{expectedResponseLength}不是偶数");
                return string.Empty;
            }

            logAction?.Invoke($"开始识别RS485串口，波特率：{baudRate}，预期响应：{normalizedExpectedResponse}");

            // 获取可用串口列表
            string[] portNames;
            try
            {
                portNames = SerialPort.GetPortNames();
                if (portNames.Length == 0)
                {
                    logAction?.Invoke("未找到可用串口");
                    return string.Empty;
                }
                logAction?.Invoke($"找到 {portNames.Length} 个串口：{string.Join(", ", portNames)}");
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"获取串口列表失败：{ex.Message}");
                return string.Empty;
            }

            // 遍历所有串口进行检测
            foreach (string portName in portNames)
            {
                logAction?.Invoke($"正在检测串口：{portName}");

                try
                {
                    using (var serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One))
                    {
                        serialPort.ReadTimeout = timeoutMs;
                        serialPort.WriteTimeout = 1000;
                        serialPort.Open();

                        // 清空缓冲区
                        serialPort.DiscardInBuffer();
                        serialPort.DiscardOutBuffer();

                        // 发送命令
                        serialPort.Write(command, 0, command.Length);

                        // 使用CancellationTokenSource实现超时控制
                        using (var cts = new CancellationTokenSource())
                        {
                            cts.CancelAfter(timeoutMs);

                            try
                            {
                                // 等待数据到达
                                while (serialPort.BytesToRead < expectedBytes && !cts.Token.IsCancellationRequested)
                                {
                                    await Task.Delay(pollIntervalMs, cts.Token);
                                }

                                if (cts.Token.IsCancellationRequested)
                                {
                                    // 超时，检查是否有部分数据
                                    if (serialPort.BytesToRead > 0)
                                    {
                                        byte[] partialData = new byte[serialPort.BytesToRead];
                                        int partialBytesRead = serialPort.Read(partialData, 0, partialData.Length);
                                        string hexResponses = HexToNorma(partialData);
                                        logAction?.Invoke($"串口 {portName} 部分响应（超时）：{hexResponses}");
                                    }
                                    else
                                    {
                                        logAction?.Invoke($"串口 {portName} 无响应（超时）");
                                    }
                                    continue;
                                }

                                // 读取完整数据
                                byte[] buffer = new byte[expectedBytes];
                                int bytesRead = serialPort.Read(buffer, 0, expectedBytes);

                                // 如果读取的字节数少于预期，调整数组大小
                                if (bytesRead < expectedBytes)
                                {
                                    Array.Resize(ref buffer, bytesRead);
                                }

                                // 转换为16进制字符串
                                string hexResponse = HexToNorma(buffer);

                                // 比较响应
                                if (hexResponse.Equals(normalizedExpectedResponse, StringComparison.OrdinalIgnoreCase))
                                {
                                    logAction?.Invoke($"成功识别到串口：{portName}");
                                    return portName;
                                }
                                else
                                {
                                    logAction?.Invoke($"串口 {portName} 响应不匹配：{hexResponse} != {normalizedExpectedResponse}");
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                logAction?.Invoke($"串口 {portName} 操作超时");
                            }
                            catch (Exception ex)
                            {
                                logAction?.Invoke($"串口 {portName} 读取异常：{ex.Message}");
                            }
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    logAction?.Invoke($"串口 {portName} 被占用或无访问权限");
                }
                catch (TimeoutException)
                {
                    logAction?.Invoke($"串口 {portName} 操作超时");
                }
                catch (IOException ex)
                {
                    logAction?.Invoke($"串口 {portName} IO错误：{ex.Message}");
                }
                catch (Exception ex)
                {
                    logAction?.Invoke($"串口 {portName} 异常：{ex.GetType().Name}: {ex.Message}");
                }
            }

            logAction?.Invoke("未识别到RS485串口");
            return string.Empty;
        }

        /// <summary>
        /// 将字节数组转换为不带空格的16进制字符串
        /// </summary>
        private static string HexToNorma(byte[] hex)
        {
            if (hex == null || hex.Length == 0)
                return string.Empty;

            return BitConverter.ToString(hex).Replace("-", "");
        }

        /// <summary>
        /// 通过发送 E1?\r\n 命令并识别电压电流数据行，自动定位电流采集模块所在的串口。
        /// 已针对持续返回数据的设备优化，使用 ReadLine 逐行匹配。
        /// 支持三种格式：旧电压电流行、纯电压行（如 0.00000V）、T0N 型号行。
        /// </summary>
        /// <param name="baudRate">波特率</param>
        /// <param name="logAction">可选日志回调（线程安全，可更新 UI）</param>
        /// <param name="timeoutMs">单个串口的总超时时间（毫秒），建议 2000~3000</param>
        /// <param name="dataPattern">
        /// 用于匹配一行的正则表达式，若不提供则使用默认格式（兼容 C:+0.07150 A1:00.00000V 及纯电压行）
        /// </param>
        /// <returns>匹配到的串口名称，未找到返回 string.Empty</returns>
        public static async Task<string> GetComNameByE1QueryAsync(
            int baudRate,
            Action<string> logAction = null,
            int timeoutMs = 2000,
            string dataPattern = null)
        {
            // ---------- UI 线程安全封装 ----------
            SynchronizationContext syncContext = SynchronizationContext.Current;
            void SafeLog(string msg)
            {
                if (logAction == null) return;
                if (syncContext != null)
                    syncContext.Post(_ => logAction(msg), null);
                else
                    logAction(msg);
            }

            // 电压电流正则（兼容有无 u、有无空格），也兼容纯电压行（如 0.00000V）
            if (string.IsNullOrWhiteSpace(dataPattern))
                dataPattern = @"(C:[+-]\d+\.\d+u?\s*A1:[+-]?\d+\.\d+V)|(\d+\.\d{5}V)";
            var dataLineRegex = new Regex(dataPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

            // 命令
            byte[] command = Encoding.ASCII.GetBytes("E1?\r\n");

            SafeLog($"开始识别电流采集串口（E1?查询），波特率：{baudRate}，超时：{timeoutMs}ms");

            // ---------- 1. 获取可用串口 ----------
            string[] portNames;
            try
            {
                portNames = SerialPort.GetPortNames();
                if (portNames.Length == 0)
                {
                    SafeLog("未找到可用串口");
                    return string.Empty;
                }
                SafeLog($"找到 {portNames.Length} 个串口：{string.Join(", ", portNames)}");
            }
            catch (Exception ex)
            {
                SafeLog($"获取串口列表失败：{ex.Message}");
                return string.Empty;
            }

            // ---------- 2. 遍历所有串口 ----------
            foreach (string portName in portNames)
            {
                SafeLog($"正在检测串口：{portName}");
                using (var cts = new CancellationTokenSource(timeoutMs))
                {
                    try
                    {
                        using (var serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One))
                        {
                            serialPort.ReadTimeout = 500;        // 单行超时
                            serialPort.WriteTimeout = 1000;
                            serialPort.Open();

                            serialPort.DiscardInBuffer();
                            serialPort.DiscardOutBuffer();

                            await Task.Run(() => serialPort.Write(command, 0, command.Length), cts.Token);
                            SafeLog("已发送命令：E1?");

                            // ---------- 3. 逐行读取并匹配 ----------
                            while (!cts.Token.IsCancellationRequested)
                            {
                                string line;
                                try
                                {
                                    line = serialPort.ReadLine();
                                }
                                catch (TimeoutException)
                                {
                                    if (!cts.Token.IsCancellationRequested)
                                        continue;
                                    break;
                                }

                                line = line.Trim();
                                if (string.IsNullOrEmpty(line))
                                    continue;

                                // 问号 → 命令无效
                                if (line == "?")
                                {
                                    SafeLog($"串口 {portName} 返回 '?'，可能波特率或命令格式不匹配，跳过。");
                                    break;
                                }

                                // ----- 匹配电压电流数据行或纯电压行 -----
                                if (dataLineRegex.IsMatch(line))
                                {
                                    SafeLog($"成功识别到目标串口：{portName}，数据行：{line}");
                                    return portName;
                                }

                                // ----- 匹配 T0N 型号标识（例如 T0N0000000）-----
                                if (line.StartsWith("T0N") && line.Length >= 10 && line.Substring(3).All(char.IsDigit))
                                {
                                    SafeLog($"成功识别到目标串口：{portName}，T0N 型号行：{line}");
                                    return portName;
                                }
                            }

                            if (cts.Token.IsCancellationRequested)
                                SafeLog($"串口 {portName} 读取超时（{timeoutMs}ms），未发现匹配数据行");
                            else
                                SafeLog($"串口 {portName} 未返回有效数据");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        SafeLog($"串口 {portName} 检测被取消或超时");
                    }
                    catch (UnauthorizedAccessException)
                    {
                        SafeLog($"串口 {portName} 被占用或无访问权限");
                    }
                    catch (IOException ex)
                    {
                        SafeLog($"串口 {portName} IO错误：{ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        SafeLog($"串口 {portName} 未知异常：{ex.Message}");
                    }
                }
            }

            SafeLog("未识别到响应 E1? 数据格式的串口");
            return string.Empty;
        }
        /// <summary>
        /// 获取特定的RS232串口（发送"setgain2\r\n"，预期响应"OK\n"）
        /// </summary>
        /// <param name="logAction">日志回调（可选）</param>
        /// <returns>匹配的串口名称，未找到则返回空字符串</returns>
        public static async Task<string> GetSerialPort232Async(Action<string> logAction = null)
        {
            const string ExpectedResponse = "OK\n";
            const string command = "setcapture4\r\n";

            foreach (string portName in SerialPort.GetPortNames())
            {
                using (SerialPort serialPort = new SerialPort(portName))
                {
                    try
                    {
                        serialPort.BaudRate = 57600;
                        serialPort.Parity = Parity.None;
                        serialPort.DataBits = 8;
                        serialPort.StopBits = StopBits.One;
                        serialPort.Open();

                        serialPort.DiscardInBuffer();
                        serialPort.DiscardOutBuffer();

                        await Task.Delay(200);
                        serialPort.Write(command);
                        logAction?.Invoke($"Serial port: {portName}, Requested data: {command.Replace("\r\n", "")}");

                        await Task.Delay(300);
                        string data = serialPort.ReadExisting();
                        logAction?.Invoke($"Serial port: {portName}, Reception data: {data.Replace("\r\n", "")}");

                        if (data.Equals(ExpectedResponse, StringComparison.OrdinalIgnoreCase))
                        {
                            return portName;
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // 端口被占用，继续下一个
                    }
                    catch (IOException)
                    {
                        // 端口不存在或硬件错误
                    }
                    catch (Exception)
                    {
                        // 其他异常，继续
                    }
                }
            }
            return string.Empty;
        }
        /// <summary>
        /// 获取RS232字符串类型串口
        /// </summary>
        /// <param name="command"></param>
        /// <param name="expectedResponse"></param>
        /// <param name="baudRate"></param>
        /// <param name="cancellationToken"></param>
        /// <param name="logAction"></param>
        /// <returns></returns>
        public static async Task<string> GetUartCom(
        string command,
        string[] expectedResponses,
        int baudRate,
        Action<string> logAction = null,
        bool appendNewLine = true)
        {
            if (expectedResponses == null || expectedResponses.Length == 0)
                throw new ArgumentException("至少提供一个预期响应", nameof(expectedResponses));

            const int WriteTimeout = 500;
            const int ReadTimeout = 200;            // 单次读取超时
            const int TotalResponseTimeoutMs = 1000; // 总响应超时（可调）
            const int ReadPollIntervalMs = 50;       // 轮询间隔

            // 处理命令换行符（避免重复追加）
            string cmdToSend = appendNewLine ? command + "\r\n" : command;

            foreach (string portName in SerialPort.GetPortNames())
            {
                
                logAction?.Invoke($"正在检测串口: {portName}");

                try
                {
                    using (var serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One))
                    {
                        serialPort.ReadTimeout = ReadTimeout;
                        serialPort.WriteTimeout = WriteTimeout;
                        serialPort.Open();

                        // 清空缓冲区
                        serialPort.DiscardInBuffer();
                        serialPort.DiscardOutBuffer();

                        // 发送命令
                        serialPort.Write(cmdToSend);
                        logAction?.Invoke($"已发送: {cmdToSend.Replace("\r", "\\r").Replace("\n", "\\n")}");

                        // 循环读取，直到超时或取消
                        string fullResponse = string.Empty;
                        int elapsedMs = 0;
                        while (elapsedMs < TotalResponseTimeoutMs )
                        {
                            await Task.Delay(ReadPollIntervalMs);
                            elapsedMs += ReadPollIntervalMs;

                            string chunk = serialPort.ReadExisting();
                            if (!string.IsNullOrEmpty(chunk))
                            {
                                fullResponse += chunk;
                                logAction?.Invoke($"读取到片段: {chunk.Replace("\r", "\\r").Replace("\n", "\\n")}");
                            }

                            // 若已获取到足够数据，可以提前判断（但为稳健，继续读完直至超时）
                        }

                        

                        // 清理换行符
                        string cleanedResponse = fullResponse?.Replace("\r\n", "").Replace("\n", "").Replace("\r", "") ?? "";

                        // 检查是否匹配任意一个预期响应
                        bool matched = expectedResponses.Any(expected =>
                            cleanedResponse.Equals(expected, StringComparison.OrdinalIgnoreCase));

                        if (matched)
                        {
                            logAction?.Invoke($"找到匹配串口: {portName} (响应: {cleanedResponse})");
                            return portName;
                        }
                        else
                        {
                            logAction?.Invoke($"响应不匹配: 期望 {string.Join(" 或 ", expectedResponses)}，实际 {cleanedResponse}");
                        }
                    }
                }
                catch (TimeoutException) { /* 忽略超时 */ }
                catch (UnauthorizedAccessException) { /* 忽略端口占用 */ }
                catch (IOException) { /* 忽略 IO 错误 */ }
                catch (OperationCanceledException)
                {
                    logAction?.Invoke("串口识别已取消");
                    throw;
                }
            }

            logAction?.Invoke("未找到匹配的串口");
            return string.Empty;
        }
        /// <summary>
        /// 发送 ASCII 命令到串口，并读取一行响应（默认添加 \r\n）
        /// </summary>
        /// <param name="portName">串口号</param>
        /// <param name="baudRate">波特率</param>
        /// <param name="command">命令字符串（不加换行符）</param>
        /// <param name="logAction">日志委托</param>
        /// <param name="timeoutMs">超时毫秒</param>
        /// <param name="appendNewline">是否自动追加 \r\n</param>
        /// <returns>响应字符串（去除末尾换行），失败返回 null</returns>
        public static async Task<string> SendCommandAndReadResponseAsync(
            string portName,
            int baudRate,
            string command,
            Action<string> logAction = null,
            int timeoutMs = 2000,
            bool appendNewline = true)
        {
            if (string.IsNullOrEmpty(portName))
            {
                logAction?.Invoke("串口未配置");
                return null;
            }

            using (var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One))
            {
                try
                {
                    port.ReadTimeout = timeoutMs;
                    port.WriteTimeout = 1000;
                    port.NewLine = "\r\n"; // 设置换行符
                    port.Open();
                    port.DiscardInBuffer();

                    string cmd = command + (appendNewline ? "\r\n" : "");
                    port.Write(cmd);
                    logAction?.Invoke($"发送命令: {command}");

                    string response = await Task.Run(() => port.ReadLine()).ConfigureAwait(false);
                    response = response.Trim();
                    logAction?.Invoke($"收到响应: {response}");
                    return response;
                }
                catch (TimeoutException)
                {
                    logAction?.Invoke($"命令超时: {command}");
                    return null;
                }
                catch (Exception ex)
                {
                    logAction?.Invoke($"串口错误: {ex.Message}");
                    return null;
                }
            }
        }
    }
}