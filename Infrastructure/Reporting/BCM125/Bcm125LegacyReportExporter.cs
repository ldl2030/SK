using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SkiaSharp;

namespace TestPlatform
{
    /// <summary>
    /// Generates the customer-compatible BCM-UP125 per-DUT TXT/PDF report.
    /// Only the 176 procedure STEP rows in BCM-125_report.psv are exported.
    /// </summary>
    public static class Bcm125LegacyReportExporter
    {
        public const int ExpectedReportItemCount = 176;

        public sealed class ExportResult
        {
            public bool Success { get; set; }
            public bool DutPassed { get; set; }
            public string TextFilePath { get; set; }
            public string PdfFilePath { get; set; }
            public string ErrorMessage { get; set; }
        }

        private sealed class ReportDefinition
        {
            public string Code { get; set; }
            public string StepId { get; set; }
            public string ReportGroup { get; set; }
            public string ProcedureRef { get; set; }
            public string SymbolicName { get; set; }
            public string Unit { get; set; }
            public string LowerLimit { get; set; }
            public string UpperLimit { get; set; }
            public double ScaleFactor { get; set; }
        }

        private sealed class ReportRow
        {
            public ReportDefinition Definition { get; set; }
            public string Value { get; set; }
            public bool Passed { get; set; }
        }

        public static Task<ExportResult> SaveAsync(
            DataTable sourceTable,
            int channelIndex,
            string operatorCode,
            string sn,
            bool testResult,
            string projectName,
            DateTime testEndTime,
            string baseDirectory)
        {
            return Task.Run(() => Save(
                sourceTable,
                channelIndex,
                operatorCode,
                sn,
                testResult,
                projectName,
                testEndTime,
                baseDirectory));
        }

        private static ExportResult Save(
            DataTable sourceTable,
            int channelIndex,
            string operatorCode,
            string sn,
            bool testResult,
            string projectName,
            DateTime testEndTime,
            string baseDirectory)
        {
            try
            {
                if (sourceTable == null)
                    return Fail("DataGrid数据为空，无法生成BCM-125报告。");

                string valueColumn = $"Channel{channelIndex + 1}Value";
                string resultColumn = $"Channel{channelIndex + 1}Result";
                if (!sourceTable.Columns.Contains("StepId") ||
                    !sourceTable.Columns.Contains(valueColumn) ||
                    !sourceTable.Columns.Contains(resultColumn))
                {
                    return Fail($"报告快照缺少StepId、{valueColumn}或{resultColumn}列。");
                }

                if (string.IsNullOrWhiteSpace(baseDirectory))
                    baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

                string definitionPath = Path.Combine(
                    baseDirectory,
                    "ProjectConfig",
                    "SK",
                    "BCM-125",
                    "BCM-125_report.psv");

                IReadOnlyList<ReportDefinition> definitions = LoadDefinitions(definitionPath);
                Dictionary<string, DataRow> rowsByStepId = sourceTable.AsEnumerable()
                    .Where(row => !string.IsNullOrWhiteSpace(row["StepId"]?.ToString()))
                    .GroupBy(
                        row => row["StepId"].ToString(),
                        StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.First(),
                        StringComparer.OrdinalIgnoreCase);

                var reportRows = new List<ReportRow>(definitions.Count);
                var missingStepIds = new List<string>();
                foreach (ReportDefinition definition in definitions)
                {
                    if (!rowsByStepId.TryGetValue(definition.StepId, out DataRow sourceRow))
                    {
                        missingStepIds.Add(definition.StepId);
                        continue;
                    }

                    string rawValue = sourceRow[valueColumn]?.ToString()?.Trim() ?? "";
                    string rawResult = sourceRow[resultColumn]?.ToString()?.Trim() ?? "";
                    bool rowPassed = IsPassed(rawResult) && !IsMissingValue(rawValue);

                    reportRows.Add(new ReportRow
                    {
                        Definition = definition,
                        Value = FormatMeasuredValue(rawValue, definition.ScaleFactor),
                        Passed = rowPassed
                    });
                }

                if (missingStepIds.Count > 0)
                {
                    return Fail(
                        "报告定义中的StepId在测试计划中不存在：" +
                        string.Join("、", missingStepIds.Take(8)) +
                        (missingStepIds.Count > 8 ? $"，共{missingStepIds.Count}项" : ""));
                }

                bool reportRowsPassed = reportRows.All(row => row.Passed);
                bool dutPassed = testResult && reportRowsPassed;
                string finalResult = dutPassed ? "PASSED" : "NOT_PASSED";

                string safeProjectName = SanitizeFileName(
                    string.IsNullOrWhiteSpace(projectName) ? "SK_BCM-125" : projectName);
                string safeOperatorCode = SanitizeFileName(
                    string.IsNullOrWhiteSpace(operatorCode) ? "UNKNOWN" : operatorCode);
                string safeSn = SanitizeFileName(
                    string.IsNullOrWhiteSpace(sn) ? "EMPTY_SN" : sn);
                string resultFolder = dutPassed ? "PASS" : "NG";
                string reportDirectory = Path.Combine(
                    baseDirectory,
                    "Reports",
                    safeProjectName,
                    $"Channel{channelIndex + 1}",
                    resultFolder);

                Directory.CreateDirectory(reportDirectory);

                string baseFileName =
                    $"BCM_UP125_{safeOperatorCode}_{safeSn}_{testEndTime:yyyy_MM_dd_HH_mm}_{finalResult}";
                string reportBasePath = BuildUniqueBasePath(reportDirectory, baseFileName);
                string textFilePath = reportBasePath + ".txt";
                string pdfFilePath = reportBasePath + ".pdf";
                string textTempPath = textFilePath + ".tmp";
                string pdfTempPath = pdfFilePath + ".tmp";

                IReadOnlyList<string> reportLines = BuildReportLines(
                    safeOperatorCode,
                    safeSn,
                    testEndTime,
                    dutPassed,
                    reportRows);

                try
                {
                    File.WriteAllLines(
                        textTempPath,
                        reportLines,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    WritePdf(pdfTempPath, reportLines);

                    File.Move(textTempPath, textFilePath);
                    File.Move(pdfTempPath, pdfFilePath);
                }
                finally
                {
                    DeleteTemporaryFile(textTempPath);
                    DeleteTemporaryFile(pdfTempPath);
                }

                return new ExportResult
                {
                    Success = true,
                    DutPassed = dutPassed,
                    TextFilePath = textFilePath,
                    PdfFilePath = pdfFilePath
                };
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        private static IReadOnlyList<ReportDefinition> LoadDefinitions(string definitionPath)
        {
            if (!File.Exists(definitionPath))
                throw new FileNotFoundException("找不到BCM-125报告定义文件。", definitionPath);

            var definitions = new List<ReportDefinition>();
            int lineNumber = 0;
            foreach (string rawLine in File.ReadLines(definitionPath, Encoding.UTF8))
            {
                lineNumber++;
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                string[] fields = line.Split('|');
                if (fields.Length != 9)
                    throw new InvalidDataException($"报告定义第{lineNumber}行应包含9列，实际为{fields.Length}列。");

                if (!double.TryParse(
                    fields[8],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double scaleFactor))
                {
                    throw new InvalidDataException($"报告定义第{lineNumber}行ScaleFactor无效：{fields[8]}");
                }

                definitions.Add(new ReportDefinition
                {
                    Code = fields[0].Trim(),
                    StepId = fields[1].Trim(),
                    ReportGroup = fields[2].Trim(),
                    ProcedureRef = fields[3].Trim(),
                    SymbolicName = fields[4].Trim(),
                    Unit = fields[5].Trim(),
                    LowerLimit = fields[6].Trim(),
                    UpperLimit = fields[7].Trim(),
                    ScaleFactor = scaleFactor
                });
            }

            if (definitions.Count != ExpectedReportItemCount)
            {
                throw new InvalidDataException(
                    $"BCM-125报告定义应包含{ExpectedReportItemCount}项，实际为{definitions.Count}项。");
            }

            string[] duplicateCodes = definitions
                .GroupBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() != 1)
                .Select(group => group.Key)
                .ToArray();
            if (duplicateCodes.Length > 0)
                throw new InvalidDataException("报告Code重复：" + string.Join("、", duplicateCodes));

            string[] duplicateStepIds = definitions
                .GroupBy(item => item.StepId, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() != 1)
                .Select(group => group.Key)
                .ToArray();
            if (duplicateStepIds.Length > 0)
                throw new InvalidDataException("报告StepId重复：" + string.Join("、", duplicateStepIds));

            return definitions;
        }

        private static IReadOnlyList<string> BuildReportLines(
            string operatorCode,
            string sn,
            DateTime testEndTime,
            bool dutPassed,
            IReadOnlyList<ReportRow> rows)
        {
            string finalResult = dutPassed ? "PASSED" : "NOT PASSED";
            var lines = new List<string>
            {
                $"BCM_UP125, Codice operatore: {operatorCode}, Seriale: {sn}, Data: {testEndTime:yyyy_MM_dd_HH_mm}, {finalResult}",
                ""
            };

            foreach (IGrouping<string, ReportRow> group in rows.GroupBy(row => row.Definition.ReportGroup))
            {
                lines.Add(group.Key + ":");
                foreach (ReportRow row in group)
                    lines.Add(FormatReportRow(row));

                lines.Add("");
                bool groupPassed = group.All(row => row.Passed);
                lines.Add($"Test {group.Key}: {(groupPassed ? "PASSED" : "NOT PASSED")}");
                lines.Add(new string('-', 70));
                lines.Add("");
            }

            return lines;
        }

        private static string FormatReportRow(ReportRow row)
        {
            ReportDefinition definition = row.Definition;
            string valueWithUnit = string.IsNullOrWhiteSpace(definition.Unit)
                ? row.Value
                : $"{row.Value} {definition.Unit}";

            string left =
                $"{definition.Code,-7} {definition.ProcedureRef,-7} {definition.SymbolicName} = {valueWithUnit}";
            string limits = string.IsNullOrWhiteSpace(definition.LowerLimit) &&
                            string.IsNullOrWhiteSpace(definition.UpperLimit)
                ? ""
                : $"[{definition.LowerLimit}; {definition.UpperLimit}]";

            return $"{left,-68}{limits,-22}{(row.Passed ? "PASSED" : "NOT PASSED")}";
        }

        private static string FormatMeasuredValue(string rawValue, double scaleFactor)
        {
            if (IsMissingValue(rawValue))
                return "NOT EXECUTED";

            string value = rawValue.Trim();
            if (Math.Abs(scaleFactor - 1.0) < 0.0000000001)
            {
                if (Regex.IsMatch(value, @"^[+-]?\d+\.\d+$"))
                    return value.Replace('.', ',');

                return value;
            }

            Match numericMatch = Regex.Match(value, @"^[+-]?(?:\d+(?:[.,]\d+)?|[.,]\d+)");
            if (!numericMatch.Success ||
                !double.TryParse(
                    numericMatch.Value.Replace(',', '.'),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double numericValue))
            {
                return value;
            }

            return (numericValue * scaleFactor)
                .ToString("0.########", CultureInfo.InvariantCulture)
                .Replace('.', ',');
        }

        private static bool IsPassed(string result)
        {
            return string.Equals(result, "PASS", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(result, "PASSED", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(result, "重试通过", StringComparison.Ordinal);
        }

        private static bool IsMissingValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ||
                   string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "未执行", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "N/A", StringComparison.OrdinalIgnoreCase);
        }

        private static void WritePdf(string filePath, IReadOnlyList<string> lines)
        {
            const float pageWidth = 595.0f;
            const float pageHeight = 842.0f;
            const float leftMargin = 24.0f;
            const float topMargin = 28.0f;
            const float bottomMargin = 28.0f;
            const float fontSize = 7.0f;
            const float lineHeight = 10.0f;

            using (SKDocument document = SKDocument.CreatePdf(filePath))
            using (SKTypeface typeface = SKTypeface.FromFamilyName("Consolas") ?? SKTypeface.Default)
            using (var font = new SKFont(typeface, fontSize))
            using (var paint = new SKPaint
            {
                Color = SKColors.Black,
                IsAntialias = true
            })
            {
                SKCanvas canvas = null;
                float y = 0;
                try
                {
                    foreach (string line in lines)
                    {
                        if (canvas == null || y + lineHeight > pageHeight - bottomMargin)
                        {
                            if (canvas != null)
                                document.EndPage();

                            canvas = document.BeginPage(pageWidth, pageHeight);
                            canvas.Clear(SKColors.White);
                            y = topMargin;
                        }

                        canvas.DrawText(line ?? "", leftMargin, y, font, paint);
                        y += lineHeight;
                    }

                    if (canvas != null)
                        document.EndPage();

                    document.Close();
                }
                catch
                {
                    document.Abort();
                    throw;
                }
            }
        }

        private static string BuildUniqueBasePath(string directory, string baseFileName)
        {
            string candidate = Path.Combine(directory, baseFileName);
            if (!File.Exists(candidate + ".txt") && !File.Exists(candidate + ".pdf"))
                return candidate;

            for (int suffix = 1; suffix < 1000; suffix++)
            {
                candidate = Path.Combine(directory, $"{baseFileName}_{suffix:D2}");
                if (!File.Exists(candidate + ".txt") && !File.Exists(candidate + ".pdf"))
                    return candidate;
            }

            throw new IOException("同一分钟内的同名BCM-125报告数量过多。");
        }

        private static string SanitizeFileName(string value)
        {
            string sanitized = value;
            foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
                sanitized = sanitized.Replace(invalidCharacter, '_');

            sanitized = sanitized.Trim().TrimEnd('.');
            return string.IsNullOrWhiteSpace(sanitized) ? "UNKNOWN" : sanitized;
        }

        private static void DeleteTemporaryFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // A failed cleanup must not hide the original report-generation error.
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
    }
}
