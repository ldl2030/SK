using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
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
                MessageBox.Show($"UI线程错误：{args.Exception.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true; // 防止崩溃，但可能仍会退出
            };
        }
    }
}
