using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace TestPlatform.TestSequences
{
    public abstract partial class SKBaseSequence
    {
        protected async Task<bool> ConfirmAsync(string message)
        {
            if (_confirmAsync == null)
            {
                OnLogWarning(message);
                return true;
            }

            return await _confirmAsync(message);
        }

        protected async Task<ConfirmDisplayWindow> ShowNoticeAsync(
            string message,
            bool allowCancel = false)
        {
            if (Application.Current == null)
            {
                OnLogWarning(message);
                return null;
            }

            return await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var dialog = new ConfirmDisplayWindow(message)
                {
                    Owner = Application.Current.MainWindow
                };
                dialog.SetNoticeMode("操作提示", allowCancel);
                dialog.Show();
                return dialog;
            });
        }

        protected async Task<bool> ConfirmFixturePressDownAsync(string message)
        {
            if (Application.Current == null)
                return await ConfirmAsync(message);

            return await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var dialog = new ConfirmDisplayWindow(message)
                {
                    Owner = Application.Current.MainWindow
                };
                dialog.SetButtonLabels("确认下压", "取消");
                return dialog.ShowDialog() == true;
            });
        }

        protected async Task CloseNoticeAsync(
            ConfirmDisplayWindow dialog,
            DateTime shownAt,
            double minimumSeconds,
            CancellationToken token)
        {
            TimeSpan minimum = TimeSpan.FromSeconds(Math.Max(0, minimumSeconds));
            TimeSpan elapsed = DateTime.Now - shownAt;
            if (elapsed < minimum)
                await Task.Delay(minimum - elapsed, token);

            await CloseNoticeAsync(dialog);
        }

        protected async Task CloseNoticeAsync(ConfirmDisplayWindow dialog)
        {
            if (dialog == null || Application.Current == null)
                return;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (dialog.IsVisible)
                    dialog.CloseNotice();
            });
        }

        protected async Task UpdateNoticeAsync(
            ConfirmDisplayWindow dialog,
            string message)
        {
            if (dialog == null || Application.Current == null)
            {
                OnLogWarning(message);
                return;
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (dialog.IsVisible)
                    dialog.UpdateMessage(message);
            });
        }
    }
}
