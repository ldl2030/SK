using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;   // 添加此行，用于识别 Button 类型
using System.Windows.Input;

namespace TestPlatform
{
    public partial class LoginWindow : Window
    {
        // 服务器密码接口地址
        private const string PasswordApiUrl = "https://updatebqc.bqc-smt.com/version/app_password";

        // 调试信息保存路径
        private static readonly string DebugLogPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "debug_login.txt"
        );

        // 共享 HttpClient
        private static readonly HttpClient _httpClient = new HttpClient();

        public bool IsLoggedIn { get; private set; } = false;

        public LoginWindow()
        {
            InitializeComponent();
            txtUsername.Text = "admin";
            pwdPassword.Focus();
            pwdPassword.KeyDown += PwdPassword_KeyDown;
        }

        private void PwdPassword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                BtnLogin_Click(null, null);
        }

        // ========== 登录按钮点击 ==========
        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            // 获取按钮引用，用于启用/禁用
            Button loginButton = sender as Button;
            if (loginButton != null)
                loginButton.IsEnabled = false;

            try
            {
                string username = txtUsername.Text.Trim();
                string inputPassword = pwdPassword.Password;

                // 从服务器获取密码（POST 请求）
                string serverPassword = await GetPasswordFromServerAsync(username);

                if (serverPassword == null)
                {
                    tbMessage.Text = "无法获取服务器密码，请检查网络或联系管理员。";
                    return;
                }

                if (username.Equals("admin", StringComparison.OrdinalIgnoreCase) &&
                    inputPassword.Equals(serverPassword, StringComparison.OrdinalIgnoreCase))
                {
                    GlobalState.IsLoggedIn = true;
                    DialogResult = true;
                    Close();
                }
                else
                {
                    tbMessage.Text = "账号或密码错误，请重试！";
                    pwdPassword.Clear();
                    txtUsername.Focus();
                }
            }
            catch (Exception ex)
            {
                tbMessage.Text = $"登录异常：{ex.Message}";
                SaveDebugInfo($"异常: {ex}", "");
            }
            finally
            {
                if (loginButton != null)
                    loginButton.IsEnabled = true;
            }
        }

        // ========== 从服务器获取密码（POST 请求） ==========
        // ========== 从服务器获取密码（GET 请求） ==========
        private async Task<string> GetPasswordFromServerAsync(string username)
        {
            string requestInfo = "";
            string responseInfo = "";

            try
            {
                // 1. 记录请求信息（GET 请求无需请求体）
                requestInfo = string.Format(
                    "请求时间: {0:yyyy-MM-dd HH:mm:ss.fff}\n" +
                    "请求地址: {1}\n" +
                    "请求方法: GET",
                    DateTime.Now, PasswordApiUrl
                );

                // 2. 发送 GET 请求（不再需要 StringContent）
                HttpResponseMessage response = await _httpClient.GetAsync(PasswordApiUrl);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync();

                // 3. 记录响应信息
                responseInfo = string.Format(
                    "响应状态码: {0} {1}\n" +
                    "响应内容: {2}",
                    (int)response.StatusCode, response.StatusCode, json
                );

                // 4. 保存调试日志
                SaveDebugInfo(requestInfo, responseInfo);

                // 5. 解析 JSON 提取密码
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;

                    // 优先 customConfig.password
                    if (root.TryGetProperty("customConfig", out JsonElement customConfig))
                    {
                        if (customConfig.TryGetProperty("password", out JsonElement passwordElem))
                        {
                            return passwordElem.GetString();
                        }
                    }

                    // 其次直接 password
                    if (root.TryGetProperty("password", out JsonElement directPassword))
                    {
                        return directPassword.GetString();
                    }
                }

                tbMessage.Text = "服务器返回的 JSON 中未找到密码字段。";
                return null;
            }
            catch (HttpRequestException ex)
            {
                responseInfo = "网络请求异常: " + ex.Message;
                SaveDebugInfo(requestInfo, responseInfo);
                tbMessage.Text = "网络请求失败: " + ex.Message;
                return null;
            }
            catch (Exception ex)
            {
                responseInfo = "解析异常: " + ex.Message;
                SaveDebugInfo(requestInfo, responseInfo);
                tbMessage.Text = "解析服务器响应失败: " + ex.Message;
                return null;
            }
        }

        // ========== 保存调试信息到 txt 文件 ==========
        private void SaveDebugInfo(string requestInfo, string responseInfo)
        {
            try
            {
                string content = string.Format(
                    "========== 登录调试信息 ==========\n" +
                    "时间: {0:yyyy-MM-dd HH:mm:ss.fff}\n" +
                    "------------------------------------\n" +
                    "【请求信息】\n{1}\n" +
                    "------------------------------------\n" +
                    "【响应信息】\n{2}\n" +
                    "====================================\n\n",
                    DateTime.Now, requestInfo, responseInfo
                );

                File.AppendAllText(DebugLogPath, content, Encoding.UTF8);
            }
            catch
            {
                // 写入日志失败不影响登录流程
            }
        }

        // ========== 取消按钮 ==========
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}