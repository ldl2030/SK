using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TestPlatform.TestSequences
{
    public class TestSequenceValue
    {
        public string Key { get; set; }

        public double NumericValue { get; set; }

        public bool HasNumericValue { get; set; }

        public string DisplayValue { get; set; }

        public string Unit { get; set; }

        public bool Pass { get; set; }
    }

    /// <summary>
    /// 独立测试序列运行上下文。
    /// 保留测试运行所需的基础参数和跨步骤复用的测量值。
    /// </summary>
    public class TestSequenceContext
    {
        private readonly Dictionary<string, TestSequenceValue> _values = new Dictionary<string, TestSequenceValue>();

        public int ChannelIndex { get; set; }

        public string SN { get; set; }

        public CancellationToken CancellationToken { get; set; }

        /// <summary>
        /// Optional cooperative pause gate supplied by the host UI. Sequence
        /// implementations call this at safe step boundaries so a pause never
        /// interrupts an in-flight hardware command.
        /// </summary>
        public Func<CancellationToken, Task> WaitIfPausedAsync { get; set; }

        /// <summary>
        /// Optional engineering-mode gate invoked once before a visible test step.
        /// The host uses the stable StepId to implement breakpoints and single-step.
        /// </summary>
        public Func<string, CancellationToken, Task> WaitBeforeStepAsync { get; set; }

        /// <summary>
        /// Engineering pause/single-step mode needs completed results visible at
        /// the next step boundary instead of waiting for a background UI batch.
        /// </summary>
        public Func<bool> RequiresSynchronousUiResults { get; set; }

        public bool StopOnFail { get; set; }

        public bool PromptOnFail { get; set; }

        public bool ContinueAfterPowerCriticalFailure { get; set; }

        public int ParallelTestCount { get; set; }

        public int FailRetryCount { get; set; }

        public ResolvedTestPlan TestPlan { get; set; }

        public ISet<string> RequiredComPorts { get; set; }

        public IDictionary<string, TestSequenceValue> Values
        {
            get { return _values; }
        }

        public void StoreValue(string key, double numericValue, string displayValue, string unit,
            bool pass, bool hasNumericValue = true)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            _values[key] = new TestSequenceValue
            {
                Key = key,
                NumericValue = numericValue,
                HasNumericValue = hasNumericValue,
                DisplayValue = displayValue,
                Unit = unit,
                Pass = pass
            };
        }

        public bool TryGetValue(string key, out TestSequenceValue value)
        {
            return _values.TryGetValue(key, out value);
        }

        public bool TryGetNumericValue(string key, out double numericValue)
        {
            TestSequenceValue value;
            if (_values.TryGetValue(key, out value))
            {
                numericValue = value.NumericValue;
                return true;
            }

            numericValue = 0;
            return false;
        }
    }
}
