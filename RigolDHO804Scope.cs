using Ivi.Visa;
using Microsoft.Graph.Models.CallRecords;
using NationalInstruments.Visa;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TestPlatform
{
    /// <summary>
    /// Rigol DHO804 示波器控制类（异步版本，适用于 WPF 等不需要阻塞 UI 的场景）
    /// </summary>
    public class RigolDHO804Scope
    {
        // 持久会话（用于波形采集，避免频繁开关）
        private IMessageBasedSession _session;
        private readonly object _sessionLock = new object();
#pragma warning disable CS0414
        private bool _disposed = false;
#pragma warning restore CS0414
        // ==================== 异步基础操作 ====================

        private static async Task DelayAsync(int milliseconds) => await Task.Delay(milliseconds).ConfigureAwait(false);
        public static string DeviceIdentifier { get; set; } = "DHO8";

        public  Task SendCommandAsync(IMessageBasedSession session, string command)
        {
            if (!command.EndsWith("\n")) command += "\n";
            return Task.Run(() => session.RawIO.Write(command));
        }

        private static Task<string> ReadResponseAsync(IMessageBasedSession session)
        {
            return Task.Run(() =>
            {
                try { return session.RawIO.ReadString().Trim(); }
                catch { return string.Empty; }
            });
        }

        private static double ParseFrequencyValue(string response, Action<string> logAction = null)
        {
            if (string.IsNullOrWhiteSpace(response)) return 0.0;
            response = response.Trim();
            if (double.TryParse(response,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double result)) return result;
            logAction?.Invoke($"⚠️ 无法解析数值: {response}");
            return 0.0;
        }

        private static string FormatFrequency(double frequencyHz)
        {
            if (frequencyHz >= 1e9) return $"{(frequencyHz / 1e9):F3} GHz";
            if (frequencyHz >= 1e6) return $"{(frequencyHz / 1e6):F3} MHz";
            if (frequencyHz >= 1e3) return $"{(frequencyHz / 1e3):F3} kHz";
            return $"{frequencyHz:F3} Hz";
        }

        // ==================== 异步设备搜索与连接 ====================

        private static async Task<(ResourceManager manager, string resourceName)> FindDeviceAsync(Action<string> logAction = null)
        {
            return await Task.Run(() =>
            {
                var rm = new ResourceManager();
                var allResources = rm.Find("(USB)?*").ToList();
                logAction?.Invoke($"找到 {allResources.Count} 个 USB 设备");
                foreach (var res in allResources)
                    logAction?.Invoke($"  资源: {res}");
                // 优先选择包含 DeviceIdentifier 的资源
                var device = allResources.FirstOrDefault(d => d.Contains(DeviceIdentifier));
                if (string.IsNullOrEmpty(device))
                {
                    logAction?.Invoke($"未找到包含 '{DeviceIdentifier}' 的设备，将使用第一个");
                    device = allResources.FirstOrDefault();
                }
                return (rm, device);
            }).ConfigureAwait(false);
        }

        public  async Task<IMessageBasedSession> OpenSessionAsync(Action<string> logAction = null)
        {
            var (rm, resource) = await FindDeviceAsync(logAction).ConfigureAwait(false);
            if (string.IsNullOrEmpty(resource))
            {
                logAction?.Invoke("未找到DHO804示波器");
                return null;
            }
            logAction?.Invoke($"找到设备: {resource}");
            return await Task.Run(() =>
            {
                var session = (IMessageBasedSession)rm.Open(resource);
                session.TimeoutMilliseconds = 10000;
                return session;
            }).ConfigureAwait(false);
        }

        // ==================== 核心异步测量方法 ====================

        /// <summary>异步测量频率（返回格式化字符串）</summary>
        public async Task<string> MeasureFrequencyAsync(int channel = 1, Action<string> logAction = null)
        {
            return await ReadGenericMeasurementAsync(channel, "FREQuency", logAction).ConfigureAwait(false);
        }

        /// <summary>异步测量正占空比（返回格式化字符串，带 %）</summary>
        public async Task<string> MeasurePositiveDutyCycleAsync(int channel = 1, Action<string> logAction = null)
        {
            return await ReadGenericMeasurementAsync(channel, "PDUTycycle", logAction).ConfigureAwait(false);
        }

        /// <summary>异步测量负占空比</summary>
        public async Task<string> MeasureNegativeDutyCycleAsync(int channel = 1, Action<string> logAction = null)
        {
            return await ReadGenericMeasurementAsync(channel, "NDUTycycle", logAction).ConfigureAwait(false);
        }

        /// <summary>异步一次性读取频率、正占空比、负占空比</summary>
        public async Task<(string frequency, string positiveDuty, string negativeDuty)> MeasureChannelFullAsync(int channel = 1, Action<string> logAction = null)
        {
            IMessageBasedSession session = null;
            try
            {
                session = await OpenSessionAsync(logAction).ConfigureAwait(false);
                if (session == null) return (null, null, null);

                var freq = await ReadGenericMeasurementAsync(session, channel, "FREQuency", logAction).ConfigureAwait(false);
                var pos = await ReadGenericMeasurementAsync(session, channel, "PDUTycycle", logAction).ConfigureAwait(false);
                var neg = await ReadGenericMeasurementAsync(session, channel, "NDUTycycle", logAction).ConfigureAwait(false);
                return (freq, pos, neg);
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"测量失败: {ex.Message}");
                return (null, null, null);
            }
            finally
            {
                session?.Dispose();
            }
        }

        /// <summary>异步配置示波器（时基、垂直刻度等）</summary>
        public async Task<bool> ConfigureScopeAsync(
    double timebaseScale = 0.3,              // 时基数
    int channelCount = 4,                    // 通道数量
    double channelScale = 2.0,               // 通道垂直刻度
    bool enableDutyCycle = true,             // 是否开启占空比测量（新增参数）
    Action<string> logAction = null)
        {
            IMessageBasedSession session = null;
            try
            {
                session = await OpenSessionAsync(logAction).ConfigureAwait(false);
                if (session == null) return false;

                logAction?.Invoke("重置仪器...");
                await SendCommandAsync(session, "*RST");
                await DelayAsync(2000);

                logAction?.Invoke($"设置时基为{timebaseScale}s/div...");
                await SendCommandAsync(session, $":TIMebase:SCALe {timebaseScale}");
                await DelayAsync(500);

                for (int ch = 1; ch <= channelCount; ch++)
                {
                    // 打开通道并设置垂直刻度
                    await SendCommandAsync(session, $":CHANnel{ch}:DISPlay ON");
                    await DelayAsync(100);
                    await SendCommandAsync(session, $":CHANnel{ch}:SCALe {channelScale}");
                    await DelayAsync(200);

                    // 如果需要占空比测量，开启正占空比和负占空比（通过查询命令触发配置）
                    if (enableDutyCycle)
                    {
                        logAction?.Invoke($"为通道{ch}开启正占空比测量...");
                        await SendCommandAsync(session, $":MEASure:PDUTy? CHANnel{ch}");
                        await DelayAsync(50);

                        logAction?.Invoke($"为通道{ch}开启负占空比测量...");
                        await SendCommandAsync(session, $":MEASure:NDUTy? CHANnel{ch}");
                        await DelayAsync(50);
                    }
                }
                logAction?.Invoke("配置完成");
                return true;
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"配置失败: {ex.Message}");
                return false;
            }
            finally { session?.Dispose(); }
        }
        /// <summary>
        /// 配置示波器并开启常用测量项（时基、垂直刻度、交流耦合、测量项）
        /// 使用传统命令确保兼容性
        /// </summary>
        /// <param name="timebaseScale">时基数</param>
        /// <param name="channelCount">通道数量</param>
        /// <param name="channelScale">通道垂直刻度</param>
        /// <param name="enableDutyCycle">是否开启正/负占空比测量</param>
        /// <param name="enableOtherMeasurements">是否开启最大值、最小值、周期、频率测量</param>
        /// <param name="logAction">日志委托</param>
        /// <returns>配置是否成功</returns>
        public async Task<bool> ConfigureAndEnableMeasurementsAsync_EI(
            double timebaseScale = 0.3,
            int channelCount = 4,
            double channelScale = 2.0,
            bool enableDutyCycle = true,
            bool enableOtherMeasurements = true,
            Action<string> logAction = null)
        {
            IMessageBasedSession session = null;
            try
            {
                session = await OpenSessionAsync(logAction).ConfigureAwait(false);
                if (session == null) return false;

                logAction?.Invoke("重置仪器...");
                await SendCommandAsync(session, "*RST");
                await DelayAsync(2000);

                logAction?.Invoke($"设置时基为 {timebaseScale} s/div...");
                await SendCommandAsync(session, $":TIMebase:SCALe {timebaseScale}");
                await DelayAsync(500);

                for (int ch = 1; ch <= channelCount; ch++)
                {
                    // 1. 打开通道
                    logAction?.Invoke($"打开通道 {ch}...");
                    await SendCommandAsync(session, $":CHANnel{ch}:DISPlay ON");
                    await DelayAsync(100);

                    // 2. 设置垂直刻度
                    logAction?.Invoke($"设置通道 {ch} 垂直刻度为 {channelScale} V/div");
                    await SendCommandAsync(session, $":CHANnel{ch}:SCALe {channelScale}");
                    await DelayAsync(200);

                    // 3. 设置耦合为 AC（交流耦合）
                    await SendCommandAsync(session, $":CHANnel{ch}:COUPling AC");
                    logAction?.Invoke($"通道 {ch} 耦合设置为 AC");
                    await DelayAsync(100);

                    // 4. 开启占空比测量（使用传统命令）
                    if (enableDutyCycle)
                    {
                        logAction?.Invoke($"为通道 {ch} 开启正/负占空比测量...");
                        await SendCommandAsync(session, $":MEASure:PDUTy? CHANnel{ch}");
                        await DelayAsync(50);
                        await SendCommandAsync(session, $":MEASure:NDUTy? CHANnel{ch}");
                        await DelayAsync(50);
                    }

                    // 5. 开启最大值、最小值、周期、频率测量（使用传统命令）
                    if (enableOtherMeasurements)
                    {
                        logAction?.Invoke($"为通道 {ch} 开启最大值、最小值、周期、频率测量...");
                        // 最大值
                        await SendCommandAsync(session, $":MEASure:VMAX? CHANnel{ch}");
                        await DelayAsync(50);
                        // 最小值
                        await SendCommandAsync(session, $":MEASure:VMIN? CHANnel{ch}");
                        await DelayAsync(50);
                        // 周期
                        await SendCommandAsync(session, $":MEASure:PERiod? CHANnel{ch}");
                        await DelayAsync(50);
                        // 频率
                        await SendCommandAsync(session, $":MEASure:FREQuency? CHANnel{ch}");
                        await DelayAsync(50);
                    }
                }

                logAction?.Invoke("配置完成");
                return true;
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"配置失败: {ex.Message}");
                return false;
            }
            finally { session?.Dispose(); }
        }

        /// <summary>
        /// 读取最大值和最小值，并根据波形幅度自动调整垂直刻度（使波形占屏幕约70%~80%）
        /// </summary>
        /// <param name="channel">通道号(1~4)</param>
        /// <param name="logAction">日志委托</param>
        /// <returns>元组 (Max, Min)，单位 V</returns>
        public async Task<(double Max, double Min)> ReadVoltageExtremesWithAutoScaleAsync(int channel, Action<string> logAction = null)
        {
            // 1. 先读取当前极值
            var (max, min) = await ReadVoltageExtremesAsync(channel, logAction);
            if (double.IsNaN(max) || double.IsNaN(min))
            {
                logAction?.Invoke("无法读取极值，自动调整失败");
                return (double.NaN, double.NaN);
            }

            // 2. 计算合适的垂直刻度
            double span = Math.Abs(max - min);
            if (span < 1e-6) span = 1.0; // 防止零

            // 假设示波器垂直有8格，目标是让波形占屏幕的70% ~ 80%
            double targetScale = span / 8.0 * 0.8; // 波形占80%
            double scale = RoundToStandardScale(targetScale);

            // 3. 设置垂直刻度（打开会话）
            IMessageBasedSession session = null;
            try
            {
                session = await OpenSessionAsync(logAction);
                if (session == null) return (double.NaN, double.NaN);

                logAction?.Invoke($"自动设置通道 {channel} 垂直刻度为 {scale} V/div");
                await SendCommandAsync(session, $":CHANnel{channel}:SCALe {scale}");
                await DelayAsync(200);

                // 重新读取一次极值（刻度变化不影响测量值，但确保稳定）
                string maxResponse = await QueryAsync(session, $":MEASure:VMAX? CHANnel{channel}", logAction);
                string minResponse = await QueryAsync(session, $":MEASure:VMIN? CHANnel{channel}", logAction);

                double newMax = ParseDouble(maxResponse, logAction);
                double newMin = ParseDouble(minResponse, logAction);
                return (newMax, newMin);
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"自动调整刻度失败: {ex.Message}");
                return (double.NaN, double.NaN);
            }
            finally { session?.Dispose(); }
        }

        /// <summary>
        /// 读取指定通道的最大值和最小值（当前测量值）
        /// </summary>
        /// <param name="channel">通道号(1~4)</param>
        /// <param name="logAction">日志委托</param>
        /// <returns>元组 (Max, Min)，单位 V，失败返回 NaN</returns>
        public async Task<(double Max, double Min)> ReadVoltageExtremesAsync(int channel, Action<string> logAction = null)
        {
            IMessageBasedSession session = null;
            try
            {
                session = await OpenSessionAsync(logAction);
                if (session == null) return (double.NaN, double.NaN);

                string maxResponse = await QueryAsync(session, $":MEASure:VMAX? CHANnel{channel}", logAction);
                string minResponse = await QueryAsync(session, $":MEASure:VMIN? CHANnel{channel}", logAction);

                double max = ParseDouble(maxResponse, logAction);
                double min = ParseDouble(minResponse, logAction);
                return (max, min);
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"读取电压极值失败: {ex.Message}");
                return (double.NaN, double.NaN);
            }
            finally { session?.Dispose(); }
        }

        

        // 辅助：解析双精度值
        private double ParseDouble(string response, Action<string> logAction = null)
        {
            if (string.IsNullOrEmpty(response)) return double.NaN;
            if (double.TryParse(response, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out double value))
                return value;
            logAction?.Invoke($"无法解析数值: {response}");
            return double.NaN;
        }

        // 辅助：圆整到标准刻度值（1-2-5序列）
        private double RoundToStandardScale(double value)
        {
            double[] standards = { 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5,
                           1, 2, 5, 10, 20, 50, 100, 200, 500, 1000 };
            double best = standards[0];
            foreach (var s in standards)
            {
                if (s >= value) { best = s; break; }
                best = s;
            }
            return best;
        }
        /// <summary>异步测量四个通道的频率</summary>
        public async Task<(string freq1, string freq2, string freq3, string freq4)> MeasureAllChannelsAsync(Action<string> logAction = null)
        {
            IMessageBasedSession session = null;
            try
            {
                session = await OpenSessionAsync(logAction).ConfigureAwait(false);
                if (session == null) return (null, null, null, null);

                var f1 = await ReadGenericMeasurementAsync(session, 1, "FREQuency", logAction).ConfigureAwait(false);
                var f2 = await ReadGenericMeasurementAsync(session, 2, "FREQuency", logAction).ConfigureAwait(false);
                var f3 = await ReadGenericMeasurementAsync(session, 3, "FREQuency", logAction).ConfigureAwait(false);
                var f4 = await ReadGenericMeasurementAsync(session, 4, "FREQuency", logAction).ConfigureAwait(false);
                return (f1, f2, f3, f4);
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"测量失败: {ex.Message}");
                return (null, null, null, null);
            }
            finally { session?.Dispose(); }
        }

        // ==================== 内部通用异步测量引擎 ====================

        private async Task<string> ReadGenericMeasurementAsync(int channel, string measurementType, Action<string> logAction)
        {
            IMessageBasedSession session = null;
            try
            {
                session = await OpenSessionAsync(logAction).ConfigureAwait(false);
                return await ReadGenericMeasurementAsync(session, channel, measurementType, logAction).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"测量 {measurementType} CH{channel} 出错: {ex.Message}");
                return "错误";
            }
            finally { session?.Dispose(); }
        }

        private async Task<string> ReadGenericMeasurementAsync(IMessageBasedSession session, int channel, string measurementType, Action<string> logAction)
        {
            string cmd = $":MEASure:ITEM? {measurementType},CHANnel{channel}";
            await SendCommandAsync(session, cmd).ConfigureAwait(false);
            await DelayAsync(300);
            string response = await ReadResponseAsync(session).ConfigureAwait(false);
            logAction?.Invoke($"CH{channel} {measurementType} 原始响应: {response}");

            if (response.Contains("9.9E+37")) return "无信号";

            if (measurementType.Equals("FREQuency", StringComparison.OrdinalIgnoreCase))
            {
                double freq = ParseFrequencyValue(response, logAction);
                return FormatFrequency(freq);
            }
            else if (measurementType.Equals("PDUTycycle", StringComparison.OrdinalIgnoreCase) ||
                     measurementType.Equals("NDUTycycle", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(response,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double duty))
                {
                    return $"{duty:F2} %";
                }
            }
            return response;
        }

        /// <summary>获取指定通道的原始频率值（Hz），失败返回 double.NaN</summary>
        public async Task<double> GetFrequencyRawAsync(int channel = 1, Action<string> logAction = null)
        {
            IMessageBasedSession session = null;
            try
            {
                session = await OpenSessionAsync(logAction);
                if (session == null) return double.NaN;
                string response = await QueryAsync(session, $":MEASure:FREQuency? CHANnel{channel}", logAction);
                if (response.Contains("9.9E+37")) return double.NaN;
                return ParseDoubleValue(response, logAction);
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"获取频率原始值失败: {ex.Message}");
                return double.NaN;
            }
            finally { session?.Dispose(); }
        }

        /// <summary>获取指定通道的正占空比原始值（%），失败返回 double.NaN</summary>
        public async Task<double> GetPositiveDutyRawAsync(int channel = 1, Action<string> logAction = null)
        {
            IMessageBasedSession session = null;
            try
            {
                session = await OpenSessionAsync(logAction);
                if (session == null) return double.NaN;
                string response = await QueryAsync(session, $":MEASure:PDUTy? CHANnel{channel}", logAction);
                if (response.Contains("9.9E+37")) return double.NaN;
                double raw = ParseDoubleValue(response, logAction);
                return raw * 100;
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"获取正占空比原始值失败: {ex.Message}");
                return double.NaN;
            }
            finally { session?.Dispose(); }
        }

        /// <summary>获取指定通道的负占空比原始值（%），失败返回 double.NaN</summary>
        public async Task<double> GetNegativeDutyRawAsync(int channel = 1, Action<string> logAction = null)
        {
            IMessageBasedSession session = null;
            try
            {
                session = await OpenSessionAsync(logAction);
                if (session == null) return double.NaN;
                string response = await QueryAsync(session, $":MEASure:NDUTy? CHANnel{channel}", logAction);
                if (response.Contains("9.9E+37")) return double.NaN;
                double raw= ParseDoubleValue(response, logAction);
                return raw * 100;
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"获取负占空比原始值失败: {ex.Message}");
                return double.NaN;
            }
            finally { session?.Dispose(); }
        }

        private static double ParseDoubleValue(string response, Action<string> logAction = null)
        {
            if (string.IsNullOrWhiteSpace(response)) return double.NaN;
            response = response.Trim();
            if (double.TryParse(response, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double result))
                return result;
            logAction?.Invoke($"无法解析数值: {response}");
            return double.NaN;
        }

        public async Task<string> QueryAsync(IMessageBasedSession session, string command, Action<string> logAction)
        {
            await SendCommandAsync(session, command, logAction);
            await Task.Delay(100);
            var response = await Task.Run(() => session.RawIO.ReadString().Trim());
            logAction?.Invoke($"[接收响应] {response}");
            // 增加错误检测
            if (response.Contains("not supported") || response.Contains("error"))
            {
                logAction?.Invoke($"⚠️ 指令可能不被支持或出错: {response}");
            }
            return response;
        }


       

        /// <summary>
        /// 关闭持久连接
        /// </summary>
        public void DisconnectPersistent()
        {
            lock (_sessionLock)
            {
                if (_session != null)
                {
                    try { _session.Dispose(); } catch { }
                    _session = null;
                }
            }
        }
        // 发送命令（带日志，接受会话参数）
        private async Task SendCommandAsync(IMessageBasedSession session, string command, Action<string> logAction)
        {
            if (session == null) throw new InvalidOperationException("会话无效");
            if (!command.EndsWith("\n")) command += "\n";
            logAction?.Invoke($"[发送命令] {command.Trim()}");
            await Task.Run(() => session.RawIO.Write(command));
        }

       
    }
}