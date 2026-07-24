using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;
using BorderStyle = NPOI.SS.UserModel.BorderStyle;
using HorizontalAlignment = NPOI.SS.UserModel.HorizontalAlignment;
using VerticalAlignment = NPOI.SS.UserModel.VerticalAlignment;

namespace TestPlatform
{
    /// <summary>
    /// WPF 平台专用 Excel 报告导出类。
    ///
    /// 注意：
    /// 1. 本类不引用 System.Windows.Forms。
    /// 2. 本类不引用旧 WinForms 项目命名空间，例如 DemoTest / LE_FSM_HV_4767。
    /// 3. 本类只接收当前 WPF 平台的 DataTable 快照。
    /// 4. 表头格式参考旧 SavaToExcel，但保存、上传、路径、通道逻辑全部交给当前 WPF 平台。
    /// </summary>
    public static class ExcelReportExporter
    {
        public sealed class ExportResult
        {
            public bool Success { get; set; }
            public string FilePath { get; set; }
            public string ErrorMessage { get; set; }
        }

        public static Task<ExportResult> SaveDataGridSnapshotAsync(
            DataTable sourceTable,
            int channelIndex,
            string sn,
            bool testResult,
            string projectName,
            DateTime testStartTime,
            DateTime testEndTime,
            string baseDirectory)
        {
            return Task.Run(() => SaveDataGridSnapshot(
                sourceTable,
                channelIndex,
                sn,
                testResult,
                projectName,
                testStartTime,
                testEndTime,
                baseDirectory));
        }

        private static ExportResult SaveDataGridSnapshot(
            DataTable sourceTable,
            int channelIndex,
            string sn,
            bool testResult,
            string projectName,
            DateTime testStartTime,
            DateTime testEndTime,
            string baseDirectory)
        {
            try
            {
                if (sourceTable == null)
                    return Fail("DataTable 为空，无法保存 Excel 报告。");

                if (string.IsNullOrWhiteSpace(baseDirectory))
                    baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

                string safeProjectName = SanitizeFileName(string.IsNullOrWhiteSpace(projectName) ? "UnknownProject" : projectName);
                string safeSn = SanitizeFileName(string.IsNullOrWhiteSpace(sn) ? "EMPTY_SN" : sn);
                string resultFolder = testResult ? "PASS" : "NG";

                string reportDir = Path.Combine(
                    baseDirectory,
                    "Reports",
                    safeProjectName,
                    $"Channel{channelIndex + 1}",
                    resultFolder);

                Directory.CreateDirectory(reportDir);

                string filePath = BuildUniqueExcelPath(reportDir, safeSn);

                IWorkbook workbook = new XSSFWorkbook();
                ISheet sheet = workbook.CreateSheet(MakeSafeSheetName($"{safeProjectName}TestReport"));

                ICellStyle borderStyle = CreateBorderStyle(workbook);
                ICellStyle titleStyle = CreateTitleStyle(workbook, borderStyle);
                ICellStyle headerStyle = CreateHeaderStyle(workbook, borderStyle);
                ICellStyle currentChannelHeaderStyle = CreateCurrentChannelHeaderStyle(workbook, borderStyle);
                ICellStyle passStyle = CreateResultStyle(workbook, borderStyle, IndexedColors.LightGreen.Index);
                ICellStyle failStyle = CreateResultStyle(workbook, borderStyle, IndexedColors.Rose.Index);

                WriteReportHeader(
                    sheet,
                    titleStyle,
                    borderStyle,
                    projectName,
                    sn,
                    channelIndex,
                    testResult,
                    testStartTime,
                    testEndTime);

                int startRowIndex = 5;
                IRow headerRow = sheet.CreateRow(startRowIndex);

                for (int col = 0; col < sourceTable.Columns.Count; col++)
                {
                    DataColumn column = sourceTable.Columns[col];
                    ICell cell = headerRow.CreateCell(col);
                    cell.SetCellValue(string.IsNullOrWhiteSpace(column.Caption) ? column.ColumnName : column.Caption);

                    cell.CellStyle = IsCurrentChannelColumn(column.ColumnName, channelIndex)
                        ? currentChannelHeaderStyle
                        : headerStyle;

                    sheet.SetColumnWidth(col, GetColumnWidth(column.ColumnName));
                }

                for (int r = 0; r < sourceTable.Rows.Count; r++)
                {
                    IRow excelRow = sheet.CreateRow(startRowIndex + r + 1);
                    DataRow dataRow = sourceTable.Rows[r];

                    for (int c = 0; c < sourceTable.Columns.Count; c++)
                    {
                        object raw = dataRow[c];
                        string value = raw == null || raw == DBNull.Value ? string.Empty : raw.ToString();

                        ICell cell = excelRow.CreateCell(c);
                        cell.SetCellValue(value);

                        string columnName = sourceTable.Columns[c].ColumnName;
                        if (columnName.EndsWith("Result", StringComparison.OrdinalIgnoreCase))
                        {
                            cell.CellStyle = IsPassText(value)
                                ? passStyle
                                : IsFailText(value) ? failStyle : borderStyle;
                        }
                        else
                        {
                            cell.CellStyle = borderStyle;
                        }
                    }
                }

                sheet.CreateFreezePane(0, startRowIndex + 1);

                using (FileStream fs = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    workbook.Write(fs);
                }

                return new ExportResult
                {
                    Success = true,
                    FilePath = filePath
                };
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        private static ExportResult Fail(string message)
        {
            return new ExportResult
            {
                Success = false,
                ErrorMessage = message
            };
        }

        private static void WriteReportHeader(
            ISheet sheet,
            ICellStyle titleStyle,
            ICellStyle borderStyle,
            string reportName,
            string sn,
            int channelIndex,
            bool testResult,
            DateTime testStartTime,
            DateTime testEndTime)
        {
            string safeReportName = string.IsNullOrWhiteSpace(reportName) ? "Test" : reportName;

            IRow row1 = sheet.CreateRow(0);
            row1.CreateCell(0).SetCellValue("Supplier: BQC");
            row1.CreateCell(1).SetCellValue("Plant: China");
            sheet.AddMergedRegion(new CellRangeAddress(0, 0, 2, 3));
            ICell titleCell = row1.CreateCell(2);
            titleCell.SetCellValue($"{safeReportName} TestReport");
            titleCell.CellStyle = titleStyle;
            row1.CreateCell(4).SetCellValue("Date: " + DateTime.Now.ToString("yyyy/MM/dd/HH/mm/ss", CultureInfo.InvariantCulture));

            IRow row2 = sheet.CreateRow(1);
            row2.CreateCell(0).SetCellValue("P/N");
            row2.CreateCell(1).SetCellValue("×TestReport");
            row2.CreateCell(2).SetCellValue("TestReport");
            sheet.AddMergedRegion(new CellRangeAddress(1, 1, 3, 4));
            ICell standardHeader = row2.CreateCell(3);
            standardHeader.SetCellValue("Applicable Standard:");
            standardHeader.CellStyle = borderStyle;

            IRow row3 = sheet.CreateRow(2);
            row3.CreateCell(0).SetCellValue("Test specification version");
            row3.CreateCell(1).SetCellValue($"☑ {safeReportName}TestReport");
            row3.CreateCell(2).SetCellValue("□ Prototype  □ Pre-serie  □ FAI  □ Serie");
            sheet.AddMergedRegion(new CellRangeAddress(2, 2, 3, 4));
            row3.CreateCell(3).SetCellValue("EN50155:2017  IPC-A-610 Class 3");

            IRow row4 = sheet.CreateRow(3);
            row4.CreateCell(0).SetCellValue("S/N:");
            row4.CreateCell(1).SetCellValue(sn ?? string.Empty);
            sheet.AddMergedRegion(new CellRangeAddress(3, 3, 2, 4));
            row4.CreateCell(2).SetCellValue("Test cond: 23 ±5℃ 50 ±20 %RH 956 ±56 mBar");

            IRow row5 = sheet.CreateRow(4);
            row5.CreateCell(0).SetCellValue("Channel:");
            row5.CreateCell(1).SetCellValue($"Channel{channelIndex + 1}");
            row5.CreateCell(2).SetCellValue("Result:");
            row5.CreateCell(3).SetCellValue(testResult ? "PASS" : "FAIL");
            row5.CreateCell(4).SetCellValue($"Start: {testStartTime:yyyy-MM-dd HH:mm:ss}  End: {testEndTime:yyyy-MM-dd HH:mm:ss}");

            for (int r = 0; r <= 4; r++)
            {
                IRow row = sheet.GetRow(r);
                for (int c = 0; c <= 4; c++)
                {
                    ICell cell = row.GetCell(c) ?? row.CreateCell(c);
                    if (!(r == 0 && c == 2))
                        cell.CellStyle = borderStyle;
                }
                row.Height = 22 * 20;
            }
        }

        private static ICellStyle CreateBorderStyle(IWorkbook workbook)
        {
            ICellStyle style = workbook.CreateCellStyle();
            style.BorderTop = BorderStyle.Thin;
            style.BorderBottom = BorderStyle.Thin;
            style.BorderLeft = BorderStyle.Thin;
            style.BorderRight = BorderStyle.Thin;
            style.VerticalAlignment = VerticalAlignment.Center;
            return style;
        }

        private static ICellStyle CreateTitleStyle(IWorkbook workbook, ICellStyle baseStyle)
        {
            ICellStyle style = workbook.CreateCellStyle();
            style.CloneStyleFrom(baseStyle);
            style.Alignment = HorizontalAlignment.Center;
            style.VerticalAlignment = VerticalAlignment.Center;
            style.FillForegroundColor = IndexedColors.Grey25Percent.Index;
            style.FillPattern = FillPattern.SolidForeground;

            IFont font = workbook.CreateFont();
            font.FontName = "Arial";
            font.FontHeightInPoints = 14;
            font.IsBold = true;
            style.SetFont(font);
            return style;
        }

        private static ICellStyle CreateHeaderStyle(IWorkbook workbook, ICellStyle baseStyle)
        {
            ICellStyle style = workbook.CreateCellStyle();
            style.CloneStyleFrom(baseStyle);
            style.FillForegroundColor = IndexedColors.LightBlue.Index;
            style.FillPattern = FillPattern.SolidForeground;

            IFont font = workbook.CreateFont();
            font.IsBold = true;
            style.SetFont(font);
            return style;
        }

        private static ICellStyle CreateCurrentChannelHeaderStyle(IWorkbook workbook, ICellStyle baseStyle)
        {
            ICellStyle style = workbook.CreateCellStyle();
            style.CloneStyleFrom(baseStyle);
            style.FillForegroundColor = IndexedColors.LightYellow.Index;
            style.FillPattern = FillPattern.SolidForeground;

            IFont font = workbook.CreateFont();
            font.IsBold = true;
            style.SetFont(font);
            return style;
        }

        private static ICellStyle CreateResultStyle(IWorkbook workbook, ICellStyle baseStyle, short color)
        {
            ICellStyle style = workbook.CreateCellStyle();
            style.CloneStyleFrom(baseStyle);
            style.FillForegroundColor = color;
            style.FillPattern = FillPattern.SolidForeground;
            return style;
        }

        private static int GetColumnWidth(string columnName)
        {
            if (string.Equals(columnName, "TestItem", StringComparison.OrdinalIgnoreCase))
                return 30 * 256;
            if (columnName.IndexOf("Value", StringComparison.OrdinalIgnoreCase) >= 0)
                return 18 * 256;
            if (columnName.IndexOf("Result", StringComparison.OrdinalIgnoreCase) >= 0)
                return 12 * 256;
            if (string.Equals(columnName, "Select", StringComparison.OrdinalIgnoreCase))
                return 8 * 256;
            return 14 * 256;
        }

        private static bool IsCurrentChannelColumn(string columnName, int channelIndex)
        {
            string prefix = $"Channel{channelIndex + 1}";
            return columnName != null && columnName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildUniqueExcelPath(string folder, string safeSn)
        {
            string normalPath = Path.Combine(folder, safeSn + ".xlsx");
            if (!File.Exists(normalPath))
                return normalPath;

            string minutePath = Path.Combine(folder, $"{safeSn}_{DateTime.Now:yyyyMMddHHmm}.xlsx");
            if (!File.Exists(minutePath))
                return minutePath;

            string secondPath = Path.Combine(folder, $"{safeSn}_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
            if (!File.Exists(secondPath))
                return secondPath;

            for (int i = 2; i < 1000; i++)
            {
                string path = Path.Combine(folder, $"{safeSn}_{DateTime.Now:yyyyMMddHHmmss}_{i}.xlsx");
                if (!File.Exists(path))
                    return path;
            }

            throw new IOException("无法生成唯一的 Excel 文件名。");
        }

        private static string SanitizeFileName(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "Unknown";

            char[] invalid = Path.GetInvalidFileNameChars();
            string cleaned = new string(text.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "Unknown" : cleaned;
        }

        private static string MakeSafeSheetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "TestReport";

            char[] invalid = { ':', '\\', '/', '?', '*', '[', ']' };
            string cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            if (cleaned.Length > 31)
                cleaned = cleaned.Substring(0, 31);
            return string.IsNullOrWhiteSpace(cleaned) ? "TestReport" : cleaned;
        }

        private static bool IsPassText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string value = text.Trim().ToUpperInvariant();
            return value.Contains("PASS") || value.Contains("OK");
        }

        private static bool IsFailText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string value = text.Trim().ToUpperInvariant();
            return value.Contains("FAIL") || value.Contains("NG");
        }
    }
}
