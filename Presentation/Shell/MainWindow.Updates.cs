using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;

namespace TestPlatform
{
    public partial class MainWindow
    {
        private async Task CheckForUpdates()
        {
            try
            {
                string updateUrl = GetUpdateUrlForCurrentProject();
                if (string.IsNullOrEmpty(updateUrl))
                {
                    AppendLog("当前项目未配置更新URL，跳过检查", LogWarning);
                    return;
                }

                var updateInfo = await UpdateHelper.CheckForUpdateAsync(updateUrl);
                if (updateInfo != null &&
                    updateInfo.hasVersion &&
                    !string.IsNullOrEmpty(updateInfo.version))
                {
                    Version currentVersion =
                        System.Reflection.Assembly.GetExecutingAssembly()
                            .GetName()
                            .Version;
                    Version newVersion = new Version(updateInfo.version);
                    if (newVersion > currentVersion)
                    {
                        var result = MessageBox.Show(
                            $"发现新版本 {newVersion}，是否更新？\n" +
                            $"当前版本：{currentVersion}\n" +
                            $"新版本：{newVersion}\n" +
                            $"更新内容：{updateInfo.releaseNotes}",
                            "软件更新",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);
                        if (result == MessageBoxResult.Yes)
                        {
                            var updateWin = new UpdateWindow { Owner = this };
                            updateWin.Show();
                            await Task.Delay(100);

                            string zipFile = null;
                            bool success = await updateWin.StartDownloadAsync(
                                async progress =>
                                {
                                    zipFile = await UpdateHelper.DownloadUpdateAsync(
                                        updateInfo.downloadUrl,
                                        progress,
                                        updateInfo.fileSize,
                                        updateInfo.md5);
                                    return zipFile;
                                });

                            if (success && !string.IsNullOrEmpty(zipFile))
                            {
                                updateWin.SetTitle("下载完成，正在准备更新...");
                                updateWin.SetProgress(100);
                                await Task.Delay(500);
                                string targetDir = AppDomain.CurrentDomain.BaseDirectory;
                                string mainExe =
                                    System.Diagnostics.Process.GetCurrentProcess()
                                        .MainModule
                                        .FileName;
                                UpdateHelper.LaunchUpdater(zipFile, targetDir, mainExe);
                                updateWin.SetTitle("更新程序已启动，程序即将关闭...");
                                await Task.Delay(1000);
                                Application.Current.Shutdown();
                            }
                            else
                            {
                                updateWin.Close();
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show(
                            $"当前已是最新版本。\n" +
                            $"当前版本：{currentVersion}\n" +
                            $"服务器版本：{newVersion}",
                            "提示",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
                else
                {
                    MessageBox.Show(
                        "获取版本信息失败或无新版本。",
                        "提示",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"检查更新失败: {ex.Message}", LogError);
                MessageBox.Show(
                    $"检查更新失败: {ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private string GetUpdateUrlForCurrentProject()
        {
            string projectListPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "ProjectList.xml");
            if (!File.Exists(projectListPath))
                return null;

            try
            {
                XDocument doc = XDocument.Load(projectListPath);
                var project = doc.Root
                    .Elements("Project")
                    .FirstOrDefault(
                        element =>
                            (string)element.Element("DisplayName") ==
                            ProjectSettings.CurrentProjectName);
                if (project != null)
                {
                    string url = (string)project.Element("UpdateUrl");
                    if (!string.IsNullOrEmpty(url))
                        return url;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"读取项目更新URL失败: {ex.Message}", LogError);
            }

            return null;
        }
    }
}
