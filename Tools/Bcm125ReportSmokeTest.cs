using System;
using System.Data;
using System.IO;
using System.Linq;
using TestPlatform;

internal static class Bcm125ReportSmokeTest
{
    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: Bcm125ReportSmokeTest <bin-directory> <output-root>");
            return 2;
        }

        string binDirectory = Path.GetFullPath(args[0]);
        string outputRoot = Path.GetFullPath(args[1]);
        string definitionPath = Path.Combine(
            binDirectory,
            "ProjectConfig",
            "SK",
            "BCM-125",
            "BCM-125_report.psv");

        var table = new DataTable();
        table.Columns.Add("StepId", typeof(string));
        table.Columns.Add("Channel1Value", typeof(string));
        table.Columns.Add("Channel1Result", typeof(string));

        foreach (string line in File.ReadLines(definitionPath)
                     .Where(value => !string.IsNullOrWhiteSpace(value) && !value.StartsWith("#")))
        {
            string[] fields = line.Split('|');
            DataRow row = table.NewRow();
            row["StepId"] = fields[1];
            row["Channel1Value"] = "1";
            row["Channel1Result"] = "PASS";
            table.Rows.Add(row);
        }

        Directory.CreateDirectory(outputRoot);
        CopyReportDefinition(definitionPath, outputRoot);

        Bcm125LegacyReportExporter.ExportResult result =
            Bcm125LegacyReportExporter.SaveAsync(
                    table,
                    0,
                    "1105",
                    "B1114700117",
                    true,
                    "SK BCM-125 自动测试",
                    new DateTime(2025, 5, 19, 17, 34, 0),
                    outputRoot)
                .GetAwaiter()
                .GetResult();

        if (!result.Success)
        {
            Console.Error.WriteLine(result.ErrorMessage);
            return 1;
        }

        Console.WriteLine(result.TextFilePath);
        Console.WriteLine(result.PdfFilePath);
        Console.WriteLine("TXT lines: " + File.ReadAllLines(result.TextFilePath).Length);
        return 0;
    }

    private static void CopyReportDefinition(string sourcePath, string outputRoot)
    {
        string targetDirectory = Path.Combine(
            outputRoot,
            "ProjectConfig",
            "SK",
            "BCM-125");
        Directory.CreateDirectory(targetDirectory);
        File.Copy(
            sourcePath,
            Path.Combine(targetDirectory, "BCM-125_report.psv"),
            overwrite: true);
    }
}
