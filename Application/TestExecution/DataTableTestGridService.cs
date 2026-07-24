using System;
using System.Data;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace TestPlatform.TestSequences
{
    /// <summary>
    /// 基于 ProjectSettings.testDataTable 的表格更新服务。
    /// 该类只负责 DataTable 读写，不包含任何测试流程逻辑。
    /// </summary>
    public class DataTableTestGridService : ITestGridService
    {
        private readonly Func<DataTable> _getTable;
        private readonly Dispatcher _dispatcher;
        private readonly Func<int, int, Task> _scrollToRowAsync;
        private readonly Action<int, int, string, bool, string> _stepResultUpdated;
        public DataTableTestGridService(
    Func<DataTable> getTable,
    Dispatcher dispatcher,
    Func<int, int, Task> scrollToRowAsync = null,
    Action<int, int, string, bool, string> stepResultUpdated = null)
        {
            _getTable = getTable ?? throw new ArgumentNullException(nameof(getTable));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _scrollToRowAsync = scrollToRowAsync;
            _stepResultUpdated = stepResultUpdated;
        }
        public async Task ScrollToRowAsync(int rowIndex, int durationMs = 150)
        {
            if (_scrollToRowAsync == null)
                return;

            if (rowIndex < 0 || rowIndex >= RowCount)
                return;

            await _scrollToRowAsync(rowIndex, durationMs);
        }

        public async Task ScrollToStepAsync(string stepId, int durationMs = 150)
        {
            int rowIndex = await GetRowIndexByStepIdAsync(stepId);
            await ScrollToRowAsync(rowIndex, durationMs);
        }
        public int RowCount
        {
            get
            {
                if (_dispatcher.CheckAccess())
                {
                    return _getTable()?.Rows.Count ?? 0;
                }

                return _dispatcher.Invoke(() => _getTable()?.Rows.Count ?? 0);
            }
        }

        public async Task<bool> IsRowSelectedAsync(int rowIndex)
        {
            return await _dispatcher.InvokeAsync(() =>
            {
                DataRow row = GetRow(rowIndex);
                if (row == null)
                    return false;

                if (!row.Table.Columns.Contains("Select"))
                    return true;

                object value = row["Select"];
                if (value == null || value == DBNull.Value)
                    return false;

                return Convert.ToBoolean(value);
            });
        }

        public async Task<bool> IsStepSelectedAsync(string stepId)
        {
            int rowIndex = await GetRowIndexByStepIdAsync(stepId);
            return await IsRowSelectedAsync(rowIndex);
        }

        public async Task SetValueAsync(int channelIndex, int rowIndex, string value)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                DataRow row = GetRow(rowIndex);
                if (row == null)
                    return;

                string valueColumn = GetValueColumn(channelIndex);
                if (row.Table.Columns.Contains(valueColumn))
                {
                    row[valueColumn] = value ?? string.Empty;
                }
            });
        }

        public async Task SetResultAsync(int channelIndex, int rowIndex, string result)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                DataRow row = GetRow(rowIndex);
                if (row == null)
                    return;

                string resultColumn = GetResultColumn(channelIndex);
                if (row.Table.Columns.Contains(resultColumn))
                {
                    row[resultColumn] = result ?? string.Empty;
                }
            });
        }

        public async Task SetValueAndResultAsync(int channelIndex, int rowIndex, string value, bool pass)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                DataRow row = GetRow(rowIndex);
                if (row == null)
                    return;

                string valueColumn = GetValueColumn(channelIndex);
                string resultColumn = GetResultColumn(channelIndex);

                if (row.Table.Columns.Contains(valueColumn))
                    row[valueColumn] = value ?? string.Empty;

                if (row.Table.Columns.Contains(resultColumn))
                    row[resultColumn] = pass ? "PASS" : "FAIL";

                string itemName = row.Table.Columns.Contains("Name") ? row["Name"]?.ToString() : string.Empty;
                _stepResultUpdated?.Invoke(channelIndex, rowIndex, value ?? string.Empty, pass, itemName);
            });
        }

        public async Task SetValueAndResultByStepIdAsync(int channelIndex, string stepId, string value, bool pass)
        {
            int rowIndex = await GetRowIndexByStepIdAsync(stepId);
            await SetValueAndResultAsync(channelIndex, rowIndex, value, pass);
        }

        public async Task SetValueAndStatusByStepIdAsync(
            int channelIndex,
            string stepId,
            string value,
            StepExecutionStatus status)
        {
            int rowIndex = await GetRowIndexByStepIdAsync(stepId);
            await _dispatcher.InvokeAsync(() =>
            {
                DataRow row = GetRow(rowIndex);
                if (row == null)
                    return;

                string valueColumn = GetValueColumn(channelIndex);
                string resultColumn = GetResultColumn(channelIndex);
                if (row.Table.Columns.Contains(valueColumn))
                    row[valueColumn] = value ?? string.Empty;
                if (row.Table.Columns.Contains(resultColumn))
                    row[resultColumn] = ToDisplayText(status);

                if (status == StepExecutionStatus.Passed ||
                    status == StepExecutionStatus.PassedAfterRetry ||
                    status == StepExecutionStatus.Failed ||
                    status == StepExecutionStatus.CleanupFailed)
                {
                    string itemName = row.Table.Columns.Contains("TestItem")
                        ? row["TestItem"]?.ToString()
                        : string.Empty;
                    bool pass = status == StepExecutionStatus.Passed ||
                                status == StepExecutionStatus.PassedAfterRetry;
                    _stepResultUpdated?.Invoke(
                        channelIndex,
                        rowIndex,
                        value ?? string.Empty,
                        pass,
                        itemName);
                }
            });
        }

        private static string ToDisplayText(StepExecutionStatus status)
        {
            switch (status)
            {
                case StepExecutionStatus.Running: return "执行中";
                case StepExecutionStatus.Passed: return "PASS";
                case StepExecutionStatus.Failed: return "FAIL";
                case StepExecutionStatus.Retrying: return "重试中";
                case StepExecutionStatus.PassedAfterRetry: return "重试通过";
                case StepExecutionStatus.Canceled: return "已取消";
                case StepExecutionStatus.NotTriggered: return "未触发";
                case StepExecutionStatus.Skipped: return "已跳过";
                case StepExecutionStatus.CleanupRunning: return "收尾中";
                case StepExecutionStatus.CleanupFailed: return "收尾失败";
                default: return "待测";
            }
        }

        public async Task SetExecTimeAsync(int rowIndex, long elapsedMs)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                DataRow row = GetRow(rowIndex);
                if (row == null)
                    return;

                if (row.Table.Columns.Contains("ExecTime"))
                    row["ExecTime"] = elapsedMs.ToString();
            });
        }

        public async Task SetExecTimeByStepIdAsync(string stepId, long elapsedMs)
        {
            int rowIndex = await GetRowIndexByStepIdAsync(stepId);
            await SetExecTimeAsync(rowIndex, elapsedMs);
        }

        public async Task<(bool IsValid, double Lower, double Upper, string ErrorMessage)> GetLimitsAsync(int rowIndex)
        {
            return await _dispatcher.InvokeAsync(() =>
            {
                DataRow row = GetRow(rowIndex);
                if (row == null)
                    return (false, 0, 0, $"无效的行索引: {rowIndex}");

                string upperStr = row.Table.Columns.Contains("UpperLimit") ? row["UpperLimit"]?.ToString().Trim() : "";
                string lowerStr = row.Table.Columns.Contains("LowerLimit") ? row["LowerLimit"]?.ToString().Trim() : "";

                if (string.IsNullOrWhiteSpace(upperStr) || string.IsNullOrWhiteSpace(lowerStr))
                    return (false, 0, 0, $"第 {rowIndex + 1} 行上下限为空");

                double upper;
                double lower;

                if (!double.TryParse(upperStr, out upper) || !double.TryParse(lowerStr, out lower))
                    return (false, 0, 0, $"第 {rowIndex + 1} 行上下限值无效: {upperStr} / {lowerStr}");

                return (true, lower, upper, string.Empty);
            });
        }

        private DataRow GetRow(int rowIndex)
        {
            DataTable table = _getTable();
            if (table == null || rowIndex < 0 || rowIndex >= table.Rows.Count)
                return null;

            return table.Rows[rowIndex];
        }

        private async Task<int> GetRowIndexByStepIdAsync(string stepId)
        {
            return await _dispatcher.InvokeAsync(() =>
            {
                if (string.IsNullOrWhiteSpace(stepId))
                    throw new TestPlanConfigurationException("StepId cannot be empty.");

                DataTable table = _getTable();
                if (table == null || !table.Columns.Contains("StepId"))
                    throw new TestPlanConfigurationException("The test grid does not contain a StepId column.");

                int foundIndex = -1;
                for (int index = 0; index < table.Rows.Count; index++)
                {
                    if (!string.Equals(
                        table.Rows[index]["StepId"]?.ToString(),
                        stepId,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (foundIndex >= 0)
                        throw new TestPlanConfigurationException($"Duplicate StepId in test grid: {stepId}");

                    foundIndex = index;
                }

                if (foundIndex < 0)
                    throw new TestPlanConfigurationException($"StepId not found in test grid: {stepId}");

                return foundIndex;
            });
        }

        private string GetValueColumn(int channelIndex)
        {
            return $"Channel{channelIndex + 1}Value";
        }

        private string GetResultColumn(int channelIndex)
        {
            return $"Channel{channelIndex + 1}Result";
        }
    }
}
