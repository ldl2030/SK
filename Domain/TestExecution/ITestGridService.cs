using System.Threading.Tasks;

namespace TestPlatform.TestSequences
{
    public enum StepExecutionStatus
    {
        Pending,
        Running,
        Passed,
        Failed,
        Retrying,
        PassedAfterRetry,
        Canceled,
        NotTriggered,
        Skipped,
        CleanupRunning,
        CleanupFailed
    }

    /// <summary>
    /// 测试序列更新表格的抽象接口。
    /// LR4_2912Sequence 通过这个接口更新 DataTable，不调用 MainWindow 的测试方法。
    /// </summary>
    public interface ITestGridService
    {
        int RowCount { get; }

        Task<bool> IsRowSelectedAsync(int rowIndex);
        Task<bool> IsStepSelectedAsync(string stepId);

        Task SetValueAsync(int channelIndex, int rowIndex, string value);

        Task SetResultAsync(int channelIndex, int rowIndex, string result);

        Task SetValueAndResultAsync(int channelIndex, int rowIndex, string value, bool pass);
        Task SetValueAndResultByStepIdAsync(int channelIndex, string stepId, string value, bool pass);
        Task SetValueAndStatusByStepIdAsync(
            int channelIndex,
            string stepId,
            string value,
            StepExecutionStatus status);

        Task SetExecTimeAsync(int rowIndex, long elapsedMs);
        Task SetExecTimeByStepIdAsync(string stepId, long elapsedMs);

        Task<(bool IsValid, double Lower, double Upper, string ErrorMessage)> GetLimitsAsync(int rowIndex);

        Task ScrollToRowAsync(int rowIndex, int durationMs = 150);
        Task ScrollToStepAsync(string stepId, int durationMs = 150);
    }
}
