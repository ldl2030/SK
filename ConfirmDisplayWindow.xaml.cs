using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace TestPlatform
{
    /// <summary>
    /// ConfirmDisplayWindow.xaml 的交互逻辑
    /// </summary>
    public partial class ConfirmDisplayWindow : Window
    {
        public bool IsConfirmed { get; private set; }
        public bool WasCancelled => _wasCancelled;
        private bool _noticeMode;
        private bool _programmaticClose;
        private volatile bool _wasCancelled;

        public ConfirmDisplayWindow(string message)
        {
            InitializeComponent();
            tbMessage.Text = message;
            // 确保关闭窗口时 DialogResult = false
            this.Closing += (s, e) =>
            {
                if (_noticeMode)
                {
                    if (!_programmaticClose)
                        _wasCancelled = true;
                    return;
                }

                if (DialogResult == null)
                    DialogResult = false;
                IsConfirmed = DialogResult == true;
            };
        }

        public void SetNoticeMode(string title = "提示", bool allowCancel = false)
        {
            _noticeMode = true;
            Title = title;
            btnYes.Visibility = Visibility.Collapsed;
            btnNo.Visibility = allowCancel ? Visibility.Visible : Visibility.Collapsed;
            btnNo.Content = "取消下压";
        }

        public void SetButtonLabels(string confirmText, string cancelText)
        {
            btnYes.Content = confirmText;
            btnNo.Content = cancelText;
        }

        public void CloseNotice()
        {
            _programmaticClose = true;
            Close();
        }

        public void UpdateMessage(string message)
        {
            tbMessage.Text = message;
        }

        private void BtnYes_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = true;
            DialogResult = true;
            Close();
        }

        private void BtnNo_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            _wasCancelled = true;
            if (_noticeMode)
            {
                Close();
                return;
            }

            DialogResult = false;
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            BtnNo_Click(sender, e);
        }

    }
}
