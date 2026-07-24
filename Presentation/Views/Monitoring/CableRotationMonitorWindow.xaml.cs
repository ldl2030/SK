using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TestPlatform
{
    public partial class CableRotationMonitorWindow : Window
    {
        private readonly int _scopeChannel;
        private readonly double _lowerLimit;
        private readonly double _upperLimit;
        private readonly RigolDHO804Scope _scope;
        private DispatcherTimer _timer;
        private int _validCount;          // 有效计数（在范围内的次数）
        private int _invalidCount;        // 无效计数（不在范围内的次数）
        private double _lastFrequency;
        private bool _success;
        private int _sampleCount;
        private readonly int _requiredValidCount = 10;   // 需要达到的有效次数
        private readonly int _maxInvalidCount = 5;       // 允许的最大无效次数（超过则重置）
        private readonly int _maxSamples = 50;           // 最大采样次数（超时）

        public bool IsSuccess { get; private set; }
        public double FinalFrequency { get; private set; }

        public CableRotationMonitorWindow(RigolDHO804Scope scope, int scopeChannel, double lowerLimit, double upperLimit)
        {
            InitializeComponent();
            _scope = scope;
            _scopeChannel = scopeChannel;
            _lowerLimit = lowerLimit;
            _upperLimit = upperLimit;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += Timer_Tick;
            tbCount.Text = $"0/{_requiredValidCount}";
            progressBar.Maximum = _maxSamples;
            progressBar.Value = 0;
            this.Closed += (s, e) => _timer.Stop();
            Loaded += (s, e) => _timer.Start();
        }

        private async void Timer_Tick(object sender, EventArgs e)
        {
            if (_success) return;
            _sampleCount++;
            progressBar.Value = _sampleCount;

            try
            {
                string freqStr = await _scope.MeasureFrequencyAsync(_scopeChannel, msg => { });
                Debug.WriteLine($"频率原始响应: {freqStr}");

                if (string.IsNullOrWhiteSpace(freqStr) ||
                    freqStr.Contains("9.9E+37") ||
                    freqStr.Contains("9.9E37") ||
                    freqStr.Equals("无信号", StringComparison.OrdinalIgnoreCase))
                {
                    tbFrequency.Text = "无信号";
                    // 无信号视为无效，增加无效计数
                    _invalidCount++;
                    tbCount.Text = $"{_validCount}/{_requiredValidCount} (无效{_invalidCount}/{_maxInvalidCount})";
                    CheckReset();
                    return;
                }

                double freq = ParseFrequencyWithUnit(freqStr);
                if (double.IsNaN(freq))
                {
                    tbFrequency.Text = "解析失败";
                    _invalidCount++;
                    tbCount.Text = $"{_validCount}/{_requiredValidCount} (无效{_invalidCount}/{_maxInvalidCount})";
                    CheckReset();
                    return;
                }

                if (Math.Abs(freq) > 1e12)
                {
                    tbFrequency.Text = "异常值";
                    _invalidCount++;
                    tbCount.Text = $"{_validCount}/{_requiredValidCount} (无效{_invalidCount}/{_maxInvalidCount})";
                    CheckReset();
                    return;
                }

                _lastFrequency = freq;
                tbFrequency.Text = $"{freq:F2} Hz";

                // 判断是否在范围内
                if (freq >= _lowerLimit && freq <= _upperLimit)
                {
                    // 有效：增加有效计数，无效计数不变
                    _validCount++;
                    tbCount.Text = $"{_validCount}/{_requiredValidCount} (无效{_invalidCount}/{_maxInvalidCount})";

                    if (_validCount >= _requiredValidCount)
                    {
                        _success = true;
                        _timer.Stop();
                        IsSuccess = true;
                        FinalFrequency = freq;
                        DialogResult = true;
                        Close();
                        return;
                    }
                }
                else
                {
                    // 无效：增加无效计数
                    _invalidCount++;
                    tbCount.Text = $"{_validCount}/{_requiredValidCount} (无效{_invalidCount}/{_maxInvalidCount})";
                    CheckReset();
                }
            }
            catch (Exception ex)
            {
                tbFrequency.Text = $"异常: {ex.Message}";
                Debug.WriteLine($"异常: {ex.Message}");
                _invalidCount++;
                tbCount.Text = $"{_validCount}/{_requiredValidCount} (无效{_invalidCount}/{_maxInvalidCount})";
                CheckReset();
                return;
            }

            if (_sampleCount >= _maxSamples && !_success)
            {
                _timer.Stop();
                IsSuccess = false;
                FinalFrequency = _lastFrequency;
                DialogResult = false;
                Close();
            }
        }

        /// <summary>
        /// 检查无效计数是否达到上限，若达到则重置所有计数
        /// </summary>
        private void CheckReset()
        {
            if (_invalidCount >= _maxInvalidCount)
            {
                _validCount = 0;
                _invalidCount = 0;
                tbCount.Text = $"{_validCount}/{_requiredValidCount} (无效{_invalidCount}/{_maxInvalidCount})";
                // 可以添加日志或提示
            }
        }

        /// <summary>
        /// 从字符串中提取频率值，并自动换算为 Hz
        /// 支持格式：1.97 kHz、1.9718E+03、1.97 MHz、1.97 mHz 等
        /// </summary>
        private double ParseFrequencyWithUnit(string input)
        {
            if (string.IsNullOrEmpty(input)) return double.NaN;
            input = input.Trim();

            if (double.TryParse(input, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double val))
                return val;

            var match = Regex.Match(input, @"([-+]?\d*\.?\d+(?:[Ee][+-]?\d+)?)\s*([a-zA-Z]*)");
            if (!match.Success) return double.NaN;

            string numStr = match.Groups[1].Value;
            string unit = match.Groups[2].Value.ToLowerInvariant();

            if (!double.TryParse(numStr, System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out double num))
                return double.NaN;

            switch (unit)
            {
                case "khz": return num * 1000;
                case "Mhz": return num * 1_000_000;
                case "hz": return num;
                case "mhz": return num / 1000;
                default: return num;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            IsSuccess = false;
            FinalFrequency = _lastFrequency;
            DialogResult = false;
            Close();
        }
    }
}