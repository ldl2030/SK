using System;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace TestPlatform
{
    public static class MESHelper
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// 异步发送 HTTP POST 请求
        /// </summary>
        public static async Task<string> PostDataAsync(string url, string data, Encoding encoding, Action<string> logAction = null)
        {
            try
            {
                logAction?.Invoke($"开始请求: {url}");
                var content = new StringContent(data, encoding, "application/x-www-form-urlencoded");
                var response = await _httpClient.PostAsync(url, content);
                response.EnsureSuccessStatusCode();
                string result = await response.Content.ReadAsStringAsync();
                logAction?.Invoke($"请求成功，响应长度: {result.Length}");
                logAction($"原始响应：{result}");
                return result;
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"MES请求失败: {ex.Message}");
                MessageBox.Show($"MES数据上传错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return string.Empty;
            }
        }

        /// <summary>
        /// 解析 MES 响应，返回是否成功（msgId == 0 即为成功）
        /// </summary>
        /// <param name="response">JSON 响应字符串</param>
        /// <param name="message">输出：msgStr 或错误描述</param>
        /// <returns>true 表示成功（msgId=0），false 表示失败（msgId≠0 或解析失败）</returns>
        public static bool ParseMESResponse(string response, out string message)
        {
            message = string.Empty;

            if (string.IsNullOrEmpty(response))
            {
                message = "MES返回空响应";
                return false;
            }

            try
            {
                // 尝试解析JSON格式
                if (response.StartsWith("{") && response.EndsWith("}"))
                {
                    // 检查msgId字段
                    int msgIdIndex = response.IndexOf("\"msgId\":");
                    if (msgIdIndex >= 0)
                    {
                        int msgIdStart = msgIdIndex + 8;
                        int msgIdEnd = response.IndexOfAny(new[] { ',', '}' }, msgIdStart);
                        if (msgIdEnd > msgIdStart)
                        {
                            string msgIdStr = response.Substring(msgIdStart, msgIdEnd - msgIdStart).Trim();
                            if (int.TryParse(msgIdStr, out int msgId))
                            {
                                // 提取msgStr
                                string msgStr = "";
                                int msgStrIndex = response.IndexOf("\"msgStr\":\"");
                                if (msgStrIndex >= 0)
                                {
                                    int msgStrStart = msgStrIndex + 10;
                                    int msgStrEnd = response.IndexOf("\"", msgStrStart);
                                    if (msgStrEnd > msgStrStart)
                                    {
                                        msgStr = response.Substring(msgStrStart, msgStrEnd - msgStrStart);
                                    }
                                }

                                // 特殊处理：对于过站检查，msgId为0且msgStr为空是正常情况
                                if (msgId == 0 && string.IsNullOrEmpty(msgStr))
                                {
                                    message = "检查通过";
                                    return true;
                                }

                                // 特殊处理：msgId为1且msgStr包含"已经测试PASS"的情况
                                if (msgId == 1 && !string.IsNullOrEmpty(msgStr) &&
                                    (msgStr.Contains("已经测试PASS") || msgStr.Contains("已经测试")))
                                {
                                    message = msgStr;
                                    return false; // 已经测试过，不允许再次测试
                                }

                                message = msgStr;
                                return msgId == 0; // msgId为0表示成功
                            }
                        }
                    }

                    // 备用解析：直接查找msgId值
                    if (response.Contains("\"msgId\":0"))
                    {
                        // 检查是否有错误消息
                        int msgStrIndex = response.IndexOf("\"msgStr\":\"");
                        if (msgStrIndex >= 0)
                        {
                            int msgStrStart = msgStrIndex + 10;
                            int msgStrEnd = response.IndexOf("\"", msgStrStart);
                            if (msgStrEnd > msgStrStart)
                            {
                                string msgStr = response.Substring(msgStrStart, msgStrEnd - msgStrStart);
                                // 如果msgId为0但有错误消息，需要根据消息内容判断
                                if (!string.IsNullOrEmpty(msgStr) &&
                                    (msgStr.Contains("已经测试PASS") || msgStr.Contains("已经测试")))
                                {
                                    message = msgStr;
                                    return false;
                                }
                                message = msgStr;
                            }
                            else
                            {
                                message = "检查通过";
                            }
                        }
                        else
                        {
                            message = "检查通过";
                        }
                        return true;
                    }
                    else if (response.Contains("\"msgId\":1"))
                    {
                        // 尝试提取错误信息
                        int msgStrIndex = response.IndexOf("\"msgStr\":\"");
                        if (msgStrIndex >= 0)
                        {
                            int msgStrStart = msgStrIndex + 10;
                            int msgStrEnd = response.IndexOf("\"", msgStrStart);
                            if (msgStrEnd > msgStrStart)
                            {
                                message = response.Substring(msgStrStart, msgStrEnd - msgStrStart);
                            }
                            else
                            {
                                message = "操作失败";
                            }
                        }
                        else
                        {
                            message = "操作失败";
                        }
                        return false;
                    }
                }

                // 如果无法解析JSON，使用原有的解析方法（兼容非JSON格式）
                if (response.Length < 10)
                {
                    message = "MES返回响应格式错误";
                    return false;
                }

                string check0 = response.Substring(9, 1);
                string[] parts = response.Split(':', '"', ',', ' ');
                message = parts.Length > 8 ? parts[8] : string.Empty;

                // 特殊处理：对于空消息但状态为0的情况
                if (check0 == "0" && string.IsNullOrEmpty(message))
                {
                    message = "操作成功";
                }

                // 特殊处理：状态为1且消息包含"已经测试PASS"的情况
                if (check0 == "1" && !string.IsNullOrEmpty(message) &&
                    (message.Contains("已经测试PASS") || message.Contains("已经测试")))
                {
                    // 保持原样，返回false
                }

                return check0 == "0";
            }
            catch (Exception ex)
            {
                message = $"解析响应时发生异常: {ex.Message}";
                return false;
            }
        }
        /// <summary>
        /// 根据通道索引构建该通道的测试数据字符串（用于 MES 上传）
        /// 格式：所有测试值（逗号分隔）,所有测试结果（逗号分隔）
        /// </summary>
        /// <param name="channelIndex">通道索引（从 0 开始）</param>
        /// <param name="valueColumnPrefix">测试值列名前缀，默认为 "Channel"</param>
        /// <param name="resultColumnPrefix">结果列名前缀，默认为 "Channel"</param>
        /// <returns>格式：value1,value2,...,result1,result2,...</returns>
        public static string BuildChannelTestData(int channelIndex, string valueColumnPrefix = "Channel", string resultColumnPrefix = "Channel")
        {
            DataTable dt = ProjectSettings.testDataTable;
            if (dt == null || dt.Rows.Count == 0)
                return string.Empty;

            string valueColumn = $"{valueColumnPrefix}{channelIndex + 1}Value";
            string resultColumn = $"{resultColumnPrefix}{channelIndex + 1}Result";

            if (!dt.Columns.Contains(valueColumn) || !dt.Columns.Contains(resultColumn))
                return string.Empty;

            var valuesBuilder = new StringBuilder();
            var resultsBuilder = new StringBuilder();
            const string specialChars = "&%+#$=\"<>\\^`{|} ";

            // 第一轮：提取所有测试值
            foreach (DataRow row in dt.Rows)
            {
                string value = row[valueColumn]?.ToString() ?? "";
                foreach (char c in value)
                {
                    if (specialChars.Contains(c))
                        valuesBuilder.Append(Uri.HexEscape(c));
                    else
                        valuesBuilder.Append(c);
                }
                valuesBuilder.Append(',');
            }

            // 第二轮：提取所有测试结果
            foreach (DataRow row in dt.Rows)
            {
                string result = row[resultColumn]?.ToString() ?? "";
                foreach (char c in result)
                {
                    if (specialChars.Contains(c))
                        resultsBuilder.Append(Uri.HexEscape(c));
                    else
                        resultsBuilder.Append(c);
                }
                resultsBuilder.Append(',');
            }

            // 去掉最后一个多余的逗号
            string values = valuesBuilder.Length > 0 ? valuesBuilder.ToString(0, valuesBuilder.Length - 1) : "";
            string results = resultsBuilder.Length > 0 ? resultsBuilder.ToString(0, resultsBuilder.Length - 1) : "";

            if (string.IsNullOrEmpty(values) && string.IsNullOrEmpty(results))
                return "";

            if (string.IsNullOrEmpty(results))
                return values;
            if (string.IsNullOrEmpty(values))
                return results;

            return $"{values},{results}";
        }

    }
}