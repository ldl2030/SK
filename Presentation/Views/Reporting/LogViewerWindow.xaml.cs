using System;
using System.Collections.Generic;
using System.IO;
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
using System.Windows.Threading;
using System.Xml.Linq;

namespace TestPlatform
{
    /// <summary>
    /// LogViewerWindow.xaml 的交互逻辑
    /// </summary>
    public partial class LogViewerWindow : Window
    {
        private readonly RichTextBox _sourceRichTextBox;
        private DispatcherTimer _syncTimer;
        private bool _isUpdating;
        public LogViewerWindow(RichTextBox sourceRichTextBox)
        {
            InitializeComponent();
            _sourceRichTextBox = sourceRichTextBox;
            SyncLog();

            _syncTimer = new DispatcherTimer();
            _syncTimer.Interval = TimeSpan.FromMilliseconds(200);
            _syncTimer.Tick += (s, e) => SyncLog();
            _syncTimer.Start();
        }
        private void SyncLog()
        {
            if (_isUpdating) return;
            _isUpdating = true;
            try
            {
                // 保存当前滚动位置
                var scrollViewer = FindScrollViewer(rtbLog);
                double? verticalOffset = scrollViewer?.VerticalOffset;

                // 复制主窗口日志内容（保留格式）
                CopyFlowDocument(_sourceRichTextBox.Document, rtbLog.Document);

                // 恢复滚动位置
                if (scrollViewer != null && verticalOffset.HasValue)
                    scrollViewer.ScrollToVerticalOffset(verticalOffset.Value);
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private ScrollViewer FindScrollViewer(DependencyObject obj)
        {
            if (obj is ScrollViewer viewer) return viewer;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                var result = FindScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        private void CopyFlowDocument(FlowDocument source, FlowDocument target)
        {
            using (var stream = new MemoryStream())
            {
                new TextRange(source.ContentStart, source.ContentEnd).Save(stream, DataFormats.Xaml);
                new TextRange(target.ContentStart, target.ContentEnd).Load(stream, DataFormats.Xaml);
            }
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e) => rtbLog.FontSize += 2;
        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            if (rtbLog.FontSize > 8) rtbLog.FontSize -= 2;
        }

        private void CopyLog_Click(object sender, RoutedEventArgs e)
        {
            var range = new TextRange(rtbLog.Document.ContentStart, rtbLog.Document.ContentEnd);
            Clipboard.SetText(range.Text);
            MessageBox.Show("日志已复制到剪贴板。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        protected override void OnClosed(EventArgs e)
        {
            _syncTimer?.Stop();
            base.OnClosed(e);
        }
    }
}
