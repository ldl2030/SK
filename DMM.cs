using NationalInstruments.Visa;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TestPlatform
{
    /// <summary>
    /// 数字万用表控制类（异步版本，适用于WPF）
    /// </summary>
    public static class DMM
    {
        public const string CMD_IDN = "\r\n*IDN?\r\n";
        public const string CMD_SET_VOLTAGE_AC = "\r\n:FUNCtion:VOLTage:AC\r\n";
        public const string CMD_SET_VOLTAGE_DC = "\r\n:FUNCtion:VOLTage:DC\r\n";
        public const string CMD_SET_CURRENT_AC = "\r\n:FUNCtion:CURRent:AC\r\n";
        public const string CMD_SET_CURRENT_DC = "\r\n:FUNCtion:CURRent:DC\r\n";
        public const string CMD_SET_RESISTANCE = "\r\n:FUNCtion:RESistance\r\n";
        public const string CMD_GET_VOLTAGE_DC = "\r\n:MEASure:VOLTage:DC?\r\n";
        public const string CMD_GET_CURRENT_DC = "\r\n:MEASure:CURRent:DC?\r\n";
        public const string CMD_GET_CURRENT_AC = "\r\n:MEASure:CURRent:AC?\r\n";
        public const string CMD_GET_RESISTANCE = "\r\n:MEASure:RESistance?\r\n";

        // 设备标识符，用于从多个 USB 设备中识别万用表
        public static string DeviceIdentifier { get; set; } = "DM3L"; // 默认匹配万用表

        private static MessageBasedSession MbSession { get; set; }
        private static bool IsConnected { get; set; } = false;
        private static string LastResourceName { get; set; }
        private static Action<string> _logAction;
        private static readonly SemaphoreSlim _sessionLock = new SemaphoreSlim(1, 1);

        private static readonly Dictionary<string, string> _functionMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"RES", "RESistance"},
            {"2WR", "RESistance"},
            {"OHM", "RESistance"},
            {"DCV", "VOLTage"},
            {"VOLT:DC", "VOLTage:DC"},
            {"DCI", "CURRent:DC"},
            {"CURR:DC", "CURRent:DC"},
            {"ACV", "VOLTage:AC"},
            {"ACI", "CURRent:AC"},
            {"FREQ", "FREQuency"},
            {"PER", "PERiod"},
            {"CONT", "CONTinuity"},
            {"DIOD", "DIODe"}
        };

        /// <summary>
        /// 初始化并连接万用表（异步，带重试）
        /// </summary>
        public static async Task<bool> ConnectAsync(Action<string> logAction = null, CancellationToken cancellationToken = default)
        {
            _logAction = logAction;
            const int MAX_ATTEMPTS = 2;

            if (IsConnected && await VerifyConnectionAsync())
                return true;

            await _sessionLock.WaitAsync(cancellationToken);
            try
            {
                for (int attempt = 1; attempt <= MAX_ATTEMPTS; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        Log($"尝试连接万用表 (第 {attempt} 次)...");

                        var rmTask = Task.Run(() =>
                        {
                            var rmLocal = new ResourceManager();
                            var allResources = rmLocal.Find("(USB)?*").ToList();
                            Log($"找到 {allResources.Count} 个 USB 设备");
                            foreach (var res in allResources)
                                Log($"  资源: {res}");
                            // 优先选择包含 DeviceIdentifier 的资源
                            var resourceNameLocal = allResources.FirstOrDefault(r => r.Contains(DeviceIdentifier));
                            if (string.IsNullOrEmpty(resourceNameLocal))
                            {
                                Log($"未找到包含 '{DeviceIdentifier}' 的设备，将使用第一个");
                                resourceNameLocal = allResources.FirstOrDefault();
                            }
                            return (rm: rmLocal, resourceName: resourceNameLocal);
                        });
                        var (rm, resourceName) = await rmTask;

                        if (string.IsNullOrEmpty(resourceName))
                        {
                            Log($"未找到数字万用表，请检查USB连接");
                            continue;
                        }

                        if (resourceName == LastResourceName)
                            await Task.Delay(500, cancellationToken);

                        IsConnected = true;
                        LastResourceName = resourceName;

                        Log($"打开万用表连接: {resourceName}");
                        MbSession = await Task.Run(() => (MessageBasedSession)rm.Open(resourceName));
                        MbSession.TimeoutMilliseconds = 3000;

                        if (await VerifyConnectionAsync())
                        {
                            Log("万用表连接成功");
                            return true;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Log("连接操作被取消");
                        return false;
                    }
                    catch (Exception ex)
                    {
                        HandleException($"连接失败: {ex.Message}", ex);
                    }

                    await Task.Delay(500, cancellationToken);
                }

                Disconnect();
                Log("万用表连接失败，请检查设备");
                return false;
            }
            finally
            {
                _sessionLock.Release();
            }
        }

        /// <summary>
        /// 断开连接并释放资源（同步）
        /// </summary>
        public static void Disconnect()
        {
            lock (typeof(DMM))
            {
                try
                {
                    MbSession?.SafeDispose();
                    MbSession = null;
                    IsConnected = false;
                    Log("万用表连接已断开");
                }
                catch (Exception ex)
                {
                    HandleException($"断开连接错误: {ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// 启用万用表功能（异步）
        /// </summary>
        public static async Task<bool> EnableFunctionAsync(string command, Action<string> logAction = null, CancellationToken cancellationToken = default)
        {
            _logAction = logAction;
            if (!await ConnectAsync(logAction, cancellationToken)) return false;

            try
            {
                string commandName = ExtractFunctionName(command);
                Log($"设置功能: {commandName}");

                await SendCommandAsync(command);
                await Task.Delay(500, cancellationToken);

                string actualFunction = await QueryFunctionAsync();
                string normalizedActual = NormalizeFunction(actualFunction);
                string normalizedExpected = NormalizeFunction(commandName);

                if (normalizedActual == normalizedExpected)
                    return true;

                Log($"设置成功但验证不匹配! 预期: {normalizedExpected}, 实际: {normalizedActual}");
                return true;
            }
            catch (Exception ex)
            {
                Log($"功能设置错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 查询当前功能模式（异步）
        /// </summary>
        public static async Task<string> QueryFunctionAsync(Action<string> logAction = null, CancellationToken cancellationToken = default)
        {
            _logAction = logAction;
            try
            {
                await SendCommandAsync("\r\n:FUNCtion?\r\n");
                string response = await ReadDataAsync(500, cancellationToken);
                return response?.Replace("\"", "").Replace("\r", "").Replace("\n", "").Trim();
            }
            catch
            {
                return "ERROR";
            }
        }

        /// <summary>
        /// 写入命令并读取结果（异步）
        /// </summary>
        public static async Task<string> WriteAndReadAsync(string command, Action<string> logAction = null, CancellationToken cancellationToken = default)
        {
            _logAction = logAction;
            const int MAX_ATTEMPTS = 2;

            for (int attempt = 1; attempt <= MAX_ATTEMPTS; attempt++)
            {
                try
                {
                    if (!await ConnectAsync(logAction, cancellationToken))
                    {
                        Log("连接不可用，无法执行命令");
                        return null;
                    }

                    string logCommand = command.Replace("\r\n", "").Replace(":MEASure:", "").Trim();
                    Log($"执行命令: {logCommand} (发送次数： {attempt})");

                    await SendCommandAsync(command);
                    string response = await ReadDataAsync(500, cancellationToken);

                    if (!string.IsNullOrEmpty(response))
                    {
                        Log($"收到响应: {response.Trim()}");
                        return response;
                    }

                    Log("未收到响应，将重试...");
                }
                catch (OperationCanceledException)
                {
                    Log("操作被取消");
                    return null;
                }
                catch (Exception ex)
                {
                    HandleException($"读写错误: {ex.Message}", ex);
                    Disconnect();
                }

                await Task.Delay(300, cancellationToken);
            }

            Log($"命令执行失败: {command}");
            return null;
        }

        // ======= 私有异步辅助方法 =======

        private static async Task SendCommandAsync(string command)
        {
            if (!IsConnected || MbSession == null)
                throw new InvalidOperationException("未连接或连接不可用");

            await Task.Run(() => MbSession.RawIO.Write(command));
        }

        private static async Task<string> ReadDataAsync(int timeoutMs, CancellationToken cancellationToken)
        {
            if (!IsConnected || MbSession == null)
                return null;

            return await Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                Exception lastException = null;
                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException();

                    try
                    {
                        return MbSession.RawIO.ReadString();
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        Thread.Sleep(10);
                    }
                }
                throw new TimeoutException($"读取数据超时: {lastException?.Message ?? "无有效响应"}");
            }, cancellationToken);
        }

        private static async Task<bool> VerifyConnectionAsync()
        {
            try
            {
                if (MbSession == null) return false;
                await SendCommandAsync(CMD_IDN);
                await Task.Delay(500);
                string response = await ReadDataAsync(500, CancellationToken.None);
                return !string.IsNullOrEmpty(response);
            }
            catch
            {
                return false;
            }
        }

        // ======= 辅助函数 =======

        private static string ExtractFunctionName(string command)
        {
            string cleaned = command
                .Replace("\r\n", "")
                .Replace(":FUNCtion:", "")
                .Trim();
            int colonIndex = cleaned.IndexOf(':');
            return colonIndex > 0 ? cleaned.Substring(0, colonIndex) : cleaned;
        }

        private static string NormalizeFunction(string functionName)
        {
            if (string.IsNullOrWhiteSpace(functionName))
                return "UNKNOWN";
            if (_functionMap.TryGetValue(functionName, out string normalized))
                return normalized;
            foreach (var pair in _functionMap)
                if (functionName.IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return pair.Value;
            return functionName;
        }

        private static void HandleException(string message, Exception ex = null)
        {
            string logMessage = $"{message} {(ex != null ? $"\n详细信息: {ex.Message}" : "")}";
            Log(logMessage);
            Debug.WriteLine(logMessage);
        }

        private static void Log(string message)
        {
            _logAction?.Invoke(message);
        }
    }

    internal static class ResourceExtensions
    {
        public static void SafeDispose(this IDisposable resource)
        {
            if (resource != null)
            {
                try
                {
                    resource.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"释放资源时出错: {ex.Message}");
                }
            }
        }
    }
}