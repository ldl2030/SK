using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TestPlatform
{
    public partial class FrequencyMonitorWindow : Window
    {
        private readonly string _portName;
        private readonly int _baudRate;
        private readonly double _lowerLimit;
        private readonly double _upperLimit;
        private readonly int _timeoutSeconds;
        private readonly CancellationTokenSource _cts;
        private DispatcherTimer _countdownTimer;
        private DispatcherTimer _waitTimer;   // 3秒等待定时器
        private int _remainingSeconds;
        private int _validCount;
        private bool _finished;
        private bool _hasStartedWaitTimer;   // 防止多次启动定时器
        private double _lastFrequency;          // 最后一次采集的频率（无论是否有效）
        private double _lastValidFrequency;     // 最后一次在范围内的频率
        public bool IsSuccess { get; private set; }
        public double ValidFrequency { get; private set; }

        public FrequencyMonitorWindow(string portName, int baudRate, double lowerLimit, double upperLimit, int timeoutSeconds = 30)
        {
            InitializeComponent();
            _portName = portName;
            _baudRate = baudRate;
            _lowerLimit = lowerLimit;
            _upperLimit = upperLimit;
            _timeoutSeconds = timeoutSeconds;
            _cts = new CancellationTokenSource();
            _remainingSeconds = timeoutSeconds;
            tbCountdown.Text = $"剩余时间: {_remainingSeconds} 秒";

            Loaded += async (s, e) => await StartMonitoring();
        }

        private async Task StartMonitoring()
        {
            // 进度条平滑动画
            progressBar.Maximum = _timeoutSeconds;
            progressBar.Value = _timeoutSeconds;
            var progressAnimation = new DoubleAnimation(0, TimeSpan.FromSeconds(_timeoutSeconds))
            {
                EasingFunction = new PowerEase { EasingMode = EasingMode.EaseInOut }
            };
            progressBar.BeginAnimation(ProgressBar.ValueProperty, progressAnimation);

            // 倒计时文本更新（每秒）
            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += CountdownTimer_Tick;
            _countdownTimer.Start();

            try
            {
                await Task.Run(() => MonitorFrequencyLoop(_cts.Token));
            }
            catch (OperationCanceledException) { }
            finally
            {
                _countdownTimer?.Stop();
                _waitTimer?.Stop();
            }
        }

        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            if (_finished) return;
            _remainingSeconds--;
            tbCountdown.Text = $"剩余时间: {_remainingSeconds} 秒";
            if (_remainingSeconds <= 0)
            {
                _countdownTimer.Stop();
                Timeout();
            }
        }

        private void MonitorFrequencyLoop(CancellationToken token)
        {
            using (var port = new SerialPort(_portName, _baudRate, Parity.None, 8, StopBits.One))
            {
                port.ReadTimeout = 500;
                port.WriteTimeout = 500;
                port.Open();
                port.DiscardInBuffer();

                byte[] command = { 0x01, 0x03, 0x00, 0x00, 0x00, 0x02, 0xC4, 0x0B };

                while (!token.IsCancellationRequested && !_finished && _remainingSeconds > 0)
                {
                    port.Write(command, 0, command.Length);
                    try
                    {
                        byte[] buffer = new byte[9];
                        int bytesRead = 0;
                        DateTime start = DateTime.Now;
                        while (bytesRead < 9 && (DateTime.Now - start).TotalMilliseconds < 1000)
                        {
                            if (port.BytesToRead > 0)
                            {
                                int read = port.Read(buffer, bytesRead, Math.Min(port.BytesToRead, 9 - bytesRead));
                                bytesRead += read;
                            }
                            else
                                Thread.Sleep(10);
                        }
                        if (bytesRead >= 9)
                        {
                            int rawValue = (buffer[3] << 8) | buffer[4];
                            double frequency = rawValue;

                            // 总是更新最后一次采集的频率
                            _lastFrequency = frequency;
                            Dispatcher.Invoke(() => tbFrequency.Text = $"{frequency:F0} Hz");

                            if (frequency >= _lowerLimit && frequency <= _upperLimit)
                            {
                                _lastValidFrequency = frequency;   // 更新有效值
                                _validCount++;
                                Dispatcher.Invoke(() =>
                                {
                                    tbStatus.Text = $"检测到有效频率，次数: {_validCount}";
                                    tbValidCount.Text = _validCount.ToString();
                                });

                                if (!_hasStartedWaitTimer)
                                {
                                    _hasStartedWaitTimer = true;
                                    Dispatcher.Invoke(() => StartWaitTimer());
                                }
                            }
                        }
                    }
                    catch (TimeoutException) { }
                    catch (Exception) { }

                    Thread.Sleep(1000);
                }
            }
        }

        private void StartWaitTimer()
        {
            if (_waitTimer != null) return;
            _waitTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _waitTimer.Tick += (s, e) =>
            {
                _waitTimer.Stop();
                if (!_finished)
                {
                    _finished = true;
                    // 判定成功：至少有一次有效频率且已启动等待
                    IsSuccess = (_validCount >= 1);
                    // 决定返回的值
                    if (IsSuccess && _validCount > 0)
                        ValidFrequency = _lastValidFrequency;
                    else
                        ValidFrequency = _lastFrequency;
                    Dispatcher.Invoke(() => Close());
                }
            };
            _waitTimer.Start();
        }

        private void Timeout()
        {
            if (!_finished)
            {
                _finished = true;
                IsSuccess = (_validCount >= 1 && _hasStartedWaitTimer);
                Dispatcher.Invoke(() => Close());
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _cts.Cancel();
            _finished = true;
            IsSuccess = false;
            ValidFrequency = _lastFrequency;   // 取消时返回最后一次采集值
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _cts.Cancel();
            base.OnClosed(e);
        }
    }
}