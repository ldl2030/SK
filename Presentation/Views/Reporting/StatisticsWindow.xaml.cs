using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace TestPlatform
{
    public partial class StatisticsWindow : Window
    {
        public ObservableCollection<StatisticsItem> StatisticsData { get; set; }

        public StatisticsWindow()
        {
            InitializeComponent();
            StatisticsData = new ObservableCollection<StatisticsItem>();
            dgStatistics.ItemsSource = StatisticsData;
            LoadStatistics();
        }

        private void LoadStatistics()
        {
            string reportsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports");
            if (!Directory.Exists(reportsDir))
            {
                MessageBox.Show("尚未生成任何测试报告，无法统计。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 遍历所有项目文件夹
            var projectDirs = Directory.GetDirectories(reportsDir);
            foreach (var projectDir in projectDirs)
            {
                string projectName = Path.GetFileName(projectDir);
                int pass = 0, fail = 0;
                // 遍历所有通道文件夹
                var channelDirs = Directory.GetDirectories(projectDir);
                foreach (var channelDir in channelDirs)
                {
                    // 遍历 PASS 和 NG 文件夹
                    foreach (string resultFolder in new[] { "PASS", "NG" })
                    {
                        string resultPath = Path.Combine(channelDir, resultFolder);
                        if (!Directory.Exists(resultPath)) continue;
                        var csvFiles = Directory.GetFiles(resultPath, "TestReport_*.csv");
                        foreach (var csvFile in csvFiles)
                        {
                            // 读取CSV文件，统计第四行开始的数据行（第4列整体结果）
                            var lines = File.ReadAllLines(csvFile, Encoding.UTF8);
                            if (lines.Length < 4) continue; // 至少要有标题行+上、下限+数据行
                            // 从第4行开始（索引3）是数据行
                            for (int i = 3; i < lines.Length; i++)
                            {
                                string line = lines[i];
                                if (string.IsNullOrWhiteSpace(line)) continue;
                                // CSV格式：每行双引号包裹字段，按逗号分隔
                                var parts = SplitCsvLine(line);
                                if (parts.Length >= 5) // 保证有整体结果列（索引4，0-时间戳,1-SN,2-通道,3-整体结果,4-耗时,...）
                                {
                                    string result = parts[3].Trim('"');
                                    if (result.Equals("PASS", StringComparison.OrdinalIgnoreCase))
                                        pass++;
                                    else if (result.Equals("FAIL", StringComparison.OrdinalIgnoreCase))
                                        fail++;
                                }
                            }
                        }
                    }
                }
                if (pass + fail > 0)
                {
                    StatisticsData.Add(new StatisticsItem
                    {
                        ProjectName = projectName,
                        TotalCount = pass + fail,
                        PassCount = pass,
                        FailCount = fail,
                        FailRate = (fail * 100.0 / (pass + fail)).ToString("0.00") + "%"
                    });
                }
            }
            if (StatisticsData.Count == 0)
                MessageBox.Show("未找到有效统计数据。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // 简单CSV行分割（处理引号包裹的字段）
        private string[] SplitCsvLine(string line)
        {
            var result = new System.Collections.Generic.List<string>();
            bool inQuote = false;
            int start = 0;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '"')
                    inQuote = !inQuote;
                else if (line[i] == ',' && !inQuote)
                {
                    result.Add(line.Substring(start, i - start));
                    start = i + 1;
                }
            }
            result.Add(line.Substring(start));
            return result.ToArray();
        }
    }

    public class StatisticsItem
    {
        public string ProjectName { get; set; }
        public int TotalCount { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public string FailRate { get; set; }
    }
}