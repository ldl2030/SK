using System;
using System.Collections.Generic;
using System.Linq;

namespace TestPlatform
{
    /// <summary>
    /// 高压测试数据解析器（处理 TD? 命令返回数据）
    /// </summary>
    public static class HighVoltageTestParser
    {
        // 标记位 -> 状态描述映射表
        private static readonly Dictionary<int, string> FlagStatusMap = new Dictionary<int, string>
        {
            { 255, "未测" },
            { 0,   "测试中" },
            { 1,   "测试合格" },
            { 2,   "超上限" },
            { 3,   "超下限" },
            { 4,   "电弧不合格" },
            { 6,   "硬件保护" },
            { 7,   "开路保护" },
            { 23,  "等待中" },
            { 30,  "测试步被中止" },
            { 33,  "接地电压超开路电压设定值" },
            { 39,  "缓升中" },
            { 40,  "缓降中" },
            { 41,  "过流保护" },
            { 42,  "欠压保护" },
            { 43,  "过载保护" },
            { 45,  "漏电保护" },
            { 46,  "未知错误" },
            { 47,  "通信超时" },
            { 98,  "未读到结论" },
            { 99,  "通讯异常" }
        };

        // 综合结论 -> 描述映射表
        private static readonly Dictionary<string, string> OverallResultMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "NOTTEST", "未测试" },
            { "TESTING", "测试中" },
            { "ABORT",   "测试中止" },
            { "OK",      "测试合格" },
            { "NG",      "测试不合格" }
        };

        // 测试模式缩写 -> 全称映射表
        private static readonly Dictionary<string, string> TestModeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ACW",    "交流耐电压测试" },
            { "DCW",    "直流耐电压测试" },
            { "IR",     "绝缘电阻测试" },
            { "DCGB",   "直流接地电阻测试仪" }
        };

        /// <summary>
        /// 获取标记位对应的状态描述
        /// </summary>
        public static string GetFlagDescription(int flag)
        {
            return FlagStatusMap.TryGetValue(flag, out string desc) ? desc : $"未知标记({flag})";
        }

        /// <summary>
        /// 获取综合结论对应的描述
        /// </summary>
        public static string GetOverallResultDescription(string overallResult)
        {
            if (string.IsNullOrEmpty(overallResult))
                return "未知结论";

            return OverallResultMap.TryGetValue(overallResult, out string desc) ? desc : overallResult;
        }

        /// <summary>
        /// 获取测试模式对应的全称描述
        /// </summary>
        public static string GetTestModeDescription(string testMode)
        {
            if (string.IsNullOrEmpty(testMode))
                return "未知模式";

            return TestModeMap.TryGetValue(testMode, out string desc) ? desc : testMode;
        }

        /// <summary>
        /// 解析 TD? 命令的响应数据
        /// </summary>
        /// <param name="response">原始响应字符串，如 "0,DCW,1.698kV,0.0uA,1.0s,OK,1;OK"</param>
        /// <param name="logAction">日志回调</param>
        /// <returns>解析结果对象，失败返回 null</returns>
        public static TdResponseData ParseTdResponse(string response, Action<string> logAction = null)
        {
            if (string.IsNullOrEmpty(response))
            {
                logAction?.Invoke("响应为空");
                return null;
            }

            // 去除可能的命令回显前缀，如 "TD? " 或 "TD?"
            string raw = response.Trim();
            if (raw.StartsWith("TD?", StringComparison.OrdinalIgnoreCase))
            {
                // 找到第一个空格或冒号后的内容
                int idx = raw.IndexOfAny(new char[] { ' ', ':' });
                if (idx > 0)
                    raw = raw.Substring(idx + 1).Trim();
                else
                    raw = raw.Substring(3).Trim(); // 去掉 "TD?"
            }

            // 去除末尾可能的分号或换行
            raw = raw.TrimEnd(';', '\r', '\n');

            // 按分号分割
            string[] parts = raw.Split(';');
            if (parts.Length < 2)
            {
                logAction?.Invoke($"响应格式错误，缺少分号分隔: {raw}");
                return null;
            }

            string dataPart = parts[0];
            string overallResult = parts[1].Trim();

            // 按逗号分割数据字段
            string[] fields = dataPart.Split(',');
            if (fields.Length < 7)
            {
                logAction?.Invoke($"数据字段不足，期望7个，实际{fields.Length}: {dataPart}");
                return null;
            }

            TdResponseData data = new TdResponseData();
            try
            {
                data.Index = int.Parse(fields[0]);
                data.TestMode = fields[1].Trim();
                data.Voltage = fields[2].Trim();
                data.Current = fields[3].Trim();
                data.Time = fields[4].Trim();
                data.Result = fields[5].Trim();
                data.Flag = int.Parse(fields[6]);
                data.OverallResult = overallResult;

                data.FlagDescription = GetFlagDescription(data.Flag);
                data.OverallResultDescription = GetOverallResultDescription(overallResult);
                data.TestModeDescription = GetTestModeDescription(data.TestMode);
                data.Pass = overallResult?.Equals("OK", StringComparison.OrdinalIgnoreCase) ?? false;

                logAction?.Invoke($"解析成功: {data}");
                return data;
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"解析异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 从 TdResponseData 中提取数值参数列表（保留原始字符串用于显示）
        /// </summary>
        /// <param name="data">解析后的数据</param>
        /// <returns>参数列表：显示名称，原始值字符串（含单位），数值（double），单位</returns>
        public static List<(string DisplayName, string RawValueWithUnit, double NumericValue, string Unit)> ExtractNumericParameters(TdResponseData data)
        {
            var list = new List<(string, string, double, string)>();

            // 电压参数（直接使用原始字符串）
            var (voltageVal, voltageUnit) = ParseValueWithUnit(data.Voltage);
            if (!double.IsNaN(voltageVal))
                list.Add(($"{data.TestModeDescription} 电压", data.Voltage, voltageVal, voltageUnit));

            // 电流参数
            var (currentVal, currentUnit) = ParseValueWithUnit(data.Current);
            if (!double.IsNaN(currentVal))
                list.Add(($"{data.TestModeDescription} 电流", data.Current, currentVal, currentUnit));

            // 时间参数
            var (timeVal, timeUnit) = ParseValueWithUnit(data.Time);
            if (!double.IsNaN(timeVal))
                list.Add(($"{data.TestModeDescription} 时间", data.Time, timeVal, timeUnit));

            // 综合结论（显示为 "1" 或 "0"，数值为 1.0 或 0.0）
            list.Add(("综合结论", data.Pass ? "1" : "0", data.Pass ? 1.0 : 0.0, data.OverallResultDescription));

            return list;
        }

        /// <summary>
        /// 格式化数值显示（保留3位小数，去除末尾多余的0）
        /// </summary>
        private static string FormatValue(double value)
        {
            // 保留3位小数，并去除末尾多余的0
            string formatted = value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            // 如果结果是整数，添加 ".0" 保持一致性
            if (!formatted.Contains('.'))
                formatted += ".0";
            return formatted;
        }


        // <summary>
        /// 解析带单位的数值，返回数值和单位
        /// </summary>
        private static (double value, string unit) ParseValueWithUnit(string valueWithUnit)
        {
            if (string.IsNullOrEmpty(valueWithUnit))
                return (double.NaN, "");

            // 匹配数字（包括小数点）和单位（字母或希腊字母µ）
            var match = System.Text.RegularExpressions.Regex.Match(valueWithUnit, @"([\d.]+)\s*([a-zA-Zµ]*)");
            if (match.Success)
            {
                if (double.TryParse(match.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double val))
                {
                    string unit = match.Groups[2].Value;
                    // 处理特殊单位 µA -> uA
                    if (unit == "µA") unit = "uA";
                    return (val, unit);
                }
            }
            return (double.NaN, "");
        }


    }

    /// <summary>
    /// TD? 响应解析结果
    /// </summary>
    public class TdResponseData
    {
        public int Index { get; set; }                      // 测试序号
        public string TestMode { get; set; }                // 测试模式缩写 (ACW/DCW/IR/DCGB)
        public string Voltage { get; set; }                 // 电压值 (如 1.698kV)
        public string Current { get; set; }                 // 电流值 (如 0.0uA)
        public string Time { get; set; }                    // 测试时间 (如 1.0s)
        public string Result { get; set; }                  // 单个测试结论 (OK/NG)
        public int Flag { get; set; }                       // 标记位 (0,1,2...)
        public string OverallResult { get; set; }           // 综合结论 (OK/NG/NOTTEST/TESTING/ABORT)
        public string FlagDescription { get; set; }         // 标记位对应文字描述
        public string OverallResultDescription { get; set; } // 综合结论对应描述 (如 "测试合格")
        public string TestModeDescription { get; set; }      // 测试模式全称 (如 "直流耐电压测试")
        public bool Pass { get; set; }                      // 是否通过（综合结论为 OK）

        public override string ToString()
        {
            return $"[{Index}] {TestMode}({TestModeDescription}) U={Voltage} I={Current} T={Time} " +
                   $"结论={OverallResult}({OverallResultDescription}) Flag={Flag}({FlagDescription})";
        }

        public string GetShortDisplay()
        {
            return $"{TestModeDescription} U={Voltage} I={Current} T={Time} {OverallResultDescription}";
        }
    }
}