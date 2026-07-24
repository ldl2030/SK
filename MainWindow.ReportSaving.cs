using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace TestPlatform
{
    public partial class MainWindow
    {
        /// <summary>
        /// 单 SN 独立 Excel 报告保存，并复用当前 WPF 平台的 SMB / FTP 上传方式。
        /// </summary>
        private async Task<bool> SaveSingleExcelReportAndUploadAsync(
            int channelIndex,
            string sn,
            bool testResult,
            DateTime testStartTime,
            DateTime testEndTime)
        {
            try
            {
                DataTable snapshot = null;

                await Dispatcher.InvokeAsync(() =>
                {
                    if (ProjectSettings.testDataTable != null)
                        snapshot = ProjectSettings.testDataTable.Copy();
                }, DispatcherPriority.Background);

                if (snapshot == null)
                {
                    AppendLog("保存 Excel 报告失败：DataGrid 数据为空。", LogError);
                    return false;
                }

                ExcelReportExporter.ExportResult result = await ExcelReportExporter.SaveDataGridSnapshotAsync(
                    snapshot,
                    channelIndex,
                    sn,
                    testResult,
                    ProjectSettings.CurrentProjectName,
                    testStartTime,
                    testEndTime,
                    AppDomain.CurrentDomain.BaseDirectory);

                if (!result.Success)
                {
                    AppendLog($"保存 Excel 报告失败：{result.ErrorMessage}", LogError);
                    return false;
                }

                AppendLog($"Excel 测试报告已保存到本地: {result.FilePath}", LogInfo);

                return await UploadReportFileAsync(
                    result.FilePath,
                    channelIndex,
                    sn,
                    testResult);
            }
            catch (Exception ex)
            {
                AppendLog($"保存 Excel 报告异常: {ex.Message}", LogError);
                return false;
            }
        }

        /// <summary>
        /// CSV 与 Excel 共用的上传入口。
        /// 远程路径继续保持当前 WPF 平台逻辑，并按 Channel1 / Channel2 / Channel3 区分。
        /// </summary>
        private async Task<bool> UploadReportFileAsync(
            string reportFile,
            int channelIndex,
            string sn,
            bool testResult)
        {
            if (string.IsNullOrWhiteSpace(reportFile) || !File.Exists(reportFile))
            {
                AppendLog($"上传报告失败：本地文件不存在 {reportFile}", LogError);
                return false;
            }

            List<Task<bool>> uploadTasks = new List<Task<bool>>();

            if (appSettings.FTPEnabled)
            {
                uploadTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        string ftpBasePath = GetFtpBasePathByProject(ProjectSettings.CurrentProjectName);
                        if (string.IsNullOrEmpty(ftpBasePath))
                        {
                            Dispatcher.Invoke(() =>
                                AppendLog($"FTP 上传跳过：未配置项目 '{ProjectSettings.CurrentProjectName}' 的 FTP 路径", LogWarning));
                            return true;
                        }

                        string channelFtpBasePath = CombineFtpPath(
                            ftpBasePath,
                            $"Channel{channelIndex + 1}");

                        await FTPHelper.UploadTestReportAsync(
                            reportFile,
                            sn,
                            testResult,
                            channelFtpBasePath,
                            appSettings.FTPServer,
                            appSettings.FTPPort,
                            appSettings.FTPUser,
                            appSettings.FTPPassword,
                            msg => Dispatcher.Invoke(() => AppendLog(msg, LogInfo)));

                        return true;
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => AppendLog($"FTP 上传失败: {ex.Message}", LogError));
                        return false;
                    }
                }));
            }

            if (appSettings.SMBEnabled)
            {
                uploadTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        string deviceFolder = Path.Combine(
                            SanitizeRemotePathSegment(ProjectSettings.CurrentProjectName ?? "DefaultDevice"),
                            $"Channel{channelIndex + 1}");

                        string resultFolderName = testResult ? "PASS" : "NG";

                        bool success = await SMBHelper.UploadFileToSmbAsync(
                            reportFile,
                            deviceFolder,
                            resultFolderName,
                            appSettings.SMBServerPath,
                            appSettings.SMBUsername,
                            appSettings.SMBPassword,
                            msg => Dispatcher.Invoke(() => AppendLog(msg, LogInfo)));

                        return success;
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => AppendLog($"SMB 上传异常: {ex.Message}", LogError));
                        return false;
                    }
                }));
            }

            if (uploadTasks.Count == 0)
                return true;

            bool[] results = await Task.WhenAll(uploadTasks);
            bool allSuccess = results.All(r => r);

            if (!allSuccess)
                AppendLog("部分上传任务失败，测试结果将被标记为失败", LogError);

            return allSuccess;
        }
    }
}
