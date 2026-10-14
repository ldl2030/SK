using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace TestPlatform.TestSequences
{
    /// <summary>
    /// 基于 ProjectSettings.testDataTable 的表格更新服务。
    /// 该类只负责 DataTable 读写，不包含任何测试流程逻辑。
    /// </summary>
    public class DataTableTestGridService : ITestGridService, IFlushableTestGridService
    {
        private sealed class PendingOutcome
        {
            public int ChannelIndex;
            public string StepId;
            public string Value;
            public StepExecutionStatus Status;
            public long ElapsedMs;
        }

        private readonly Func<DataTable> _getTable;
        private readonly Dispatcher _dispatcher;
        private readonly Func<int, int, Task> _scrollToRowAsync;
        private readonly Action<int, int, string, bool, string> _stepResultUpdated;
        private readonly object _pendingOutcomeSync = new object();
        private readonly Queue<PendingOutcome> _pendingOutcomes = new Queue<PendingOutcome>();
        private bool _outcomeDrainScheduled;
        private Exception _outcomeDrainError;
        private DataTable _indexedTable;
        private int _indexedRowCount = -1;
        private Dictionary<string, int> _stepIndexById;
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
                case StepExecutionStatus.Warning: return "警告";
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

        public Task SetOutcomeByStepIdAsync(
            int channelIndex,
            string stepId,
            string value,
            StepExecutionStatus status,
            long elapsedMs)
        {
            if (string.IsNullOrWhiteSpace(stepId))
                throw new TestPlanConfigurationException("StepId cannot be empty.");

            bool scheduleDrain = false;
            lock (_pendingOutcomeSync)
            {
                if (_outcomeDrainError != null)
                    throw new InvalidOperationException(
                        "A queued test-grid update failed.", _outcomeDrainError);
                _pendingOutcomes.Enqueue(new PendingOutcome
                {
                    ChannelIndex = channelIndex,
                    StepId = stepId,
                    Value = value ?? string.Empty,
                    Status = status,
                    ElapsedMs = elapsedMs
                });
                if (!_outcomeDrainScheduled)
                {
                    _outcomeDrainScheduled = true;
                    scheduleDrain = true;
                }
            }

            if (scheduleDrain)
                ScheduleOutcomeDrain();
            return Task.CompletedTask;
        }

        public async Task FlushPendingOutcomesAsync()
        {
            await _dispatcher.InvokeAsync(
                () => DrainPendingOutcomes(int.MaxValue),
                DispatcherPriority.Normal);
            lock (_pendingOutcomeSync)
            {
                if (_outcomeDrainError != null)
                    throw new InvalidOperationException(
                        "A queued test-grid update failed.", _outcomeDrainError);
            }
        }

        private void ScheduleOutcomeDrain()
        {
            _dispatcher.BeginInvoke(
                new Action(() => DrainPendingOutcomes(64)),
                DispatcherPriority.Background);
        }

        private void DrainPendingOutcomes(int maximumBatchSize)
        {
            var batch = new List<PendingOutcome>();
            lock (_pendingOutcomeSync)
            {
                while (batch.Count < maximumBatchSize && _pendingOutcomes.Count > 0)
                    batch.Add(_pendingOutcomes.Dequeue());
                _outcomeDrainScheduled = false;
            }

            // Only the latest item in a batch needs to update the large channel
            // banner. Every row still receives its own value, status and timing.
            for (int index = 0; index < batch.Count; index++)
            {
                try
                {
                    ApplyOutcome(batch[index], index == batch.Count - 1);
                }
                catch (Exception ex)
                {
                    lock (_pendingOutcomeSync)
                        _outcomeDrainError = _outcomeDrainError ?? ex;
                }
            }

            bool scheduleDrain = false;
            lock (_pendingOutcomeSync)
            {
                if (_pendingOutcomes.Count > 0 && !_outcomeDrainScheduled)
                {
                    _outcomeDrainScheduled = true;
                    scheduleDrain = true;
                }
            }
            if (scheduleDrain)
                ScheduleOutcomeDrain();
        }

        private void ApplyOutcome(PendingOutcome outcome, bool updateBanner)
        {
            int rowIndex = GetRowIndexByStepId(outcome.StepId);
            DataRow row = GetRow(rowIndex);
            if (row == null)
                return;

            string valueColumn = GetValueColumn(outcome.ChannelIndex);
            string resultColumn = GetResultColumn(outcome.ChannelIndex);
            if (row.Table.Columns.Contains("ExecTime"))
                row["ExecTime"] = outcome.ElapsedMs.ToString();
            if (row.Table.Columns.Contains(valueColumn))
                row[valueColumn] = outcome.Value;
            if (row.Table.Columns.Contains(resultColumn))
                row[resultColumn] = ToDisplayText(outcome.Status);

            if (updateBanner &&
                (outcome.Status == StepExecutionStatus.Passed ||
                 outcome.Status == StepExecutionStatus.PassedAfterRetry ||
                 outcome.Status == StepExecutionStatus.Failed ||
                 outcome.Status == StepExecutionStatus.CleanupFailed))
            {
                string itemName = row.Table.Columns.Contains("TestItem")
                    ? row["TestItem"]?.ToString()
                    : string.Empty;
                bool pass = outcome.Status == StepExecutionStatus.Passed ||
                            outcome.Status == StepExecutionStatus.PassedAfterRetry;
                _stepResultUpdated?.Invoke(
                    outcome.ChannelIndex,
                    rowIndex,
                    outcome.Value,
                    pass,
                    itemName);
            }
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
            return await _dispatcher.InvokeAsync(() => GetRowIndexByStepId(stepId));
        }

        private int GetRowIndexByStepId(string stepId)
        {
                if (string.IsNullOrWhiteSpace(stepId))
                    throw new TestPlanConfigurationException("StepId cannot be empty.");

                DataTable table = _getTable();
                if (table == null || !table.Columns.Contains("StepId"))
                    throw new TestPlanConfigurationException("The test grid does not contain a StepId column.");

                if (!ReferenceEquals(_indexedTable, table) ||
                    _indexedRowCount != table.Rows.Count ||
                    _stepIndexById == null)
                {
                    var indexById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    for (int index = 0; index < table.Rows.Count; index++)
                    {
                        string candidate = table.Rows[index]["StepId"]?.ToString();
                        if (string.IsNullOrWhiteSpace(candidate))
                            continue;
                        if (indexById.ContainsKey(candidate))
                            throw new TestPlanConfigurationException($"Duplicate StepId in test grid: {candidate}");
                        indexById[candidate] = index;
                    }

                    _indexedTable = table;
                    _indexedRowCount = table.Rows.Count;
                    _stepIndexById = indexById;
                }

                int foundIndex;
                if (!_stepIndexById.TryGetValue(stepId, out foundIndex))
                    throw new TestPlanConfigurationException($"StepId not found in test grid: {stepId}");

                return foundIndex;
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
