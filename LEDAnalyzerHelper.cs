using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TestPlatform
{
    public static class LEDAnalyzerHelper
    {
        /// <summary>
        /// 发送命令并获取返回字符串（自动换行符）
        /// </summary>
        private static async Task<string> SendCommandAsync(string portName, int baudRate, string command, int timeoutMs, Action<string> logAction)
        {
            if (string.IsNullOrEmpty(portName))
            {
                logAction?.Invoke("LED分析仪串口未配置");
                return null;
            }

            using (var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One))
            {
                try
                {
                    port.ReadTimeout = timeoutMs;
                    port.WriteTimeout = 1000;
                    port.Open();
                    port.DiscardInBuffer();
                    port.Write(command);
                    logAction?.Invoke($"发送命令: {command.Trim()}");

                    var result = await Task.Run(() => port.ReadLine()).ConfigureAwait(false);
                    logAction?.Invoke($"收到响应: {result}");
                    return result?.Trim();
                }
                catch (TimeoutException)
                {
                    logAction?.Invoke("LED分析仪响应超时");
                    return null;
                }
                catch (Exception ex)
                {
                    logAction?.Invoke($"LED分析仪串口错误: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// 获取所有通道的RGBI数据 (命令: Getallrgbi\r\n)
        /// 返回: 每个通道的 (R, G, B, Brightness)
        /// </summary>
        public static async Task<List<(int R, int G, int B, int Brightness)>> GetAllRGBIAsync(
            string portName, int baudRate, Action<string> logAction, CancellationToken ct)
        {
            string response = await SendCommandAsync(portName, baudRate, "Getallrgbi\r\n", 3000, logAction);
            if (string.IsNullOrEmpty(response))
                return null;

            // 解析: 空格分割，每4个一组: R G B Brightness
            var parts = response.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<(int, int, int, int)>();
            for (int i = 0; i < parts.Length && result.Count < 8; i += 4)
            {
                if (i + 3 >= parts.Length) break;
                if (int.TryParse(parts[i], out int r) &&
                    int.TryParse(parts[i + 1], out int g) &&
                    int.TryParse(parts[i + 2], out int b) &&
                    int.TryParse(parts[i + 3], out int brightness))
                {
                    result.Add((r, g, b, brightness));
                }
                else
                {
                    logAction?.Invoke($"解析RGBI失败: {string.Join(" ", parts.Skip(i).Take(4))}");
                }
            }
            return result;
        }

        /// <summary>
        /// 获取所有通道的频率、计数值、RGB、色调、亮度相对值 (命令: getallfreq4\r\n)
        /// 返回: 每个通道的 (频率,计数值,R,G,B,色调,亮度)
        /// </summary>
        public static async Task<List<(double Freq, int Count, int R, int G, int B, double Hue, int Brightness)>> GetAllFreq4Async(
            string portName, int baudRate, Action<string> logAction, CancellationToken ct)
        {
            string response = await SendCommandAsync(portName, baudRate, "getallfreq4\r\n", 3000, logAction);
            if (string.IsNullOrEmpty(response))
                return null;

            // 格式示例: "01.0 004 189 035 030 001.89 11062"
            var tokens = response.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<(double, int, int, int, int, double, int)>();
            for (int i = 0; i < tokens.Length && result.Count < 8; i += 7)
            {
                if (i + 6 >= tokens.Length) break;
                if (double.TryParse(tokens[i], out double freq) &&
                    int.TryParse(tokens[i + 1], out int count) &&
                    int.TryParse(tokens[i + 2], out int r) &&
                    int.TryParse(tokens[i + 3], out int g) &&
                    int.TryParse(tokens[i + 4], out int b) &&
                    double.TryParse(tokens[i + 5], out double hue) &&
                    int.TryParse(tokens[i + 6], out int brightness))
                {
                    result.Add((freq, count, r, g, b, hue, brightness));
                }
                else
                {
                    logAction?.Invoke($"解析Freq4失败: {string.Join(" ", tokens.Skip(i).Take(7))}");
                }
            }
            return result;
        }

        /// <summary>
        /// 验证所有通道数据是否在配置范围内，返回每个通道的验证结果
        /// </summary>
        public static (bool AllPass, List<LEDChannelResult> Results) ValidateAllChannels(
            LEDConfigSet config,
            List<(double Freq, int Count, int R, int G, int B, double Hue, int Brightness)> freqData,
            List<(int R, int G, int B, int Brightness)> rgbiData)
        {
            var results = new List<LEDChannelResult>();
            bool allPass = true;

            for (int i = 0; i < 8; i++)
            {
                var chConfig = config.Channels[i];
                var chResult = new LEDChannelResult { ChannelIndex = i + 1 };

                if (freqData != null && i < freqData.Count)
                {
                    var fd = freqData[i];
                    chResult.FreqValue = fd.Freq;
                    chResult.CountValue = fd.Count;
                    chResult.RValue = fd.R;
                    chResult.GValue = fd.G;
                    chResult.BValue = fd.B;
                    chResult.HueValue = fd.Hue;
                    chResult.BrightnessValue = fd.Brightness;

                    // 判定：如果上下限都为0（或未设置），则跳过该项检测视为通过
                    chResult.FreqPass = (chConfig.FreqLower == 0 && chConfig.FreqUpper == 0) || (fd.Freq >= chConfig.FreqLower && fd.Freq <= chConfig.FreqUpper);
                    chResult.CountPass = (chConfig.CountLower == 0 && chConfig.CountUpper == 0) || (fd.Count >= chConfig.CountLower && fd.Count <= chConfig.CountUpper);
                    chResult.RPass = (chConfig.RedLower == 0 && chConfig.RedUpper == 0) || (fd.R >= chConfig.RedLower && fd.R <= chConfig.RedUpper);
                    chResult.GPass = (chConfig.GreenLower == 0 && chConfig.GreenUpper == 0) || (fd.G >= chConfig.GreenLower && fd.G <= chConfig.GreenUpper);
                    chResult.BPass = (chConfig.BlueLower == 0 && chConfig.BlueUpper == 0) || (fd.B >= chConfig.BlueLower && fd.B <= chConfig.BlueUpper);
                    chResult.HuePass = (chConfig.HueLower == 0 && chConfig.HueUpper == 0) || (fd.Hue >= chConfig.HueLower && fd.Hue <= chConfig.HueUpper);
                    chResult.BrightnessPass = (chConfig.BrightnessLower == 0 && chConfig.BrightnessUpper == 0) || (fd.Brightness >= chConfig.BrightnessLower && fd.Brightness <= chConfig.BrightnessUpper);
                }
                else if (rgbiData != null && i < rgbiData.Count)
                {
                    var rd = rgbiData[i];
                    chResult.RValue = rd.R;
                    chResult.GValue = rd.G;
                    chResult.BValue = rd.B;
                    chResult.BrightnessValue = rd.Brightness;

                    chResult.RPass = (chConfig.RedLower == 0 && chConfig.RedUpper == 0) || (rd.R >= chConfig.RedLower && rd.R <= chConfig.RedUpper);
                    chResult.GPass = (chConfig.GreenLower == 0 && chConfig.GreenUpper == 0) || (rd.G >= chConfig.GreenLower && rd.G <= chConfig.GreenUpper);
                    chResult.BPass = (chConfig.BlueLower == 0 && chConfig.BlueUpper == 0) || (rd.B >= chConfig.BlueLower && rd.B <= chConfig.BlueUpper);
                    chResult.BrightnessPass = (chConfig.BrightnessLower == 0 && chConfig.BrightnessUpper == 0) || (rd.Brightness >= chConfig.BrightnessLower && rd.Brightness <= chConfig.BrightnessUpper);
                    // 频率和计数值无法从RGBI获取，默认通过
                    chResult.FreqPass = true;
                    chResult.CountPass = true;
                    chResult.HuePass = true;
                }
                else
                {
                    // 无数据，所有项失败
                    chResult.FreqPass = chResult.CountPass = chResult.RPass = chResult.GPass = chResult.BPass = chResult.HuePass = chResult.BrightnessPass = false;
                }

                bool chPass = chResult.FreqPass && chResult.CountPass && chResult.RPass && chResult.GPass && chResult.BPass && chResult.HuePass && chResult.BrightnessPass;
                chResult.AllPass = chPass;
                if (!chPass) allPass = false;
                results.Add(chResult);
            }
            return (allPass, results);
        }

        /// <summary>
        /// 便捷方法：一次调用读取两种数据并验证
        /// </summary>
        public static async Task<(bool AllPass, List<LEDChannelResult> Results)> ReadAndValidateLEDChannelsAsync(
            string portName, int baudRate, LEDConfigSet config, Action<string> logAction, CancellationToken ct)
        {
            var freqTask = GetAllFreq4Async(portName, baudRate, logAction, ct);
            var rgbiTask = GetAllRGBIAsync(portName, baudRate, logAction, ct);
            await Task.WhenAll(freqTask, rgbiTask);
            if (freqTask.Result == null && rgbiTask.Result == null)
            {
                logAction?.Invoke("无法获取LED数据，请检查串口和命令");
                return (false, null);
            }
            return ValidateAllChannels(config, freqTask.Result, rgbiTask.Result);
        }

        private static IEnumerable<string> Skip(this string[] array, int count)
        {
            for (int i = count; i < array.Length; i++)
                yield return array[i];
        }
    }
}