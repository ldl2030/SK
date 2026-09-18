using System;
using System.Windows;
using System.Windows.Media;

namespace TestPlatform
{
    public partial class FirmwareProgrammingWindow : Window
    {
        public FirmwareProgrammingWindow(string firmwareName)
        {
            InitializeComponent();
            firmwareNameText.Text = string.IsNullOrWhiteSpace(firmwareName)
                ? "正在写入目标固件"
                : "目标固件：" + firmwareName;
        }

        public void UpdateProgress(string phase, int percent, string detail = null)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => UpdateProgress(phase, percent, detail)));
                return;
            }

            progressBar.Value = Math.Max(0, Math.Min(100, percent));
            phaseText.Text = string.IsNullOrWhiteSpace(phase) ? "正在烧录..." : phase;
            if (!string.IsNullOrWhiteSpace(detail))
                detailText.Text = detail;
        }

        public void SetCompleted(bool success, string detail)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SetCompleted(success, detail)));
                return;
            }

            progressBar.Value = success ? 100 : progressBar.Value;
            progressBar.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(success ? "#2E7D32" : "#B42318"));
            statusRing.Stroke = progressBar.Foreground;
            statusIcon.Foreground = progressBar.Foreground;
            statusIcon.Text = success ? "\uE73E" : "\uEA39";
            phaseText.Foreground = progressBar.Foreground;
            phaseText.Text = success ? "烧录及回读校验成功" : "固件烧录失败";
            detailText.Text = detail ?? string.Empty;
        }

        public void CloseSafely()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(CloseSafely));
                return;
            }

            if (IsVisible)
                Close();
        }
    }
}
