using System;
using System.Data;
using System.Threading.Tasks;

namespace TestPlatform
{
    public partial class MainWindow
    {
        private async Task<bool> SaveBcm125TxtPdfReportAndUploadAsync(
            int channelIndex,
            string sn,
            bool testResult,
            DateTime testEndTime)
        {
            try
            {
                DataTable snapshot = null;
                await Dispatcher.InvokeAsync(() =>
                {
                    if (ProjectSettings.testDataTable != null)
                        snapshot = ProjectSettings.testDataTable.Copy();
                });

                if (snapshot == null)
                {
                    AppendLog("保存BCM-125 TXT/PDF报告失败：DataGrid数据为空。", LogError);
                    return false;
                }

                string operatorCode = !string.IsNullOrWhiteSpace(appSettings.ReportOperatorCode)
                    ? appSettings.ReportOperatorCode
                    : !string.IsNullOrWhiteSpace(GlobalState.CurrentUserName)
                        ? GlobalState.CurrentUserName
                        : Environment.UserName;

                Bcm125LegacyReportExporter.ExportResult result =
                    await Bcm125LegacyReportExporter.SaveAsync(
                        snapshot,
                        channelIndex,
                        operatorCode,
                        sn,
                        testResult,
                        ProjectSettings.CurrentProjectName,
                        testEndTime,
                        AppDomain.CurrentDomain.BaseDirectory);

                if (!result.Success)
                {
                    AppendLog($"保存BCM-125 TXT/PDF报告失败：{result.ErrorMessage}", LogError);
                    return false;
                }

                AppendLog($"BCM-125 TXT报告已保存: {result.TextFilePath}", LogInfo);
                AppendLog($"BCM-125 PDF报告已保存: {result.PdfFilePath}", LogInfo);

                if (result.DutPassed != testResult)
                {
                    AppendLog(
                        "BCM-125报告检测到缺失、未执行或未通过的必需输出项，报告结果已标记为NOT PASSED。",
                        LogWarning);
                }

                bool[] uploadResults = await Task.WhenAll(
                    UploadReportFileAsync(
                        result.TextFilePath,
                        channelIndex,
                        sn,
                        result.DutPassed),
                    UploadReportFileAsync(
                        result.PdfFilePath,
                        channelIndex,
                        sn,
                        result.DutPassed));

                return uploadResults[0] && uploadResults[1];
            }
            catch (Exception ex)
            {
                AppendLog($"保存BCM-125 TXT/PDF报告异常: {ex.Message}", LogError);
                return false;
            }
        }
    }
}
