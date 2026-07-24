namespace TestPlatform
{
    /// <summary>
    /// 测试报告保存模式。
    /// AppendCsv：保持当前 WPF 平台按天累加 CSV 的模式。
    /// SingleExcel：每个通道每次测试保存一个独立 Excel 文件，文件名以 SN 命名。
    /// </summary>
    public enum ReportSaveMode
    {
        AppendCsv = 0,
        SingleExcel = 1
    }
}
