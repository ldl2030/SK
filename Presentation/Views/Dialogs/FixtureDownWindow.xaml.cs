using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TestPlatform
{
    public partial class FixtureDownWindow : Window
    {
        private CancellationTokenSource cts;
        private readonly Func<CancellationToken, Task<bool>> detectionFunc;
        private DispatcherTimer statusTimer;
        private int remainingSeconds = 60;

        /// <summary>
        /// 防止取消按钮、检测任务、异常处理重复关闭窗口。
        /// </summary>
        private bool isClosingHandled = false;

        public FixtureDownWindow(Func<CancellationToken, Task<bool>> detectAsync)
        {
            InitializeComponent();
            detectionFunc = detectAsync;
            Loaded += FixtureDownWindow_Loaded;
        }

        private async void FixtureDownWindow_Loaded(object sender, RoutedEventArgs e)
        {
            cts = new CancellationTokenSource();

            statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };

            statusTimer.Tick += StatusTimer_Tick;
            statusTimer.Start();

            try
            {
                bool isSuccess = await detectionFunc(cts.Token);

                if (isClosingHandled)
                    return;

                SafeClose(isSuccess);
            }
            catch (OperationCanceledException)
            {
                if (isClosingHandled)
                    return;

                SafeClose(false);
            }
            catch (Exception ex)
            {
                if (!isClosingHandled)
                {
                    MessageBox.Show(
                        $"检测失败: {ex.Message}",
                        "错误",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    SafeClose(false);
                }
            }
            finally
            {
                statusTimer?.Stop();
            }
        }

        private void StatusTimer_Tick(object sender, EventArgs e)
        {
            remainingSeconds--;

            if (remainingSeconds <= 0)
                remainingSeconds = 0;

            int percent = (int)((double)remainingSeconds / 60 * 100);
            progressBar.Value = percent;

            tbStatus.Text = $"等待下压... {remainingSeconds} 秒 / 60 秒";
            tbStatus.Text += "\nWaiting for press...";

            if (remainingSeconds == 0)
            {
                statusTimer?.Stop();
                tbStatus.Text = "超时未下压，请检查治具。\nTimeout, please check the fixture.";
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (isClosingHandled)
                return;

            isClosingHandled = true;

            try
            {
                cts?.Cancel();
            }
            catch
            {
                // 忽略取消异常
            }

            SafeClose(false);
        }

        /// <summary>
        /// 安全关闭窗口，避免重复设置 DialogResult 导致异常。
        /// </summary>
        private void SafeClose(bool dialogResult)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SafeClose(dialogResult));
                return;
            }

            if (isClosingHandled && !IsVisible)
                return;

            isClosingHandled = true;

            statusTimer?.Stop();

            try
            {
                // 只有 ShowDialog 打开的窗口才能设置 DialogResult。
                // 如果窗口已经关闭或不是对话框模式，这里可能抛异常，所以需要兜底。
                DialogResult = dialogResult;
            }
            catch (InvalidOperationException)
            {
                // 如果不是 ShowDialog 打开的，就不要设置 DialogResult，直接 Close。
            }

            try
            {
                if (IsVisible)
                    Close();
            }
            catch
            {
                // 避免关闭过程中再次抛异常
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            isClosingHandled = true;

            statusTimer?.Stop();

            try
            {
                cts?.Cancel();
            }
            catch
            {
                // 忽略取消异常
            }

            cts?.Dispose();

            base.OnClosed(e);
        }
    }
}