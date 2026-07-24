using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TestPlatform
{
    public partial class WaitDialog : Window
    {
        private DispatcherTimer timer;
        private DateTime startTime;
        private double totalMilliseconds;
        private CancellationTokenSource cts;
#pragma warning disable CS0414
        private bool isCancelled = false;
#pragma warning restore CS0414

        public WaitDialog(string message, double seconds, CancellationToken cancellationToken = default)
        {
            InitializeComponent();
            tbTitle.Text = message;
            totalMilliseconds = seconds * 1000;
            tbCountdown.Text = $"剩余 {seconds:F1} 秒";
            progressBar.Maximum = totalMilliseconds;
            progressBar.Value = totalMilliseconds;

            // 外部取消支持
            if (cancellationToken.CanBeCanceled)
            {
                cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }
            else
            {
                cts = new CancellationTokenSource();
            }

            cts.Token.Register(() =>
            {
                Dispatcher.Invoke(() =>
                {
                    isCancelled = true;
                    DialogResult = false;
                    Close();
                });
            });

            this.Loaded += WaitDialog_Loaded;
        }

        private void WaitDialog_Loaded(object sender, RoutedEventArgs e)
        {
            startTime = DateTime.Now;
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(30);
            timer.Tick += Timer_Tick;
            timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            TimeSpan elapsed = DateTime.Now - startTime;
            double remaining = totalMilliseconds - elapsed.TotalMilliseconds;
            if (remaining <= 0)
            {
                timer.Stop();
                DialogResult = true;
                Close();
                return;
            }

            progressBar.Value = remaining;
            tbCountdown.Text = $"剩余 {remaining / 1000:F1} 秒";

            if (cts.IsCancellationRequested)
            {
                timer.Stop();
                isCancelled = true;
                DialogResult = false;
                Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            timer?.Stop();
            isCancelled = true;
            DialogResult = false;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            timer?.Stop();
            cts?.Dispose();
            base.OnClosed(e);
        }

        public static async Task WaitOrThrowAsync(string message, double seconds, Window owner = null, CancellationToken cancellationToken = default)
        {
            var dialog = new WaitDialog(message, seconds, cancellationToken);
            dialog.Owner = owner ?? Application.Current.MainWindow;
            if (dialog.ShowDialog() != true)
                throw new OperationCanceledException("等待被取消");
            await Task.CompletedTask;
        }
    }
}