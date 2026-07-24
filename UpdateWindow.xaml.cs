using System;
using System.Threading.Tasks;
using System.Windows;

namespace TestPlatform
{
    public partial class UpdateWindow : Window
    {
        public UpdateWindow()
        {
            InitializeComponent();
        }

        public async Task<bool> StartDownloadAsync(Func<IProgress<int>, Task<string>> downloadFunc)
        {
            var progress = new Progress<int>(percent =>
            {
                progressBar.Value = percent;
                tbDetail.Text = $"{percent}%";
            });
            try
            {
                string file = await downloadFunc(progress);
                return !string.IsNullOrEmpty(file);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"下载失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public void SetTitle(string title)
        {
            Dispatcher.Invoke(() => tbTitle.Text = title);
        }

        public void SetProgress(int percent)
        {
            Dispatcher.Invoke(() =>
            {
                progressBar.Value = percent;
                tbDetail.Text = $"{percent}%";
            });
        }
    }
}