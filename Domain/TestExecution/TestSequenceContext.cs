using System.Collections.Generic;
using System.Threading;

namespace TestPlatform.TestSequences
{
    public class TestSequenceValue
    {
        public string Key { get; set; }

        public double NumericValue { get; set; }

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

        public bool StopOnFail { get; set; }

        public int ParallelTestCount { get; set; }

        public int FailRetryCount { get; set; }

        public ResolvedTestPlan TestPlan { get; set; }

        public IDictionary<string, TestSequenceValue> Values
        {
            get { return _values; }
        }

        public void StoreValue(string key, double numericValue, string displayValue, string unit, bool pass)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            _values[key] = new TestSequenceValue
            {
                Key = key,
                NumericValue = numericValue,
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
