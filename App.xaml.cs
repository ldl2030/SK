using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace TestPlatform
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // 捕获所有未处理的异常
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                Exception ex = args.ExceptionObject as Exception;
                MessageBox.Show($"致命错误：{ex?.Message}\n{ex?.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                // 可以选择是否关闭程序
                Environment.Exit(1);
            };
            DispatcherUnhandledException += (s, args) =>
            {
                string details = args.Exception?.ToString() ?? "未知UI异常";
                try
                {
                    string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                    Directory.CreateDirectory(logDirectory);
                    File.AppendAllText(
                        Path.Combine(logDirectory, "UIUnhandled.log"),
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\r\n{details}\r\n\r\n",
                        new UTF8Encoding(false));
                }
                catch
                {
                    // Exception logging must not recursively fail the UI thread.
                }
                MessageBox.Show(
                    $"UI线程错误：{args.Exception.Message}\n\n完整调用栈已保存到 Logs\\UIUnhandled.log。",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                args.Handled = true; // 防止崩溃，但可能仍会退出
            };
        }
    }
}
