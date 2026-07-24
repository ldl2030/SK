using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace TestPlatform
{
    public partial class MainWindow
    {
        private void BtnShowLogViewer_Click(object sender, RoutedEventArgs e)
        {
            var logWindow = new LogViewerWindow(rictxB_log);
            logWindow.Owner = this;
            logWindow.Show();
        }

        private void BtnUtilityTools_Click(object sender, RoutedEventArgs e)
        {
            if (btnUtilityTools.ContextMenu == null)
                return;

            btnUtilityTools.ContextMenu.PlacementTarget = btnUtilityTools;
            btnUtilityTools.ContextMenu.Placement =
                System.Windows.Controls.Primitives.PlacementMode.Bottom;
            btnUtilityTools.ContextMenu.IsOpen = true;
        }

        private void btnStatistics_Click(object sender, RoutedEventArgs e)
        {
            var win = new StatisticsWindow();
            win.Owner = this;
            win.ShowDialog();
        }

        private void 打开设备管理器ToolStripMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start("devmgmt.msc");
                AppendLog("启动设备管理器成功！", LogSuccess);
            }
            catch (Exception ex)
            {
                AppendLog($"打开设备管理器失败: {ex.Message}", LogError);
                MessageBox.Show(
                    "无法打开设备管理器，请检查系统权限或尝试手动打开。",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void 打开默认打印机首选项ToolStripMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start("ms-settings:printers");
                AppendLog("启动打印机默认首选项成功！", LogSuccess);
            }
            catch (Exception ex)
            {
                AppendLog($"打开打印机首选项失败: {ex.Message}", LogError);
                MessageBox.Show(
                    "无法打开打印机首选项，请手动打开。",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void 快速截图ToolStripMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string screenshotDir = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Screenshots");
                if (!Directory.Exists(screenshotDir))
                    Directory.CreateDirectory(screenshotDir);

                string fileName = DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                string filePath = Path.Combine(screenshotDir, fileName);
                int screenWidth = (int)SystemParameters.PrimaryScreenWidth;
                int screenHeight = (int)SystemParameters.PrimaryScreenHeight;

                using (var bitmap = new System.Drawing.Bitmap(screenWidth, screenHeight))
                {
                    using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                    {
                        graphics.CopyFromScreen(0, 0, 0, 0, bitmap.Size);
                    }

                    bitmap.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
                }

                AppendLog($"截图已保存至: {filePath}", LogSuccess);
                MessageBox.Show(
                    $"截图已保存至:\n{filePath}",
                    "截图成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppendLog($"截图失败: {ex.Message}", LogError);
                MessageBox.Show(
                    $"截图失败: {ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Keyboard.Modifiers ==
                    (ModifierKeys.Control | ModifierKeys.Shift) &&
                e.Key == Key.S)
            {
                快速截图ToolStripMenuItem_Click(null, null);
                e.Handled = true;
            }

            base.OnKeyDown(e);
        }

        private void 打开计算器ToolStripMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start("calc.exe");
            }
            catch (Exception ex)
            {
                AppendLog($"打开计算器失败: {ex.Message}", LogError);
                MessageBox.Show(
                    "无法打开计算器，请检查系统。",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void btnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("Click to update 1", LogInfo);
            btnCheckUpdate.IsEnabled = false;
            try
            {
                AppendLog("Click to update 1", LogInfo);
                await CheckForUpdates();
            }
            catch (Exception ex)
            {
                AppendLog($"检查更新异常: {ex.Message}", LogError);
                MessageBox.Show(
                    $"检查更新失败: {ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                btnCheckUpdate.IsEnabled = true;
            }
        }

        private void btnOffsetConfig_Click(object sender, RoutedEventArgs e)
        {
            var win = new OffsetConfigWindow();
            win.Owner = this;
            win.ShowDialog();
        }

        private void btnLEDConfig_Click(object sender, RoutedEventArgs e)
        {
            var win = new LEDConfigWindow(_ledConfig);
            win.Owner = this;
            if (win.ShowDialog() == true)
            {
                _ledConfig = win.Config;
                AppendLog("LED配置已保存并生效", LogSuccess);
            }
        }
    }
}
