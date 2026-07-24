using Ivi.Visa;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Xml.Linq;
using System.Xml.Serialization;
using TestPlatform.TestSequences;



namespace TestPlatform
{
    public partial class MainWindow : Window
    {
        private AppSettings appSettings;

        public ObservableCollection<ResultWindowModel> ResultWindows { get; set; } = new ObservableCollection<ResultWindowModel>();

        // 日志颜色常量
        private static readonly Brush LogSuccess = new SolidColorBrush(Color.FromRgb(24, 115, 60));
        private static readonly Brush LogError = new SolidColorBrush(Color.FromRgb(180, 35, 24));
        private static readonly Brush LogWarning = new SolidColorBrush(Color.FromRgb(154, 77, 0));
        private static readonly Brush LogInfo = new SolidColorBrush(Color.FromRgb(23, 32, 51));
        private static readonly Brush StatusSuccess = new SolidColorBrush(Color.FromRgb(24, 115, 60));
        private static readonly Brush StatusError = new SolidColorBrush(Color.FromRgb(180, 35, 24));
        private static readonly Brush StatusInfo = new SolidColorBrush(Color.FromRgb(20, 92, 158));
        private static readonly Brush StatusWarning = new SolidColorBrush(Color.FromRgb(244, 180, 0));
        private readonly ObservableCollection<RuntimeGroupNode> _runtimeGroups =
            new ObservableCollection<RuntimeGroupNode>();
        private bool _loadingRuntimeTree;
        private bool _runtimeTreeRefreshPending;
        private bool _updatingRuntimeGroupSummary;
        private readonly Dictionary<string, bool> _runtimeGroupExpansion =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // 全局计时器
        private System.Windows.Threading.DispatcherTimer globalTimer;
        private DateTime globalStartTime;
        private bool isTimerRunning = false;
        private bool _stopInProgress;
        /// <summary>
        /// led
        /// </summary>
        private LEDConfigSet _ledConfig;
        public MainWindow()
        {
            InitializeComponent();
            InitializeProjectConfigs();
            this.Closing += MainWindow_Closing;
            GlobalState.OnLoginStatusChanged += (s, e) => Dispatcher.Invoke(() => RefreshStatus());

            // 初始化计时器（轻量）
            globalTimer = new DispatcherTimer();
            globalTimer.Interval = TimeSpan.FromMilliseconds(1000);
            globalTimer.Tick += GlobalTimer_Tick;

            // 初始时未登录，禁用管理员按钮
            toolStripButton2.IsEnabled = false;
            toolStripButton3.IsEnabled = false;

            Version ver = Assembly.GetExecutingAssembly().GetName().Version;
            AppendLog($"当前程序版本: {ver}", LogInfo);
        }

        #region 全局计时器
        private void GlobalTimer_Tick(object sender, EventArgs e)
        {
            if (isTimerRunning)
            {
                TimeSpan elapsed = DateTime.Now - globalStartTime;
                Dispatcher.Invoke(() =>
                {
                    txb_testTime.Text = $"{elapsed.Hours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}.{elapsed.Milliseconds:D1}";
                });
            }
        }

        private void StartGlobalTimer()
        {
            if (!isTimerRunning)
            {
                globalStartTime = DateTime.Now;
                isTimerRunning = true;
                globalTimer.Start();
            }
        }

        private void StopGlobalTimer()
        {
            if (isTimerRunning)
            {
                isTimerRunning = false;
                globalTimer.Stop();
                // 可选：最终显示总时间
                TimeSpan total = DateTime.Now - globalStartTime;
                txb_testTime.Text = $"{total.Hours:D2}:{total.Minutes:D2}:{total.Seconds:D2}.{total.Milliseconds:D1}";
                // 所有测试完成，保存日志
                SaveActivityLogToFile();
            }
        }
        #endregion

        #region 配置加载与保存
        private void LoadSettings()
        {
            appSettings = new AppSettings();
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestPlatform.xml");
            if (File.Exists(configPath))
            {
                try
                {
                    var serializer = new XmlSerializer(typeof(AppSettings));
                    using (var reader = new StreamReader(configPath))
                    {
                        appSettings = (AppSettings)serializer.Deserialize(reader);
                    }
                }
                catch { }
            }

            if (appSettings.RememberWindowPos)
                RestoreWindowBoundsToVisibleArea();

            // 恢复上次选中的项目
            if (!string.IsNullOrEmpty(appSettings.CurrentProjectName))
            {
                ProjectSettings.CurrentProjectName = appSettings.CurrentProjectName;
                SetTestFilePathByProjectName(ProjectSettings.CurrentProjectName);
            }
            else
            {
                SetDefaultProject();
            }
            UpdateProjectLabel();
        }

        /// <summary>
        /// 将保存的窗口位置限制在当前所有显示器的可见工作区域内。
        /// 复制到分辨率不同的测试 PC，或移除外接显示器后，窗口也不会跑到屏幕外。
        /// </summary>
        private void RestoreWindowBoundsToVisibleArea()
        {
            double virtualLeft = SystemParameters.VirtualScreenLeft;
            double virtualTop = SystemParameters.VirtualScreenTop;
            double virtualWidth = SystemParameters.VirtualScreenWidth;
            double virtualHeight = SystemParameters.VirtualScreenHeight;

            double savedWidth = double.IsNaN(appSettings.WindowWidth) ? Width : appSettings.WindowWidth;
            double savedHeight = double.IsNaN(appSettings.WindowHeight) ? Height : appSettings.WindowHeight;
            double width = Math.Min(Math.Max(savedWidth, MinWidth), virtualWidth);
            double height = Math.Min(Math.Max(savedHeight, MinHeight), virtualHeight);

            double defaultLeft = virtualLeft + Math.Max(0, (virtualWidth - width) / 2);
            double defaultTop = virtualTop + Math.Max(0, (virtualHeight - height) / 2);
            double savedLeft = double.IsNaN(appSettings.WindowLeft) ? defaultLeft : appSettings.WindowLeft;
            double savedTop = double.IsNaN(appSettings.WindowTop) ? defaultTop : appSettings.WindowTop;

            Left = Math.Min(Math.Max(savedLeft, virtualLeft), virtualLeft + virtualWidth - width);
            Top = Math.Min(Math.Max(savedTop, virtualTop), virtualTop + virtualHeight - height);
            Width = width;
            Height = height;
        }

        private void SaveSettings()
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestPlatform.xml");
            try
            {
                var serializer = new XmlSerializer(typeof(AppSettings));
                using (var writer = new StreamWriter(configPath))
                {
                    serializer.Serialize(writer, appSettings);
                }
            }
            catch { }
        }



        private bool SetTestFilePathByProjectName(string projectName)
        {
            string projectListPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProjectList.xml");
            if (!File.Exists(projectListPath))
            {
                AppendLog($"项目列表文件不存在: {projectListPath}", LogError);
                return false;
            }

            try
            {
                XDocument doc = XDocument.Load(projectListPath);
                var project = doc.Root.Elements("Project")
                    .FirstOrDefault(e => (string)e.Element("DisplayName") == projectName);
                if (project == null)
                {
                    AppendLog($"未找到项目 '{projectName}' 的配置", LogError);
                    return false;
                }

                string storedPath = (string)project.Element("FilePath");
                string absolutePath = null;

                // 1. 如果已经是绝对路径且存在
                if (Path.IsPathRooted(storedPath) && File.Exists(storedPath))
                {
                    absolutePath = storedPath;
                }
                else
                {
                    // 2. 尝试基于程序目录组合为绝对路径
                    string candidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, storedPath);
                    if (File.Exists(candidate))
                    {
                        absolutePath = candidate;
                    }
                    else
                    {
                        // 3. 尝试从 ProjectConfig 目录递归查找同名文件
                        string configDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProjectConfig");
                        if (Directory.Exists(configDir))
                        {
                            var found = Directory.GetFiles(configDir, "*.xml", SearchOption.AllDirectories)
                                                 .FirstOrDefault(f => Path.GetFileName(f).Equals(Path.GetFileName(storedPath), StringComparison.OrdinalIgnoreCase));
                            if (found != null)
                                absolutePath = found;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(absolutePath) && File.Exists(absolutePath))
                {
                    ProjectSettings.TestFikePath = absolutePath;
                    AppendLog($"项目 '{projectName}' 的测试文件路径已设置为: {absolutePath}", LogSuccess);
                    return true;
                }
                else
                {
                    AppendLog($"无法找到项目 '{projectName}' 的有效测试文件，存储路径为: {storedPath}", LogError);
                    return false;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"加载项目配置失败: {ex.Message}", LogError);
                return false;
            }
        }

        private void SetDefaultProject()
        {
            string projectListPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProjectList.xml");
            if (File.Exists(projectListPath))
            {
                try
                {
                    XDocument doc = XDocument.Load(projectListPath);
                    var firstProject = doc.Root.Elements("Project").FirstOrDefault();
                    if (firstProject != null)
                    {
                        string name = (string)firstProject.Element("DisplayName");
                        string path = (string)firstProject.Element("FilePath");
                        if (!string.IsNullOrEmpty(path))
                        {
                            appSettings.CurrentProjectName = name;
                            ProjectSettings.CurrentProjectName = name;
                            ProjectSettings.TestFikePath = ResolveProjectConfigPath(path);
                            AppendLog($"使用默认项目: {name}", LogSuccess);
                            UpdateProjectLabel();
                            return;
                        }
                    }
                }
                catch { }
            }
            AppendLog("没有可用的测试项目，请通过项目配置添加。", LogError);
        }

        #region 自动串口识别
        private bool _comPortsReady = false;
        private static bool hasAttemptedComInit = false; // 是否已尝试过串口识别
        /// <summary>
        /// 确保串口已初始化（首次测试时识别，后续仅检查）
        /// </summary>
        private async Task<bool> EnsureComPortsInitializedAsync(IProgress<string> progress = null)
        {
            var requiredProps = GetRequiredComPortsForCurrentProject();

            if (requiredProps.Count == 0)
            {
                AppendLog($"项目 '{ProjectSettings.CurrentProjectName}' 无需串口，跳过识别", LogInfo);
                _comPortsReady = true;  // 重要：需要设置为 true
                return true;
            }

            // 检查是否所有需要的串口都已配置
            bool allConfigured = true;
            foreach (string propName in requiredProps)
            {
                var prop = typeof(ComName).GetProperty(propName);
                if (prop == null)
                {
                    AppendLog($"ComName 类中不存在属性 '{propName}'，请检查代码", LogError);
                    return false;
                }
                string value = prop.GetValue(null) as string;
                if (string.IsNullOrEmpty(value))
                {
                    allConfigured = false;
                    AppendLog($"串口 {propName} 未配置", LogInfo);
                    break;
                }
            }
            if (allConfigured)
                return true;

            if (hasAttemptedComInit)
            {
                AppendLog("串口未完全配置且已尝试过识别，请检查硬件。", LogError);
                return false;
            }

            hasAttemptedComInit = true;
            AppendLog("首次测试，开始自动识别串口...", LogInfo);
            progress?.Report("开始自动识别串口...");

            if (_projectConfigs.TryGetValue(ProjectSettings.CurrentProjectName, out var config))
            {
                if (config.InitializeComPortsAsync == null)
                {
                    AppendLog($"项目 '{ProjectSettings.CurrentProjectName}' 没有配置串口初始化方法。", LogError);
                    return false;
                }
                bool success = await config.InitializeComPortsAsync(progress);
                if (success)
                {
                    _comPortsReady = true;
                    UpdateTestControlsState();
                }
                else
                {
                    _comPortsReady = false;
                    UpdateTestControlsState();
                }
                return success;
            }
            else
            {
                AppendLog($"未定义项目 '{ProjectSettings.CurrentProjectName}' 的串口识别参数，请手动配置", LogError);
                return false;
            }
        }

        private List<string> GetRequiredComPortsForCurrentProject()
        {
            if (_projectConfigs.TryGetValue(ProjectSettings.CurrentProjectName, out var config))
                return config.RequiredComPorts;
            return new List<string>();
        }
        #endregion 自动串口识别END

        #endregion 配置加载与保存 END

        #region UI 主题与启动项
        private void ApplySettingsToUI()
        {
            if (!string.IsNullOrEmpty(appSettings.AccentColor))
            {
                try
                {
                    var accentBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(appSettings.AccentColor));
                    Background = accentBrush;
                }
                catch { }
            }
            else
            {
                Background = appSettings.LightTheme
                    ? new SolidColorBrush(Colors.WhiteSmoke)
                    : new SolidColorBrush(Colors.DimGray);
            }

            SetAutoStart(appSettings.AutoStart);
        }

        private void SetAutoStart(bool enable)
        {
            const string appName = "TestPlatform";
            string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (enable)
                    key.SetValue(appName, exePath);
                else
                    key.DeleteValue(appName, false);
            }
        }
        private Dictionary<string, int> _onlineLimits = new Dictionary<string, int>();
        private int _currentProjectLimit = -1;   // -1表示无限
        private int _currentProjectCount = 0;
        private readonly object _testCountLock = new object(); // 多通道计数锁
        private async Task SyncOnlineLimitsAsync()
        {
            try
            {
                _onlineLimits = await OnlineConfigHelper.GetTestLimitsAsync();
                AppendLog("在线测试次数配置同步成功", LogSuccess);
            }
            catch (Exception ex)
            {
                AppendLog($"同步测试次数配置失败: {ex.Message}", LogError);
                _onlineLimits = new Dictionary<string, int>(); // 使用空字典作为默认
            }
            // 更新当前项目的限制显示
            UpdateCurrentProjectLimit();
        }
        private void UpdateCurrentProjectLimit()
        {
            string projectName = ProjectSettings.CurrentProjectName;
            if (string.IsNullOrEmpty(projectName))
            {
                AppendLog("当前项目名称为空，无法获取测试限制", LogError);
                _currentProjectLimit = -1;
                _currentProjectCount = 0;
                UpdateRemainingDisplay();
                return;
            }

            // 调试日志
            AppendLog($"当前项目名称: '{projectName}'", LogInfo);
            AppendLog($"在线配置中的键: {string.Join(", ", _onlineLimits.Keys)}", LogInfo);

            if (_onlineLimits.TryGetValue(projectName, out int limit))
            {
                _currentProjectLimit = limit;
                AppendLog($"匹配成功，上限: {limit}", LogSuccess);
            }
            else
            {
                _currentProjectLimit = -1;
                AppendLog($"未找到项目 '{projectName}' 的在线配置，视为无限次", LogWarning);
            }

            _currentProjectCount = TestCountHelper.GetCount(projectName);
            AppendLog($"当前已测试次数: {_currentProjectCount}", LogInfo);
            UpdateRemainingDisplay();
        }
        private void UpdateRemainingDisplay()
        {
            string display;
            if (_currentProjectLimit == -1)
                display = "无限制";
            else
            {
                int remaining = _currentProjectLimit - _currentProjectCount;
                if (remaining < 0) remaining = 0;
                display = $"{remaining} / {_currentProjectLimit}";
            }
            Dispatcher.Invoke(() => txtRemainingTests.Text = $"剩余测试次数: {display}");
        }
        #endregion

        #region 动态窗口与 DataGrid
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 启动主内容动画
            var windowAnimation = FindResource("WindowFadeInZoom") as Storyboard;
            windowAnimation?.Begin(MainContentGrid);

            var progress = new Progress<(int percent, string message)>(update =>
            {
                SplashProgressBar.Value = update.percent;
                SplashPercent.Text = $"{update.percent}%";
                SplashText.Text = update.message;
            });

            // 执行轻量初始化（不包含串口和更新）
            await InitializeApplicationAsync(progress);

            // 后台异步执行串口初始化（不等待）
            _ = Task.Run(async () =>
            {
                await EnsureComPortsInitializedAsync();
                Dispatcher.Invoke(() => AppendLog("串口初始化完成", LogSuccess));
            });

            // 后台异步执行检查更新（不等待）
            _ = CheckForUpdates();

            // 淡出覆盖层
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (s, _) => SplashOverlay.Visibility = Visibility.Collapsed;
            SplashOverlay.BeginAnimation(OpacityProperty, fadeOut);
        }
        private async Task InitializeApplicationAsync(IProgress<(int percent, string message)> progress)
        {
            // 1. 加载设置 (10%)
            progress?.Report((10, "加载系统配置..."));
            await Dispatcher.InvokeAsync(() => LoadSettings());
            // 1. 操作员模式强制开启 MES 和 FTP
            if (appSettings.OperatorMode)
            {
                progress?.Report((10, "操作员模式下，MES 和 FTP 自动开启"));
                appSettings.MESEnabled = true;
                appSettings.FTPEnabled = true;
                SaveSettings(); // 保存强制设定
                AppendLog("操作员模式下，MES 和 FTP 自动开启", LogInfo);
            }
            // 2. 应用UI主题 (20%)
            progress?.Report((20, "应用主题设置..."));
            await Dispatcher.InvokeAsync(() => ApplySettingsToUI());

            // 3. 构建动态窗口和 DataGrid (35%)
            progress?.Report((35, "构建测试界面..."));
            await Dispatcher.InvokeAsync(() =>
            {
                BuildResultWindows(appSettings.ParallelTestCount);
                BuildDataGridColumns(appSettings.ParallelTestCount);
            });

            // 4. 恢复项目并加载测试数据 (50%)
            progress?.Report((50, "加载测试项目数据..."));
            await Task.Run(() =>
            {
                if (!string.IsNullOrEmpty(ProjectSettings.CurrentProjectName))
                    SetTestFilePathByProjectName(ProjectSettings.CurrentProjectName);
                else
                    SetTestFilePathBasedOnProcess("VC Docking Station Board测试");
                Dispatcher.Invoke(() => LoadTestDataFromXML());
            });

            // 5. 刷新状态 (60%)
            progress?.Report((60, "刷新状态..."));
            await Dispatcher.InvokeAsync(() => RefreshStatus());

            // 6. 同步在线测试次数配置 (70%)
            progress?.Report((70, "同步在线测试次数配置..."));
            await SyncOnlineLimitsAsync();

            // 7. 加载LED配置 (75%)
            progress?.Report((75, "加载LED测试参数..."));
            _ledConfig = LEDConfigManager.Load();

            // 8. 串口初始化 (90%)
            progress?.Report((90, "初始化串口..."));
            var comProgress = new Progress<string>(msg =>
            {
                progress?.Report((90, msg));
            });
            await EnsureComPortsInitializedAsync(comProgress);

            // 9. 检查更新 (100%)
            progress?.Report((98, "检查更新..."));
            //await CheckForUpdates();   // 如需启用请取消注释
            progress?.Report((100, "系统就绪\r\nSystem Ready"));
            await Task.Delay(200);
        }
        private void BuildResultWindows(int count)
        {
            ResultWindows.Clear();
            ProjectSettings.Channels.Clear();
            for (int i = 0; i < count; i++)
            {
                var model = new ResultWindowModel { DisplayText = $"通道 {i + 1} 空闲" };
                ResultWindows.Add(model);
                ProjectSettings.Channels.Add(new ChannelContext
                {
                    Index = i,
                    IsBusy = false,
                    CancelToken = new CancellationTokenSource(),
                    ResultModel = model
                });
            }
            icResultWindows.ItemsSource = ResultWindows;
        }

        private void BuildDataGridColumns(int channelCount)
        {
            dataGridView1.Columns.Clear();
            ProjectSettings.testDataTable = new DataTable();
            ProjectSettings.testDataTable.RowChanged += RuntimeTreeDataTable_RowChanged;

            // 固定列
            ProjectSettings.testDataTable.Columns.Add(new DataColumn("Select", typeof(bool)) { Caption = "选择" });
            ProjectSettings.testDataTable.Columns.Add(new DataColumn("StepId", typeof(string)));
            ProjectSettings.testDataTable.Columns.Add(new DataColumn("GroupId", typeof(string)));
            ProjectSettings.testDataTable.Columns.Add(new DataColumn("GroupHeader", typeof(string)));
            ProjectSettings.testDataTable.Columns.Add(new DataColumn("GroupStatus", typeof(string)));
            ProjectSettings.testDataTable.Columns.Add(new DataColumn("GroupSummary", typeof(string)));
            ProjectSettings.testDataTable.Columns.Add(new DataColumn("SequenceOrder", typeof(int)));
            ProjectSettings.testDataTable.Columns.Add(new DataColumn("DefaultEnabled", typeof(bool)));
            ProjectSettings.testDataTable.Columns.Add(new DataColumn("Mandatory", typeof(bool)));
            ProjectSettings.testDataTable.Columns.Add(new DataColumn("AlwaysRun", typeof(bool)));
            ProjectSettings.testDataTable.Columns.Add(new DataColumn("RunCondition", typeof(string)));
            ProjectSettings.testDataTable.Columns.Add(new DataColumn("DependsOn", typeof(string)));
            ProjectSettings.testDataTable.Columns.Add(new DataColumn("TestItem", typeof(string)) { Caption = "测试项" });
            ProjectSettings.testDataTable.Columns.Add(new DataColumn("UpperLimit", typeof(string)) { Caption = "上限" });
            ProjectSettings.testDataTable.Columns.Add(new DataColumn("LowerLimit", typeof(string)) { Caption = "下限" });
            ProjectSettings.testDataTable.Columns.Add(new DataColumn("Unit", typeof(string)) { Caption = "单位" });
            ProjectSettings.testDataTable.Columns.Add(new DataColumn("ExecTime", typeof(string)) { Caption = "执行时间" });

            // 通道列
            for (int i = 1; i <= channelCount; i++)
            {
                ProjectSettings.testDataTable.Columns.Add(new DataColumn($"Channel{i}Value", typeof(string)) { Caption = $"通道 {i} 测试值" });
                ProjectSettings.testDataTable.Columns.Add(new DataColumn($"Channel{i}Result", typeof(string)) { Caption = $"通道 {i} 结果" });
            }

            ICollectionView groupedView = CollectionViewSource.GetDefaultView(
                ProjectSettings.testDataTable.DefaultView);
            groupedView.GroupDescriptions.Clear();
            groupedView.GroupDescriptions.Add(new PropertyGroupDescription("GroupHeader"));
            dataGridView1.ItemsSource = groupedView;

            // 构建 DataGrid 列
            dataGridView1.Columns.Add(new DataGridCheckBoxColumn { Header = "选择", Binding = new System.Windows.Data.Binding("Select"), Width = 50 });
            AddTextColumn("TestItem", "测试项", 150);
            AddTextColumn("UpperLimit", "上限", 100);
            AddTextColumn("LowerLimit", "下限", 100);
            AddTextColumn("Unit", "单位", 80);
            AddTextColumn("ExecTime", "执行时间（ms）", 100);

            for (int i = 1; i <= channelCount; i++)
            {
                // 测试值列
                AddTextColumn($"Channel{i}Value", $"通道 {i} 测试值", 120);

                // 结果列 - 使用 DataGridTextColumn 并设置 ElementStyle
                DataGridTextColumn resultColumn = new DataGridTextColumn();
                resultColumn.Header = $"通道 {i} 结果";
                resultColumn.Binding = new System.Windows.Data.Binding($"Channel{i}Result");
                resultColumn.Width = 80;

                // 创建样式，作用于列内每个 TextBlock
                Style textStyle = new Style(typeof(TextBlock));
                textStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Bold));
                textStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
                textStyle.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(5, 2, 5, 2)));
                DataTrigger passTrigger = new DataTrigger();
                passTrigger.Binding = new System.Windows.Data.Binding($"Channel{i}Result");
                passTrigger.Value = "PASS";
                passTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, StatusSuccess));
                passTrigger.Setters.Add(new Setter(TextBlock.BackgroundProperty, new SolidColorBrush(Color.FromRgb(223, 246, 232))));

                DataTrigger failTrigger = new DataTrigger();
                failTrigger.Binding = new System.Windows.Data.Binding($"Channel{i}Result");
                failTrigger.Value = "FAIL";
                failTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.White));
                failTrigger.Setters.Add(new Setter(TextBlock.BackgroundProperty, StatusError));

                DataTrigger runningTrigger = new DataTrigger();
                runningTrigger.Binding = new System.Windows.Data.Binding($"Channel{i}Result");
                runningTrigger.Value = "执行中";
                runningTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.White));
                runningTrigger.Setters.Add(new Setter(TextBlock.BackgroundProperty, StatusInfo));

                DataTrigger retryTrigger = new DataTrigger();
                retryTrigger.Binding = new System.Windows.Data.Binding($"Channel{i}Result");
                retryTrigger.Value = "重试中";
                retryTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.Black));
                retryTrigger.Setters.Add(new Setter(TextBlock.BackgroundProperty, StatusWarning));

                DataTrigger retryPassedTrigger = new DataTrigger();
                retryPassedTrigger.Binding = new System.Windows.Data.Binding($"Channel{i}Result");
                retryPassedTrigger.Value = "重试通过";
                retryPassedTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(24, 89, 52))));
                retryPassedTrigger.Setters.Add(new Setter(TextBlock.BackgroundProperty, new SolidColorBrush(Color.FromRgb(255, 235, 160))));

                DataTrigger canceledTrigger = new DataTrigger();
                canceledTrigger.Binding = new System.Windows.Data.Binding($"Channel{i}Result");
                canceledTrigger.Value = "已取消";
                canceledTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.White));
                canceledTrigger.Setters.Add(new Setter(TextBlock.BackgroundProperty, Brushes.DimGray));

                DataTrigger cleanupTrigger = new DataTrigger();
                cleanupTrigger.Binding = new System.Windows.Data.Binding($"Channel{i}Result");
                cleanupTrigger.Value = "收尾中";
                cleanupTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.White));
                cleanupTrigger.Setters.Add(new Setter(TextBlock.BackgroundProperty, new SolidColorBrush(Color.FromRgb(91, 67, 153))));

                DataTrigger cleanupFailTrigger = new DataTrigger();
                cleanupFailTrigger.Binding = new System.Windows.Data.Binding($"Channel{i}Result");
                cleanupFailTrigger.Value = "收尾失败";
                cleanupFailTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.White));
                cleanupFailTrigger.Setters.Add(new Setter(TextBlock.BackgroundProperty, StatusError));

                textStyle.Triggers.Add(passTrigger);
                textStyle.Triggers.Add(failTrigger);
                textStyle.Triggers.Add(runningTrigger);
                textStyle.Triggers.Add(retryTrigger);
                textStyle.Triggers.Add(retryPassedTrigger);
                textStyle.Triggers.Add(canceledTrigger);
                textStyle.Triggers.Add(cleanupTrigger);
                textStyle.Triggers.Add(cleanupFailTrigger);

                resultColumn.ElementStyle = textStyle;
                dataGridView1.Columns.Add(resultColumn);
            }
        }

        private void AddTextColumn(string bindingPath, string header, double width)
        {
            dataGridView1.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new System.Windows.Data.Binding(bindingPath),
                Width = width
            });
        }
        private void dataGridView1_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString();
        }
        private ScrollViewer GetDataGridScrollViewer()
        {
            if (dataGridView1 == null) return null;
            // 遍历视觉树查找 ScrollViewer
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(dataGridView1); i++)
            {
                var child = VisualTreeHelper.GetChild(dataGridView1, i);
                if (child is ScrollViewer sv) return sv;
                var result = GetScrollViewerFromChild(child);
                if (result != null) return result;
            }
            return null;
        }

        private ScrollViewer GetScrollViewerFromChild(DependencyObject obj)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                if (child is ScrollViewer sv) return sv;
                var result = GetScrollViewerFromChild(child);
                if (result != null) return result;
            }
            return null;
        }

        /// <summary>
        /// 平滑滚动 DataGrid 到指定行，使其尽可能居中显示
        /// </summary>
        /// <param name="rowIndex">目标行索引（0-based）</param>
        /// <param name="durationMs">动画持续时间（毫秒），0 表示瞬间跳转</param>
        private async Task SmoothScrollToRow(int rowIndex, int durationMs = 200)
        {
            if (dataGridView1 == null || rowIndex < 0 || rowIndex >= dataGridView1.Items.Count) return;

            // 强制滚动到目标行，确保行容器生成
            dataGridView1.ScrollIntoView(dataGridView1.Items[rowIndex]);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            dataGridView1.UpdateLayout();

            // 获取目标行的容器（现在应该已生成）
            var targetRow = dataGridView1.ItemContainerGenerator.ContainerFromIndex(rowIndex) as DataGridRow;
            if (targetRow == null) return;

            // 获取 ScrollViewer
            var scrollViewer = GetDataGridScrollViewer();
            if (scrollViewer == null) return;

            // 计算目标行相对于 DataGrid 的 Y 坐标
            var transform = targetRow.TransformToAncestor(dataGridView1);
            double rowTop = transform.Transform(new Point(0, 0)).Y;
            double viewportHeight = scrollViewer.ViewportHeight;
            double targetOffset = rowTop - (viewportHeight / 2) + (targetRow.ActualHeight / 2);
            targetOffset = Math.Max(0, Math.Min(targetOffset, scrollViewer.ExtentHeight - viewportHeight));

            if (durationMs <= 0)
            {
                scrollViewer.ScrollToVerticalOffset(targetOffset);
            }
            else
            {
                // 平滑动画
                double startOffset = scrollViewer.VerticalOffset;
                var animation = new DoubleAnimation(startOffset, targetOffset, TimeSpan.FromMilliseconds(durationMs))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                scrollViewer.BeginAnimation(ScrollViewer.VerticalOffsetProperty, animation);
                await Task.Delay(durationMs);
            }
        }
        private Task ScrollToTestRowAsync(int rowIndex, int durationMs = 150)
        {
            if (rowIndex < 0 || dataGridView1 == null)
                return Task.CompletedTask;

            return Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    DataTable table = ProjectSettings.testDataTable;
                    if (table == null || rowIndex < 0 || rowIndex >= table.Rows.Count)
                        return;

                    DataRow targetDataRow = table.Rows[rowIndex];
                    EnsureRuntimeGroupExpanded(targetDataRow["GroupHeader"]?.ToString());
                    if (rowIndex == _lastScrolledRow)
                        return;

                    _lastScrolledRow = rowIndex;

                    DataRowView item = table.DefaultView
                        .Cast<DataRowView>()
                        .FirstOrDefault(x => ReferenceEquals(x.Row, targetDataRow));
                    if (item == null)
                        return;

                    dataGridView1.ScrollIntoView(item);
                    dataGridView1.UpdateLayout();

                    var row = dataGridView1.ItemContainerGenerator
                        .ContainerFromItem(item) as DataGridRow;
                    if (row != null)
                    {
                        row.BringIntoView();
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"DataGrid滚动到第 {rowIndex + 1} 行失败：{ex.Message}", LogWarning);
                }
            }).Task;
        }
        #endregion

        #region 测试流程（核心）
        private List<ChannelContext> _pendingChannels = new List<ChannelContext>();
        private readonly object _pendingLock = new object();


        /// <summary>
        /// 处理 SN 扫描（供 Enter 键和测试按钮调用）
        /// </summary>
        private async Task ProcessSNAsync(string sn)
        {
            try
            {
                // 测试次数限制检查
                if (_currentProjectLimit != -1 && _currentProjectCount >= _currentProjectLimit)
                {
                    AppendLog($"当前项目已达测试上限 {_currentProjectLimit} 次", LogError);
                    MessageBox.Show($"该项目最多允许测试 {_currentProjectLimit} 次，已达到上限。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // SN 空值处理
                if (string.IsNullOrEmpty(sn))
                {
                    if (appSettings.AllowEmptySN)
                    {
                        sn = "EMPTY_SN";
                        AppendLog("条码为空，已自动填充占位符", LogInfo);
                    }
                    else
                    {
                        AppendLog("SN 不能为空！", LogError);
                        return;
                    }
                }

                // 串口就绪检查
                bool comReady = await EnsureComPortsInitializedAsync();
                if (!comReady)
                {
                    AppendLog("串口未就绪，无法开始测试", LogError);
                    return;
                }

                // 自动转换为大写
                if (appSettings.AutoUpper)
                    sn = sn.ToUpper();

                // 前缀校验（保持不变）
                if (appSettings.EnforcePrefix && !string.IsNullOrEmpty(appSettings.SNPrefix))
                {
                    string prefix = appSettings.AutoUpper ? appSettings.SNPrefix.ToUpper() : appSettings.SNPrefix;
                    if (!sn.StartsWith(prefix))
                    {
                        AppendLog($"SN 必须以 \"{prefix}\" 开头，实际: {sn}", LogError);
                        return;
                    }
                }

                // 长度校验
                if (appSettings.EnforceLength && appSettings.SNLength > 0 && sn.Length != appSettings.SNLength)
                {
                    AppendLog($"SN 长度应为 {appSettings.SNLength} 位，实际 {sn.Length} 位，请重新扫描！", LogError);
                    return;
                }

                // 重复检查（如果该SN已经在当前测试批次中被分配到某个通道，则拒绝）
                if (ProjectSettings.IsSNUsed(sn))
                {
                    AppendLog($"SN {sn} 已扫描过，请勿重复！", LogError);
                    MessageBox.Show($"SN :{sn} 已扫描过，请勿重复！\r\nSN :{sn} has already been scanned; please do not repeat!", "提示/Tip");
                    return;
                }

                // 寻找空闲通道
                ChannelContext freeChannel = ProjectSettings.Channels.FirstOrDefault(c => !c.IsBusy);
                if (freeChannel == null)
                {
                    AppendLog("无空闲测试通道，请等待当前测试完成", LogError);
                    return;
                }

                // ===== MES 过站检查（如果启用） =====

                if (appSettings.MESEnabled)
                {
                    AppendLog($"[通道{freeChannel.Index + 1}] 正在进行 MES 过站检查...", LogInfo);
                    // 注意：检查URL中的SN就是当前sn
                    string checkUrl = $"{GetMESBaseUrl()}/checkRoute?pcbSeq={sn}&prodNo={appSettings.WorkOrder}&stationNo={appSettings.WorkStation}&retest=false";
                    string checkResponse = await MESHelper.PostDataAsync(checkUrl, "", Encoding.UTF8,
                        msg => AppendLog($"[通道{freeChannel.Index + 1}] {msg}"));

                    if (!MESHelper.ParseMESResponse(checkResponse, out string checkMsg))
                    {
                        AppendLog($"[通道{freeChannel.Index + 1}] 过站检查失败: {checkMsg}", LogError);
                        MessageBox.Show($"条码过站检查失败：{checkMsg}", "提示/Tip", MessageBoxButton.OK, MessageBoxImage.Warning);
                        // 检查失败，不分配通道，直接返回，允许重新扫描该SN
                        return;
                    }
                    AppendLog($"[通道{freeChannel.Index + 1}] 过站检查成功", LogSuccess);
                    // 标记该通道已过站检查成功
                    freeChannel.IsMesChecked = true;
                }

                // ===== 分配通道 =====
                freeChannel.CancelToken?.Dispose();
                freeChannel.CancelToken = new CancellationTokenSource();
                freeChannel.IsBusy = true;
                freeChannel.CurrentSN = sn;
                ProjectSettings.AddSN(sn);
                UpdateTestControlsState();
                ResetUnassignedIdleChannels();
                ResetChannelTestData(freeChannel.Index);

                freeChannel.ResultModel.Background = Brushes.Yellow;
                freeChannel.ResultModel.DisplayText = $"通道 {freeChannel.Index + 1} 测试中...\nSN: {sn}";
                AppendLog($"SN {sn} 已分配到通道 {freeChannel.Index + 1}，等待测试...", LogSuccess);

                // ===== 等待所有并行通道分配完成 =====
                int totalChannels = appSettings.ParallelTestCount;
                int busyCount = ProjectSettings.Channels.Count(c => c.IsBusy);

                if (busyCount == 1)
                {
                    StartGlobalTimer();
                    UpdateTestControlsState();
                }

                if (busyCount == totalChannels)
                {
                    AppendLog($"所有 {totalChannels} 个通道已分配 SN，开始并行测试", LogInfo);
                    var tasks = new List<Task>();
                    foreach (var ch in ProjectSettings.Channels)
                    {
                        tasks.Add(RunChannelTestAsync(ch, ch.CurrentSN));
                    }
                    await Task.WhenAll(tasks);
                }
                else
                {
                    AppendLog($"已分配 {busyCount}/{totalChannels} 个通道，等待更多 SN...", LogInfo);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"处理SN时发生异常: {ex.Message}", LogError);
            }
            finally
            {
                Dispatcher.Invoke(() =>
                {
                    txb_snInput.Clear();
                    txb_snInput.Focus();
                });
            }
        }
        private void ResetUnassignedIdleChannels()
        {
            Dispatcher.Invoke(() =>
            {
                foreach (var ch in ProjectSettings.Channels)
                {
                    if (ch == null || ch.ResultModel == null)
                        continue;

                    // 只重置还没有分配SN、没有正在测试的通道。
                    // 已经 IsBusy 的通道不能重置，否则会覆盖“测试中”。
                    if (!ch.IsBusy && string.IsNullOrWhiteSpace(ch.CurrentSN))
                    {
                        ch.ResultModel.DisplayText = $"通道 {ch.Index + 1}\n空闲";
                        ch.ResultModel.Background = Brushes.White;
                        ch.ResultModel.DisplayForeground = Brushes.Black;
                        ch.IsMesChecked = false;
                    }
                }
            });
        }
        private async Task RunChannelTestAsync(ChannelContext channel, string sn)
        {
            bool testResult = false;
            bool finalResult = false;
            bool reportUploadSuccess = true;
            DateTime testStartTime = DateTime.Now;
            DateTime testEndTime = DateTime.Now;

            try
            {
                if (channel == null)
                    return;

                if (string.IsNullOrWhiteSpace(sn))
                {
                    AppendLog($"[通道{channel.Index + 1}] SN为空，取消测试", LogError);
                    SetChannelFinalUI(channel, sn, false, "SN为空");
                    return;
                }

                // 1. MES 过站前检查 checkRoute
                if (appSettings.MESEnabled)
                {
                    bool checkOk = await CheckMESBeforeTestAsync(channel, sn);
                    if (!checkOk)
                    {
                        testResult = false;
                        finalResult = false;
                        return;
                    }
                }
                else
                {
                    AppendLog($"[通道{channel.Index + 1}] MES未启用，跳过过站前检查", LogInfo);
                    channel.ResultModel.DisplayText = $"通道 {channel.Index + 1}\nSN: {sn}\n正在测试...";
                    channel.ResultModel.Background = Brushes.Yellow;
                    channel.ResultModel.DisplayForeground = Brushes.Black;
                }

                // 2. 执行本地测试 RunLocalTestAsync
                testStartTime = DateTime.Now;
                testResult = await RunLocalTestAsync(channel, sn);
                testEndTime = DateTime.Now;
                finalResult = testResult;

                // 3. 保存 CSV 并上传 SMB / FTP
                reportUploadSuccess = await AppendToCsvReport(
                    channel.Index,
                    sn,
                    testResult,
                    testStartTime,
                    testEndTime);

                // 4. 如果上传失败，根据规则修正 testResult
                // 当前规则：上传失败则最终结果标记为失败。
                if (!reportUploadSuccess)
                {
                    AppendLog($"[通道{channel.Index + 1}] 报告保存或上传失败，最终结果修正为 FAIL", LogError);
                    finalResult = false;
                }

                // 5. MES createRoute 上传最终结果
                if (appSettings.MESEnabled)
                {
                    bool mesUploadOk = await UploadFinalResultToMESAsync(channel, sn, finalResult);
                    if (!mesUploadOk)
                    {
                        finalResult = false;
                        SetChannelFinalUI(channel, sn, false, "MES上报失败");
                        return;
                    }

                    // 6. 如果 PASS，验证是否已过站
                    if (finalResult)
                    {
                        bool routeVerified = await VerifyRouteAfterUpload(channel, sn);
                        if (!routeVerified)
                        {
                            AppendLog($"[通道{channel.Index + 1}] MES过站验证失败，最终结果修正为 FAIL", LogError);
                            finalResult = false;
                        }
                    }
                }

                // 7. 更新通道 UI
                if (!reportUploadSuccess)
                {
                    SetChannelFinalUI(channel, sn, false, "上传失败");
                }
                else
                {
                    SetChannelFinalUI(channel, sn, finalResult, finalResult ? "PASS" : "FAIL");
                }
            }
            catch (OperationCanceledException)
            {
                AppendLog($"[通道{channel.Index + 1}] 测试已取消", LogWarning);
                finalResult = false;
                SetChannelFinalUI(channel, sn, false, "已取消");
            }
            catch (Exception ex)
            {
                AppendLog($"[通道{channel.Index + 1}] 测试异常: {ex.Message}", LogError);
                finalResult = false;
                SetChannelFinalUI(channel, sn, false, $"异常: {ex.Message}");
            }
            finally
            {
                // 8. ReleaseChannel
                await ReleaseChannel(channel, sn);

                // 只有最终通过才增加测试计数
                if (finalResult)
                {
                    lock (_testCountLock)
                    {
                        TestCountHelper.IncrementCount(ProjectSettings.CurrentProjectName);
                        _currentProjectCount++;
                        UpdateRemainingDisplay();
                    }
                }
            }
        }
        private void SetChannelFinalUI(ChannelContext channel, string sn, bool pass, string statusText)
        {
            Dispatcher.Invoke(() =>
            {
                if (channel == null || channel.ResultModel == null)
                    return;

                channel.ResultModel.DisplayText =
                    $"通道 {channel.Index + 1}\nSN: {sn}\n{statusText}";

                channel.ResultModel.Background = pass ? Brushes.LimeGreen : Brushes.Red;
                channel.ResultModel.DisplayForeground = pass ? Brushes.Black : Brushes.White;
            });
        }

        private void UpdateChannelStepResultDisplay(int channelIndex, int rowIndex, string value, bool pass, string itemName)
        {
            Dispatcher.Invoke(() =>
            {
                if (ProjectSettings.Channels == null || channelIndex < 0 || channelIndex >= ProjectSettings.Channels.Count)
                    return;

                ChannelContext channel = ProjectSettings.Channels[channelIndex];
                if (channel == null || channel.ResultModel == null)
                    return;

                string sn = string.IsNullOrWhiteSpace(channel.CurrentSN) ? "-" : channel.CurrentSN;
                string status = pass ? "PASS" : "FAIL";
                string stepName = string.IsNullOrWhiteSpace(itemName) ? $"Row {rowIndex + 1}" : itemName;
                string displayValue = string.IsNullOrWhiteSpace(value) ? "-" : value;

                channel.ResultModel.DisplayText =
                    $"通道 {channel.Index + 1}\nSN: {sn}\n{status}: {stepName}\n值: {displayValue}";

                channel.ResultModel.Background = pass ? Brushes.Yellow : Brushes.Red;
                channel.ResultModel.DisplayForeground = pass ? Brushes.Black : Brushes.White;
            });
        }
        private async Task<bool> UploadFinalResultToMESAsync(ChannelContext channel, string sn, bool finalResult)
        {
            await MesUploadLock.WaitAsync();
            try
            {
                string result = finalResult ? "PASS" : "FAIL";
                string testData = MESHelper.BuildChannelTestData(channel.Index);

                AppendLog($"[通道{channel.Index + 1}] 开始MES最终结果上报，SN={sn}，Result={result}", LogInfo);

                string createUrl =
                    $"{GetMESBaseUrl()}/createRoute?pcbSeq={sn}&prodNo={appSettings.WorkOrder}&stationNo={appSettings.WorkStation}&result={result}&remark={{}}{{}}{{}}&testItem={testData}&userNo=&weight=00&packNo=&rmk1=&rmk2=&rmk3=&rmk4=";

                string createResponse = await MESHelper.PostDataAsync(
                    createUrl,
                    "",
                    Encoding.UTF8,
                    msg => AppendLog($"[通道{channel.Index + 1}] {msg}"));

                bool uploadSuccess = MESHelper.ParseMESResponse(createResponse, out string uploadMsg);
                if (!uploadSuccess)
                {
                    AppendLog($"[通道{channel.Index + 1}] MES最终结果上报失败: {uploadMsg}", LogError);
                    return false;
                }

                AppendLog($"[通道{channel.Index + 1}] MES最终结果上报成功，SN={sn}，Result={result}", LogSuccess);
                return true;
            }
            catch (Exception ex)
            {
                AppendLog($"[通道{channel.Index + 1}] MES最终结果上报异常: {ex.Message}", LogError);
                return false;
            }
            finally
            {
                MesUploadLock.Release();
            }
        }



        private async Task<bool> CheckMESBeforeTestAsync(ChannelContext channel, string sn)
        {
            try
            {
                // 分配SN时如果已经检查过，这里可以跳过，避免重复checkRoute。
                if (channel.IsMesChecked)
                {
                    AppendLog($"[通道{channel.Index + 1}] 过站前检查已通过，跳过重复检查", LogInfo);
                    channel.ResultModel.DisplayText = $"通道 {channel.Index + 1}\nSN: {sn}\n过站成功，正在测试...";
                    channel.ResultModel.Background = Brushes.Yellow;
                    channel.ResultModel.DisplayForeground = Brushes.Black;
                    return true;
                }

                AppendLog($"[通道{channel.Index + 1}] 正在进行 MES 过站前检查，SN={sn}", LogInfo);

                string checkUrl =
                    $"{GetMESBaseUrl()}/checkRoute?pcbSeq={sn}&prodNo={appSettings.WorkOrder}&stationNo={appSettings.WorkStation}&retest=false";

                string checkResponse = await MESHelper.PostDataAsync(
                    checkUrl,
                    "",
                    Encoding.UTF8,
                    msg => AppendLog($"[通道{channel.Index + 1}] {msg}"));

                if (!MESHelper.ParseMESResponse(checkResponse, out string checkMsg))
                {
                    AppendLog($"[通道{channel.Index + 1}] MES过站前检查失败: {checkMsg}", LogError);
                    channel.ResultModel.DisplayText = $"通道 {channel.Index + 1}\nSN: {sn}\n过站失败\n{checkMsg}";
                    channel.ResultModel.Background = Brushes.Red;
                    channel.ResultModel.DisplayForeground = Brushes.White;
                    return false;
                }

                channel.IsMesChecked = true;
                channel.ResultModel.DisplayText = $"通道 {channel.Index + 1}\nSN: {sn}\n过站成功，正在测试...";
                channel.ResultModel.Background = Brushes.Yellow;
                channel.ResultModel.DisplayForeground = Brushes.Black;
                AppendLog($"[通道{channel.Index + 1}] MES过站前检查成功", LogSuccess);
                return true;
            }
            catch (Exception ex)
            {
                AppendLog($"[通道{channel.Index + 1}] MES过站前检查异常: {ex.Message}", LogError);
                channel.ResultModel.DisplayText = $"通道 {channel.Index + 1}\nSN: {sn}\nMES检查异常";
                channel.ResultModel.Background = Brushes.Red;
                channel.ResultModel.DisplayForeground = Brushes.White;
                return false;
            }
        }
        private async Task<bool> RunLocalTestAsync(ChannelContext channel, string sn)
        {
            int channelIdx = channel.Index;
            AppendLog($"[通道{channelIdx + 1}] 开始本地测试...");

            bool testResult = false;
            try
            {
                channelIdx = channel.Index;
                if (_projectConfigs.TryGetValue(ProjectSettings.CurrentProjectName, out var config))
                {
                    if (config.RunTestSequenceAsync == null)
                    {
                        AppendLog(
                            $"项目 {ProjectSettings.CurrentProjectName} 没有绑定测试流程，请检查 ProjectList.xml 中的 SequenceKey。",
                            LogError
                        );

                        return false;
                    }

                    return await config.RunTestSequenceAsync(
                        channelIdx,
                        sn,
                        channel.CancelToken.Token
                    );
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[通道{channelIdx + 1}] 测试序列执行异常: {ex.Message}", LogError);
                testResult = false;
            }
            return testResult;
        }

        private async Task ReleaseChannel(ChannelContext channel, string sn)
        {
            if (channel == null)
                return;

            channel.IsBusy = false;
            channel.CurrentSN = null;
            channel.IsMesChecked = false;

            if (!string.IsNullOrWhiteSpace(sn))
                ProjectSettings.RemoveSN(sn);

            // 不要在这里把结果窗口改成空闲，否则会覆盖 PASS/FAIL 最终结果。
            // 空闲显示由 ResetUnassignedIdleChannels 在新一轮分配SN时处理。

            if (!ProjectSettings.Channels.Any(c => c.IsBusy))
            {
                _stopInProgress = false;
                StopGlobalTimer();
            }

            UpdateTestControlsState();
            await Task.CompletedTask;
        }

        private static readonly SemaphoreSlim MesUploadLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// 上报结果后验证产品是否成功过站
        /// </summary>
        private async Task<bool> VerifyRouteAfterUpload(ChannelContext channel, string sn, int maxRetries = 3)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                await Task.Delay(2000); // 等待 MES 处理

                string checkUrl = $"{GetMESBaseUrl()}/checkRoute?pcbSeq={sn}&prodNo={appSettings.WorkOrder}&stationNo={appSettings.WorkStation}&retest=false";
                string checkResponse = await MESHelper.PostDataAsync(checkUrl, "", Encoding.UTF8,
                    msg => AppendLog($"[通道{channel.Index + 1}] 验证过站: {msg}"));

                bool isStillInStation = ValidateMESResponse(checkResponse, out string verifyMsg);
                if (!isStillInStation)
                {
                    AppendLog($"[通道{channel.Index + 1}] 过站验证成功（产品已过站）", LogSuccess);
                    return true;
                }
                else
                {
                    AppendLog($"[通道{channel.Index + 1}] 第{i + 1}次过站验证失败，产品仍在工站，将重试... 返回信息: {verifyMsg}", LogWarning);
                }
            }
            AppendLog($"[通道{channel.Index + 1}] 过站验证失败，超过最大重试次数", LogError);
            return false;
        }

        private string GetMESBaseUrl()
        {
            string baseUrl = $"http://{appSettings.MESIP}";
            // 如果端口号大于 0 且不是默认的 80，则添加端口号；否则使用默认 80（不显式写端口）
            if (appSettings.MESPort > 0 && appSettings.MESPort != 80)
            {
                baseUrl += $":{appSettings.MESPort}";
            }
            baseUrl += appSettings.MESPath;
            return baseUrl;
        }


        /*
        maxRetries = -1  使用系统设置 appSettings.FailRetryCount
        maxRetries = 0   当前步骤不重试
        maxRetries = 1   当前步骤失败后重试 1 次
        maxRetries = 2   当前步骤失败后重试 2 次
        */
        private int _lastScrolledRow = -1;
        /// <summary>
        /// 执行测试步骤（支持重试），并更新进度
        /// </summary>
        /// <param name="channelIndex">通道索引（0-based）</param>
        /// <param name="stepAction">实际测试动作（异步委托，返回 bool）</param>
        /// <param name="stepName">步骤名称</param>
        /// <param name="rowIndex">对应 DataGrid 中的行索引</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <param name="currentStep">当前步骤序号（从1开始）</param>
        /// <param name="totalSteps">总步骤数</param>
        /// <param name="maxRetries">最大重试次数</param>
        private async Task<bool> ExecuteTestStepAsync(int channelIndex, Func<CancellationToken, Task<bool>> stepAction, string stepName, int rowIndex, CancellationToken cancellationToken, int currentStep, int totalSteps, int maxRetries = -1)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            bool finalResult = false;
            // 滚动到当前行（仅在行索引变化时）
            if (rowIndex != _lastScrolledRow)
            {
                _lastScrolledRow = rowIndex;

                try
                {
                    await ScrollToTestRowAsync(rowIndex, 150);
                }
                catch
                {
                    // DataGrid滚动失败不能影响测试结果
                }
            }

            /*
        -1 = 跟随系统设置 appSettings.FailRetryCount
 0 = 不重试，只执行 1 次
 1 = 失败后重试 1 次，总共最多执行 2 次
 2 = 失败后重试 2 次，总共最多执行 3 次
 3 = 失败后重试 3 次，总共最多执行 4 次
        */
            // 以配置的自动重试次数为准，配置为0则绝不重试
            int effectiveRetries = maxRetries >= 0 ? maxRetries : Math.Max(0, appSettings.FailRetryCount);

            for (int attempt = 0; attempt <= effectiveRetries; attempt++)
            {
                if (attempt > 0)
                    AppendLog($"[通道{channelIndex + 1}] 步骤 '{stepName}' 第 {attempt} 次自动重试...", LogWarning);

                try
                {
                    finalResult = await stepAction(cancellationToken);
                    if (finalResult)
                        break;
                }
                catch (OperationCanceledException)
                {
                    AppendLog($"[通道{channelIndex + 1}] 步骤 '{stepName}' 已取消", LogError);
                    finalResult = false;
                    break;
                }
                catch (Exception ex)
                {
                    AppendLog($"[通道{channelIndex + 1}] 步骤 '{stepName}' 异常: {ex.Message}", LogError);
                    finalResult = false;
                }

                if (!finalResult && attempt < effectiveRetries)
                {
                    // 自动重试，无需提示
                    continue;
                }
            }

            stopwatch.Stop();
            long elapsedMs = stopwatch.ElapsedMilliseconds;
            AppendLog($"[通道{channelIndex + 1}] 更新行 {rowIndex} 的执行时间 {elapsedMs} ms", LogInfo);
            // 更新执行时间到 DataTable（无论成功或失败）
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (rowIndex >= 0 && rowIndex < ProjectSettings.testDataTable.Rows.Count)
                    {
                        ProjectSettings.testDataTable.Rows[rowIndex]["ExecTime"] = elapsedMs.ToString();
                    }
                    else
                    {
                        AppendLog($"[通道{channelIndex + 1}] 更新执行时间失败: rowIndex {rowIndex} 无效", LogError);
                    }
                });
            }
            catch (Exception ex)
            {
                AppendLog($"[通道{channelIndex + 1}] 更新执行时间异常: {ex.Message}", LogError);
            }

            // 更新测试进度（UI）
            await Dispatcher.InvokeAsync(() =>
            {
                int percent = (int)((currentStep * 100.0) / totalSteps);
                progressBar1.Value = percent;
                lblPercent.Text = $"{percent}%";
            });

            if (finalResult)
                AppendLog($"[通道{channelIndex + 1}] 步骤 '{stepName}' 通过 (耗时 {elapsedMs} ms)", LogSuccess);
            else
                AppendLog($"[通道{channelIndex + 1}] 步骤 '{stepName}' 失败 (耗时 {elapsedMs} ms)", LogError);

            return finalResult;
        }
        #endregion
        #region 报告保存（CSV格式）
        private static readonly object csvLock = new object(); // 多线程写文件锁

        private async Task<bool> AppendToCsvReport(int channelIndex, string sn, bool testResult, DateTime testStartTime, DateTime testEndTime)
        {
            if (!appSettings.AutoSaveResult)
                return true;

            // 报告保存模式统一入口：
            // 1. AppendCsv：保持当前按天累加 CSV 的保存方式。
            // 2. SingleExcel：每个 SN 单独保存一个 Excel，然后复用同一套 FTP / SMB 上传方法。
            if (appSettings.ReportSaveMode == ReportSaveMode.SingleExcel)
            {
                return await SaveSingleExcelReportAndUploadAsync(
                    channelIndex,
                    sn,
                    testResult,
                    testStartTime,
                    testEndTime);
            }

            try
            {
                string projectName = ProjectSettings.CurrentProjectName;
                if (string.IsNullOrEmpty(projectName))
                    projectName = "UnknownProject";

                // 移除项目名称中的非法文件名字符（防止路径错误）
                string safeProjectName = string.Join("_", projectName.Split(Path.GetInvalidFileNameChars()));

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string resultFolder = testResult ? "PASS" : "NG";
                // 新路径：Reports\项目名称\ChannelX\结果\
                string channelDir = Path.Combine(baseDir, "Reports", safeProjectName, $"Channel{channelIndex + 1}", resultFolder);
                Directory.CreateDirectory(channelDir);

                string today = DateTime.Now.ToString("yyyyMMdd");
                string reportFile = Path.Combine(channelDir, $"TestReport_{today}.csv");
                bool fileExists = File.Exists(reportFile);

                DataTable dt = ProjectSettings.testDataTable;
                if (dt == null) return false;

                var rows = dt.AsEnumerable().ToList();
                int testItemCount = rows.Count;

                // 固定信息列标题
                List<string> infoHeaders = new List<string> { "时间戳", "SN", "通道", "整体结果", "测试耗时(ms)" };

                // 收集测试项名称、上限、下限
                List<string> testItemNames = new List<string>();
                List<string> upperLimits = new List<string>();
                List<string> lowerLimits = new List<string>();
                foreach (DataRow row in rows)
                {
                    testItemNames.Add(row["TestItem"]?.ToString() ?? "");
                    upperLimits.Add(row["UpperLimit"]?.ToString() ?? "");
                    lowerLimits.Add(row["LowerLimit"]?.ToString() ?? "");
                }

                lock (csvLock)
                {
                    using (var writer = new StreamWriter(reportFile, append: true, encoding: Encoding.UTF8))
                    {
                        if (!fileExists)
                        {
                            // 第一行：标题
                            var firstRow = new List<string>();
                            firstRow.AddRange(infoHeaders);
                            foreach (var name in testItemNames)
                            {
                                firstRow.Add($"{name}_测量值");
                                firstRow.Add($"{name}_结果");
                            }
                            writer.WriteLine(string.Join(",", firstRow.Select(c => $"\"{c}\"")));

                            // 第二行：上限标识和上限值
                            var secondRow = new List<string>();
                            secondRow.Add("上限");
                            for (int i = 1; i < infoHeaders.Count; i++) secondRow.Add("");
                            foreach (var upper in upperLimits)
                            {
                                secondRow.Add(upper);
                                secondRow.Add("");
                            }
                            writer.WriteLine(string.Join(",", secondRow.Select(c => $"\"{c}\"")));

                            // 第三行：下限标识和下限值
                            var thirdRow = new List<string>();
                            thirdRow.Add("下限");
                            for (int i = 1; i < infoHeaders.Count; i++) thirdRow.Add("");
                            foreach (var lower in lowerLimits)
                            {
                                thirdRow.Add(lower);
                                thirdRow.Add("");
                            }
                            writer.WriteLine(string.Join(",", thirdRow.Select(c => $"\"{c}\"")));
                        }

                        // 数据行
                        var dataRow = new List<string>();
                        dataRow.Add(testStartTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                        dataRow.Add(sn);
                        dataRow.Add((channelIndex + 1).ToString());
                        dataRow.Add(testResult ? "PASS" : "FAIL");
                        dataRow.Add(((int)(testEndTime - testStartTime).TotalMilliseconds).ToString());

                        for (int i = 0; i < testItemCount; i++)
                        {
                            DataRow row = rows[i];
                            string valueColumn = $"Channel{channelIndex + 1}Value";
                            string resultColumn = $"Channel{channelIndex + 1}Result";
                            string value = row[valueColumn]?.ToString() ?? "";
                            string result = row[resultColumn]?.ToString() ?? "";
                            dataRow.Add(value);
                            dataRow.Add(result);
                        }
                        writer.WriteLine(string.Join(",", dataRow.Select(d => $"\"{d}\"")));
                    }
                }
                AppendLog($"测试报告已保存到本地: {reportFile}", LogInfo);

                // CSV 本地保存完成后，不再在这里单独写 FTP / SMB 上传逻辑。
                // CSV 和 Excel 统一复用 UploadReportFileAsync，确保远程目录、通道区分、失败判定完全一致。
                return await UploadReportFileAsync(
                    reportFile,
                    channelIndex,
                    sn,
                    testResult);
            }
            catch (Exception ex)
            {
                AppendLog($"保存CSV报告失败: {ex.Message}", LogError);
                return false;
            }
        }
        private async Task<bool> SaveSingleExcelReportAndUploadAsync(
    int channelIndex,
    string sn,
    bool testResult,
    DateTime testStartTime,
    DateTime testEndTime)
        {
            try
            {
                DataTable snapshot = null;

                await Dispatcher.InvokeAsync(() =>
                {
                    if (ProjectSettings.testDataTable != null)
                        snapshot = ProjectSettings.testDataTable.Copy();
                });

                if (snapshot == null)
                {
                    AppendLog("保存Excel报告失败：DataGrid数据为空。", LogError);
                    return false;
                }

                var result = await ExcelReportExporter.SaveDataGridSnapshotAsync(
                    snapshot,
                    channelIndex,
                    sn,
                    testResult,
                    ProjectSettings.CurrentProjectName,
                    testStartTime,
                    testEndTime,
                    AppDomain.CurrentDomain.BaseDirectory);

                if (!result.Success)
                {
                    AppendLog($"保存Excel报告失败：{result.ErrorMessage}", LogError);
                    return false;
                }

                AppendLog($"Excel测试报告已保存到本地: {result.FilePath}", LogInfo);

                return await UploadReportFileAsync(result.FilePath, channelIndex, sn, testResult);
            }
            catch (Exception ex)
            {
                AppendLog($"保存Excel报告异常: {ex.Message}", LogError);
                return false;
            }
        }
        private async Task<bool> UploadReportFileAsync(string reportFile, int channelIndex, string sn, bool testResult)
        {
            List<Task<bool>> uploadTasks = new List<Task<bool>>();

            if (appSettings.FTPEnabled)
            {
                uploadTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        string ftpBasePath = GetFtpBasePathByProject(ProjectSettings.CurrentProjectName);
                        if (string.IsNullOrEmpty(ftpBasePath))
                        {
                            Dispatcher.Invoke(() => AppendLog($"FTP 上传跳过：未配置项目 '{ProjectSettings.CurrentProjectName}' 的 FTP 路径", LogWarning));
                            return true;
                        }

                        string channelFtpBasePath = CombineFtpPath(ftpBasePath, $"Channel{channelIndex + 1}");

                        await FTPHelper.UploadTestReportAsync(
                            reportFile,
                            sn,
                            testResult,
                            channelFtpBasePath,
                            appSettings.FTPServer,
                            appSettings.FTPPort,
                            appSettings.FTPUser,
                            appSettings.FTPPassword,
                            msg => Dispatcher.Invoke(() => AppendLog(msg, LogInfo)));

                        return true;
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => AppendLog($"FTP 上传失败: {ex.Message}", LogError));
                        return false;
                    }
                }));
            }

            if (appSettings.SMBEnabled)
            {
                uploadTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        string deviceFolder = Path.Combine(
                            SanitizeRemotePathSegment(ProjectSettings.CurrentProjectName ?? "DefaultDevice"),
                            $"Channel{channelIndex + 1}");

                        string resultFolderName = testResult ? "PASS" : "NG";

                        bool success = await SMBHelper.UploadFileToSmbAsync(
                            reportFile,
                            deviceFolder,
                            resultFolderName,
                            appSettings.SMBServerPath,
                            appSettings.SMBUsername,
                            appSettings.SMBPassword,
                            msg => Dispatcher.Invoke(() => AppendLog(msg, LogInfo)));

                        return success;
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => AppendLog($"SMB 上传异常: {ex.Message}", LogError));
                        return false;
                    }
                }));
            }

            if (uploadTasks.Count == 0)
                return true;

            bool[] results = await Task.WhenAll(uploadTasks);
            bool allSuccess = results.All(r => r);
            if (!allSuccess)
                AppendLog("部分上传任务失败，测试结果将被标记为失败", LogError);

            return allSuccess;
        }
        /// <summary>
        /// 根据项目名称获取 FTP 基础路径（可根据实际配置修改）
        /// </summary>
        private string GetFtpBasePathByProject(string projectName)
        {
            if (_projectConfigs.TryGetValue(projectName, out var config))
                return config.GetFtpBasePath();
            return "";
        }

        /// <summary>
        /// 保存 Activity Log 到文件（按天追加）
        /// </summary>
        private void SaveActivityLogToFile()
        {
            try
            {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                Directory.CreateDirectory(logDir);
                string today = DateTime.Now.ToString("yyyyMMdd");
                string logFile = Path.Combine(logDir, $"ActivityLog_{today}.txt");

                // 获取 RichTextBox 中的纯文本内容
                string logContent = new TextRange(rictxB_log.Document.ContentStart, rictxB_log.Document.ContentEnd).Text;

                // 简单去重：如果内容与上次保存的相同，则跳过
                string hash = logContent.GetHashCode().ToString();
                if (hash == ProjectSettings.LastSavedLogHash) return;
                ProjectSettings.LastSavedLogHash = hash;

                // 追加模式写入
                File.AppendAllText(logFile, logContent + Environment.NewLine);
                AppendLog($"日志已保存至: {logFile}", LogInfo);
            }
            catch (Exception ex)
            {
                AppendLog($"保存日志文件失败: {ex.Message}", LogError);
            }
        }
        private string SanitizeRemotePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Default";

            char[] invalid = Path.GetInvalidFileNameChars();
            string cleaned = new string(value.Where(c => !invalid.Contains(c)).ToArray());
            cleaned = cleaned.Replace('/', '_').Replace('\\', '_').Trim();

            return string.IsNullOrWhiteSpace(cleaned) ? "Default" : cleaned;
        }

        private string CombineFtpPath(params string[] parts)
        {
            var cleanedParts = parts
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim().Replace('\\', '/').Trim('/'))
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            if (cleanedParts.Count == 0)
                return string.Empty;

            return "/" + string.Join("/", cleanedParts);
        }
        #endregion
        #region MES 响应解析
        /// <summary>
        /// 验证 MES 响应是否表示操作成功
        /// </summary>
        private bool ValidateMESResponse(string response, out string message)
        {
            message = string.Empty;

            if (string.IsNullOrEmpty(response))
            {
                message = "MES返回空响应";
                return false;
            }

            try
            {
                // 尝试解析JSON格式
                if (response.StartsWith("{") && response.EndsWith("}"))
                {
                    // 查找 msgId 字段
                    int msgIdIndex = response.IndexOf("\"msgId\":");
                    if (msgIdIndex >= 0)
                    {
                        int msgIdStart = msgIdIndex + 8;
                        int msgIdEnd = response.IndexOfAny(new[] { ',', '}' }, msgIdStart);
                        if (msgIdEnd > msgIdStart)
                        {
                            string msgIdStr = response.Substring(msgIdStart, msgIdEnd - msgIdStart).Trim();
                            if (int.TryParse(msgIdStr, out int msgId))
                            {
                                // 提取 msgStr 字段
                                string msgStr = ""; // 提前声明
                                int msgStrIndex = response.IndexOf("\"msgStr\":\"");
                                if (msgStrIndex >= 0)
                                {
                                    int msgStrStart = msgStrIndex + 10;
                                    int msgStrEnd = response.IndexOf("\"", msgStrStart);
                                    if (msgStrEnd > msgStrStart)
                                    {
                                        msgStr = response.Substring(msgStrStart, msgStrEnd - msgStrStart);
                                    }
                                }

                                // 特殊处理：对于过站检查，msgId为0且msgStr为空是正常情况
                                if (msgId == 0 && string.IsNullOrEmpty(msgStr))
                                {
                                    message = "检查通过";
                                    return true;
                                }

                                // 特殊处理：msgId为1且msgStr包含"已经测试PASS"的情况
                                if (msgId == 1 && !string.IsNullOrEmpty(msgStr) &&
                                    (msgStr.Contains("已经测试PASS") || msgStr.Contains("已经测试")))
                                {
                                    // 已经测试过，不允许再次测试
                                    return false;
                                }

                                message = msgStr;
                                return msgId == 0; // msgId为0表示成功
                            }
                        }
                    }

                    // 备用解析：直接查找msgId值
                    if (response.Contains("\"msgId\":0"))
                    {
                        // 检查是否有错误消息
                        string msgStr = ""; // 提前声明
                        int msgStrIndex = response.IndexOf("\"msgStr\":\"");
                        if (msgStrIndex >= 0)
                        {
                            int msgStrStart = msgStrIndex + 10;
                            int msgStrEnd = response.IndexOf("\"", msgStrStart);
                            if (msgStrEnd > msgStrStart)
                            {
                                msgStr = response.Substring(msgStrStart, msgStrEnd - msgStrStart);
                                // 如果msgId为0但有错误消息，需要根据消息内容判断
                                if (!string.IsNullOrEmpty(msgStr) &&
                                    (msgStr.Contains("已经测试PASS") || msgStr.Contains("已经测试")))
                                {
                                    message = msgStr;
                                    return false;
                                }
                                message = msgStr;
                            }
                            else
                            {
                                message = "检查通过";
                            }
                        }
                        else
                        {
                            message = "检查通过";
                        }
                        return true;
                    }
                    else if (response.Contains("\"msgId\":1"))
                    {
                        // 尝试提取错误信息
                        int msgStrIndex = response.IndexOf("\"msgStr\":\"");
                        if (msgStrIndex >= 0)
                        {
                            int msgStrStart = msgStrIndex + 10;
                            int msgStrEnd = response.IndexOf("\"", msgStrStart);
                            if (msgStrEnd > msgStrStart)
                            {
                                message = response.Substring(msgStrStart, msgStrEnd - msgStrStart);
                            }
                            else
                            {
                                message = "操作失败";
                            }
                        }
                        else
                        {
                            message = "操作失败";
                        }
                        return false;
                    }
                }

                // 如果无法解析JSON，使用原有的解析方法（兼容非JSON格式）
                if (response.Length < 10)
                {
                    message = "MES返回响应格式错误";
                    return false;
                }

                string check0 = response.Substring(9, 1);
                string[] parts = response.Split(':', '"', ',', ' ');
                message = parts.Length > 8 ? parts[8] : string.Empty;

                // 特殊处理：对于空消息但状态为0的情况
                if (check0 == "0" && string.IsNullOrEmpty(message))
                {
                    message = "操作成功";
                }

                // 特殊处理：状态为1且消息包含"已经测试PASS"的情况
                if (check0 == "1" && !string.IsNullOrEmpty(message) &&
                    (message.Contains("已经测试PASS") || message.Contains("已经测试")))
                {
                    // 保持原样，返回false
                }

                return check0 == "0";
            }
            catch (Exception ex)
            {
                message = $"解析响应时发生异常: {ex.Message}";
                return false;
            }
        }
        #endregion

        #region 辅助方法
        private void AppendLog(string message, Brush color = null)
        {
            Dispatcher.Invoke(() =>
            {
                if (color == null) color = LogInfo;
                var tr = new TextRange(rictxB_log.Document.ContentEnd, rictxB_log.Document.ContentEnd)
                {
                    Text = $"{DateTime.Now:HH:mm:ss} - {message}\n"
                };
                tr.ApplyPropertyValue(TextElement.ForegroundProperty, color);
                rictxB_log.ScrollToEnd();
            });
        }

        private void RefreshStatus()
        {
            txtCurrentWorkOrder.Text = appSettings.WorkOrder;
            txtCurrentWorkStation.Text = appSettings.WorkStation;

            // MES 状态
            mesStatusLight.Fill = appSettings.MESEnabled ? Brushes.Lime : Brushes.Gray;
            mesStatusText.Text = appSettings.MESEnabled ? "已连接" : "已关闭";
            mesStatusText.Foreground = appSettings.MESEnabled ? StatusSuccess : StatusError;

            // FTP 状态
            ftpStatusLight.Fill = appSettings.FTPEnabled ? Brushes.Lime : Brushes.Gray;
            ftpStatusText.Text = appSettings.FTPEnabled ? "已连接" : "已关闭";
            ftpStatusText.Foreground = appSettings.FTPEnabled ? StatusSuccess : StatusError;

            // SMB 状态
            smbStatusLight.Fill = appSettings.SMBEnabled ? Brushes.Lime : Brushes.Gray;
            smbStatusText.Text = appSettings.SMBEnabled ? "已连接" : "已关闭";
            smbStatusText.Foreground = appSettings.SMBEnabled ? StatusSuccess : StatusError;

            // 登录状态
            if (GlobalState.IsLoggedIn)
            {
                loginStatusLight.Fill = Brushes.Lime;
                loginStatusText.Text = "已登录";
                loginStatusText.Foreground = StatusSuccess;
            }
            else
            {
                loginStatusLight.Fill = Brushes.Gray;
                loginStatusText.Text = "未登录";
                loginStatusText.Foreground = StatusError;
            }

            // 根据登录状态启用/禁用管理员按钮
            toolStripButton2.IsEnabled = GlobalState.IsLoggedIn;
            toolStripButton3.IsEnabled = GlobalState.IsLoggedIn;
        }

        private void LoadTestDataFromXML()
        {
            string xmlPath = ProjectSettings.TestFikePath;
            if (string.IsNullOrEmpty(xmlPath) || !File.Exists(xmlPath))
            {
                MessageBox.Show($"测试配置文件不存在: {xmlPath}\n请检查项目配置路径。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                // 清空 DataGrid
                if (ProjectSettings.testDataTable != null)
                    ProjectSettings.testDataTable.Rows.Clear();
                return;
            }

            try
            {
                _loadingRuntimeTree = true;
                XDocument doc = XDocument.Load(xmlPath);
                var testItems = doc.Root.Elements("TestItem");
                var groupNames = doc.Root.Element("Groups")?
                    .Elements("Group")
                    .ToDictionary(
                        x => (string)x.Element("GroupId") ?? string.Empty,
                        x => (string)x.Element("Name") ??
                             (string)x.Element("GroupId") ??
                             string.Empty,
                        StringComparer.OrdinalIgnoreCase)
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                ProjectSettings.testDataTable.Rows.Clear();
                int channelCount = appSettings.ParallelTestCount;

                foreach (var elem in testItems)
                {
                    DataRow row = ProjectSettings.testDataTable.NewRow();
                    row["Select"] = (bool?)elem.Element("Enabled") ?? false;
                    row["StepId"] = (string)elem.Element("StepId") ?? "";
                    row["GroupId"] = (string)elem.Element("GroupId") ?? "";
                    string groupId = row["GroupId"]?.ToString() ?? string.Empty;
                    string groupName;
                    if (!groupNames.TryGetValue(groupId, out groupName))
                        groupName = groupId;
                    row["GroupHeader"] = groupName;
                    row["GroupStatus"] = "待测";
                    row["GroupSummary"] = "待测";
                    row["SequenceOrder"] = (int?)elem.Element("SequenceOrder") ?? ProjectSettings.testDataTable.Rows.Count + 1;
                    row["DefaultEnabled"] = (bool?)elem.Element("DefaultEnabled") ?? (bool?)elem.Element("Enabled") ?? false;
                    row["Mandatory"] = (bool?)elem.Element("Mandatory") ?? false;
                    row["AlwaysRun"] = (bool?)elem.Element("AlwaysRun") ?? false;
                    row["RunCondition"] = (string)elem.Element("RunCondition") ?? "";
                    row["DependsOn"] = (string)elem.Element("DependsOn") ?? "";
                    row["TestItem"] = (string)elem.Element("Name") ?? "";
                    row["UpperLimit"] = (string)elem.Element("UpperLimit") ?? "";
                    row["LowerLimit"] = (string)elem.Element("LowerLimit") ?? "";
                    row["Unit"] = (string)elem.Element("Unit") ?? "";
                    row["ExecTime"] = "";

                    for (int i = 1; i <= channelCount; i++)
                    {
                        row[$"Channel{i}Value"] = "";
                        row[$"Channel{i}Result"] =
                            string.Equals(
                                row["RunCondition"]?.ToString(),
                                "OnChapterFailure",
                                StringComparison.OrdinalIgnoreCase)
                                ? "未触发"
                                : "待测";
                    }
                    ProjectSettings.testDataTable.Rows.Add(row);
                }

                if (doc.Root.Element("PlanMetadata") != null)
                {
                    ResolvedTestPlan plan = TestPlanService.Resolve(
                        xmlPath,
                        TestPlanRuntimeState.ActiveProfileId,
                        TestPlanRuntimeState.GetStepOverridesSnapshot());
                    foreach (DataRow row in ProjectSettings.testDataTable.Rows)
                    {
                        row["Select"] = plan.ShouldRun(row["StepId"]?.ToString());
                    }
                }

                BuildRuntimeGroupTree(doc, channelCount);
                _loadingRuntimeTree = false;
                RefreshRuntimeGroupTree();
                dataGridView1.UpdateLayout();
                AppendLog($"成功加载测试数据，共 {testItems.Count()} 项", LogSuccess);
            }
            catch (Exception ex)
            {
                _loadingRuntimeTree = false;
                MessageBox.Show($"加载测试数据失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                AppendLog($"加载测试数据失败: {ex.Message}", LogError);
            }
        }

        private void BuildRuntimeGroupTree(XDocument doc, int channelCount)
        {
            _runtimeGroups.Clear();
            if (doc?.Root == null)
                return;

            var groupElements = doc.Root
                .Element("Groups")?
                .Elements("Group")
                .OrderBy(x => (int?)x.Element("SequenceOrder") ?? int.MaxValue)
                .ToList();

            IEnumerable<XElement> groups;
            if (groupElements != null && groupElements.Count > 0)
            {
                groups = groupElements;
            }
            else
            {
                groups = doc.Root.Elements("TestItem")
                    .GroupBy(x => (string)x.Element("GroupId") ?? string.Empty)
                    .Select(x => new XElement(
                        "Group",
                        new XElement("GroupId", x.Key),
                        new XElement("Name", x.Key),
                        new XElement(
                            "SequenceOrder",
                            (int?)x.First().Element("SequenceOrder") ?? int.MaxValue)))
                    .OrderBy(x => (int?)x.Element("SequenceOrder") ?? int.MaxValue);
            }

            foreach (XElement group in groups)
            {
                string groupId = (string)group.Element("GroupId") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(groupId))
                    continue;

                DataRow firstRow = ProjectSettings.testDataTable.Rows
                    .Cast<DataRow>()
                    .Where(x => string.Equals(
                        x["GroupId"]?.ToString(),
                        groupId,
                        StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => Convert.ToInt32(x["SequenceOrder"]))
                    .FirstOrDefault();

                var node = new RuntimeGroupNode
                {
                    GroupId = groupId,
                    DisplayName = (string)group.Element("Name") ?? groupId,
                    FirstStepId = firstRow?["StepId"]?.ToString()
                };
                node.Update("待测", "待测", 0, 0);
                _runtimeGroups.Add(node);
            }

            int previousChannel = Math.Max(0, cmbRuntimeTreeChannel.SelectedIndex);
            cmbRuntimeTreeChannel.ItemsSource = Enumerable
                .Range(1, Math.Max(1, channelCount))
                .Select(x => $"通道 {x}")
                .ToList();
            cmbRuntimeTreeChannel.SelectedIndex =
                Math.Min(previousChannel, Math.Max(0, channelCount - 1));
            cmbRuntimeGroupJump.ItemsSource = _runtimeGroups;
            cmbRuntimeGroupJump.SelectedIndex = -1;
            _runtimeGroupExpansion.Clear();
        }

        private void RuntimeTreeDataTable_RowChanged(object sender, DataRowChangeEventArgs e)
        {
            if (_updatingRuntimeGroupSummary)
                return;
            QueueRuntimeTreeRefresh();
        }

        private void QueueRuntimeTreeRefresh()
        {
            if (_loadingRuntimeTree || _runtimeTreeRefreshPending || !IsLoaded)
                return;

            _runtimeTreeRefreshPending = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _runtimeTreeRefreshPending = false;
                RefreshRuntimeGroupTree();
            }), DispatcherPriority.Background);
        }

        private void RefreshRuntimeGroupTree()
        {
            if (_loadingRuntimeTree ||
                ProjectSettings.testDataTable == null ||
                _runtimeGroups.Count == 0)
            {
                return;
            }

            int channelIndex = Math.Max(0, cmbRuntimeTreeChannel.SelectedIndex);
            string resultColumn = $"Channel{channelIndex + 1}Result";
            if (!ProjectSettings.testDataTable.Columns.Contains(resultColumn))
                return;

            RuntimeGroupNode activeNode = null;
            DataRow activeRow = null;
            _updatingRuntimeGroupSummary = true;
            foreach (RuntimeGroupNode node in _runtimeGroups)
            {
                List<DataRow> allRows = ProjectSettings.testDataTable.Rows
                    .Cast<DataRow>()
                    .Where(x => string.Equals(
                        x["GroupId"]?.ToString(),
                        node.GroupId,
                        StringComparison.OrdinalIgnoreCase))
                    .Where(x => !string.Equals(
                        x["RunCondition"]?.ToString(),
                        "OnChapterFailure",
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();

                List<DataRow> selectedRows = allRows.Where(x =>
                    Convert.ToBoolean(x["Select"]) ||
                    Convert.ToBoolean(x["Mandatory"]) ||
                    Convert.ToBoolean(x["AlwaysRun"])).ToList();

                if (selectedRows.Count == 0)
                {
                    node.Update("已跳过", "已跳过", 0, 0);
                    UpdateRuntimeGroupRows(allRows, "已跳过", "已跳过");
                    continue;
                }

                string[] results = selectedRows
                    .Select(x => x[resultColumn]?.ToString() ?? "待测")
                    .ToArray();
                int completed = results.Count(IsTerminalRuntimeStatus);
                bool cleanupFailed = results.Any(x => x == "收尾失败");
                bool failed = results.Any(x => x == "FAIL");
                bool canceled = results.Any(x => x == "已取消");
                bool cleanupRunning = results.Any(x => x == "收尾中");
                bool running = results.Any(x =>
                    x == "执行中" ||
                    x == "重试中" ||
                    x == "收尾中");
                bool allPassed = results.All(x => x == "PASS" || x == "重试通过");

                string status;
                if (cleanupFailed)
                    status = "收尾失败";
                else if (failed)
                    status = "FAIL";
                else if (canceled)
                    status = "已取消";
                else if (cleanupRunning)
                    status = "收尾中";
                else if (running)
                    status = "执行中";
                else if (allPassed)
                    status = "PASS";
                else if (completed > 0)
                    status = "执行中";
                else
                    status = "待测";

                string previousStatus = node.Status;
                node.Update(
                    status,
                    status == "待测"
                        ? "待测"
                        : $"{status}  {completed}/{selectedRows.Count}",
                    completed,
                    selectedRows.Count);
                UpdateRuntimeGroupRows(allRows, status, node.ProgressText);
                if (status == "PASS" &&
                    previousStatus == "执行中" &&
                    !node.GroupId.EndsWith(".CLEANUP", StringComparison.OrdinalIgnoreCase))
                    SetRuntimeGroupExpanded(node.DisplayName, false);

                if (running || cleanupRunning)
                {
                    activeNode = node;
                    activeRow = selectedRows.FirstOrDefault(x =>
                    {
                        string value = x[resultColumn]?.ToString() ?? string.Empty;
                        return value == "执行中" ||
                               value == "重试中" ||
                               value == "收尾中";
                    });
                }
            }
            _updatingRuntimeGroupSummary = false;

            if (activeNode != null)
            {
                EnsureRuntimeGroupExpanded(activeNode.DisplayName);
                string stepName = activeRow?["TestItem"]?.ToString() ?? string.Empty;
                txtRuntimeCurrentStep.Text =
                    $"当前：{activeNode.DisplayName} / {stepName}";
            }
            else
            {
                txtRuntimeCurrentStep.Text = "当前：等待测试";
            }
        }

        private static void UpdateRuntimeGroupRows(
            IEnumerable<DataRow> rows,
            string status,
            string summary)
        {
            foreach (DataRow row in rows)
            {
                row["GroupStatus"] = status;
                row["GroupSummary"] = summary;
            }
        }

        private static bool IsTerminalRuntimeStatus(string status)
        {
            return status == "PASS" ||
                   status == "FAIL" ||
                   status == "重试通过" ||
                   status == "已取消" ||
                   status == "收尾失败";
        }

        private void CmbRuntimeTreeChannel_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            RefreshRuntimeGroupTree();
        }

        private async void CmbRuntimeGroupJump_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (!(cmbRuntimeGroupJump.SelectedItem is RuntimeGroupNode node) ||
                string.IsNullOrWhiteSpace(node.FirstStepId) ||
                ProjectSettings.testDataTable == null)
            {
                return;
            }

            EnsureRuntimeGroupExpanded(node.DisplayName);
            int rowIndex = ProjectSettings.testDataTable.Rows
                .Cast<DataRow>()
                .Select((row, index) => new { row, index })
                .Where(x => string.Equals(
                    x.row["StepId"]?.ToString(),
                    node.FirstStepId,
                    StringComparison.OrdinalIgnoreCase))
                .Select(x => x.index)
                .FirstOrDefault();
            await ScrollToTestRowAsync(rowIndex);
        }

        private void RuntimeGroupExpander_Loaded(object sender, RoutedEventArgs e)
        {
            if (!(sender is Expander expander))
                return;

            string groupHeader = expander.Tag?.ToString() ?? string.Empty;
            bool expanded;
            expander.IsExpanded =
                !_runtimeGroupExpansion.TryGetValue(groupHeader, out expanded) || expanded;
        }

        private void RuntimeGroupExpander_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is Expander expander)
                _runtimeGroupExpansion[expander.Tag?.ToString() ?? string.Empty] = true;
        }

        private void RuntimeGroupExpander_Collapsed(object sender, RoutedEventArgs e)
        {
            if (sender is Expander expander)
                _runtimeGroupExpansion[expander.Tag?.ToString() ?? string.Empty] = false;
        }

        private void EnsureRuntimeGroupExpanded(string groupHeader)
        {
            SetRuntimeGroupExpanded(groupHeader, true);
        }

        private void SetRuntimeGroupExpanded(string groupHeader, bool expanded)
        {
            if (string.IsNullOrWhiteSpace(groupHeader))
                return;

            _runtimeGroupExpansion[groupHeader] = expanded;
            foreach (Expander expander in FindVisualChildren<Expander>(dataGridView1))
            {
                if (string.Equals(
                    expander.Tag?.ToString(),
                    groupHeader,
                    StringComparison.OrdinalIgnoreCase))
                {
                    expander.IsExpanded = expanded;
                    if (expanded)
                        expander.BringIntoView();
                    break;
                }
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
            where T : DependencyObject
        {
            if (parent == null)
                yield break;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int index = 0; index < count; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, index);
                if (child is T typedChild)
                    yield return typedChild;

                foreach (T nestedChild in FindVisualChildren<T>(child))
                    yield return nestedChild;
            }
        }

        /// <summary>
        /// 重置指定通道的所有测试项的值和结果
        /// </summary>
        private void ResetChannelTestData(int channelIndex)
        {
            Dispatcher.Invoke(() =>
            {
                DataTable dt = ProjectSettings.testDataTable;
                if (dt == null) return;
                string valueColumn = $"Channel{channelIndex + 1}Value";
                string resultColumn = $"Channel{channelIndex + 1}Result";
                foreach (DataRow row in dt.Rows)
                {
                    row[valueColumn] = "unknown";          // 清空测试值
                    row[resultColumn] =
                        string.Equals(
                            row["RunCondition"]?.ToString(),
                            "OnChapterFailure",
                            StringComparison.OrdinalIgnoreCase)
                            ? "未触发"
                            : "待测";
                    row["ExecTime"] = "";            // 清空执行时间
                }
            });
        }

        /// <summary>
        /// 根据当前通道占用情况，自动启用/禁用 SN 输入框和测试按钮
        /// </summary>
        private void UpdateTestControlsState()
        {
            // 检查是否存在空闲通道
            bool hasFreeChannel = ProjectSettings.Channels.Any(c => !c.IsBusy);
            bool hasBusyChannel = ProjectSettings.Channels.Any(c => c.IsBusy);
            // 串口就绪且有空闲通道时，才允许测试
            bool canTest = _comPortsReady && hasFreeChannel && !_stopInProgress;
            Dispatcher.Invoke(() =>
            {
                txb_snInput.IsEnabled = canTest;
                btn_statsTest.IsEnabled = canTest;
                button2.IsEnabled = hasBusyChannel;
            });
        }

        #endregion

        #region 项目配置相关
        private bool SetTestFilePathBasedOnProcess(string processName)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string testFilePath = "";

                if (_projectConfigs.TryGetValue(processName, out var config))
                {
                    testFilePath = config.GetTestFilePath(processName);
                    if (File.Exists(testFilePath))
                    {
                        ProjectSettings.TestFikePath = testFilePath;
                        AppendLog($"测试文件路径已设置: {testFilePath}", LogSuccess);
                        return true;
                    }
                    else
                    {
                        AppendLog($"测试文件不存在: {testFilePath}", LogError);
                    }
                }
                else
                {
                    AppendLog($"未找到工序 '{processName}' 对应的配置", LogError);
                }
                return false;


            }
            catch (Exception ex)
            {
                AppendLog($"设置测试文件路径失败: {ex.Message}", LogError);
                return false;
            }
        }
        /// <summary>
        /// 更新工具栏上的项目名称标签
        /// </summary>
        private void UpdateProjectLabel()
        {
            string projectName = ProjectSettings.CurrentProjectName;
            Dispatcher.Invoke(() =>
            {
                if (string.IsNullOrEmpty(projectName))
                {
                    toolStripLabel1.Content = "未选择项目";
                    toolStripLabel1.Foreground = Brushes.Gray;
                }
                else
                {
                    toolStripLabel1.Content = projectName;
                    toolStripLabel1.Foreground = Brushes.Black;
                }
            });

        }

        #endregion

        #region 按钮事件
        private void btn_statsTest_Click(object sender, RoutedEventArgs e)
        {
            string sn = txb_snInput.Text.Trim();
            txb_snInput.Clear();
            txb_snInput.Focus();
            try
            {
                _ = ProcessSNAsync(sn); // 异步调用，不等待
                txb_snInput.Clear();
                txb_snInput.Focus();
            }
            finally
            {
                txb_snInput.Clear();
                txb_snInput.Focus();
            }

        }

        private async void txb_snInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            string sn = txb_snInput.Text.Trim();
            txb_snInput.Clear();
            txb_snInput.Focus();
            try
            {
                await ProcessSNAsync(sn);
            }
            catch (Exception ex)
            {
                AppendLog($"处理SN异常: {ex.Message}", LogError);
            }
            finally
            {
                await Dispatcher.InvokeAsync(() =>
                {

                    txb_snInput.Clear();
                    txb_snInput.Focus();
                }, DispatcherPriority.Background);
            }
        }

        private void toolStripButton1_Click(object sender, RoutedEventArgs e)
        {
            if (GlobalState.IsLoggedIn)
            {
                MessageBox.Show("您已经登录！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var loginWin = new LoginWindow { Owner = this };
            if (loginWin.ShowDialog() == true)
                RefreshStatus();
        }

        private void toolStripButton2_Click(object sender, RoutedEventArgs e)
        {
            var configWin = new ProjectConfigWindow(ProjectSettings.CurrentProjectName);
            configWin.Owner = this;
            if (configWin.ShowDialog() == true)
            {
                string newProject = configWin.SelectedProjectName;
                AppendLog($"配置窗口返回，选中的项目: {newProject}", LogInfo);

                if (!string.IsNullOrEmpty(newProject))
                {
                    // 更新当前项目名（允许相同，因为用户可能修改了测试项）
                    ProjectSettings.CurrentProjectName = newProject;
                    appSettings.CurrentProjectName = newProject;
                    SaveSettings();

                    // 重新设置文件路径（根据项目名称解析）
                    bool pathSet = SetTestFilePathByProjectName(newProject);
                    if (!pathSet)
                    {
                        AppendLog($"警告：未能找到项目 '{newProject}' 的测试文件路径", LogWarning);
                    }
                    // 重新加载测试数据（会使用新的 ProjectSettings.TestFikePath）
                    LoadTestDataFromXML();
                    RefreshStatus();
                    UpdateProjectLabel();
                    //更新测试次数
                    UpdateCurrentProjectLimit();
                }
                else
                {
                    AppendLog("未选择任何项目，重新加载当前数据", LogInfo);
                    LoadTestDataFromXML();
                }
            }
        }

        private void toolStripButton3_Click(object sender, RoutedEventArgs e)
        {
            var settingsWin = new SettingsWindow(appSettings) { Owner = this };
            if (settingsWin.ShowDialog() == true)
            {
                appSettings = settingsWin.CurrentSettings;
                SaveSettings();
                ApplySettingsToUI();
                RefreshStatus();
                BuildResultWindows(appSettings.ParallelTestCount);
                BuildDataGridColumns(appSettings.ParallelTestCount);
                LoadTestDataFromXML();
            }
        }

        private void button2_Click(object sender, RoutedEventArgs e)
        {
            _stopInProgress = true;
            foreach (var channel in ProjectSettings.Channels.Where(c => c.IsBusy))
            {
                channel.CancelToken?.Cancel();
                channel.ResultModel.DisplayText =
                    $"通道 {channel.Index + 1}\n已收到停止请求\n正在执行安全收尾，请勿取板";
                channel.ResultModel.Background = StatusWarning;
                channel.ResultModel.DisplayForeground = Brushes.Black;
                AppendLog(
                    $"通道 {channel.Index + 1} 已收到停止请求，等待当前步骤取消并完成安全收尾。",
                    LogWarning);
            }

            button2.IsEnabled = false;
            txb_snInput.IsEnabled = false;
            btn_statsTest.IsEnabled = false;
            // 通道只能由运行任务的 finally 释放；ReleaseChannel 会在所有收尾完成后停止计时并保存日志。
        }


        #endregion 按钮事件END
        #region 公共测试方法

        /// <summary>
        /// 等待治具下压，并实时将接收到的状态值更新到 DataGrid
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <param name="rowIndex">当前行索引</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否检测到下压</returns>
        private async Task<bool> WaitForFixtureDownAsync(int channelIndex, int rowIndex, CancellationToken cancellationToken)
        {
            // 定义串口命令（读取输入状态）
            byte[] command = { 0x01, 0x04, 0x00, 0x00, 0x00, 0x02, 0x71, 0xCB };
            int baudRate = 38400;
            string portName = ComName.rs485ComName;
            if (string.IsNullOrEmpty(portName))
            {
                AppendLog("RS485串口未配置，无法检测治具下压", LogError);
                return false;
            }

            using (SerialPort port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One))
            {
                try
                {
                    port.Open();
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    TimeSpan timeout = TimeSpan.FromSeconds(60); // 总超时时间

                    while (stopwatch.Elapsed < timeout)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // 清空输入缓冲区
                        port.DiscardInBuffer();

                        // 发送命令
                        port.Write(command, 0, command.Length);
                        AppendLog($"发送下压检测命令: {BytesToHex(command)}", LogInfo);

                        // 等待响应（100ms）
                        await Task.Delay(100, cancellationToken);

                        // 读取所有可用数据
                        if (port.BytesToRead > 0)
                        {
                            byte[] buffer = new byte[port.BytesToRead];
                            port.Read(buffer, 0, buffer.Length);
                            string hexResponse = BytesToHex(buffer);
                            AppendLog($"收到响应: {hexResponse}", LogInfo);

                            // 实时更新 DataGrid 中当前行的测试值列（显示原始十六进制或特定字节）
                            string displayValue = buffer[4].ToString(); // 或提取特定字节 
                            await Dispatcher.InvokeAsync(() =>
                            {
                                DataRow row = ProjectSettings.testDataTable.Rows[rowIndex];
                                string valueColumn = $"Channel{channelIndex + 1}Value";
                                row[valueColumn] = displayValue;
                            });

                            // 检查第5个字节（索引4）是否为 0x01（下压到位）
                            if (buffer.Length > 4 && buffer[4] == 0x01)
                            {
                                return true;
                            }
                        }

                        // 等待间隔，避免频繁发送
                        await Task.Delay(500, cancellationToken);
                    }

                    return false; // 超时未检测到下压
                }
                catch (OperationCanceledException)
                {
                    AppendLog("治具下压检测已取消", LogWarning);
                    return false;
                }
                catch (Exception ex)
                {
                    AppendLog($"串口错误：{ex.Message}", LogError);
                    return false;
                }
            }
        }
        private static string BytesToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return string.Empty;
            }
            return BitConverter.ToString(bytes).Replace("-", "");
        }

        private string GetFixtureDownSessionKey(int rowIndex, bool openRelay)
        {
            return $"{ProjectSettings.CurrentProjectName}|Row={rowIndex}|OpenRelay={openRelay}";
        }

        private FixtureDownWaitSession JoinOrCreateFixtureDownSession(
            int channelIndex,
            int rowIndex,
            bool openRelay)
        {
            string key = GetFixtureDownSessionKey(rowIndex, openRelay);

            lock (_fixtureDownSessionLock)
            {
                if (!_fixtureDownSessions.TryGetValue(key, out var session) ||
                    session == null ||
                    session.IsCompleted)
                {
                    session = new FixtureDownWaitSession
                    {
                        Key = key,
                        RowIndex = rowIndex,
                        OpenRelay = openRelay,
                        ExpectedParticipants = Math.Max(1, appSettings?.ParallelTestCount ?? 1)
                    };

                    AddFixtureDownParticipant(session, channelIndex, rowIndex);

                    session.WaitTask = RunSharedFixtureDownWindowAsync(session);

                    _fixtureDownSessions[key] = session;

                    AppendLog($"共享治具下压检测已启动：Row={rowIndex}，等待串口返回 01...", LogInfo);
                }
                else
                {
                    AddFixtureDownParticipant(session, channelIndex, rowIndex);

                    AppendLog($"通道 {channelIndex + 1} 加入共享治具下压检测：Row={rowIndex}", LogInfo);
                }

                return session;
            }
        }

        private void AddFixtureDownParticipant(
            FixtureDownWaitSession session,
            int channelIndex,
            int rowIndex)
        {
            lock (session.SyncRoot)
            {
                bool exists = session.Participants.Any(p =>
                    p.ChannelIndex == channelIndex &&
                    p.RowIndex == rowIndex);

                if (!exists)
                {
                    session.Participants.Add(new FixtureDownParticipant
                    {
                        ChannelIndex = channelIndex,
                        RowIndex = rowIndex
                    });
                }
            }
        }
        private async Task<bool> RunSharedFixtureDownWindowAsync(FixtureDownWaitSession session)
        {
            try
            {
                if (session.OpenRelay)
                {
                    await RelayController.SendCommandWithCrcAsync(
                        CommandList.CloseAllRelay_01,
                        38400,
                        ComName.rs485ComName,
                        1000,
                        msg => AppendLog(msg));
                }

                async Task<bool> DetectAsync(CancellationToken ct)
                {
                    return await WaitForFixtureDownSharedAsync(session, ct);
                }

                bool isPressed = await Dispatcher.InvokeAsync(() =>
                {
                    var waitWindow = new FixtureDownWindow(DetectAsync);
                    waitWindow.Owner = this;

                    return waitWindow.ShowDialog() == true;
                });

                await UpdateFixtureDownParticipantsFinalAsync(session, isPressed);

                return isPressed;
            }
            catch (Exception ex)
            {
                AppendLog($"共享治具下压检测异常：{ex.Message}", LogError);

                await UpdateFixtureDownParticipantsFinalAsync(session, false);

                return false;
            }
            finally
            {
                session.IsCompleted = true;

                lock (_fixtureDownSessionLock)
                {
                    if (_fixtureDownSessions.TryGetValue(session.Key, out var current) &&
                        ReferenceEquals(current, session))
                    {
                        _fixtureDownSessions.Remove(session.Key);
                    }
                }
            }
        }
        private List<FixtureDownParticipant> GetFixtureDownParticipantsSnapshot(
            FixtureDownWaitSession session)
        {
            lock (session.SyncRoot)
            {
                return session.Participants.ToList();
            }
        }

        private async Task<bool> WaitForFixtureDownSharedAsync(
    FixtureDownWaitSession session,
    CancellationToken cancellationToken)
        {
            byte[] command = { 0x01, 0x04, 0x00, 0x00, 0x00, 0x02, 0x71, 0xCB };
            int baudRate = 38400;
            string portName = ComName.rs485ComName;

            if (string.IsNullOrEmpty(portName))
            {
                AppendLog("RS485串口未配置，无法检测治具下压", LogError);
                return false;
            }

            await WaitForExpectedFixtureParticipantsAsync(session, cancellationToken);

            using (SerialPort port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One))
            {
                try
                {
                    port.Open();

                    Stopwatch stopwatch = Stopwatch.StartNew();
                    TimeSpan timeout = TimeSpan.FromSeconds(60);

                    while (stopwatch.Elapsed < timeout)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        port.DiscardInBuffer();

                        port.Write(command, 0, command.Length);

                        AppendLog($"发送共享下压检测命令: {BytesToHex(command)}", LogInfo);

                        await Task.Delay(100, cancellationToken);

                        if (port.BytesToRead > 0)
                        {
                            byte[] buffer = new byte[port.BytesToRead];
                            port.Read(buffer, 0, buffer.Length);

                            string hexResponse = BytesToHex(buffer);
                            AppendLog($"收到共享下压检测响应: {hexResponse}", LogInfo);

                            if (buffer.Length > 4)
                            {
                                string displayValue = buffer[4].ToString("X2");

                                // 这里只更新测试值，不更新结果列。
                                // 收到 00 只是表示还没下压，不代表失败。
                                await UpdateFixtureDownParticipantsValueAsync(session, displayValue);

                                if (buffer[4] == 0x01)
                                {
                                    return true;
                                }
                            }
                        }

                        await Task.Delay(500, cancellationToken);
                    }

                    return false;
                }
                catch (OperationCanceledException)
                {
                    AppendLog("共享治具下压检测已取消", LogWarning);
                    return false;
                }
                catch (Exception ex)
                {
                    AppendLog($"共享治具下压串口错误：{ex.Message}", LogError);
                    return false;
                }
            }
        }
        private async Task WaitForExpectedFixtureParticipantsAsync(
    FixtureDownWaitSession session,
    CancellationToken cancellationToken)
        {
            Stopwatch sw = Stopwatch.StartNew();
            TimeSpan maxWait = TimeSpan.FromMilliseconds(1000);

            while (sw.Elapsed < maxWait)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int count;

                lock (session.SyncRoot)
                {
                    count = session.Participants.Count;
                }

                if (count >= session.ExpectedParticipants)
                    return;

                await Task.Delay(50, cancellationToken);
            }
        }
        private async Task UpdateFixtureDownParticipantsValueAsync(
    FixtureDownWaitSession session,
    string value)
        {
            var participants = GetFixtureDownParticipantsSnapshot(session);

            await Dispatcher.InvokeAsync(() =>
            {
                DataTable dt = ProjectSettings.testDataTable;

                if (dt == null)
                    return;

                foreach (var p in participants)
                {
                    if (p.RowIndex < 0 || p.RowIndex >= dt.Rows.Count)
                        continue;

                    string valueColumn = $"Channel{p.ChannelIndex + 1}Value";

                    if (dt.Columns.Contains(valueColumn))
                    {
                        dt.Rows[p.RowIndex][valueColumn] = value;
                    }
                }
            });
        }

        private async Task UpdateFixtureDownParticipantsFinalAsync(
            FixtureDownWaitSession session,
            bool isPressed)
        {
            var participants = GetFixtureDownParticipantsSnapshot(session);

            await Dispatcher.InvokeAsync(() =>
            {
                DataTable dt = ProjectSettings.testDataTable;

                if (dt == null)
                    return;

                foreach (var p in participants)
                {
                    if (p.RowIndex < 0 || p.RowIndex >= dt.Rows.Count)
                        continue;

                    string valueColumn = $"Channel{p.ChannelIndex + 1}Value";
                    string resultColumn = $"Channel{p.ChannelIndex + 1}Result";

                    if (dt.Columns.Contains(valueColumn))
                    {
                        dt.Rows[p.RowIndex][valueColumn] = isPressed ? "01" : "00";
                    }

                    if (dt.Columns.Contains(resultColumn))
                    {
                        dt.Rows[p.RowIndex][resultColumn] = isPressed ? "PASS" : "FAIL";
                    }
                }
            });
        }
        #endregion 公共测试方法END

        #region VC Docking Station Board测试

        /// <summary>
        /// 执行 VC Docking Station Board 测试序列
        /// </summary>
        /// <param name="channelIndex">通道索引（0-based）</param>
        /// <param name="sn">当前测试的序列号</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>测试是否通过</returns>
        private async Task<bool> RunVCDockingStationBoard_TestSequence(int channelIndex, string sn, CancellationToken ct)
        {
            int stepRowIndex = 0;
            await RelayController.SendCommandWithCrcAsync(CommandList.CloseAllRelay_01, 38400, ComName.rs485ComName, 1000, msg => AppendLog(msg));
            await RelayController.SendCommandWithCrcAsync(CommandList.CloseAllRelay_02, 38400, ComName.rs485ComName, 1000, msg => AppendLog(msg));
            /*
             * MaxRetries=定义单个步骤测试
            -1 = 跟随系统设置 appSettings.FailRetryCount
            0 = 不重试，只执行 1 次
            1 = 失败后重试 1 次，总共最多执行 2 次
            2 = 失败后重试 2 次，总共最多执行 3 次
            3 = 失败后重试 3 次，总共最多执行 4 次
            /// <summary>
    /// -1 = 跟随系统设置
    ///  0 = 不重试
    ///  1 = 失败后重试 1 次
    ///  2 = 失败后重试 2 次
    /// </summary>
             */
            var steps = new List<(Func<CancellationToken, Task<bool>> Action, string Name, int RowIndex, int MaxRetries)>();

            // 步骤1：SN输入
            int row0 = stepRowIndex;
            steps.Add((async (token) => { await SN_Input(channelIndex, row0, sn, token); return true; }, "SN输入", row0, -1));
            stepRowIndex++;

            // 步骤2：治具下压确认
            int row1 = stepRowIndex;
            steps.Add((async (token) => { return await ConfirmFixtureDownward_FC(channelIndex, row1, token); }, "治具下压确认", row1, -1));
            stepRowIndex++;

            // 步骤3：治具下压确认
            int row2 = stepRowIndex;
            steps.Add((async (token) => { return await ConfirmFixtureDownward(channelIndex, row2, "请更换电阻测试接头", token); }, "更换电阻测试接头", row2, -1));
            stepRowIndex++;

            // 步骤4：打开烧录继电器Y1-Y5（开启）
            int row3 = stepRowIndex;
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row3, 2, 1, true, 4, 38400, token); }, "打开Y1-Y5", row3, -1));
            stepRowIndex++;

            // 步骤5：等待稳定时间
            int row4 = stepRowIndex;
            steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row4, token); }, "等待稳定时间", row4, -1));
            stepRowIndex++;

            // 步骤6：USB开路测试
            int row5 = stepRowIndex;
            steps.Add((async (token) => { return await GetResistanceValue(channelIndex, row5, 0, token); }, "电阻值1", row5, -1));
            stepRowIndex++;
            // 步骤7：USB开路测试
            int row6 = stepRowIndex;
            steps.Add((async (token) => { return await GetResistanceValue(channelIndex, row6, 1, token); }, "电阻值2", row6, -1));
            stepRowIndex++;
            // 步骤8：USB开路测试
            int row7 = stepRowIndex;
            steps.Add((async (token) => { return await GetResistanceValue(channelIndex, row7, 2, token); }, "电阻值3", row7, -1));
            stepRowIndex++;
            // 步骤9：USB开路测试
            int row8 = stepRowIndex;
            steps.Add((async (token) => { return await GetResistanceValue(channelIndex, row8, 3, token); }, "电阻值4", row8, -1));
            stepRowIndex++;
            // 步骤10：关闭烧录继电器Y1-Y5（关闭）
            int row9 = stepRowIndex;
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row9, 2, 1, false, 4, 38400, token); }, "关闭Y1-Y5", row9, -1));
            stepRowIndex++;
            // 步骤11：打开烧录继电器Y6-Y9（开启）
            int row10 = stepRowIndex;
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row10, 2, 6, true, 4, 38400, token); }, "打开Y6-Y9", row10, -1));
            stepRowIndex++;
            // 步骤12：等待稳定时间
            int row11 = stepRowIndex;
            steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row11, token); }, "等待稳定时间", row11, -1));
            stepRowIndex++;
            // 步骤13：USB开路测试
            int row12 = stepRowIndex;
            steps.Add((async (token) => { return await GetResistanceValue(channelIndex, row12, 1, token); }, "电阻值5", row12, -1));
            stepRowIndex++;
            // 步骤14：USB开路测试
            int row13 = stepRowIndex;
            steps.Add((async (token) => { return await GetResistanceValue(channelIndex, row13, 2, token); }, "电阻值6", row13, -1));
            stepRowIndex++;
            // 步骤15：USB开路测试
            int row14 = stepRowIndex;
            steps.Add((async (token) => { return await GetResistanceValue(channelIndex, row14, 3, token); }, "电阻值7", row14, -1));
            stepRowIndex++;
            // 步骤16：关闭烧录继电器Y6-Y9（关闭）
            int row15 = stepRowIndex;
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row15, 2, 6, false, 4, 38400, token); }, "关闭Y6-Y9", row15, -1));
            stepRowIndex++;
            // 步骤17：打开烧录继电器Y10-Y12（开启）
            int row16 = stepRowIndex;
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row16, 2, 10, true, 3, 38400, token); }, "打开Y10-Y12", row16, -1));
            stepRowIndex++;
            // 步骤18：等待稳定时间
            int row17 = stepRowIndex;
            steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row17, token); }, "等待稳定时间", row17, -1));
            stepRowIndex++;

            // 步骤19：USB开路测试
            int row18 = stepRowIndex;
            steps.Add((async (token) => { return await GetResistanceValue(channelIndex, row18, 0, token); }, "电阻值1", row18, -1));
            stepRowIndex++;
            // 步骤20：USB开路测试
            int row19 = stepRowIndex;
            steps.Add((async (token) => { return await GetResistanceValue(channelIndex, row19, 1, token); }, "电阻值2", row19, -1));
            stepRowIndex++;
            // 步骤21：USB开路测试
            int row20 = stepRowIndex;
            steps.Add((async (token) => { return await GetResistanceValue(channelIndex, row20, 2, token); }, "电阻值3", row20, -1));
            stepRowIndex++;

            // 步骤23：关闭烧录继电器Y10（关闭）
            int row22 = stepRowIndex;
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row22, 2, 10, false, 1, 38400, token); }, "关闭Y10", row22, -1));
            stepRowIndex++;
            // 步骤24：等待稳定时间
            int row23 = stepRowIndex;
            steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row23, token); }, "等待稳定时间", row23, -1));
            stepRowIndex++;
            // 步骤25：USB开路测试
            int row24 = stepRowIndex;
            steps.Add((async (token) => { return await GetResistanceValue(channelIndex, row24, 7, token); }, "电阻值8", row24, -1));
            stepRowIndex++;
            // 步骤26：关闭继电器Y12（关闭）
            int row25 = stepRowIndex;
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row25, 2, 12, false, 1, 38400, token); }, "关闭Y12", row25, -1));
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row25, 2, 32, true, 1, 38400, token); }, "打开Y32", row25, -1));
            stepRowIndex++;
            // 步骤27：等待稳定时间
            int row26 = stepRowIndex;
            steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row26, token); }, "等待稳定时间", row26, -1));
            stepRowIndex++;
            // 步骤28：USB开路测试
            int row27 = stepRowIndex;
            steps.Add((async (token) => { return await GetResistanceValue(channelIndex, row27, 7, token); }, "电阻值8", row27, -1));
            stepRowIndex++;
            // 步骤29：关闭继电器Y11Y32（关闭）
            int row28 = stepRowIndex;
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row28, 2, 11, false, 1, 38400, token); }, "关闭继电器Y11Y32（关闭）", row28, -1));
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row28, 2, 32, false, 1, 38400, token); }, "关闭继电器Y11Y32（关闭）", row28, -1));
            stepRowIndex++;
            // 步骤30：治具下压确认
            int row29 = stepRowIndex;
            steps.Add((async (token) => { return await ConfirmFixtureDownward(channelIndex, row29, "请更换充电测试接头", token); }, "请更换充电测试接头", row29, -1));
            stepRowIndex++;
            // 步骤31：打开继电器Y13-Y16
            int row30 = stepRowIndex;
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row30, 2, 13, true, 4, 38400, token); }, "打开Y13-1Y16", row30, -1));
            //// 步骤31：打开继电器Y1
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row30, 1, 1, true, 1, 38400, token); }, "打开Y1", row30, -1));
            stepRowIndex++;

            // 步骤32：LED检测
            int row31 = stepRowIndex;
            steps.Add((async (token) => { return await CheckLEDChannelsAsync(channelIndex, row31, token); }, "LED 4通道检测", row31, -1));
            stepRowIndex++;

            // 步骤33：读取TP1
            int row32 = stepRowIndex;
            steps.Add((async (token) => { return await GetVoltageValue(channelIndex, row32, 0, token); }, "电压值1 TP1", row32, -1));
            stepRowIndex++;

            // 步骤34：读取D1
            int row33 = stepRowIndex;
            steps.Add((async (token) => { return await GetVoltageValue(channelIndex, row33, 1, token); }, "电压值2 D1", row33, -1));
            stepRowIndex++;

            // 步骤35：读取TP3
            int row34 = stepRowIndex;
            steps.Add((async (token) => { return await GetVoltageValue(channelIndex, row34, 2, token); }, "电压值3 TP3", row34, -1));
            stepRowIndex++;

            // 步骤36：关闭继电器Y12（关闭） 
            int row35 = stepRowIndex;
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row35, 2, 17, true, 1, 38400, token); }, "打开Y17 TP4", row35, -1));
            stepRowIndex++;
            // 步骤37：等待稳定时间
            int row36 = stepRowIndex;
            steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row36, token); }, "等待稳定时间", row36, -1));
            stepRowIndex++;
            // 步骤38：测试通道1频率
            int row37 = stepRowIndex;
            steps.Add((async (token) => await MeasureFrequency(channelIndex, row37, 1, token), "频率 CH1 TP4", row37, -1));
            stepRowIndex++;
            // 步骤39：测试通道1频率
            int row38 = stepRowIndex;
            steps.Add((async (token) => await MeasurePositiveDuty(channelIndex, row38, 1, token), "正占空比 CH1 TP4", row38, -1));
            stepRowIndex++;
            // 步骤40：测试通道1频率
            int row39 = stepRowIndex;
            steps.Add((async (token) => await MeasureNegativeDuty(channelIndex, row39, 1, token), "负占空比 CH1 TP4", row39, -1));
            stepRowIndex++;
            // 步骤41：关闭继电器Y17（关闭）
            int row40 = stepRowIndex;
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row40, 2, 17, false, 1, 38400, token); }, "关闭Y17", row40, -1));
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row40, 2, 18, true, 1, 38400, token); }, "打开Y18 TP5", row40, -1));
            stepRowIndex++;
            // 步骤42：等待稳定时间
            int row41 = stepRowIndex;
            steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row41, token); }, "等待稳定时间", row41, -1));
            stepRowIndex++;
            // 步骤43：等待稳定时间
            int row42 = stepRowIndex;
            steps.Add((async (token) => await MeasureFrequency(channelIndex, row42, 2, token), "频率 CH2 TP5", row42, -1));
            stepRowIndex++;

            // 步骤44：等待稳定时间
            int row43 = stepRowIndex;
            steps.Add((async (token) => await MeasurePositiveDuty(channelIndex, row43, 2, token), "正占空比 CH2 TP5", row43, -1));
            stepRowIndex++;
            // 步骤45：等待稳定时间
            int row44 = stepRowIndex;
            steps.Add((async (token) => await MeasureNegativeDuty(channelIndex, row44, 2, token), "负占空比 CH2 TP5", row44, -1));
            stepRowIndex++;

            // 步骤46：关闭继电器Y18（关闭）
            int row45 = stepRowIndex;
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row45, 2, 18, false, 1, 38400, token); }, "关闭Y18", row45, -1));
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row45, 2, 19, true, 1, 38400, token); }, "打开Y19 TP6", row45, -1));
            stepRowIndex++;
            // 步骤47：等待稳定时间
            int row46 = stepRowIndex;
            steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row46, token); }, "等待稳定时间", row46, -1));
            stepRowIndex++;
            // 步骤48：等待稳定时间
            int row47 = stepRowIndex;
            steps.Add((async (token) => await MeasureFrequency(channelIndex, row47, 3, token), "频率 CH3 TP6", row47, -1));
            stepRowIndex++;

            // 步骤49：等待稳定时间
            int row48 = stepRowIndex;
            steps.Add((async (token) => await MeasurePositiveDuty(channelIndex, row48, 3, token), "正占空比 CH3 TP6", row48, -1));
            stepRowIndex++;
            // 步骤50：等待稳定时间
            int row49 = stepRowIndex;
            steps.Add((async (token) => await MeasureNegativeDuty(channelIndex, row49, 3, token), "负占空比 CH3 TP6", row49, -1));
            stepRowIndex++;

            // 步骤51：关闭继电器Y19（关闭）
            int row50 = stepRowIndex;
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row50, 2, 19, false, 1, 38400, token); }, "关闭Y19", row50, -1));
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row50, 2, 20, true, 1, 38400, token); }, "打开Y20 TP7", row50, -1));
            stepRowIndex++;
            // 步骤52：等待稳定时间
            int row51 = stepRowIndex;
            steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row51, token); }, "等待稳定时间", row51, -1));
            stepRowIndex++;
            // 步骤53：等待稳定时间
            int row52 = stepRowIndex;
            steps.Add((async (token) => await MeasureFrequency(channelIndex, row52, 4, token), "频率 CH4 TP7", row52, -1));
            stepRowIndex++;

            // 步骤54：等待稳定时间
            int row53 = stepRowIndex;
            steps.Add((async (token) => await MeasurePositiveDuty(channelIndex, row53, 4, token), "正占空比 CH4 TP7", row53, -1));
            stepRowIndex++;
            // 步骤55：等待稳定时间
            int row54 = stepRowIndex;
            steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row54, token); }, "等待稳定时间", row54, -1));
            stepRowIndex++;

            // 步骤56：关闭继电器Y20，打开Y21 TP8
            int row55 = stepRowIndex;
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row55, 2, 20, false, 1, 38400, token); }, "关闭Y20", row55, -1));
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row55, 2, 21, true, 1, 38400, token); }, "打开Y21 TP8", row55, -1));
            stepRowIndex++;

            // 步骤57：等待稳定时间
            int row56 = stepRowIndex;
            steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row56, token); }, "等待稳定时间", row56, -1));
            stepRowIndex++;

            // 步骤58：读取频率 CH1 TP8
            int row57 = stepRowIndex;
            steps.Add((async (token) => await MeasureFrequency(channelIndex, row57, 1, token), "频率 CH1 TP8", row57, -1));
            stepRowIndex++;

            // 步骤59：读取正占空比 CH4 TP8
            int row58 = stepRowIndex;
            steps.Add((async (token) => await MeasurePositiveDuty(channelIndex, row58, 1, token), "正占空比 CH1 TP8", row58, -1));
            stepRowIndex++;

            // 步骤60：读取负占空比 CH4 TP8
            int row59 = stepRowIndex;
            steps.Add((async (token) => await MeasureNegativeDuty(channelIndex, row59, 1, token), "负占空比 CH1 TP8", row59, -1));
            stepRowIndex++;

            // 步骤61：关闭继电器Y21，打开Y22 TP9
            int row60 = stepRowIndex;
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row60, 2, 21, false, 1, 38400, token); }, "关闭Y21", row60, -1));
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row60, 2, 22, true, 1, 38400, token); }, "打开Y22 TP9", row60, -1));
            stepRowIndex++;

            // 步骤62：等待稳定时间
            int row61 = stepRowIndex;
            steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row61, token); }, "等待稳定时间", row61, -1));
            stepRowIndex++;

            // 步骤63：读取频率 CH1 TP9
            int row62 = stepRowIndex;
            steps.Add((async (token) => await MeasureFrequency(channelIndex, row62, 2, token), "频率 CH2 TP9", row62, -1));
            stepRowIndex++;

            // 步骤64：读取正占空比 CH4 TP9
            int row63 = stepRowIndex;
            steps.Add((async (token) => await MeasurePositiveDuty(channelIndex, row63, 2, token), "正占空比 CH2 TP9", row63, -1));
            stepRowIndex++;

            // 步骤65：读取负占空比 CH4 TP9
            int row64 = stepRowIndex;
            steps.Add((async (token) => await MeasureNegativeDuty(channelIndex, row64, 2, token), "负占空比 CH2 TP9", row64, -1));
            stepRowIndex++;

            // 步骤66：关闭继电器Y22，打开Y23 TP10
            int row65 = stepRowIndex;
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row65, 2, 22, false, 1, 38400, token); }, "关闭Y22", row65, -1));
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row65, 2, 23, true, 1, 38400, token); }, "打开Y23 TP10", row65, -1));
            stepRowIndex++;

            // 步骤67：等待稳定时间
            int row66 = stepRowIndex;
            steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row66, token); }, "等待稳定时间", row66, -1));
            stepRowIndex++;

            // 步骤68：读取频率 CH3 TP10
            int row67 = stepRowIndex;
            steps.Add((async (token) => await MeasureFrequency(channelIndex, row67, 3, token), "频率 CH3 TP10", row67, -1));
            stepRowIndex++;

            // 步骤69：读取正占空比 CH3 TP10
            int row68 = stepRowIndex;
            steps.Add((async (token) => await MeasurePositiveDuty(channelIndex, row68, 3, token), "正占空比 CH3 TP10", row68, -1));
            stepRowIndex++;

            // 步骤70：读取负占空比 CH3 TP10
            int row69 = stepRowIndex;
            steps.Add((async (token) => await MeasureNegativeDuty(channelIndex, row69, 3, token), "负占空比 CH3 TP10", row69, -1));
            stepRowIndex++;

            // 步骤71：获取无线充电电压值
            int row70 = stepRowIndex;
            steps.Add((async (token) => { return await GetInputVolt(channelIndex, row70, token); }, "获取无线充电电压值", row70, -1));
            stepRowIndex++;

            // 步骤72：获取无线充电电流值
            int row71 = stepRowIndex;
            steps.Add((async (token) => { return await GetInputCurrent(channelIndex, row71, token); }, "获取无线充电电流值", row71, -1));
            stepRowIndex++;


            int totalSteps = steps.Count;
            int currentStep = 0;
            bool allPassed = true;

            foreach (var step in steps)
            {
                currentStep++;

                bool pass = await ExecuteTestStepAsync(
                    channelIndex,
                    step.Action,
                    step.Name,
                    step.RowIndex,
                    ct,
                    currentStep,
                    totalSteps,
                    maxRetries: step.MaxRetries);

                if (!pass)
                {
                    allPassed = false;
                    if (appSettings.StopOnFail) break;
                }
            }
            await RelayController.SendCommandWithCrcAsync(CommandList.CloseAllRelay_01, 38400, ComName.rs485ComName, 1000, msg => AppendLog(msg));
            await RelayController.SendCommandWithCrcAsync(CommandList.CloseAllRelay_02, 38400, ComName.rs485ComName, 1000, msg => AppendLog(msg));
            return allPassed;
        }

        /// <summary>
        /// SN 输入步骤
        /// </summary>
        private async Task SN_Input(int channelIndex, int rowIndex, string sn, CancellationToken cancellationToken)
        {
            try
            {
                DataTable dt = ProjectSettings.testDataTable;
                if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
                {
                    AppendLog($"无效的行索引: {rowIndex}", LogError);
                    throw new InvalidOperationException("无效的行索引");
                }

                DataRow row = dt.Rows[rowIndex];
                bool isSelected = Convert.ToBoolean(row["Select"]);

                if (!isSelected)
                {
                    AppendLog($"第 {rowIndex + 1} 行未勾选，跳过 SN 输入步骤", LogInfo);
                    return; // 跳过，不更新任何值
                }

                string valueColumn = $"Channel{channelIndex + 1}Value";
                string resultColumn = $"Channel{channelIndex + 1}Result";

                // 更新 UI 必须在 UI 线程
                await Dispatcher.InvokeAsync(() =>
                {
                    row[valueColumn] = sn;
                    row[resultColumn] = "PASS";
                });

                AppendLog($"SN {sn} 已记录到通道 {channelIndex + 1} 第 {rowIndex + 1} 行", LogSuccess);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                AppendLog($"SN 输入错误：{ex.Message}", LogError);
                throw; // 让上层处理重试
            }
        }
        private async Task<bool> ConfirmFixtureDownward(int channelIndex, int rowIndex, string tip, CancellationToken cancellationToken)
        {
            try
            {
                DataTable dt = ProjectSettings.testDataTable;
                if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
                {
                    AppendLog($"无效的行索引: {rowIndex}", LogError);
                    throw new InvalidOperationException("无效的行索引");
                }
                DataRow row = dt.Rows[rowIndex];
                bool isSelected = Convert.ToBoolean(row["Select"]);

                if (!isSelected)
                {
                    AppendLog($"第 {rowIndex + 1} 行未勾选，跳过治具下压确认步骤", LogInfo);
                    return true;
                }

                string valueColumn = $"Channel{channelIndex + 1}Value";
                string resultColumn = $"Channel{channelIndex + 1}Result";
                //string message= tip
                if (String.IsNullOrEmpty(tip))
                {
                    tip = "操作确认！";
                }
                // WPF 中使用 MessageBoxResult
                MessageBoxResult result = MessageBox.Show(
                    $"{tip}",
                    "操作提示",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    // 用户确认
                    await Dispatcher.InvokeAsync(() =>
                    {
                        row[valueColumn] = result.ToString();
                        row[resultColumn] = "PASS";
                    });
                    AppendLog($"通道 {channelIndex + 1} 操作员已确认", LogSuccess);
                    //return true;
                }
                else
                {
                    // 用户确认已下压，更新测试值和结果（示例）
                    await Dispatcher.InvokeAsync(() =>
                    {
                        row[valueColumn] = result.ToString();
                        row[resultColumn] = "NG";
                    });
                    // 用户取消，可视为测试失败或跳过
                    AppendLog($"通道 {channelIndex + 1} 治具未下压，测试中止", LogError);
                    return false;
                }

                await Task.CompletedTask;
                return true;
            }
            catch (Exception ex)
            {
                AppendLog($"确认错误：{ex.Message}", LogError);
                return false;
            }
        }
        /// <summary>
        /// 读取指定索引的电阻值，并更新到 DataGrid 指定行
        /// </summary>
        /// <param name="channelIndex">测试通道索引（0-based）</param>
        /// <param name="rowIndex">DataGrid 行索引</param>
        /// <param name="valueIndex">要读取的值在返回列表中的索引（0-based，0~7）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否通过（PASS/FAIL）</returns>
        private async Task<bool> GetResistanceValue(int channelIndex, int rowIndex, int valueIndex, CancellationToken cancellationToken)
        {
            try
            {
                DataTable dt = ProjectSettings.testDataTable;
                if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
                {
                    AppendLog($"无效的行索引: {rowIndex}", LogError);
                    return false;
                }

                DataRow row = dt.Rows[rowIndex];
                bool isSelected = Convert.ToBoolean(row["Select"]);
                if (!isSelected)
                {
                    AppendLog($"第 {rowIndex + 1} 行未勾选，跳过电阻值测量", LogInfo);
                    await UpdateResistanceResult(channelIndex, row, "跳过", true);
                    return true;
                }

                // 读取全部8个电阻值（内部会做错误处理）
                List<float> allValues = await ReadAllResistanceValuesAsync(cancellationToken);
                if (allValues == null)
                {
                    await UpdateResistanceResult(channelIndex, row, "读取失败", false);
                    return false;
                }

                if (valueIndex < 0 || valueIndex >= allValues.Count)
                {
                    AppendLog($"值索引 {valueIndex} 超出范围（0-{allValues.Count - 1}）", LogError);
                    await UpdateResistanceResult(channelIndex, row, "索引错误", false);
                    return false;
                }

                float value = allValues[valueIndex];

                // 解析上下限
                double upper = 0, lower = 0;
                bool limitValid = true;
                try
                {
                    string upperStr = row["UpperLimit"]?.ToString().Trim();
                    string lowerStr = row["LowerLimit"]?.ToString().Trim();
                    if (string.IsNullOrEmpty(upperStr) || string.IsNullOrEmpty(lowerStr))
                    {
                        AppendLog($"第 {rowIndex + 1} 行上下限为空，跳过该项判定", LogWarning);
                        limitValid = false;
                    }
                    else
                    {
                        upper = Convert.ToDouble(upperStr);
                        lower = Convert.ToDouble(lowerStr);
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"第 {rowIndex + 1} 行上下限值无效: {row["UpperLimit"]} / {row["LowerLimit"]}, 错误: {ex.Message}", LogError);
                    limitValid = false;
                }

                bool pass = limitValid ? (value >= lower && value <= upper) : false;
                string displayValue = value.ToString("F0"); // 保留整数，可根据需要调整格式

                await UpdateResistanceResult(channelIndex, row, displayValue, pass);
                AppendLog($"电阻值 [{valueIndex}]: {displayValue} {(pass ? "PASS" : "FAIL")}", pass ? LogSuccess : LogError);
                return pass;
            }
            catch (Exception ex)
            {
                AppendLog($"获取电阻值异常: {ex.Message}", LogError);
                return false;
            }
        }

        /// <summary>
        /// 更新电阻测试结果到 DataGrid
        /// </summary>
        private async Task UpdateResistanceResult(int channelIndex, DataRow row, string value, bool pass)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                string valueColumn = $"Channel{channelIndex + 1}Value";
                string resultColumn = $"Channel{channelIndex + 1}Result";
                row[valueColumn] = value;
                row[resultColumn] = pass ? "PASS" : "FAIL";
            });
        }

        /// <summary>
        /// 读取所有8个通道的电阻值（复用之前的 ReadAllResistanceValuesAsync）
        /// </summary>
        private async Task<List<float>> ReadAllResistanceValuesAsync(CancellationToken cancellationToken)
        {
            try
            {
                var values = await RelayController.ReadResistanceValuesAsync(
                    ComName.rs485ComName, 9600, CommandList.ReadOhmValue_03,
                    msg => AppendLog(msg, LogInfo));

                if (values == null || values.Count == 0)
                {
                    AppendLog("读取电阻值失败：无响应数据", LogError);
                    return null;
                }

                // 确保返回8个值，不足的补0（根据实际情况可调整）
                while (values.Count < 8)
                    values.Add(0);

                return values;
            }
            catch (Exception ex)
            {
                AppendLog($"读取电阻值异常: {ex.Message}", LogError);
                return null;
            }
        }
        /// <summary>
        /// 读取 LED 分析仪全部 4 个通道的 RGBI 数据
        /// </summary>
        /// <returns>List of (R,G,B,Brightness) for each channel (4 items)</returns>
        private async Task<List<(int R, int G, int B, int Brightness)>> ReadAllRGBIAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(ComName.ledComName))
            {
                AppendLog("LED 串口未配置", LogError);
                return null;
            }

            using (SerialPort port = new SerialPort(ComName.ledComName, 57600, Parity.None, 8, StopBits.One))
            {
                try
                {
                    port.ReadTimeout = 2000;
                    port.WriteTimeout = 1000;
                    port.Open();
                    port.DiscardInBuffer();
                    port.Write("Getallrgbi\r\n");
                    AppendLog("发送命令: Getallrgbi", LogInfo);

                    string response = await Task.Run(() => port.ReadLine()).ConfigureAwait(false);
                    AppendLog($"收到响应: {response}", LogInfo);

                    // 解析: 空格分割，每4个一组，共4组 (通道1~4)
                    string[] parts = response.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    var result = new List<(int, int, int, int)>();
                    for (int i = 0; i < 4; i++)
                    {
                        int idx = i * 4;
                        if (idx + 3 >= parts.Length) break;
                        if (int.TryParse(parts[idx], out int r) &&
                            int.TryParse(parts[idx + 1], out int g) &&
                            int.TryParse(parts[idx + 2], out int b) &&
                            int.TryParse(parts[idx + 3], out int brightness))
                        {
                            result.Add((r, g, b, brightness));
                        }
                        else
                        {
                            AppendLog($"解析通道{i + 1}数据失败: {string.Join(" ", parts.Skip(idx).Take(4))}", LogError);
                            return null;
                        }
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    AppendLog($"LED 读取失败: {ex.Message}", LogError);
                    return null;
                }
            }
        }

        /// <summary>
        /// 验证所有通道与配置的上下限，返回每个通道是否通过
        /// </summary>
        private (bool allPass, List<bool> channelPass) ValidateRGBIAgainstConfig(List<(int R, int G, int B, int Brightness)> data)
        {
            if (data == null || data.Count != 4) return (false, null);
            bool allPass = true;
            var channelPass = new List<bool>();
            for (int i = 0; i < 4; i++)
            {
                var config = _ledConfig.Channels[i];
                var (r, g, b, brightness) = data[i];
                bool pass = true;
                // 如果上下限都为0，则跳过该项检测（视为通过）；否则比较
                if (config.RedLower > 0 || config.RedUpper > 0)
                    pass = pass && (r >= config.RedLower && r <= config.RedUpper);
                if (config.GreenLower > 0 || config.GreenUpper > 0)
                    pass = pass && (g >= config.GreenLower && g <= config.GreenUpper);
                if (config.BlueLower > 0 || config.BlueUpper > 0)
                    pass = pass && (b >= config.BlueLower && b <= config.BlueUpper);
                if (config.BrightnessLower > 0 || config.BrightnessUpper > 0)
                    pass = pass && (brightness >= config.BrightnessLower && brightness <= config.BrightnessUpper);
                channelPass.Add(pass);
                if (!pass) allPass = false;
            }
            return (allPass, channelPass);
        }

        /// <summary>
        /// 测试步骤中调用：读取全部4通道 RGBI 并验证，更新指定行的结果（整体判定）
        /// </summary>
        /// <param name="channelIndex">测试通道索引（用于 DataGrid 列前缀）</param>
        /// <param name="rowIndex">要更新的行索引</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否全部通过</returns>
        private async Task<bool> CheckLEDChannelsAsync(int channelIndex, int rowIndex, CancellationToken cancellationToken)
        {
            DataTable dt = ProjectSettings.testDataTable;
            if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
            {
                AppendLog($"无效的行索引: {rowIndex}", LogError);
                return false;
            }

            DataRow row = dt.Rows[rowIndex];
            if (!Convert.ToBoolean(row["Select"]))
            {
                AppendLog($"第 {rowIndex + 1} 行未勾选，跳过 LED 检测", LogInfo);
                await UpdateLEDResult(channelIndex, row, "跳过", true);
                return true;
            }

            // 读取数据
            var data = await ReadAllRGBIAsync(cancellationToken);
            if (data == null)
            {
                await UpdateLEDResult(channelIndex, row, "读取失败", false);
                return false;
            }

            // 验证
            var (allPass, channelPass) = ValidateRGBIAgainstConfig(data);
            // 输出详细日志
            for (int i = 0; i < 4; i++)
            {
                var (r, g, b, br) = data[i];
                AppendLog($"通道{i + 1}: R={r}, G={g}, B={b}, 亮度={br} => {(channelPass[i] ? "PASS" : "FAIL")}",
                          channelPass[i] ? LogSuccess : LogError);
            }

            string displayValue = allPass ? "4通道全部通过" : "存在失败通道";
            await UpdateLEDResult(channelIndex, row, displayValue, allPass);
            return allPass;
        }

        // 辅助更新方法（与之前类似）
        private async Task UpdateLEDResult(int channelIndex, DataRow row, string value, bool pass)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                string valueColumn = $"Channel{channelIndex + 1}Value";
                string resultColumn = $"Channel{channelIndex + 1}Result";
                row[valueColumn] = value;
                row[resultColumn] = pass ? "PASS" : "FAIL";
            });
        }
        /// <summary>
        /// 读取指定索引的电压值，与 DataGrid 当前行的上下限比较，并更新该行结果
        /// </summary>
        /// <param name="channelIndex">测试通道索引（0-based）</param>
        /// <param name="rowIndex">DataGrid 行索引</param>
        /// <param name="valueIndex">要取的电压索引（0~7）</param>
        /// <param name="token">取消令牌</param>
        /// <returns>是否通过</returns>
        private async Task<bool> GetVoltageValue(int channelIndex, int rowIndex, int valueIndex, CancellationToken token)
        {
            DataTable dt = ProjectSettings.testDataTable;
            if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
            {
                AppendLog($"无效的行索引: {rowIndex}", LogError);
                return false;
            }

            DataRow row = dt.Rows[rowIndex];
            if (!Convert.ToBoolean(row["Select"]))
            {
                AppendLog($"第 {rowIndex + 1} 行未勾选，跳过电压测量", LogInfo);
                await UpdateVoltageResult(channelIndex, row, "跳过", true);
                return true;
            }

            // 不需要乘系数（使用原始值）
            double? voltage = await RelayController.ReadVoltageValueAsync(
                ComName.rs485ComName, 9600, "DA DB DC DC 02 CC", valueIndex,
                msg => AppendLog(msg, LogInfo), 2000);

            if (voltage == null)
            {
                await UpdateVoltageResult(channelIndex, row, "读取失败", false);
                return false;
            }

            // 解析上下限
            double upper = 0, lower = 0;
            bool limitValid = true;
            try
            {
                string upperStr = row["UpperLimit"]?.ToString().Trim();
                string lowerStr = row["LowerLimit"]?.ToString().Trim();
                if (string.IsNullOrEmpty(upperStr) || string.IsNullOrEmpty(lowerStr))
                {
                    AppendLog($"第 {rowIndex + 1} 行上下限为空，跳过判定", LogWarning);
                    limitValid = false;
                }
                else
                {
                    upper = Convert.ToDouble(upperStr);
                    lower = Convert.ToDouble(lowerStr);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"上下限解析错误: {ex.Message}", LogError);
                limitValid = false;
            }

            bool pass = limitValid ? (voltage.Value >= lower && voltage.Value <= upper) : false;
            string displayValue = voltage.Value.ToString("F2");
            await UpdateVoltageResult(channelIndex, row, displayValue, pass);
            AppendLog($"电压值[{valueIndex}]: {displayValue} {(pass ? "PASS" : "FAIL")}", pass ? LogSuccess : LogError);
            return pass;
        }

        /// <summary>读取频率并更新到 DataGrid 指定行</summary>
        private async Task<bool> MeasureFrequency(int channelIndex, int rowIndex, int scopeChannel, CancellationToken token)
        {
            DataTable dt = ProjectSettings.testDataTable;
            if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count) return false;
            DataRow row = dt.Rows[rowIndex];
            if (!Convert.ToBoolean(row["Select"]))
            {
                await UpdateScopeResult(channelIndex, row, "跳过", true);
                return true;
            }

            double value = await _scope.GetFrequencyRawAsync(scopeChannel, msg => AppendLog(msg, LogInfo));
            if (double.IsNaN(value))
            {
                await UpdateScopeResult(channelIndex, row, "无信号", false);
                return false;
            }

            bool limitValid = ParseLimits(row, out double lower, out double upper);
            bool pass = limitValid ? (value >= lower && value <= upper) : false;
            string displayValue = FormatFrequency(value); // 自己写一个简单格式化方法，或者使用 _scope 中的格式化方法（需改为 public）
            await UpdateScopeResult(channelIndex, row, displayValue, pass);
            AppendLog($"CH{scopeChannel}频率: {displayValue} {(pass ? "PASS" : "FAIL")}", pass ? LogSuccess : LogError);
            return pass;
        }

        /// <summary>读取正占空比并更新到 DataGrid</summary>
        private async Task<bool> MeasurePositiveDuty(int channelIndex, int rowIndex, int scopeChannel, CancellationToken token)
        {
            DataTable dt = ProjectSettings.testDataTable;
            if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count) return false;
            DataRow row = dt.Rows[rowIndex];
            if (!Convert.ToBoolean(row["Select"]))
            {
                await UpdateScopeResult(channelIndex, row, "跳过", true);
                return true;
            }

            double value = await _scope.GetPositiveDutyRawAsync(scopeChannel, msg => AppendLog(msg, LogInfo));
            if (double.IsNaN(value))
            {
                await UpdateScopeResult(channelIndex, row, "无信号", false);
                return false;
            }

            bool limitValid = ParseLimits(row, out double lower, out double upper);
            bool pass = limitValid ? (value >= lower && value <= upper) : false;
            string displayValue = $"{value:F2} %";
            await UpdateScopeResult(channelIndex, row, displayValue, pass);
            AppendLog($"CH{scopeChannel}正占空比: {displayValue} {(pass ? "PASS" : "FAIL")}", pass ? LogSuccess : LogError);
            return pass;
        }

        /// <summary>读取负占空比并更新到 DataGrid</summary>
        private async Task<bool> MeasureNegativeDuty(int channelIndex, int rowIndex, int scopeChannel, CancellationToken token)
        {
            DataTable dt = ProjectSettings.testDataTable;
            if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count) return false;
            DataRow row = dt.Rows[rowIndex];
            if (!Convert.ToBoolean(row["Select"]))
            {
                await UpdateScopeResult(channelIndex, row, "跳过", true);
                return true;
            }

            double value = await _scope.GetNegativeDutyRawAsync(scopeChannel, msg => AppendLog(msg, LogInfo));
            if (double.IsNaN(value))
            {
                await UpdateScopeResult(channelIndex, row, "无信号", false);
                return false;
            }

            bool limitValid = ParseLimits(row, out double lower, out double upper);
            bool pass = limitValid ? (value >= lower && value <= upper) : false;
            string displayValue = $"{value:F2} %";
            await UpdateScopeResult(channelIndex, row, displayValue, pass);
            AppendLog($"CH{scopeChannel}负占空比: {displayValue} {(pass ? "PASS" : "FAIL")}", pass ? LogSuccess : LogError);
            return pass;
        }

        // 辅助：解析上下限
        private bool ParseLimits(DataRow row, out double lower, out double upper)
        {
            lower = upper = 0;
            try
            {
                string lowerStr = row["LowerLimit"]?.ToString().Trim();
                string upperStr = row["UpperLimit"]?.ToString().Trim();
                if (string.IsNullOrEmpty(lowerStr) || string.IsNullOrEmpty(upperStr)) return false;
                lower = Convert.ToDouble(lowerStr);
                upper = Convert.ToDouble(upperStr);
                return true;
            }
            catch { return false; }
        }

        // 辅助：更新 DataGrid 结果
        private async Task UpdateScopeResult(int channelIndex, DataRow row, string value, bool pass)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                string valueColumn = $"Channel{channelIndex + 1}Value";
                string resultColumn = $"Channel{channelIndex + 1}Result";
                row[valueColumn] = value;
                row[resultColumn] = pass ? "PASS" : "FAIL";
            });
        }

        // 辅助：格式化频率（可选，与 RigolDHO804Scope 中的 FormatFrequency 逻辑相同）
        private string FormatFrequency(double hz)
        {
            if (double.IsNaN(hz)) return "无效";
            if (hz >= 1e9) return $"{(hz / 1e9):F3} GHz";
            if (hz >= 1e6) return $"{(hz / 1e6):F3} MHz";
            if (hz >= 1e3) return $"{(hz / 1e3):F3} kHz";
            return $"{hz:F3} Hz";
        }

        #endregion VC Docking测试 END

        #region FC 840300-52 REV3烧录
        /// <summary>
        /// 执行 FC 840300-52 REV3烧录序列
        /// </summary>
        /// <param name="channelIndex">通道索引（0-based）</param>
        /// <param name="sn">当前测试的序列号</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>测试是否通过</returns>
        private async Task<bool> RunFC_840300_52_REV3_TestSequence(int channelIndex, string sn, CancellationToken ct)
        {
            int stepRowIndex = 0;

            /*
             * MaxRetries=定义单个步骤测试
            -1 = 跟随系统设置 appSettings.FailRetryCount
            0 = 不重试，只执行 1 次
            1 = 失败后重试 1 次，总共最多执行 2 次
            2 = 失败后重试 2 次，总共最多执行 3 次
            3 = 失败后重试 3 次，总共最多执行 4 次
            /// <summary>
    /// -1 = 跟随系统设置
    ///  0 = 不重试
    ///  1 = 失败后重试 1 次
    ///  2 = 失败后重试 2 次
    /// </summary>
             */
            var steps = new List<(Func<CancellationToken, Task<bool>> Action, string Name, int RowIndex, int MaxRetries)>();

            // 步骤1：SN输入
            int row0 = stepRowIndex;
            steps.Add((async (token) => { await SN_Input(channelIndex, row0, sn, token); return true; }, "SN输入", row0, -1));
            stepRowIndex++;

            // 步骤2：治具下压确认
            int row1 = stepRowIndex;
            steps.Add((async (token) => { return await ConfirmFixtureDownward_FC(channelIndex, row1, token); }, "治具下压确认", row1, -1));
            stepRowIndex++;
            // 步骤3：烧录MCU U6 P5连接器
            int row2 = stepRowIndex;
            await WaitDialog.WaitOrThrowAsync("初始化中,请稍候...\r\nInitializing... Please wait a moment...", 2, this);
            string u6Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProjectConfig", "FC 840300-52 REV3 BrunConfig", "BurnScript_P5", "flash.bat");//
            steps.Add((async (token) => { return await BurnFirmware(channelIndex, row2, u6Path, token); }, "通过P5烧录U6芯片", row2, -1));
            stepRowIndex++;
            // 步骤4：烧录MCU U6 P5连接器
            int row3 = stepRowIndex;

            steps.Add((async (token) => { return await OpendAllRelay(channelIndex, row3, token); }, "打开所有继电器", row3, -1));
            stepRowIndex++;
            // 步骤5：烧录MCU U20 P3连接器
            int row4 = stepRowIndex;
            await WaitDialog.WaitOrThrowAsync("初始化中,请稍候...\r\nInitializing... Please wait a moment...", 2, this);
            string u20Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProjectConfig", "FC 840300-52 REV3 BrunConfig", "BurnScript_P3", "flash.bat");
            steps.Add((async (token) => { return await BurnFirmware(channelIndex, row4, u20Path, token); }, "通过P3烧录U20芯片", row4, -1));
            stepRowIndex++;

            // 步骤6：烧录MCU U16 P2连接器
            int row5 = stepRowIndex;
            await WaitDialog.WaitOrThrowAsync("初始化中,请稍候...\r\nInitializing... Please wait a moment...", 2, this);
            string u16Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProjectConfig", "FC 840300-52 REV3 BrunConfig", "BurnScript_P2", "flash.bat");
            steps.Add((async (token) => { return await BurnFirmware(channelIndex, row5, u16Path, token); }, "通过P2烧录U16芯片", row5, -1));
            stepRowIndex++;

            int totalSteps = steps.Count;
            int currentStep = 0;
            bool allPassed = true;

            foreach (var step in steps)
            {
                currentStep++;

                bool pass = await ExecuteTestStepAsync(
                    channelIndex,
                    step.Action,
                    step.Name,
                    step.RowIndex,
                    ct,
                    currentStep,
                    totalSteps,
                    maxRetries: step.MaxRetries);

                if (!pass)
                {
                    allPassed = false;
                    if (appSettings.StopOnFail) break;
                }
            }

            await RelayController.SendCommandWithCrcAsync(CommandList.CloseAllRelay_01, 38400, ComName.rs485ComName, 1000, msg => AppendLog(msg));
            return allPassed;
        }
        /// <summary>
        /// 自动检测治具下压状态，并更新 DataGrid
        /// </summary>
        /// <returns>下压成功返回 true，超时/失败返回 false</returns>
        private async Task<bool> ConfirmFixtureDownward_FC(
    int channelIndex,
    int rowIndex,
    CancellationToken cancellationToken,
    bool openrelay = true)
        {
            try
            {
                DataTable dt = ProjectSettings.testDataTable;

                if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
                {
                    AppendLog($"无效的行索引: {rowIndex}", LogError);
                    return false;
                }

                DataRow row = dt.Rows[rowIndex];

                bool isSelected = Convert.ToBoolean(row["Select"]);

                if (!isSelected)
                {
                    AppendLog($"第 {rowIndex + 1} 行未勾选，跳过治具下压检测步骤", LogInfo);
                    return true;
                }

                string valueColumn = $"Channel{channelIndex + 1}Value";
                string resultColumn = $"Channel{channelIndex + 1}Result";

                await Dispatcher.InvokeAsync(() =>
                {
                    if (dt.Columns.Contains(valueColumn))
                        row[valueColumn] = "等待01";

                    if (dt.Columns.Contains(resultColumn))
                        row[resultColumn] = "测试中";
                });

                FixtureDownWaitSession session = JoinOrCreateFixtureDownSession(channelIndex, rowIndex, openrelay);

                // 所有通道都等待同一个串口检测结果。
                // 收到 00 不失败，直到收到 01 或 60 秒超时。
                bool isPressed = await WaitFixtureSessionForCallerAsync(session, cancellationToken);

                if (isPressed)
                {
                    AppendLog($"通道 {channelIndex + 1} 治具已下压确认", LogSuccess);
                }
                else
                {
                    AppendLog($"通道 {channelIndex + 1} 治具下压超时或取消", LogError);
                }

                return isPressed;
            }
            catch (Exception ex)
            {
                AppendLog($"治具下压检测错误：{ex.Message}", LogError);
                return false;
            }
        }
        private async Task<bool> WaitFixtureSessionForCallerAsync(
    FixtureDownWaitSession session,
    CancellationToken cancellationToken)
        {
            Task cancelTask = Task.Delay(Timeout.Infinite, cancellationToken);
            Task completedTask = await Task.WhenAny(session.WaitTask, cancelTask);

            if (completedTask == cancelTask)
                throw new OperationCanceledException(cancellationToken);

            return await session.WaitTask;
        }
        /// <summary>
        /// 执行烧录脚本，并判断结果
        /// </summary>
        private async Task<bool> BurnFirmware(int channelIndex, int rowIndex, string batFilePath, CancellationToken cancellationToken)
        {
            try
            {
                // 检查当前行是否勾选
                DataTable dt = ProjectSettings.testDataTable;
                if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
                {
                    AppendLog($"无效的行索引: {rowIndex}", LogError);
                    return false;
                }
                DataRow row = dt.Rows[rowIndex];
                bool isSelected = Convert.ToBoolean(row["Select"]);
                if (!isSelected)
                {
                    AppendLog($"第 {rowIndex + 1} 行未勾选，跳过烧录步骤", LogInfo);
                    await UpdateBurnResult(channelIndex, rowIndex, true, "Skip");
                    return true; // 未勾选视为通过
                }

                //await WaitDialog.WaitOrThrowAsync("初始化中,请稍候...\r\nInitializing... Please wait a moment...", 2, this);
                if (!File.Exists(batFilePath))
                {
                    AppendLog($"烧录脚本不存在: {batFilePath}", LogError);
                    await UpdateBurnResult(channelIndex, rowIndex, false, "脚本文件不存在");
                    return false;
                }

                AppendLog($"开始执行烧录: {batFilePath}", LogInfo);

                var startInfo = new ProcessStartInfo
                {
                    FileName = batFilePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(batFilePath)
                };

                using (var process = new System.Diagnostics.Process { StartInfo = startInfo })
                {
                    var outputBuilder = new StringBuilder();
                    var errorBuilder = new StringBuilder();

                    process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    await Task.Run(() => process.WaitForExit(), cancellationToken);

                    int exitCode = process.ExitCode;
                    string stdout = outputBuilder.ToString();
                    string stderr = errorBuilder.ToString();

                    string combinedOutput = stdout + "\n" + stderr;

                    // 错误模式和白名单（保持不变）
                    string[] errorPatterns = {
                @"^\*\*\*\* Error:",  @"Verify failed", @"programming failed",
                @"failed to erase", @"timeout", @"cannot", @"abort", @"not found", @"unable"
            };
                    string[] ignorePatterns = {
                @"NoException", @"Failed to attach to CPU", @"Trying connect under reset",
                @"RESET \(pin 15\) high", @"SWD speed too high", @"Invalid flash header detected",
                @"Cannot connect to target",
                @"^ERROR:",
            };

                    bool hasError = false;
                    string failReason = "";

                    if (exitCode != 0)
                    {
                        hasError = true;
                        failReason = $"退出代码 {exitCode}";
                    }
                    else
                    {
                        var lines = combinedOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                        foreach (var line in lines)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;

                            bool shouldIgnore = false;
                            foreach (var ignore in ignorePatterns)
                            {
                                if (Regex.IsMatch(line, ignore, RegexOptions.IgnoreCase))
                                {
                                    shouldIgnore = true;
                                    break;
                                }
                            }
                            if (shouldIgnore) continue;

                            foreach (var pattern in errorPatterns)
                            {
                                if (Regex.IsMatch(line, pattern, RegexOptions.IgnoreCase))
                                {
                                    hasError = true;
                                    failReason = $"输出中包含错误: {line.Trim()}";
                                    break;
                                }
                            }
                            if (hasError) break;
                        }

                        if (!hasError)
                        {
                            // 1. 必须包含 "O.K." (J-Link 通用成功标志)
                            bool hasOk = combinedOutput.Contains("O.K.");

                            // 2. 必须包含 "Erasing" 或 "erase" 相关的完成标志
                            // J-Link 擦除成功通常会输出 "Erasing range ..." 或 "Erase successful"
                            bool hasEraseActivity = combinedOutput.Contains("Erasing") ||
                                                   combinedOutput.Contains("Erase") ||
                                                   combinedOutput.Contains("Total time needed");

                            if (!hasOk || !hasEraseActivity)
                            {
                                hasError = true;
                                failReason = "烧录未完成（缺少成功标志）";
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(stdout))
                        AppendLog($"烧录输出:\n{stdout.Trim()}", LogInfo);
                    if (!string.IsNullOrEmpty(stderr))
                        AppendLog($"烧录错误流:\n{stderr.Trim()}", LogError);

                    if (!hasError)
                    {
                        AppendLog($"烧录成功 (退出码 {exitCode})", LogSuccess);
                        await UpdateBurnResult(channelIndex, rowIndex, true, "成功");
                        return true;
                    }
                    else
                    {
                        AppendLog($"烧录失败: {failReason}", LogError);
                        string errorPreview = combinedOutput.Length > 200 ? combinedOutput.Substring(0, 5) : combinedOutput;
                        if (errorPreview.Length > 5)
                        {
                            errorPreview = errorPreview.Substring(0, 5);
                        }
                        await UpdateBurnResult(channelIndex, rowIndex, false, $"失败: {errorPreview}");
                        return false;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                AppendLog($"烧录被取消", LogWarning);
                await UpdateBurnResult(channelIndex, rowIndex, false, "取消");
                return false;
            }
            catch (Exception ex)
            {
                AppendLog($"烧录异常: {ex.Message}", LogError);
                await UpdateBurnResult(channelIndex, rowIndex, false, ex.Message);
                return false;
            }
        }
        /// <summary>
        /// 更新烧录结果到 DataGrid
        /// </summary>
        private async Task UpdateBurnResult(int channelIndex, int rowIndex, bool success, string message)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                DataTable dt = ProjectSettings.testDataTable;
                if (dt != null && rowIndex >= 0 && rowIndex < dt.Rows.Count)
                {
                    DataRow row = dt.Rows[rowIndex];
                    string valueColumn = $"Channel{channelIndex + 1}Value";
                    string resultColumn = $"Channel{channelIndex + 1}Result";
                    // 成功时显示传入的消息（如 "成功" 或 "Skip"），失败时显示错误信息
                    row[valueColumn] = message;
                    row[resultColumn] = success ? "PASS" : "FAIL";
                }
            });
        }

        /// <summary>
        /// 打开所有继电器，并与预期响应比较（从当前行的上限/下限读取期望值）
        /// </summary>
        private async Task<bool> OpendAllRelay(int channelIndex, int rowIndex, CancellationToken cancellationToken)
        {
            try
            {
                DataTable dt = ProjectSettings.testDataTable;
                if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
                {
                    AppendLog($"无效的行索引: {rowIndex}", LogError);
                    return false;
                }
                DataRow row = dt.Rows[rowIndex];
                bool isSelected = Convert.ToBoolean(row["Select"]);
                if (!isSelected)
                {
                    AppendLog($"第 {rowIndex + 1} 行未勾选，跳过打开所有继电器步骤", LogInfo);
                    return true; // 未勾选视为跳过，不影响整体测试结果
                }

                // 从 DataTable 读取上限和下限作为预期响应（去除空格，转为大写）
                string upperExpect = row["UpperLimit"]?.ToString()?.Trim().Replace(" ", "").ToUpper() ?? "";
                string lowerExpect = row["LowerLimit"]?.ToString()?.Trim().Replace(" ", "").ToUpper() ?? "";

                // 优先使用上限，若未填则使用下限，若都未填则使用默认预期（命令回显）
                string expectedResponse = "";
                if (!string.IsNullOrEmpty(upperExpect))
                    expectedResponse = upperExpect;
                else if (!string.IsNullOrEmpty(lowerExpect))
                    expectedResponse = lowerExpect;
                else
                {
                    // 默认预期响应 = 发送的命令（含CRC），大多数 Modbus 写命令会回显请求帧
                    byte[] fullCommand = RelayController.BuildCommandWithCrc(CommandList.OpenAllRelay_01);
                    expectedResponse = BytesToHex(fullCommand);
                    AppendLog($"未配置上下限，使用默认预期响应: {expectedResponse}", LogWarning);
                }

                // 发送命令并获取实际响应（十六进制字符串，无空格）
                string actualResponse = await RelayController.SendCommandWithCrcAsync(
                    CommandList.OpenAllRelay_01, 38400, ComName.rs485ComName, 1000, msg => AppendLog(msg));

                if (string.IsNullOrEmpty(actualResponse))
                {
                    AppendLog($"打开所有继电器失败：无响应", LogError);
                    await UpdateRelayResult(channelIndex, row, actualResponse, expectedResponse, false);
                    return false;
                }

                // 比较实际响应与预期响应（不区分大小写）
                bool isSuccess = actualResponse.Equals(expectedResponse, StringComparison.OrdinalIgnoreCase);

                await UpdateRelayResult(channelIndex, row, actualResponse, expectedResponse, isSuccess);

                if (isSuccess)
                    AppendLog($"通道 {channelIndex + 1} 打开所有继电器成功，响应: {actualResponse}", LogSuccess);
                else
                    AppendLog($"通道 {channelIndex + 1} 打开所有继电器失败，预期: {expectedResponse}，实际: {actualResponse}", LogError);

                return isSuccess;
            }
            catch (Exception ex)
            {
                AppendLog($"打开所有继电器错误：{ex.Message}", LogError);
                return false;
            }
        }

        /// <summary>
        /// 更新继电器测试结果到 DataGrid
        /// </summary>
        private async Task UpdateRelayResult(int channelIndex, DataRow row, string actualResponse, string expectedResponse, bool success)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                string valueColumn = $"Channel{channelIndex + 1}Value";
                string resultColumn = $"Channel{channelIndex + 1}Result";
                row[valueColumn] = success ? actualResponse : $"实际:{actualResponse} 期望:{expectedResponse}";
                row[resultColumn] = success ? "PASS" : "FAIL";
            });
        }

        #endregion FC 840300-52 REV3烧录 END


        #region KB BMU-KB52SA_A00R20烧录
        private async Task<bool> RunKB_BMU_KB52SA_A00R20BRunSequence(int channelIndex, string sn, CancellationToken ct)
        {
            int stepRowIndex = 0;



            await RelayController.SendCommandWithCrcAsync(CommandList.CloseAllRelay_01, 38400, ComName.rs485ComName, 1000, msg => AppendLog(msg));
            /*
             * MaxRetries=定义单个步骤测试
            -1 = 跟随系统设置 appSettings.FailRetryCount
            0 = 不重试，只执行 1 次
            1 = 失败后重试 1 次，总共最多执行 2 次
            2 = 失败后重试 2 次，总共最多执行 3 次
            3 = 失败后重试 3 次，总共最多执行 4 次
            /// <summary>
    /// -1 = 跟随系统设置
    ///  0 = 不重试
    ///  1 = 失败后重试 1 次
    ///  2 = 失败后重试 2 次
    /// </summary>
             */
            var steps = new List<(Func<CancellationToken, Task<bool>> Action, string Name, int RowIndex, int MaxRetries)>();

            // 步骤1：SN输入
            int row0 = stepRowIndex;
            steps.Add((async (token) => { await SN_Input(channelIndex, row0, sn, token); return true; }, "SN输入", row0, -1));
            stepRowIndex++;

            // 步骤2：治具下压确认
            int row1 = stepRowIndex;
            steps.Add((async (token) => { return await ConfirmFixtureDownward_FC(channelIndex, row1, token); }, "治具下压确认", row1, -1));
            stepRowIndex++;

            // 步骤3：短接JP1（闭合Y1继电器）
            int row2 = stepRowIndex;
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row2, 1, 01, true, 01, 38400, token); }, "短接JP1*(闭合Y1继电器)", row2, -1));
            stepRowIndex++;

            // 步骤4：校验固件MD5（在烧录前确认文件完整性）
            int rowMd5 = stepRowIndex;
            string binFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProjectConfig", "KB BMU-KB52SA_A00R20ProgrammeConfig", "BurnScript", "BTLAndBMU_APP_V08.02.00.XX.bin");
            steps.Add((async (token) => await VerifyFileMD5(channelIndex, rowMd5, binFilePath, token), "校验固件MD5", rowMd5, -1));
            stepRowIndex++;

            // 步骤5：等待稳定时间
            int row3 = stepRowIndex;
            steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row3, token); }, "等待稳定时间", row3, -1));
            stepRowIndex++;

            // 步骤6：烧录芯片
            int row4 = stepRowIndex;
            string u20Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProjectConfig", "KB BMU-KB52SA_A00R20ProgrammeConfig", "BurnScript", "flash.bat");
            steps.Add((async (token) => { return await BurnFirmware(channelIndex, row4, u20Path, token); }, "短接JP1，通过JP2烧录芯片（JP2-1VCC;JP2-3SWDIO;JP2-4SWCLK;JP2-8GND）", row4, -1));
            stepRowIndex++;

            // 步骤7：断开短接JP1
            int row5 = stepRowIndex;
            steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row5, 1, 01, false, 01, 38400, token); }, "断开JP1*(打开Y1继电器)", row5, -1));
            stepRowIndex++;
            int totalSteps = steps.Count;
            int currentStep = 0;
            bool allPassed = true;

            foreach (var step in steps)
            {
                currentStep++;

                bool pass = await ExecuteTestStepAsync(
                    channelIndex,
                    step.Action,
                    step.Name,
                    step.RowIndex,
                    ct,
                    currentStep,
                    totalSteps,
                    maxRetries: step.MaxRetries);

                if (!pass)
                {
                    allPassed = false;
                    if (appSettings.StopOnFail) break;
                }
            }

            await RelayController.SendCommandWithCrcAsync(CommandList.CloseAllRelay_01, 38400, ComName.rs485ComName, 1000, msg => AppendLog(msg));
            return allPassed;
        }


        /// <summary>
        /// 计算文件的MD5并与预期值比较（从DataGrid当前行的UpperLimit读取预期MD5）
        /// </summary>
        private async Task<bool> VerifyFileMD5(int channelIndex, int rowIndex, string filePath, CancellationToken token)
        {
            DataTable dt = ProjectSettings.testDataTable;
            if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
            {
                AppendLog($"无效的行索引: {rowIndex}", LogError);
                return false;
            }
            DataRow row = dt.Rows[rowIndex];
            if (!Convert.ToBoolean(row["Select"]))
            {
                AppendLog($"第 {rowIndex + 1} 行未勾选，跳过MD5校验", LogInfo);
                await UpdateMD5Result(channelIndex, row, "跳过", true);
                return true;
            }

            string expectedMD5 = row["UpperLimit"]?.ToString().Trim();
            if (string.IsNullOrEmpty(expectedMD5))
            {
                AppendLog($"第 {rowIndex + 1} 行未设置预期MD5值", LogError);
                await UpdateMD5Result(channelIndex, row, "无预期值", false);
                return false;
            }

            if (!File.Exists(filePath))
            {
                AppendLog($"固件文件不存在: {filePath}", LogError);
                await UpdateMD5Result(channelIndex, row, "文件缺失", false);
                return false;
            }

            try
            {
                // 在 .NET Framework 中，使用 Task.Run 将同步的 ComputeHash 放到后台线程
                string actualMD5 = await Task.Run(() =>
                {
                    using (var md5 = System.Security.Cryptography.MD5.Create())
                    using (var stream = File.OpenRead(filePath))
                    {
                        byte[] hash = md5.ComputeHash(stream);
                        return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
                    }
                });
                bool match = actualMD5.Equals(expectedMD5, StringComparison.OrdinalIgnoreCase);
                string displayValue = $"MD5: {actualMD5}";
                await UpdateMD5Result(channelIndex, row, displayValue, match);
                AppendLog($"固件MD5: {actualMD5}，预期: {expectedMD5}，{(match ? "匹配" : "不匹配")}", match ? LogSuccess : LogError);
                return match;
            }
            catch (Exception ex)
            {
                AppendLog($"计算MD5失败: {ex.Message}", LogError);
                await UpdateMD5Result(channelIndex, row, "异常", false);
                return false;
            }
        }

        private async Task UpdateMD5Result(int channelIndex, DataRow row, string value, bool pass)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                string valueColumn = $"Channel{channelIndex + 1}Value";
                string resultColumn = $"Channel{channelIndex + 1}Result";
                row[valueColumn] = value;
                row[resultColumn] = pass ? "PASS" : "FAIL";
            });
        }


        #endregion KB BMU-KB52SA_A00R20烧录END

        #region ME MTD005 436_01-50-01（ FBAD64202）
        private async Task<bool> ME_MTD005_436_01_50_01_FBAD64202(int channelIndex, string sn, CancellationToken ct)
        {
            try
            {
                await RelayController.SendCommandWithCrcAsync(CommandList.CloseAllRelay_01, 38400, ComName.rs485ComName, 1000, msg => AppendLog(msg));
                int stepRowIndex = 0;

                /*
            * MaxRetries=定义单个步骤测试
           -1 = 跟随系统设置 appSettings.FailRetryCount
           0 = 不重试，只执行 1 次
           1 = 失败后重试 1 次，总共最多执行 2 次
           2 = 失败后重试 2 次，总共最多执行 3 次
           3 = 失败后重试 3 次，总共最多执行 4 次
           /// <summary>
   /// -1 = 跟随系统设置
   ///  0 = 不重试
   ///  1 = 失败后重试 1 次
   ///  2 = 失败后重试 2 次
   /// </summary>
            */
                var steps = new List<(Func<CancellationToken, Task<bool>> Action, string Name, int RowIndex, int MaxRetries)>();

                // 步骤1：SN输入
                int row0 = stepRowIndex;
                steps.Add((async (token) => { await SN_Input(channelIndex, row0, sn, token); return true; }, "SN输入", row0, -1));
                stepRowIndex++;

                // 步骤2：治具下压确认
                int row1 = stepRowIndex;
                steps.Add((async (token) => { return await ConfirmFixtureDownward_FC(channelIndex, row1, token); }, "治具下压确认", row1, -1));
                stepRowIndex++;

                // 步骤3：打开烧录继电器Y13-Y16（开启）
                int row2 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row2, 1, 13, true, 4, 38400, token); }, "打开烧录Y13-Y16", row2, -1));
                stepRowIndex++;

                // 步骤4：等待稳定时间
                int row3 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row3, token); }, "等待稳定时间", row3, -1));
                stepRowIndex++;

                // 步骤5：擦除芯片
                int row4 = stepRowIndex;
                string erasPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProjectConfig", "ME MTD500TestConfig", "Erase", "flash.bat");
                steps.Add((async (token) => { return await BurnFirmware(channelIndex, row4, erasPath, token); }, "擦除芯片", row4, -1));
                stepRowIndex++;

                // 步骤6：关闭烧录继电器Y13-Y16
                int row5 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row5, 1, 13, false, 4, 38400, token); }, "关闭烧录Y13-Y16", row5, -1));
                stepRowIndex++;

                // 步骤7：读取输入电压（烧录前）
                int row6 = stepRowIndex;
                steps.Add((async (token) => { return await GetInputVolt(channelIndex, row6, token); }, "获取供电电压值(烧录前)", row6, -1));
                stepRowIndex++;

                // 步骤8：打开Y1供电
                int row7 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row7, 1, 1, true, 1, 38400, token); }, "开机-打开Y1", row7, -1));
                stepRowIndex++;

                // 步骤9：等待稳定时间
                int row8 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row8, token); }, "等待稳定时间", row8, -1));
                stepRowIndex++;

                // 步骤10：读取输入电流（烧录前）
                int row9 = stepRowIndex;
                steps.Add((async (token) => { return await GetInputCurrent(channelIndex, row9, token); }, "获取供电电流值(烧录前)", row9, -1));
                stepRowIndex++;

                // 步骤11：读取TP152电压值（电压模块通道1）
                int row10 = stepRowIndex;
                steps.Add((async (token) => { return await GetTPVolt(channelIndex, row10, CommandList.Read16_02Volt, 0, token); }, "测试TP152电压值", row10, -1));
                stepRowIndex++;

                // 步骤12：读取TP157电压值（电压模块通道2）
                int row11 = stepRowIndex;
                steps.Add((async (token) => { return await GetTPVolt(channelIndex, row11, CommandList.Read16_02Volt, 1, token); }, "测试TP157电压值", row11, -1));
                stepRowIndex++;

                // 步骤13：读取TP167电压值（电压模块通道3）
                int row12 = stepRowIndex;
                steps.Add((async (token) => { return await GetTPVolt(channelIndex, row12, CommandList.Read16_02Volt, 2, token); }, "测试TP167电压值", row12, -1));
                stepRowIndex++;

                // 步骤14：打开烧录继电器Y13-Y16（烧录准备）
                int row13 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row13, 1, 13, true, 4, 38400, token); }, "打开烧录Y13-Y16(烧录前)", row13, -1));
                stepRowIndex++;

                // 步骤15：等待稳定时间
                int row14 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row14, token); }, "等待稳定时间", row14, -1));
                stepRowIndex++;

                // 步骤16：固件烧录
                int row15 = stepRowIndex;
                string brunPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProjectConfig", "ME MTD500TestConfig", "Programme_FBAD64202", "flash.bat");
                steps.Add((async (token) => { return await BurnFirmware(channelIndex, row15, brunPath, token); }, "烧录固件", row15, -1));
                stepRowIndex++;

                // 步骤17：关闭烧录继电器Y13-Y16
                int row16 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row16, 1, 13, false, 4, 38400, token); }, "关闭烧录Y13-Y16(烧录后)", row16, -1));
                stepRowIndex++;

                // 步骤18：关闭Y1供电
                int row17 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row17, 1, 1, false, 1, 38400, token); }, "关机-关闭Y1", row17, -1));
                stepRowIndex++;

                // 步骤19：等待稳定时间
                int row18 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row18, token); }, "等待稳定时间", row18, -1));
                stepRowIndex++;

                // 步骤20：打开Y1供电（重启）
                int row19 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row19, 1, 1, true, 1, 38400, token); }, "开机-打开Y1(重启)", row19, -1));
                stepRowIndex++;

                // 步骤21：等待稳定时间
                int row20 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row20, token); }, "等待稳定时间", row20, -1));
                stepRowIndex++;

                // 步骤22：读取输入电压（烧录后）
                int row21 = stepRowIndex;
                steps.Add((async (token) => { return await GetInputVolt(channelIndex, row21, token); }, "获取供电电压值(烧录后)", row21, -1));
                stepRowIndex++;

                // 步骤23：读取输入电流（烧录后）
                int row22 = stepRowIndex;
                steps.Add((async (token) => { return await GetInputCurrent(channelIndex, row22, token); }, "获取供电电流值(烧录后)", row22, -1));
                stepRowIndex++;

                // 步骤24：开机并读取版本号（占用7行）
                int row23 = stepRowIndex;
                steps.Add((async (token) => { return await IdentifyTestComPort(channelIndex, row23, token); }, "开机并读取版本号", row23, -1));
                stepRowIndex += 7; // 跳过已占用的7行

                // 步骤25：等待稳定时间
                int row24 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row24, token); }, "等待稳定时间", row24, -1));
                stepRowIndex++;
                // 步骤26：蜂鸣器检测
                int row25 = stepRowIndex;
                //steps.Add((async (token) => { return await CheckFrequency(channelIndex, row25, token); }, "频率检测", row25));
                steps.Add((async (token) => await StartFrequencyDetectionAsync(channelIndex, row25, token), "启动频率检测", row25, -1));
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row25, 1, 1, false, 1, 38400, token); }, "关闭Y1", row25, -1));
                steps.Add((async (token) => { await Task.Delay(1000, token); return true; }, "延迟1000ms", row25, -1));
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row25, 1, 1, true, 1, 38400, token); }, "打开Y1", row25, -1));
                steps.Add((async (token) => { await Task.Delay(1000, token); return true; }, "延迟1000ms", row25, -1));
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row25, 1, 2, true, 1, 38400, token); }, "打开Y2", row25, -1));
                steps.Add((async (token) => { await Task.Delay(400, token); return true; }, "延迟400ms", row25, -1));
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row25, 1, 2, false, 1, 38400, token); }, "关闭Y2", row25, -1));
                steps.Add((async (token) => await WaitForFrequencyResultAsync(token), "等待频率检测结果", row25, -1));
                steps.Add((async (token) => { await Task.Delay(500, token); return true; }, "延迟400ms", row25, -1));
                stepRowIndex++;


                // 步骤27：打开Y3TP96短接GND
                int row26 = stepRowIndex;
                steps.Add((async (token) => await StartFrequencyDetectionAsync(channelIndex, row26, token), "启动频率检测", row26, -1));

                stepRowIndex++;
                // 步骤28：蜂鸣器检测
                int row27 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row27, 1, 3, true, 1, 38400, token); }, "打开Y3", row27, -1));
                steps.Add((async (token) => { await Task.Delay(400, token); return true; }, "延迟400ms", row27, -1));
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row27, 1, 3, false, 1, 38400, token); }, "关闭Y3", row27, -1));
                steps.Add((async (token) => await WaitForFrequencyResultAsync(token), "等待频率检测结果", row27, -1));
                stepRowIndex++;

                // 步骤29：等待稳定时间
                int row28 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row28, token); }, "等待稳定时间", row28, -1));
                stepRowIndex++;

                // 步骤30：读取输入电流 TP96短接GND
                int row29 = stepRowIndex;
                steps.Add((async (token) => { return await GetInputCurrent(channelIndex, row29, token); }, "获取供电电流值TP96短接GND", row29, -1));
                stepRowIndex++;

                // 步骤31：读取TP173TP174电压值（电压模块通道4）
                int row30 = stepRowIndex;
                steps.Add((async (token) => { return await GetTPVolt(channelIndex, row30, CommandList.Read16_02Volt, 3, token, true); }, "测试TP173TP174电压值", row30, -1));
                stepRowIndex++;

                // 步骤32：打开Y4 Y5,TP173TP174接入10W负载
                int row31 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row31, 1, 4, true, 2, 38400, token); }, "打开Y4 Y5,TP173TP174接入10W负载", row31, -1));
                stepRowIndex++;

                // 步骤33：等待稳定时间
                int row32 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row32, token); }, "等待稳定时间", row32, -1));
                stepRowIndex++;

                // 步骤34：打开Y3TP96短接GND
                int row33 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row33, 1, 3, true, 1, 38400, token); }, "打开Y3", row33, -1));
                await Task.Delay(400);
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row33, 1, 3, false, 1, 38400, token); }, "关闭Y3", row33, -1));
                stepRowIndex++;

                // 步骤35：等待稳定时间
                int row34 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row34, token); }, "等待稳定时间", row34, -1));
                stepRowIndex++;

                // 步骤36：读取输入电流 带负载
                int row35 = stepRowIndex;
                steps.Add((async (token) => { return await GetInputCurrent(channelIndex, row35, token); }, "读取输入电流 带负载", row35, -1));
                stepRowIndex++;


                // 步骤37：读取TP152电压值（电压模块通道1）
                int row36 = stepRowIndex;
                steps.Add((async (token) => { return await GetTPVolt(channelIndex, row36, CommandList.Read16_02Volt, 0, token); }, "测试TP152电压值", row36, -1));
                stepRowIndex++;

                // 步骤38：读取TP157电压值（电压模块通道2）
                int row37 = stepRowIndex;
                steps.Add((async (token) => { return await GetTPVolt(channelIndex, row37, CommandList.Read16_02Volt, 1, token); }, "测试TP157电压值", row37, -1));
                stepRowIndex++;

                // 步骤39：重复开启和关闭Y6 TP126 4次
                int row38 = stepRowIndex;
                steps.Add((async (token) => { return await RepeatToggleRelay(channelIndex, row38, 1, 6, 6, 38400, 300, token); }, "重复开关Y6 TP126 4次", row38, -1));
                stepRowIndex++;

                // 步骤40：等待稳定时间
                int row39 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row39, token); }, "等待稳定时间", row39, -1));
                stepRowIndex++;

                // 步骤41：读取TP173TP174电压值（电压模块通道4）
                int row40 = stepRowIndex;
                steps.Add((async (token) => { return await GetTPVolt(channelIndex, row40, CommandList.Read16_02Volt, 4, token); }, "测试TP173TP174电压值", row40, -1));
                stepRowIndex++;


                // 步骤42：重复开启和关闭Y6 TP126 10次
                int row41 = stepRowIndex;
                steps.Add((async (token) => { return await RepeatToggleRelay(channelIndex, row41, 1, 7, 12, 38400, 300, token); }, "重复开关Y7 TP111 10次", row41, -1));
                stepRowIndex++;

                // 步骤43：等待稳定时间
                int row42 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row42, token); }, "等待稳定时间", row42, -1));
                stepRowIndex++;

                // 步骤44：读取TP173TP174电压值（电压模块通道4）
                int row43 = stepRowIndex;
                steps.Add((async (token) => { return await GetTPVolt(channelIndex, row43, CommandList.Read16_02Volt, 4, token); }, "测试TP173TP174电压值", row43, -1));
                stepRowIndex++;

                // 步骤45：断开Y4 Y5,TP173TP174断开10W负载
                int row44 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row44, 1, 4, false, 2, 38400, token); }, "打开Y4 Y5,TP173TP174接入10W负载", row44, -1));
                stepRowIndex++;

                // 步骤46：等待稳定时间
                int row45 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row45, token); }, "等待稳定时间", row45, -1));
                stepRowIndex++;

                // 步骤47：读取TP173TP174电压值（电压模块通道3）
                int row46 = stepRowIndex;
                steps.Add((async (token) => { return await GetTPVolt(channelIndex, row46, CommandList.Read16_02Volt, 3, token, true); }, "测试TP173TP174电压值", row46, -1));
                stepRowIndex++;

                // 步骤48：等待稳定时间
                int row47 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row47, token); }, "等待稳定时间", row47, -1));
                stepRowIndex++;

                // 步骤49：重复开启和关闭Y6 TP96 1次
                int row48 = stepRowIndex;
                steps.Add((async (token) => { return await RepeatToggleRelay(channelIndex, row48, 1, 3, 1, 38400, 300, token); }, "重复开关Y3 TP96 1次", row48, -1));
                stepRowIndex++;

                // 步骤50：打开Y8短接TP173, TP174
                int row49 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row49, 1, 8, true, 1, 38400, token); }, "打开Y8 短接TP173, TP174", row49, -1));
                stepRowIndex++;

                // 步骤51：等待稳定时间
                int row50 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row50, token); }, "等待稳定时间", row50, -1));
                stepRowIndex++;

                // 步骤52：重复开启和关闭Y6 TP96 1次
                int row51 = stepRowIndex;
                steps.Add((async (token) => { return await RepeatToggleRelay(channelIndex, row51, 1, 3, 1, 38400, 300, token); }, "重复开关Y3 TP96 1次", row51, -1));
                stepRowIndex++;

                // 步骤53：等待稳定时间
                int row52 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row52, token); }, "等待稳定时间", row52, -1));
                stepRowIndex++;

                // 步骤54：读取TP80电压值（电压模块通道6）
                int row53 = stepRowIndex;
                steps.Add((async (token) => { return await GetVoltageValue(channelIndex, row53, 0, token); }, "测试TP180电压值", row53, -1));
                stepRowIndex++;

                // 步骤55：重复开启和关闭Y7 TP111 1次
                int row54 = stepRowIndex;
                steps.Add((async (token) => { return await RepeatToggleRelay(channelIndex, row54, 1, 7, 2, 38400, 300, token); }, "重复开关Y7 TP111 2次", row54, -1));
                stepRowIndex++;

                // 步骤56：等待稳定时间
                int row55 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row55, token); }, "等待稳定时间", row55, -1));
                stepRowIndex++;
                // 步骤57：读取TP80电压值（电压模块通道6）
                int row56 = stepRowIndex;
                steps.Add((async (token) => { return await GetVoltageValue(channelIndex, row56, 0, token); }, "测试TP180电压值", row56, -1));
                stepRowIndex++;
                // 步骤58：显示屏确认
                int row57 = stepRowIndex;
                steps.Add((async (token) => { return await ConfirmDisplay(channelIndex, row57, "显示屏是否正常亮起并显示150Hz", token); }, "显示屏确认", row57, -1));
                stepRowIndex++;
                // 步骤59：关机
                int row58 = stepRowIndex;
                steps.Add((async (token) => { return await RepeatToggleRelay(channelIndex, row58, 1, 2, 1, 38400, 300, token); }, "关机-重复开关Y2 1次", row58, -1));
                stepRowIndex++;
                // 步骤60：断电
                int row59 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row59, 1, 1, false, 1, 38400, token); }, "断电-关闭Y1", row59, -1));
                stepRowIndex++;
                // 步骤61：打印
                int row60 = stepRowIndex;
                steps.Add((async (token) => { return await PrintLabel(channelIndex, row60, "FBAD64202", token); }, "打印标签", row60, -1));
                stepRowIndex++;

                // 步骤62：验证打印
                int row61 = stepRowIndex;
                steps.Add((async (token) =>
                {
                    var ch = ProjectSettings.Channels.First(c => c.Index == channelIndex);
                    string expectedPrintContent = $"FBAD64202 {ch.PrintedSN}";
                    return await CompareScannedCodes(channelIndex, row61, sn, expectedPrintContent, token);
                }, "条码比对", row61, -1));



                int totalSteps = steps.Count;
                int currentStep = 0;
                bool allPassed = true;

                foreach (var step in steps)
                {
                    currentStep++;

                    bool pass = await ExecuteTestStepAsync(
                        channelIndex,
                        step.Action,
                        step.Name,
                        step.RowIndex,
                        ct,
                        currentStep,
                        totalSteps,
                        maxRetries: step.MaxRetries);

                    if (!pass)
                    {
                        allPassed = false;
                        if (appSettings.StopOnFail) break;
                    }
                }
                await RelayController.SendCommandWithCrcAsync(CommandList.CloseAllRelay_01, 38400, ComName.rs485ComName, 1000, msg => AppendLog(msg));
                return allPassed;
            }
            catch (Exception ex)
            {
                AppendLog($"测试错误,错误类型：{ex.GetType().ToString()};错误信息：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 向 RS485 串口发送 "E0?\r\n" 命令并读取响应
        /// </summary>
        /// <param name="comPort">串口名称（如 COM1）</param>
        /// <param name="baudRate">波特率（默认 9600）</param>
        /// <param name="timeoutMs">读取超时时间（毫秒）</param>
        /// <param name="logAction">日志回调</param>
        /// <returns>响应字符串，失败返回空字符串</returns>
        private async Task<string> SendE0CommandAsync(string comPort, int baudRate = 9600, int timeoutMs = 5000, Action<string> logAction = null)
        {
            if (string.IsNullOrEmpty(comPort))
            {
                logAction?.Invoke("串口名称无效");
                return string.Empty;
            }

            try
            {
                using (var port = new SerialPort(comPort, baudRate, Parity.None, 8, StopBits.One))
                {
                    port.ReadTimeout = timeoutMs;
                    port.WriteTimeout = 1000;
                    port.Open();

                    logAction?.Invoke($"串口 {comPort} 已打开，发送命令：E0?");

                    port.DiscardInBuffer();
                    port.Write("E0?\r\n");

                    // 定义有效数据行的判断条件（包含电压或电流/电压）
                    Func<string, bool> isValidDataLine = line =>
                        line.Contains("V") && (line.Contains("A1:") || line.Contains(":"));

                    DateTime startTime = DateTime.Now;
                    while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
                    {
                        if (port.BytesToRead > 0)
                        {
                            string line = await Task.Run(() => port.ReadLine()).ConfigureAwait(false);
                            line = line.Trim();
                            if (string.IsNullOrEmpty(line)) continue;

                            logAction?.Invoke($"收到响应行：{line}");

                            // 跳过非数据行（如 E0N0000000 等）
                            if (!isValidDataLine(line))
                            {
                                logAction?.Invoke($"跳过无效行：{line}");
                                continue;
                            }

                            return line; // 返回有效的电压/电流行
                        }
                        await Task.Delay(50);
                    }

                    logAction?.Invoke($"超时未收到有效数据行");
                    return string.Empty;
                }
            }
            catch (TimeoutException)
            {
                logAction?.Invoke($"串口 {comPort} 读取超时");
                return string.Empty;
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"串口错误：{ex.Message}");
                return string.Empty;
            }
        }
        /// <summary>
        /// 解析 E0? 命令返回的电压和电流数据（支持多种格式和单位）
        /// </summary>
        /// <param name="data">原始响应字符串，如 "C:+008.500uA1:14.97625V"</param>
        /// <returns>元组 (电流值（安培）, 电压值（伏特）)，解析失败返回 (double.NaN, double.NaN)</returns>
        private (double CurrentInMa, double Voltage) ParseE0Response(string data)
        {
            if (string.IsNullOrEmpty(data))
                return (double.NaN, double.NaN);

            data = data.Trim();
            AppendLog($"原始数据: {data}", LogInfo);

            // 提取电压（伏特）
            double voltage = double.NaN;
            var voltageMatch = Regex.Match(data, @"(\d+\.?\d*)V", RegexOptions.IgnoreCase);
            if (voltageMatch.Success && double.TryParse(voltageMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out voltage))
                AppendLog($"提取电压: {voltage} V", LogInfo);
            else
                AppendLog($"无法解析电压值", LogError);

            // 提取电流，并转换为毫安
            double currentInMa = double.NaN;
            var currentMatch = Regex.Match(data, @"C:([+-]?\d+\.?\d*)(uA|mA|A)", RegexOptions.IgnoreCase);
            if (currentMatch.Success)
            {
                string valueStr = currentMatch.Groups[1].Value;
                string unit = currentMatch.Groups[2].Value.ToLower();
                if (double.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                {
                    switch (unit)
                    {
                        case "ua": currentInMa = val * 0.001; break;   // uA → mA
                        case "ma": currentInMa = val; break;          // mA → mA
                        case "a": currentInMa = val * 1000; break;   // A → mA
                    }
                    AppendLog($"提取电流: {val} {unit} -> {currentInMa} mA", LogInfo);
                }
            }
            else
            {
                // 兼容无单位情况（假设为安培）
                var fallback = Regex.Match(data, @"C:([+-]?\d+\.?\d*)\s*A1:", RegexOptions.IgnoreCase);
                if (fallback.Success && double.TryParse(fallback.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                {
                    currentInMa = val * 1000;
                    AppendLog($"提取电流（无单位，假设A）: {val} A -> {currentInMa} mA", LogInfo);
                }
                else
                    AppendLog($"无法解析电流值，原始数据: {data}", LogError);
            }

            return (currentInMa, voltage);
        }


        /// <summary>
        /// 获取供电电压值并判定是否在上下限范围内
        /// </summary>
        private async Task<bool> GetInputVolt(int channelIndex, int rowIndex, CancellationToken cancellationToken)
        {
            try
            {

                DataTable dt = ProjectSettings.testDataTable;
                if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
                {
                    AppendLog($"无效的行索引: {rowIndex}", LogError);
                    return false;
                }
                DataRow row = dt.Rows[rowIndex];
                bool isSelected = Convert.ToBoolean(row["Select"]);
                if (!isSelected)
                {
                    AppendLog($"第 {rowIndex + 1} 行未勾选，跳过电压测量", LogInfo);
                    await UpdateVoltResult(channelIndex, row, "Skip", true);
                    return true; // 未勾选视为通过
                }

                // 读取上下限
                double upper = 0, lower = 0;
                bool limitValid = true;
                try
                {
                    string upperStr = row["UpperLimit"]?.ToString().Trim();
                    string lowerStr = row["LowerLimit"]?.ToString().Trim();
                    if (string.IsNullOrEmpty(upperStr) || string.IsNullOrEmpty(lowerStr))
                    {
                        AppendLog($"第 {rowIndex + 1} 行上下限为空，跳过该项判定", LogWarning);
                        limitValid = false;
                    }
                    else
                    {
                        upper = Convert.ToDouble(upperStr);
                        lower = Convert.ToDouble(lowerStr);
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"第 {rowIndex + 1} 行上下限值无效: {row["UpperLimit"]} / {row["LowerLimit"]}, 错误: {ex.Message}", LogError);
                    limitValid = false;
                }

                string responseLine = await SendE0CommandAsync(ComName.powerSupplyComName, 9600, 5000, msg => AppendLog(msg, LogInfo));
                if (string.IsNullOrEmpty(responseLine))
                {
                    AppendLog("读取电压值失败（无有效响应）", LogError);
                    await UpdateVoltResult(channelIndex, row, "无响应", false);
                    return false;
                }

                var (current, voltage) = ParseE0Response(responseLine);
                if (double.IsNaN(voltage))
                {
                    AppendLog($"无法解析电压值，原始数据: {responseLine}", LogError);
                    await UpdateVoltResult(channelIndex, row, "解析失败", false);
                    return false;
                }


                bool pass = limitValid ? (voltage >= lower && voltage <= upper) : false;
                string displayValue = voltage.ToString("F3") + " V";
                await UpdateVoltResult(channelIndex, row, displayValue, pass);

                if (pass)
                    AppendLog($"电压值 {voltage:F3} V 在范围 [{lower}, {upper}] 内", LogSuccess);
                else
                    AppendLog($"电压值 {voltage:F3} V 超出范围 [{lower}, {upper}]", LogError);

                return pass;
            }
            catch (Exception ex)
            {
                AppendLog($"获取供电电压异常: {ex.Message}", LogError);
                return false;
            }
        }

        /// <summary>
        /// 更新电压测试结果到 DataGrid
        /// </summary>
        private async Task UpdateVoltResult(int channelIndex, DataRow row, string value, bool pass)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                string valueColumn = $"Channel{channelIndex + 1}Value";
                string resultColumn = $"Channel{channelIndex + 1}Result";
                row[valueColumn] = value;
                row[resultColumn] = pass ? "PASS" : "FAIL";
            });
        }


        /// <summary>
        /// 获取供电电流值并判定是否在上下限范围内（单位转换为mA）
        /// </summary>
        private async Task<bool> GetInputCurrent(int channelIndex, int rowIndex, CancellationToken cancellationToken)
        {
            try
            {
                await RelayController.SendCommandAsync(1, 1, true, 1, 38400, null, msg => AppendLog(msg, LogInfo));//开机 打开Y1
                await WaitDialog.WaitOrThrowAsync("请等待电流值稳定，请稍候...", 3, this);

                DataTable dt = ProjectSettings.testDataTable;
                if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
                {
                    AppendLog($"无效的行索引: {rowIndex}", LogError);
                    return false;
                }
                DataRow row = dt.Rows[rowIndex];
                bool isSelected = Convert.ToBoolean(row["Select"]);
                if (!isSelected)
                {
                    AppendLog($"第 {rowIndex + 1} 行未勾选，跳过电流测量", LogInfo);
                    await UpdateCurrentResult(channelIndex, row, "Skip", true);
                    return true;
                }

                // 读取上下限（注意：上下限单位应为毫安 mA，或根据实际调整）
                double upper = 0, lower = 0;
                bool limitValid = true;
                try
                {
                    string upperStr = row["UpperLimit"]?.ToString().Trim();
                    string lowerStr = row["LowerLimit"]?.ToString().Trim();
                    if (string.IsNullOrEmpty(upperStr) || string.IsNullOrEmpty(lowerStr))
                    {
                        AppendLog($"第 {rowIndex + 1} 行上下限为空，跳过该项判定", LogWarning);
                        limitValid = false;
                    }
                    else
                    {
                        // 假设上下限配置的是mA，直接转换为double
                        upper = Convert.ToDouble(upperStr);
                        lower = Convert.ToDouble(lowerStr);
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"第 {rowIndex + 1} 行上下限值无效: {row["UpperLimit"]} / {row["LowerLimit"]}, 错误: {ex.Message}", LogError);
                    limitValid = false;
                }

                // 发送命令并获取有效响应行
                string responseLine = await SendE0CommandAsync(ComName.powerSupplyComName, 9600, 5000, msg => AppendLog(msg, LogInfo));
                if (string.IsNullOrEmpty(responseLine))
                {
                    AppendLog("读取电流值失败（无有效响应）", LogError);
                    await UpdateCurrentResult(channelIndex, row, "无响应", false);
                    return false;
                }

                var (currentInMa, voltage) = ParseE0Response(responseLine);
                if (double.IsNaN(currentInMa))
                {
                    AppendLog($"无法解析电流值，原始数据: {responseLine}", LogError);
                    await UpdateCurrentResult(channelIndex, row, "解析失败", false);
                    return false;
                }

                // 上下限应为毫安（用户填写时注意单位）
                bool pass = limitValid ? (currentInMa >= lower && currentInMa <= upper) : false;
                string displayValue = currentInMa.ToString("F3") + " mA";
                await UpdateCurrentResult(channelIndex, row, displayValue, pass);

                if (pass)
                    AppendLog($"电流值 {currentInMa:F3} mA 在范围 [{lower}, {upper}] 内", LogSuccess);
                else
                    AppendLog($"电流值 {currentInMa:F3} mA 超出范围 [{lower}, {upper}]", LogError);

                return pass;
            }
            catch (Exception ex)
            {
                AppendLog($"获取供电电流异常: {ex.Message}", LogError);
                return false;
            }
        }

        /// <summary>
        /// 更新电流测试结果到 DataGrid
        /// </summary>
        private async Task UpdateCurrentResult(int channelIndex, DataRow row, string value, bool pass)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                string valueColumn = $"Channel{channelIndex + 1}Value";
                string resultColumn = $"Channel{channelIndex + 1}Result";
                row[valueColumn] = value;
                row[resultColumn] = pass ? "PASS" : "FAIL";
            });
        }

        /// <summary>
        /// 获取采集模块电压值（通用方法）
        /// </summary>
        /// <param name="channelIndex">测试通道索引（用于更新 DataGrid 列）</param>
        /// <param name="rowIndex">当前测试项在 DataGrid 中的行索引</param>
        /// <param name="commandWithoutCrc">Modbus 读取命令（不含 CRC），例如 {0x02, 0x04, 0x00, 0x00, 0x00, 0x10}</param>
        /// <param name="voltageIndex">要读取的电压在返回列表中的索引（0-based）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <param name="invert">是否将电压值取反（变为负值）</param>
        /// <returns>是否通过（PASS/FAIL）</returns>
        private async Task<bool> GetTPVolt(int channelIndex, int rowIndex, byte[] commandWithoutCrc, int voltageIndex, CancellationToken cancellationToken, bool invert = false)
        {
            try
            {
                DataTable dt = ProjectSettings.testDataTable;
                if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
                {
                    AppendLog($"无效的行索引: {rowIndex}", LogError);
                    return false;
                }
                DataRow row = dt.Rows[rowIndex];
                bool isSelected = Convert.ToBoolean(row["Select"]);
                if (!isSelected)
                {
                    AppendLog($"第 {rowIndex + 1} 行未勾选，跳过电压测量", LogInfo);
                    await UpdateVoltageResult(channelIndex, row, "Skip", true);
                    return true;
                }

                // 读取上下限
                double upper = 0, lower = 0;
                bool limitValid = true;
                try
                {
                    string upperStr = row["UpperLimit"]?.ToString().Trim();
                    string lowerStr = row["LowerLimit"]?.ToString().Trim();
                    if (string.IsNullOrEmpty(upperStr) || string.IsNullOrEmpty(lowerStr))
                    {
                        AppendLog($"第 {rowIndex + 1} 行上下限为空，跳过该项判定", LogWarning);
                        limitValid = false;
                    }
                    else
                    {
                        upper = Convert.ToDouble(upperStr);
                        lower = Convert.ToDouble(lowerStr);
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"第 {rowIndex + 1} 行上下限值无效: {row["UpperLimit"]} / {row["LowerLimit"]}, 错误: {ex.Message}", LogError);
                    limitValid = false;
                }

                // 发送命令并获取电压列表
                List<double> voltages = await RelayController.ReadModbusRegistersAsync(
                    ComName.rs485ComName,
                    commandWithoutCrc,
                    9600,
                    3000,
                    msg => AppendLog(msg, LogInfo));

                if (voltages.Count == 0)
                {
                    AppendLog("读取电压失败（无有效响应）", LogError);
                    await UpdateVoltageResult(channelIndex, row, "无响应", false);
                    return false;
                }

                if (voltageIndex < 0 || voltageIndex >= voltages.Count)
                {
                    AppendLog($"电压索引 {voltageIndex} 超出范围（0-{voltages.Count - 1}）", LogError);
                    await UpdateVoltageResult(channelIndex, row, "索引错误", false);
                    return false;
                }

                double voltage = voltages[voltageIndex];
                if (invert)
                    voltage = -voltage;

                bool pass = limitValid ? (voltage >= lower && voltage <= upper) : false;
                string displayValue = voltage.ToString("F3") + " V";
                await UpdateVoltageResult(channelIndex, row, displayValue, pass);

                if (pass)
                    AppendLog($"电压值 {voltage:F3} V 在范围 [{lower}, {upper}] 内", LogSuccess);
                else
                    AppendLog($"电压值 {voltage:F3} V 超出范围 [{lower}, {upper}]", LogError);

                return pass;
            }
            catch (Exception ex)
            {
                AppendLog($"获取电压异常: {ex.Message}", LogError);
                return false;
            }
        }

        /// <summary>
        /// 更新电压测试结果到 DataGrid
        /// </summary>
        private async Task UpdateVoltageResult(int channelIndex, DataRow row, string value, bool pass)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                string valueColumn = $"Channel{channelIndex + 1}Value";
                string resultColumn = $"Channel{channelIndex + 1}Result";
                row[valueColumn] = value;
                row[resultColumn] = pass ? "PASS" : "FAIL";
            });
        }

        /// <summary>
        /// 识别测试串口（先并行打开所有串口并开始读取，再发送继电器命令，最后收集响应并解析）
        /// </summary>
        private async Task<bool> IdentifyTestComPort(int channelIndex, int startRowIndex, CancellationToken cancellationToken)
        {
            try
            {
                // 1. 获取所有可用串口（排除 RS485 串口）
                string[] allPorts = SerialPort.GetPortNames();
                var candidatePorts = allPorts.Where(p => p != ComName.rs485ComName).ToList();
                if (candidatePorts.Count == 0)
                {
                    AppendLog("未找到任何可用串口", LogError);
                    return false;
                }

                // 2. 从 DataGrid 读取预期值（仅用于后面比较）
                DataTable dt = ProjectSettings.testDataTable;
                if (dt == null || startRowIndex < 0 || startRowIndex >= dt.Rows.Count)
                {
                    AppendLog("无效的行索引或 DataTable 为空", LogError);
                    return false;
                }

                // 定义设备信息的顺序（与 DataGrid 中从 startRowIndex 开始的顺序一致）
                string[] orderedKeys = { "Revive II Type", "Parameter set", "HAL Rev", "MNLib", "STM32 Library", "Date", "HW-Rev." };
                int deviceInfoCount = orderedKeys.Length;

                // 读取预期值（仅针对这7个键）
                Dictionary<string, string> expectedValues = new Dictionary<string, string>();
                for (int i = 0; i < deviceInfoCount; i++)
                {
                    int rowIndex = startRowIndex + i;
                    if (rowIndex >= dt.Rows.Count) break;
                    DataRow row = dt.Rows[rowIndex];
                    string testItem = row["TestItem"]?.ToString().Trim();
                    string key = orderedKeys[i];
                    if (!string.Equals(testItem, key, StringComparison.OrdinalIgnoreCase))
                    {
                        AppendLog($"注意：第 {rowIndex + 1} 行的测试项 '{testItem}' 与预期键 '{key}' 不匹配，将使用该行数据", LogWarning);
                    }
                    string expected = row["LowerLimit"]?.ToString().Trim();
                    if (!string.IsNullOrEmpty(expected))
                        expectedValues[key] = expected;
                }

                // 3. 并行打开所有串口并启动读取任务
                var readTasks = new List<Task<(string PortName, string Response)>>();
                var openedPorts = new List<SerialPort>();
                bool anyOpen = false;

                foreach (var portName in candidatePorts)
                {
                    try
                    {
                        var port = new SerialPort(portName, 57600, Parity.None, 8, StopBits.One);
                        port.ReadTimeout = 2000;
                        port.WriteTimeout = 1000;
                        port.Open();
                        port.DiscardInBuffer();
                        openedPorts.Add(port);
                        var task = Task.Run(async () =>
                        {
                            StringBuilder sb = new StringBuilder();
                            DateTime start = DateTime.Now;
                            while ((DateTime.Now - start).TotalMilliseconds < 3000)
                            {
                                try
                                {
                                    if (port.BytesToRead > 0)
                                    {
                                        string line = port.ReadLine();
                                        sb.AppendLine(line);
                                    }
                                    else
                                    {
                                        await Task.Delay(20);
                                    }
                                }
                                catch (TimeoutException) { break; }
                                catch { break; }
                            }
                            return (PortName: portName, Response: sb.ToString());
                        }, cancellationToken);
                        readTasks.Add(task);
                        anyOpen = true;
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"打开串口 {portName} 失败: {ex.Message}", LogError);
                    }
                }

                if (!anyOpen)
                {
                    AppendLog("无法打开任何串口", LogError);
                    return false;
                }

                // 4. 发送继电器命令（通过 RS485 串口触发设备输出）
                AppendLog("发送继电器命令，触发设备输出信息...", LogInfo);
                await RelayController.SendCommandAsync(1, 2, true, 1, 38400, null, msg => AppendLog(msg, LogInfo));
                await WaitDialog.WaitOrThrowAsync("请等待电流值稳定，请稍候...", 0.4, this);
                await RelayController.SendCommandAsync(1, 2, false, 1, 38400, null, msg => AppendLog(msg, LogInfo));

                // 等待设备输出
                //await Task.Delay(1000, cancellationToken);

                // 5. 等待所有读取任务完成
                var results = await Task.WhenAll(readTasks);

                // 6. 关闭所有串口
                foreach (var port in openedPorts)
                {
                    try { port.Close(); port.Dispose(); } catch { }
                }

                // 7. 分析响应，查找包含设备信息的串口
                string successPort = null;
                Dictionary<string, string> actualValues = null;

                foreach (var result in results)
                {
                    if (string.IsNullOrEmpty(result.Response)) continue;
                    AppendLog($"串口 {result.PortName} 响应:\n{result.Response}", LogInfo);

                    var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    string[] lines = result.Response.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string line in lines)
                    {
                        string trimmedLine = line.Trim();
                        if (string.IsNullOrEmpty(trimmedLine)) continue;
                        if (trimmedLine.StartsWith("Warning") || trimmedLine.Contains("MT.DERM")) continue;

                        // Revive II Type
                        var match = Regex.Match(trimmedLine, @"Revive II Type (\d+) v([\d\.\-]+)");
                        if (match.Success)
                        {
                            parsed["Revive II Type"] = $"{match.Groups[1].Value} v{match.Groups[2].Value}";
                            continue;
                        }

                        // Parameter set + HAL Rev (同一行)
                        match = Regex.Match(trimmedLine, @"Parameter set:\s*([^\s]+)\s+HAL Rev:\s+([^\s]+)");
                        if (match.Success)
                        {
                            parsed["Parameter set"] = match.Groups[1].Value;
                            parsed["HAL Rev"] = match.Groups[2].Value;
                            continue;
                        }
                        // 单独 Parameter set
                        if (!parsed.ContainsKey("Parameter set"))
                        {
                            match = Regex.Match(trimmedLine, @"Parameter set:\s*([^\s]+)");
                            if (match.Success) parsed["Parameter set"] = match.Groups[1].Value;
                        }
                        // 单独 HAL Rev
                        if (!parsed.ContainsKey("HAL Rev"))
                        {
                            match = Regex.Match(trimmedLine, @"HAL Rev:\s+([^\s]+)");
                            if (match.Success) parsed["HAL Rev"] = match.Groups[1].Value;
                        }

                        // MNLib + STM32 Library (同一行)
                        match = Regex.Match(trimmedLine, @"MNLib:\s+([^\s]+)\s+STM32 Library:\s+([^\s]+)");
                        if (match.Success)
                        {
                            parsed["MNLib"] = match.Groups[1].Value;
                            parsed["STM32 Library"] = match.Groups[2].Value;
                            continue;
                        }
                        // 单独 MNLib
                        if (!parsed.ContainsKey("MNLib"))
                        {
                            match = Regex.Match(trimmedLine, @"MNLib:\s+([^\s]+)");
                            if (match.Success) parsed["MNLib"] = match.Groups[1].Value;
                        }
                        // 单独 STM32 Library
                        if (!parsed.ContainsKey("STM32 Library"))
                        {
                            match = Regex.Match(trimmedLine, @"STM32 Library:\s+([^\s]+)");
                            if (match.Success) parsed["STM32 Library"] = match.Groups[1].Value;
                        }

                        // Date + HW-Rev. (同一行)
                        match = Regex.Match(trimmedLine, @"Date:\s+([^\s]+)\s+HW-Rev.:\s+(\S+)");
                        if (match.Success)
                        {
                            parsed["Date"] = match.Groups[1].Value;
                            parsed["HW-Rev."] = match.Groups[2].Value;
                            continue;
                        }
                        // 单独 Date
                        if (!parsed.ContainsKey("Date"))
                        {
                            match = Regex.Match(trimmedLine, @"Date:\s+([^\s]+)");
                            if (match.Success) parsed["Date"] = match.Groups[1].Value;
                        }
                        // 单独 HW-Rev.
                        if (!parsed.ContainsKey("HW-Rev."))
                        {
                            match = Regex.Match(trimmedLine, @"HW-Rev.:\s+(\S+)");
                            if (match.Success) parsed["HW-Rev."] = match.Groups[1].Value;
                        }
                    }

                    // 打印解析结果
                    foreach (var kv in parsed)
                    {
                        AppendLog($"解析字段: {kv.Key} = {kv.Value}", LogInfo);
                    }

                    if (parsed.ContainsKey("Revive II Type") && parsed.ContainsKey("Parameter set"))
                    {
                        successPort = result.PortName;
                        actualValues = parsed;
                        break;
                    }
                }

                if (successPort == null)
                {
                    AppendLog("未找到包含设备信息的串口", LogError);
                    return false;
                }

                // 8. 保存串口并更新 DataGrid（只更新7项设备信息）
                ComName.testComName = successPort;
                AppendLog($"成功识别测试串口: {successPort}", LogSuccess);

                bool allMatch = true;
                for (int i = 0; i < deviceInfoCount; i++)
                {
                    int rowIndex = startRowIndex + i;
                    if (rowIndex >= dt.Rows.Count)
                    {
                        AppendLog($"警告：DataGrid 行数不足，预期需要 {deviceInfoCount} 行，实际只有 {dt.Rows.Count - startRowIndex} 行", LogWarning);
                        break;
                    }
                    DataRow row = dt.Rows[rowIndex];
                    string key = orderedKeys[i];
                    string expected = expectedValues.ContainsKey(key) ? expectedValues[key] : "";
                    string actual = actualValues.ContainsKey(key) ? actualValues[key] : "";
                    bool pass = false;

                    if (!string.IsNullOrEmpty(expected) && !string.IsNullOrEmpty(actual))
                    {
                        string expectedTrim = expected.Trim();
                        string actualTrim = actual.Trim();
                        pass = actualTrim.Equals(expectedTrim, StringComparison.OrdinalIgnoreCase) ||
                               actualTrim.Contains(expectedTrim);
                    }
                    else if (string.IsNullOrEmpty(expected) && !string.IsNullOrEmpty(actual))
                    {
                        pass = true;
                    }

                    if (!pass) allMatch = false;

                    string displayValue = string.IsNullOrEmpty(actual) ? "未获取" : actual;
                    await UpdateTestResult(channelIndex, rowIndex, row, displayValue, pass);
                }

                if (allMatch)
                    AppendLog("所有设备信息匹配成功", LogSuccess);
                else
                    AppendLog("部分设备信息不匹配", LogError);

                return allMatch;
            }
            catch (Exception ex)
            {
                AppendLog($"识别测试串口异常: {ex.Message}", LogError);
                return false;
            }
        }
        private async Task UpdateTestResult(int channelIndex, int rowIndex, DataRow row, string value, bool pass)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                string valueColumn = $"Channel{channelIndex + 1}Value";
                string resultColumn = $"Channel{channelIndex + 1}Result";
                row[valueColumn] = value;
                row[resultColumn] = pass ? "PASS" : "FAIL";
            });
        }

        /// <summary>
        /// 从当前行的上限/下限列读取等待时间（毫秒），转换为秒，并执行等待对话框。
        /// </summary>
        /// <param name="channelIndex">通道索引（用于日志）</param>
        /// <param name="rowIndex">当前行索引</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>等待完成（未取消）返回 true，否则 false</returns>
        private async Task<bool> WaitWithRowTimeout(int channelIndex, int rowIndex, CancellationToken cancellationToken)
        {
            DataTable dt = ProjectSettings.testDataTable;
            if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
            {
                AppendLog($"[通道{channelIndex + 1}] 无效的行索引: {rowIndex}", LogError);
                return false;
            }
            DataRow row = dt.Rows[rowIndex];
            bool isSelected = Convert.ToBoolean(row["Select"]);
            if (!isSelected)
            {
                AppendLog($"[通道{channelIndex + 1}] 第 {rowIndex + 1} 行未勾选，跳过等待", LogInfo);
                await Dispatcher.InvokeAsync(() =>
                {
                    string valueColumn = $"Channel{channelIndex + 1}Value";
                    string resultColumn = $"Channel{channelIndex + 1}Result";
                    row[valueColumn] = "Skip";
                    row[resultColumn] = "PASS";
                });
                return true;
            }

            double milliseconds = 0;
            string upperStr = row["UpperLimit"]?.ToString().Trim();
            string lowerStr = row["LowerLimit"]?.ToString().Trim();

            if (!string.IsNullOrEmpty(upperStr) && double.TryParse(upperStr, out double upperVal))
                milliseconds = upperVal;
            else if (!string.IsNullOrEmpty(lowerStr) && double.TryParse(lowerStr, out double lowerVal))
                milliseconds = lowerVal;
            else
            {
                AppendLog($"[通道{channelIndex + 1}] 第 {rowIndex + 1} 行未找到有效的时间值（毫秒）", LogError);
                return false;
            }

            double seconds = milliseconds / 1000.0;
            AppendLog($"[通道{channelIndex + 1}] 读取到等待时间: {milliseconds} ms → {seconds:F2} 秒", LogInfo);

            try
            {
                await WaitDialog.WaitOrThrowAsync("请等待电流值稳定，请稍候...", seconds, this);
                // 等待成功（未取消），更新 DataGrid 为 PASS
                await Dispatcher.InvokeAsync(() =>
                {
                    string valueColumn = $"Channel{channelIndex + 1}Value";
                    string resultColumn = $"Channel{channelIndex + 1}Result";
                    row[valueColumn] = $"{seconds} 秒";
                    row[resultColumn] = "PASS";
                });
                return true;
            }
            catch (OperationCanceledException)
            {
                AppendLog($"[通道{channelIndex + 1}] 等待被用户取消", LogWarning);
                await Dispatcher.InvokeAsync(() =>
                {
                    string valueColumn = $"Channel{channelIndex + 1}Value";
                    string resultColumn = $"Channel{channelIndex + 1}Result";
                    row[valueColumn] = $"{seconds} 秒";
                    row[resultColumn] = "FAIL";
                });
                return false;
            }
            catch (Exception ex)
            {
                AppendLog($"[通道{channelIndex + 1}] 等待异常: {ex.Message}", LogError);
                await Dispatcher.InvokeAsync(() =>
                {
                    string valueColumn = $"Channel{channelIndex + 1}Value";
                    string resultColumn = $"Channel{channelIndex + 1}Result";
                    row[valueColumn] = "异常";
                    row[resultColumn] = "FAIL";
                });
                return false;
            }
        }
        /// <summary>
        /// 控制继电器并更新 DataGrid 结果
        /// </summary>
        /// <param name="channelIndex">测试通道索引</param>
        /// <param name="rowIndex">当前测试项在 DataGrid 中的行索引</param>
        /// <param name="address">设备地址（站位号）</param>
        /// <param name="relayIndex">继电器索引（从1开始）</param>
        /// <param name="isOpen">true=开启，false=关闭</param>
        /// <param name="count">连续操作数量（默认为1）</param>
        /// <param name="baudRate">波特率（默认38400）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否成功</returns>
        private async Task<bool> ControlRelayAndUpdate(int channelIndex, int rowIndex, int address, int relayIndex, bool isOpen, int count = 1, int baudRate = 38400, CancellationToken cancellationToken = default)
        {
            try
            {
                DataTable dt = ProjectSettings.testDataTable;
                if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
                {
                    AppendLog($"[通道{channelIndex + 1}] 无效的行索引: {rowIndex}", LogError);
                    return false;
                }
                DataRow row = dt.Rows[rowIndex];
                bool isSelected = Convert.ToBoolean(row["Select"]);
                if (!isSelected)
                {
                    AppendLog($"[通道{channelIndex + 1}] 第 {rowIndex + 1} 行未勾选，跳过继电器控制", LogInfo);
                    await UpdateRelayResult(channelIndex, row, "跳过", true);
                    return true;
                }

                string result = await RelayController.SendCommandAsync(address, relayIndex, isOpen, count, baudRate, null, msg => AppendLog(msg, LogInfo));
                bool success = !string.IsNullOrEmpty(result) && !result.Contains("错误") && !result.Contains("TIMEOUT");
                string displayValue = success ? (isOpen ? "开启" : "关闭") : "失败";
                await UpdateRelayResult(channelIndex, row, displayValue, success);

                if (success)
                    AppendLog($"[通道{channelIndex + 1}] 继电器 {relayIndex} {(isOpen ? "开启" : "关闭")} 成功", LogSuccess);
                else
                    AppendLog($"[通道{channelIndex + 1}] 继电器 {relayIndex} {(isOpen ? "开启" : "关闭")} 失败", LogError);

                return success;
            }
            catch (Exception ex)
            {
                AppendLog($"[通道{channelIndex + 1}] 继电器控制异常: {ex.Message}", LogError);
                DataTable dt = ProjectSettings.testDataTable;
                if (dt != null && rowIndex >= 0 && rowIndex < dt.Rows.Count)
                {
                    DataRow row = dt.Rows[rowIndex];
                    await UpdateRelayResult(channelIndex, row, "异常", false);
                }
                return false;
            }
        }

        /// <summary>
        /// 更新继电器测试结果到 DataGrid
        /// </summary>
        private async Task UpdateRelayResult(int channelIndex, DataRow row, string value, bool success)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                string valueColumn = $"Channel{channelIndex + 1}Value";
                string resultColumn = $"Channel{channelIndex + 1}Result";
                row[valueColumn] = value;
                row[resultColumn] = success ? "PASS" : "FAIL";
            });
        }
        /// <summary>
        /// 检查蜂鸣器
        /// </summary>
        /// <param name="channelIndex"></param>
        /// <param name="rowIndex"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task<bool> CheckFrequency(int channelIndex, int rowIndex, CancellationToken cancellationToken)
        {
            try
            {
                DataTable dt = ProjectSettings.testDataTable;
                if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
                {
                    AppendLog($"无效的行索引: {rowIndex}", LogError);
                    return false;
                }
                DataRow row = dt.Rows[rowIndex];
                bool isSelected = Convert.ToBoolean(row["Select"]);
                if (!isSelected)
                {
                    AppendLog($"第 {rowIndex + 1} 行未勾选，跳过频率检测", LogInfo);
                    await UpdateFrequencyResult(channelIndex, rowIndex, row, "跳过", true);
                    return true;
                }

                double lower = 0, upper = 0;
                try
                {
                    string lowerStr = row["LowerLimit"]?.ToString().Trim();
                    string upperStr = row["UpperLimit"]?.ToString().Trim();
                    if (string.IsNullOrEmpty(lowerStr) || string.IsNullOrEmpty(upperStr))
                    {
                        AppendLog($"第 {rowIndex + 1} 行上下限为空", LogError);
                        return false;
                    }
                    lower = Convert.ToDouble(lowerStr);
                    upper = Convert.ToDouble(upperStr);
                }
                catch (Exception ex)
                {
                    AppendLog($"上下限解析失败: {ex.Message}", LogError);
                    return false;
                }

                var win = new FrequencyMonitorWindow(ComName.uartComName, 115200, lower, upper);
                win.Owner = this;
                win.ShowDialog();
                bool success = win.IsSuccess;
                string displayValue = win.ValidFrequency > 0 ? $"{win.ValidFrequency:F0} Hz" : "无数据";
                await UpdateFrequencyResult(channelIndex, rowIndex, row, displayValue, success);
                return success;
            }
            catch (Exception ex)
            {
                AppendLog($"频率检测异常: {ex.Message}", LogError);
                return false;
            }
        }

        private async Task UpdateFrequencyResult(int channelIndex, int rowIndex, DataRow row, string value, bool pass)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                string valueColumn = $"Channel{channelIndex + 1}Value";
                string resultColumn = $"Channel{channelIndex + 1}Result";
                row[valueColumn] = value;
                row[resultColumn] = pass ? "PASS" : "FAIL";
            });
        }


        private Task<bool> _pendingFrequencyTask;
        /// <summary>
        /// 非模态启动频率检测窗口，立即返回，不阻塞调用线程。
        /// 窗口关闭时会自动更新 DataGrid，并将结果保存到 _pendingFrequencyTask。
        /// </summary>
        private async Task<bool> StartFrequencyDetectionAsync(int channelIndex, int rowIndex, CancellationToken cancellationToken)
        {
            try
            {
                DataTable dt = ProjectSettings.testDataTable;
                if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
                {
                    AppendLog($"无效的行索引: {rowIndex}", LogError);
                    return false;
                }
                DataRow row = dt.Rows[rowIndex];
                bool isSelected = Convert.ToBoolean(row["Select"]);
                if (!isSelected)
                {
                    AppendLog($"第 {rowIndex + 1} 行未勾选，跳过频率检测", LogInfo);
                    await UpdateFrequencyResult(channelIndex, rowIndex, row, "跳过", true);
                    return true;
                }

                // 解析上下限
                double lower = 0, upper = 0;
                try
                {
                    string lowerStr = row["LowerLimit"]?.ToString().Trim();
                    string upperStr = row["UpperLimit"]?.ToString().Trim();
                    if (string.IsNullOrEmpty(lowerStr) || string.IsNullOrEmpty(upperStr))
                    {
                        AppendLog($"第 {rowIndex + 1} 行上下限为空", LogError);
                        return false;
                    }
                    lower = Convert.ToDouble(lowerStr);
                    upper = Convert.ToDouble(upperStr);
                }
                catch (Exception ex)
                {
                    AppendLog($"上下限解析失败: {ex.Message}", LogError);
                    return false;
                }

                // 创建 TaskCompletionSource，用于等待窗口关闭
                var tcs = new TaskCompletionSource<bool>();

                // 创建窗口，非模态显示
                var win = new FrequencyMonitorWindow(ComName.uartComName, 115200, lower, upper);
                win.Owner = this;
                win.Closed += async (s, e) =>
                {
                    try
                    {
                        bool success = win.IsSuccess;
                        string displayValue = win.ValidFrequency > 0 ? $"{win.ValidFrequency:F0} Hz" : "无数据";
                        await Dispatcher.InvokeAsync(() => UpdateFrequencyResult(channelIndex, rowIndex, row, displayValue, success));
                        tcs.SetResult(success);
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"频率检测窗口关闭处理异常: {ex.Message}", LogError);
                        tcs.SetResult(false);
                    }
                };
                win.Show();  // 非模态，立即返回

                // 保存任务，供后续等待
                _pendingFrequencyTask = tcs.Task;
                return true;
            }
            catch (Exception ex)
            {
                AppendLog($"启动频率检测异常: {ex.Message}", LogError);
                return false;
            }
        }

        /// <summary>
        /// 等待已启动的频率检测窗口关闭，返回最终结果（PASS/FAIL）。
        /// 如果没有启动任何窗口，则返回 false。
        /// </summary>
        private async Task<bool> WaitForFrequencyResultAsync(CancellationToken cancellationToken)
        {
            if (_pendingFrequencyTask == null)
            {
                AppendLog("未找到待等待的频率检测任务", LogError);
                return false;
            }

            try
            {
                bool result = await _pendingFrequencyTask;
                _pendingFrequencyTask = null; // 清空，避免重复等待
                return result;
            }
            catch (Exception ex)
            {
                AppendLog($"等待频率检测结果异常: {ex.Message}", LogError);
                return false;
            }
        }

        /// <summary>
        /// 重复开关继电器多次（作为一个测试步骤）
        /// </summary>
        /// <param name="channelIndex">测试通道索引</param>
        /// <param name="rowIndex">当前测试项在 DataGrid 中的行索引</param>
        /// <param name="address">继电器地址</param>
        /// <param name="relayIndex">继电器编号</param>
        /// <param name="repeatCount">重复次数（开关一次计为一次）</param>
        /// <param name="baudRate">波特率</param>
        /// <param name="delayMs">打开和关闭之间的延时毫秒数</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否全部成功</returns>
        private async Task<bool> RepeatToggleRelay(int channelIndex, int rowIndex, int address, int relayIndex, int repeatCount, int baudRate = 38400, int delayMs = 100, CancellationToken cancellationToken = default)
        {
            try
            {
                DataTable dt = ProjectSettings.testDataTable;
                if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
                {
                    AppendLog($"[通道{channelIndex + 1}] 无效的行索引: {rowIndex}", LogError);
                    return false;
                }
                DataRow row = dt.Rows[rowIndex];
                bool isSelected = Convert.ToBoolean(row["Select"]);
                if (!isSelected)
                {
                    AppendLog($"[通道{channelIndex + 1}] 第 {rowIndex + 1} 行未勾选，跳过重复开关继电器", LogInfo);
                    await UpdateRelayResult(channelIndex, row, "跳过", true);
                    return true;
                }

                bool allSuccess = true;
                for (int i = 0; i < repeatCount; i++)
                {
                    // 开启继电器
                    bool openSuccess = await ControlRelaySingle(channelIndex, address, relayIndex, true, baudRate);
                    if (!openSuccess)
                    {
                        AppendLog($"[通道{channelIndex + 1}] 第 {i + 1} 次开启继电器失败", LogError);
                        allSuccess = false;
                        break;
                    }
                    await Task.Delay(delayMs, cancellationToken);

                    // 关闭继电器
                    bool closeSuccess = await ControlRelaySingle(channelIndex, address, relayIndex, false, baudRate);
                    if (!closeSuccess)
                    {
                        AppendLog($"[通道{channelIndex + 1}] 第 {i + 1} 次关闭继电器失败", LogError);
                        allSuccess = false;
                        break;
                    }
                    await Task.Delay(delayMs, cancellationToken);
                }

                string resultMsg = allSuccess ? $"成功开关{repeatCount}次" : "失败";
                await UpdateRelayResult(channelIndex, row, resultMsg, allSuccess);
                return allSuccess;
            }
            catch (Exception ex)
            {
                AppendLog($"[通道{channelIndex + 1}] 重复开关继电器异常: {ex.Message}", LogError);
                DataTable dt = ProjectSettings.testDataTable;
                if (dt != null && rowIndex >= 0 && rowIndex < dt.Rows.Count)
                {
                    await UpdateRelayResult(channelIndex, dt.Rows[rowIndex], "异常", false);
                }
                return false;
            }
        }

        /// <summary>
        /// 控制单个继电器（不更新 DataGrid，仅返回成功状态）
        /// </summary>
        private async Task<bool> ControlRelaySingle(int channelIndex, int address, int relayIndex, bool isOpen, int baudRate)
        {
            string result = await RelayController.SendCommandAsync(address, relayIndex, isOpen, 1, baudRate, null, msg => AppendLog(msg, LogInfo));
            return !string.IsNullOrEmpty(result) && !result.Contains("错误") && !result.Contains("TIMEOUT");
        }
        /// <summary>
        /// 弹出确认对话框，根据用户选择判定 PASS/FAIL
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <param name="rowIndex">DataGrid 行索引</param>
        /// <param name="promptMessage">提示消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>用户确认返回 true，否则 false</returns>
        private async Task<bool> ConfirmDisplay(int channelIndex, int rowIndex, string promptMessage, CancellationToken cancellationToken)
        {
            try
            {
                DataTable dt = ProjectSettings.testDataTable;
                if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
                {
                    AppendLog($"无效的行索引: {rowIndex}", LogError);
                    return false;
                }
                DataRow row = dt.Rows[rowIndex];
                bool isSelected = Convert.ToBoolean(row["Select"]);
                if (!isSelected)
                {
                    AppendLog($"第 {rowIndex + 1} 行未勾选，跳过确认", LogInfo);
                    await UpdateConfirmResult(channelIndex, row, "跳过", true);
                    return true;
                }

                var win = new ConfirmDisplayWindow(promptMessage);
                win.Owner = this;
                bool? result = win.ShowDialog();
                bool confirmed = result == true && win.IsConfirmed;

                string displayValue = confirmed ? "确认" : "取消";
                await UpdateConfirmResult(channelIndex, row, displayValue, confirmed);
                AppendLog($"用户确认结果: {(confirmed ? "确认" : "取消")} - {promptMessage}", confirmed ? LogSuccess : LogError);

                return confirmed;
            }
            catch (Exception ex)
            {
                AppendLog($"确认异常: {ex.Message}", LogError);
                return false;
            }
        }

        /// <summary>
        /// 更新确认结果到 DataGrid
        /// </summary>
        private async Task UpdateConfirmResult(int channelIndex, DataRow row, string value, bool pass)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                string valueColumn = $"Channel{channelIndex + 1}Value";
                string resultColumn = $"Channel{channelIndex + 1}Result";
                row[valueColumn] = value;
                row[resultColumn] = pass ? "PASS" : "FAIL";
            });
        }

        /// <summary>
        /// 打印标签（动态日期+递增序列号）
        /// </summary>
        /// <summary>
        /// 打印动态标签（支持日期+递增序列号）
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <param name="rowIndex">DataGrid 行索引</param>
        /// <param name="fixedPart">固定部分（如 "FBAD64202"）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否成功</returns>
        private async Task<bool> PrintLabel(int channelIndex, int rowIndex, string fixedPart, CancellationToken cancellationToken)
        {
            // 检查在线打印开关
            if (!appSettings.OnlinePrint)
            {
                AppendLog("在线打印未启用，跳过打印标签", LogInfo);
                DataTable dt = ProjectSettings.testDataTable;
                if (dt != null && rowIndex >= 0 && rowIndex < dt.Rows.Count)
                {
                    DataRow row = dt.Rows[rowIndex];
                    await UpdatePrintResult(channelIndex, row, "在线打印未启用，跳过打印标签", true);
                }
                return true; // 跳过视为通过
            }

            // 原有的打印逻辑（保持不变）
            try
            {
                DataTable dt = ProjectSettings.testDataTable;
                if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
                {
                    AppendLog($"无效的行索引: {rowIndex}", LogError);
                    return false;
                }
                DataRow row = dt.Rows[rowIndex];
                bool isSelected = Convert.ToBoolean(row["Select"]);
                if (!isSelected)
                {
                    AppendLog($"第 {rowIndex + 1} 行未勾选，跳过打印", LogInfo);
                    await UpdatePrintResult(channelIndex, row, "跳过", true);
                    return true;
                }

                // 获取偏移量和序列号
                var (xOffset, yOffset) = ZplOffsetHelper.GetOffset(ProjectSettings.CurrentProjectName);
                int serial = ZplOffsetHelper.GetNextSerial(ProjectSettings.CurrentProjectName);
                string serialStr = serial.ToString("D5");
                string datePart = DateTime.Now.ToString("yyMMdd");
                string dynamicSN = $"{datePart}{serialStr}";

                string zplTemplate = @"^CT~~CD,~CC^~CT~
^XA
~TA000
~JSN
^LT0
^MNW
^MTT
^PON
^PMN
^LH0,0
^JMA
^PR2,2
~SD30
^JUS
^LRN
^CI27
^PA0,1,1,0
^XZ
^XA
^MMT
^PW177
^LL177
^LS0
^FT131,123^A@B,15,18,TT0003M_^FH\^CI28^FD{0}^FS^CI27
^FT105,122^BXB,5,200,0,0,1,_,1
^FH\^FD{0} {1}^FS
^FT155,136^A@B,15,18,TT0003M_^FH\^CI28^FDCandela_v1.0.3^FS^CI27
^PQ1,0,1,Y
^XZ
";

                string zpl = string.Format(zplTemplate, fixedPart, dynamicSN);
                zpl = ZplOffsetHelper.ApplyOffset(zpl, xOffset, yOffset);

                bool success = await ZplPrinterHelper.PrintZplAsync(zpl, msg => AppendLog(msg, LogInfo));
                if (success)
                {
                    var ch = ProjectSettings.Channels.FirstOrDefault(c => c.Index == channelIndex);
                    if (ch != null) ch.PrintedSN = dynamicSN;
                }
                await UpdatePrintResult(channelIndex, row, success ? dynamicSN : "失败", success);
                AppendLog($"打印标签: {fixedPart} {dynamicSN} {(success ? "成功" : "失败")}", success ? LogSuccess : LogError);
                return success;
            }
            catch (Exception ex)
            {
                AppendLog($"打印标签异常: {ex.Message}", LogError);
                return false;
            }
        }

        private async Task UpdatePrintResult(int channelIndex, DataRow row, string value, bool pass)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                string valueColumn = $"Channel{channelIndex + 1}Value";
                string resultColumn = $"Channel{channelIndex + 1}Result";
                row[valueColumn] = value;
                row[resultColumn] = pass ? "PASS" : "FAIL";
            });
        }
        /// <summary>
        /// 比对主板镭雕码和打印条码
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <param name="rowIndex">DataGrid 行索引</param>
        /// <param name="expectedSN">期望的主板码（即测试前扫描的 SN）</param>
        /// <param name="expectedPrintContent">期望的打印条码内容（如 "FBAD64202 26052200001"）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否比对成功</returns>
        private async Task<bool> CompareScannedCodes(int channelIndex, int rowIndex, string expectedSN, string expectedPrintContent, CancellationToken cancellationToken)
        {
            // 检查在线打印开关
            if (!appSettings.OnlinePrint)
            {
                AppendLog("在线打印未启用，跳过条码比对", LogInfo);
                DataTable dt = ProjectSettings.testDataTable;
                if (dt != null && rowIndex >= 0 && rowIndex < dt.Rows.Count)
                {
                    DataRow row = dt.Rows[rowIndex];
                    await UpdateCompareResult(channelIndex, row, "跳过", true);
                }
                return true;
            }

            try
            {
                DataTable dt = ProjectSettings.testDataTable;
                if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
                {
                    AppendLog($"无效的行索引: {rowIndex}", LogError);
                    return false;
                }
                DataRow row = dt.Rows[rowIndex];
                bool isSelected = Convert.ToBoolean(row["Select"]);
                if (!isSelected)
                {
                    AppendLog($"第 {rowIndex + 1} 行未勾选，跳过比对", LogInfo);
                    await UpdateCompareResult(channelIndex, row, "跳过", true);
                    return true;
                }

                var win = new ScanCompareWindow(); // 使用无参构造函数，窗口会显示默认提示
                win.Owner = this;
                if (win.ShowDialog() != true)
                {
                    AppendLog("条码比对取消或未完整扫描", LogWarning);
                    await UpdateCompareResult(channelIndex, row, "取消", false);
                    return false;
                }

                string boardCode = win.FirstCode;
                string printCode = win.SecondCode;
                bool pass = boardCode.Equals(expectedSN, StringComparison.OrdinalIgnoreCase) &&
                            printCode.Equals(expectedPrintContent, StringComparison.OrdinalIgnoreCase);

                string displayValue = pass ? "一致" : $"主板:{boardCode}, 打印:{printCode}";
                await UpdateCompareResult(channelIndex, row, displayValue, pass);
                AppendLog($"条码比对结果: {(pass ? "通过" : "失败")} (主板码={boardCode}, 打印码={printCode})", pass ? LogSuccess : LogError);
                return pass;
            }
            catch (Exception ex)
            {
                AppendLog($"条码比对异常: {ex.Message}", LogError);
                return false;
            }
        }

        private async Task UpdateCompareResult(int channelIndex, DataRow row, string value, bool pass)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                string valueColumn = $"Channel{channelIndex + 1}Value";
                string resultColumn = $"Channel{channelIndex + 1}Result";
                row[valueColumn] = value;
                row[resultColumn] = pass ? "PASS" : "FAIL";
            });
        }

        #endregion ME MTD005 436_01-50-01（ FBAD64202） END


        #region ME MTD005 436_01-50-01(FBAD61004)
        private async Task<bool> ME_MTD005_436_01_50_01_FBAD61004(int channelIndex, string sn, CancellationToken ct)
        {
            try
            {
                await RelayController.SendCommandWithCrcAsync(CommandList.CloseAllRelay_01, 38400, ComName.rs485ComName, 1000, msg => AppendLog(msg));
                int stepRowIndex = 0;

                /*
           * MaxRetries=定义单个步骤测试
          -1 = 跟随系统设置 appSettings.FailRetryCount
          0 = 不重试，只执行 1 次
          1 = 失败后重试 1 次，总共最多执行 2 次
          2 = 失败后重试 2 次，总共最多执行 3 次
          3 = 失败后重试 3 次，总共最多执行 4 次
          /// <summary>
  /// -1 = 跟随系统设置
  ///  0 = 不重试
  ///  1 = 失败后重试 1 次
  ///  2 = 失败后重试 2 次
  /// </summary>
           */
                var steps = new List<(Func<CancellationToken, Task<bool>> Action, string Name, int RowIndex, int MaxRetries)>();


                // 步骤1：SN输入
                int row0 = stepRowIndex;
                steps.Add((async (token) => { await SN_Input(channelIndex, row0, sn, token); return true; }, "SN输入", row0, -1));
                stepRowIndex++;

                // 步骤2：治具下压确认
                int row1 = stepRowIndex;
                steps.Add((async (token) => { return await ConfirmFixtureDownward_FC(channelIndex, row1, token); }, "治具下压确认", row1, -1));
                stepRowIndex++;

                // 步骤3：打开烧录继电器Y13-Y16（开启）
                int row2 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row2, 1, 13, true, 4, 38400, token); }, "打开烧录Y13-Y16", row2, -1));
                stepRowIndex++;

                // 步骤4：等待稳定时间
                int row3 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row3, token); }, "等待稳定时间", row3, -1));
                stepRowIndex++;

                // 步骤5：擦除芯片
                int row4 = stepRowIndex;
                string erasPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProjectConfig", "ME MTD500TestConfig", "Erase", "flash.bat");
                steps.Add((async (token) => { return await BurnFirmware(channelIndex, row4, erasPath, token); }, "擦除芯片", row4, -1));
                stepRowIndex++;

                // 步骤6：关闭烧录继电器Y13-Y16
                int row5 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row5, 1, 13, false, 4, 38400, token); }, "关闭烧录Y13-Y16", row5, -1));
                stepRowIndex++;

                // 步骤7：读取输入电压（烧录前）
                int row6 = stepRowIndex;
                steps.Add((async (token) => { return await GetInputVolt(channelIndex, row6, token); }, "获取供电电压值(烧录前)", row6, -1));
                stepRowIndex++;

                // 步骤8：打开Y1供电
                int row7 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row7, 1, 1, true, 1, 38400, token); }, "开机-打开Y1", row7, -1));
                stepRowIndex++;

                // 步骤9：等待稳定时间
                int row8 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row8, token); }, "等待稳定时间", row8, -1));
                stepRowIndex++;

                // 步骤10：读取输入电流（烧录前）
                int row9 = stepRowIndex;
                steps.Add((async (token) => { return await GetInputCurrent(channelIndex, row9, token); }, "获取供电电流值(烧录前)", row9, -1));
                stepRowIndex++;

                // 步骤11：读取TP152电压值（电压模块通道1）
                int row10 = stepRowIndex;
                steps.Add((async (token) => { return await GetTPVolt(channelIndex, row10, CommandList.Read16_02Volt, 0, token); }, "测试TP152电压值", row10, -1));
                stepRowIndex++;

                // 步骤12：读取TP157电压值（电压模块通道2）
                int row11 = stepRowIndex;
                steps.Add((async (token) => { return await GetTPVolt(channelIndex, row11, CommandList.Read16_02Volt, 1, token); }, "测试TP157电压值", row11, -1));
                stepRowIndex++;

                // 步骤13：读取TP167电压值（电压模块通道3）
                int row12 = stepRowIndex;
                steps.Add((async (token) => { return await GetTPVolt(channelIndex, row12, CommandList.Read16_02Volt, 2, token); }, "测试TP167电压值", row12, -1));
                stepRowIndex++;

                // 步骤14：打开烧录继电器Y13-Y16（烧录准备）
                int row13 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row13, 1, 13, true, 4, 38400, token); }, "打开烧录Y13-Y16(烧录前)", row13, -1));
                stepRowIndex++;

                // 步骤15：等待稳定时间
                int row14 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row14, token); }, "等待稳定时间", row14, -1));
                stepRowIndex++;

                // 步骤16：固件烧录
                int row15 = stepRowIndex;
                string brunPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProjectConfig", "ME MTD500TestConfig", "Programme_FBAD61004", "flash.bat");
                steps.Add((async (token) => { return await BurnFirmware(channelIndex, row15, brunPath, token); }, "烧录固件", row15, -1));
                stepRowIndex++;

                // 步骤17：关闭烧录继电器Y13-Y16
                int row16 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row16, 1, 13, false, 4, 38400, token); }, "关闭烧录Y13-Y16(烧录后)", row16, -1));
                stepRowIndex++;

                // 步骤18：关闭Y1供电
                int row17 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row17, 1, 1, false, 1, 38400, token); }, "关机-关闭Y1", row17, -1));
                stepRowIndex++;

                // 步骤19：等待稳定时间
                int row18 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row18, token); }, "等待稳定时间", row18, -1));
                stepRowIndex++;

                // 步骤20：打开Y1供电（重启）
                int row19 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row19, 1, 1, true, 1, 38400, token); }, "开机-打开Y1(重启)", row19, -1));
                stepRowIndex++;

                // 步骤21：等待稳定时间
                int row20 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row20, token); }, "等待稳定时间", row20, -1));
                stepRowIndex++;

                // 步骤22：读取输入电压（烧录后）
                int row21 = stepRowIndex;
                steps.Add((async (token) => { return await GetInputVolt(channelIndex, row21, token); }, "获取供电电压值(烧录后)", row21, -1));
                stepRowIndex++;

                // 步骤23：读取输入电流（烧录后）
                int row22 = stepRowIndex;
                steps.Add((async (token) => { return await GetInputCurrent(channelIndex, row22, token); }, "获取供电电流值(烧录后)", row22, -1));
                stepRowIndex++;

                // 步骤24：开机并读取版本号（占用7行）
                int row23 = stepRowIndex;
                steps.Add((async (token) => { return await IdentifyTestComPort_FBAD61004(channelIndex, row23, token); }, "开机并读取版本号", row23, -1));
                stepRowIndex += 7; // 跳过已占用的7行

                // 步骤25：等待稳定时间
                int row24 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row24, token); }, "等待稳定时间", row24, -1));
                stepRowIndex++;
                // 步骤26：蜂鸣器检测
                int row25 = stepRowIndex;
                //steps.Add((async (token) => { return await CheckFrequency(channelIndex, row25, token); }, "频率检测", row25));
                steps.Add((async (token) => await StartFrequencyDetectionAsync(channelIndex, row25, token), "启动频率检测", row25, -1));
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row25, 1, 1, false, 1, 38400, token); }, "关闭Y1", row25, -1));
                steps.Add((async (token) => { await Task.Delay(1000, token); return true; }, "延迟1000ms", row25, -1));
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row25, 1, 1, true, 1, 38400, token); }, "打开Y1", row25, -1));
                steps.Add((async (token) => { await Task.Delay(1000, token); return true; }, "延迟1000ms", row25, -1));
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row25, 1, 2, true, 1, 38400, token); }, "打开Y2", row25, -1));
                steps.Add((async (token) => { await Task.Delay(500, token); return true; }, "延迟400ms", row25, -1));
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row25, 1, 2, false, 1, 38400, token); }, "关闭Y2", row25, -1));
                steps.Add((async (token) => await WaitForFrequencyResultAsync(token), "等待频率检测结果", row25, -1));
                steps.Add((async (token) => { await Task.Delay(500, token); return true; }, "延迟400ms", row25, -1));
                stepRowIndex++;


                // 步骤27：打开Y3TP96短接GND
                int row26 = stepRowIndex;
                steps.Add((async (token) => await StartFrequencyDetectionAsync(channelIndex, row26, token), "启动频率检测", row26, -1));

                stepRowIndex++;
                // 步骤28：蜂鸣器检测
                int row27 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row27, 1, 3, true, 1, 38400, token); }, "打开Y3", row27, -1));
                steps.Add((async (token) => { await Task.Delay(500, token); return true; }, "延迟400ms", row27, -1));
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row27, 1, 3, false, 1, 38400, token); }, "关闭Y3", row27, -1));
                steps.Add((async (token) => await WaitForFrequencyResultAsync(token), "等待频率检测结果", row27, -1));
                stepRowIndex++;


                // 步骤29：等待稳定时间
                int row28 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row28, token); }, "等待稳定时间", row28, -1));
                stepRowIndex++;

                // 步骤30：读取输入电流 TP96短接GND
                int row29 = stepRowIndex;
                steps.Add((async (token) => { return await GetInputCurrent(channelIndex, row29, token); }, "获取供电电流值TP96短接GND", row29, -1));
                stepRowIndex++;

                // 步骤31：读取TP173TP174电压值（电压模块通道4）
                int row30 = stepRowIndex;
                steps.Add((async (token) => { return await GetTPVolt(channelIndex, row30, CommandList.Read16_02Volt, 3, token, true); }, "测试TP173TP174电压值", row30, -1));
                stepRowIndex++;

                // 步骤32：打开Y4 Y5,TP173TP174接入10W负载
                int row31 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row31, 1, 4, true, 2, 38400, token); }, "打开Y4 Y5,TP173TP174接入10W负载", row31, -1));
                stepRowIndex++;

                // 步骤33：等待稳定时间
                int row32 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row32, token); }, "等待稳定时间", row32, -1));
                stepRowIndex++;

                // 步骤34：打开Y3TP96短接GND
                int row33 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row33, 1, 3, true, 1, 38400, token); }, "打开Y3", row33, -1));
                await Task.Delay(400);
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row33, 1, 3, false, 1, 38400, token); }, "关闭Y3", row33, -1));
                stepRowIndex++;

                // 步骤35：等待稳定时间
                int row34 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row34, token); }, "等待稳定时间", row34, -1));
                stepRowIndex++;

                // 步骤36：读取输入电流 带负载
                int row35 = stepRowIndex;
                steps.Add((async (token) => { return await GetInputCurrent(channelIndex, row35, token); }, "读取输入电流 带负载", row35, -1));
                stepRowIndex++;


                // 步骤37：读取TP152电压值（电压模块通道1）
                int row36 = stepRowIndex;
                steps.Add((async (token) => { return await GetTPVolt(channelIndex, row36, CommandList.Read16_02Volt, 0, token); }, "测试TP152电压值", row36, -1));
                stepRowIndex++;

                // 步骤38：读取TP157电压值（电压模块通道2）
                int row37 = stepRowIndex;
                steps.Add((async (token) => { return await GetTPVolt(channelIndex, row37, CommandList.Read16_02Volt, 1, token); }, "测试TP157电压值", row37, -1));
                stepRowIndex++;

                // 步骤39：重复开启和关闭Y6 TP126 4次
                int row38 = stepRowIndex;
                steps.Add((async (token) => { return await RepeatToggleRelay(channelIndex, row38, 1, 6, 5, 38400, 350, token); }, "重复开关Y6 TP126 4次", row38, -1));
                stepRowIndex++;

                // 步骤40：等待稳定时间
                int row39 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row39, token); }, "等待稳定时间", row39, -1));
                stepRowIndex++;

                // 步骤41：读取TP173TP174电压值（电压模块通道4）
                int row40 = stepRowIndex;
                steps.Add((async (token) => { return await GetTPVolt(channelIndex, row40, CommandList.Read16_02Volt, 4, token); }, "测试TP173TP174电压值", row40, -1));
                stepRowIndex++;


                // 步骤42：重复开启和关闭Y6 TP126 10次
                int row41 = stepRowIndex;
                steps.Add((async (token) => { return await RepeatToggleRelay(channelIndex, row41, 1, 7, 12, 38400, 350, token); }, "重复开关Y7 TP111 10次", row41, -1));
                stepRowIndex++;

                // 步骤43：等待稳定时间
                int row42 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row42, token); }, "等待稳定时间", row42, -1));
                stepRowIndex++;

                // 步骤44：读取TP173TP174电压值（电压模块通道4）
                int row43 = stepRowIndex;
                steps.Add((async (token) => { return await GetTPVolt(channelIndex, row43, CommandList.Read16_02Volt, 4, token); }, "测试TP173TP174电压值", row43, -1));
                stepRowIndex++;

                // 步骤45：断开Y4 Y5,TP173TP174断开10W负载
                int row44 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row44, 1, 4, false, 2, 38400, token); }, "打开Y4 Y5,TP173TP174接入10W负载", row44, -1));
                stepRowIndex++;

                // 步骤46：等待稳定时间
                int row45 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row45, token); }, "等待稳定时间", row45, -1));
                stepRowIndex++;

                // 步骤47：读取TP173TP174电压值（电压模块通道3）
                int row46 = stepRowIndex;
                steps.Add((async (token) => { return await GetTPVolt(channelIndex, row46, CommandList.Read16_02Volt, 3, token, true); }, "测试TP173TP174电压值", row46, -1));
                stepRowIndex++;

                // 步骤48：等待稳定时间
                int row47 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row47, token); }, "等待稳定时间", row47, -1));
                stepRowIndex++;

                // 步骤49：重复开启和关闭Y6 TP96 1次
                int row48 = stepRowIndex;
                steps.Add((async (token) => { return await RepeatToggleRelay(channelIndex, row48, 1, 3, 1, 38400, 200, token); }, "重复开关Y3 TP96 1次", row48, -1));
                stepRowIndex++;

                // 步骤50：打开Y8短接TP173, TP174
                int row49 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row49, 1, 8, true, 1, 38400, token); }, "打开Y8 短接TP173, TP174", row49, -1));
                stepRowIndex++;

                // 步骤51：等待稳定时间
                int row50 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row50, token); }, "等待稳定时间", row50, -1));
                stepRowIndex++;

                // 步骤52：重复开启和关闭Y6 TP96 1次
                int row51 = stepRowIndex;
                steps.Add((async (token) => { return await RepeatToggleRelay(channelIndex, row51, 1, 3, 1, 38400, 200, token); }, "重复开关Y3 TP96 1次", row51, -1));
                stepRowIndex++;

                // 步骤53：等待稳定时间
                int row52 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row52, token); }, "等待稳定时间", row52, -1));
                stepRowIndex++;

                // 步骤54：读取TP80电压值（电压模块通道6）
                int row53 = stepRowIndex;
                steps.Add((async (token) => { return await GetVoltageValue(channelIndex, row53, 0, token); }, "测试TP180电压值", row53, -1));
                stepRowIndex++;

                // 步骤55：重复开启和关闭Y7 TP111 1次
                int row54 = stepRowIndex;
                steps.Add((async (token) => { return await RepeatToggleRelay(channelIndex, row54, 1, 7, 1, 38400, 350, token); }, "重复开关Y7 TP111 1次", row54, -1));
                stepRowIndex++;

                // 步骤56：等待稳定时间
                int row55 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row55, token); }, "等待稳定时间", row55, -1));
                stepRowIndex++;
                // 步骤57：读取TP80电压值（电压模块通道6）
                int row56 = stepRowIndex;
                steps.Add((async (token) => { return await GetVoltageValue(channelIndex, row56, 0, token); }, "测试TP180电压值", row56, -1));
                stepRowIndex++;
                // 步骤58：显示屏确认
                int row57 = stepRowIndex;
                steps.Add((async (token) => { return await ConfirmDisplay(channelIndex, row57, "显示屏是否正常亮起并显示150Hz", token); }, "显示屏确认", row57, -1));
                stepRowIndex++;
                // 步骤59：关机
                int row58 = stepRowIndex;
                steps.Add((async (token) => { return await RepeatToggleRelay(channelIndex, row58, 1, 2, 1, 38400, 350, token); }, "关机-重复开关Y2 1次", row58, -1));
                stepRowIndex++;
                // 步骤60：断电
                int row59 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row59, 1, 8, false, 1, 38400, token); }, "关闭Y8", row59, -1));
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row59, 1, 1, false, 1, 38400, token); }, "断电-关闭Y1", row59, -1));
                stepRowIndex++;

                // 步骤61：打印
                int row60 = stepRowIndex;
                steps.Add((async (token) => { return await PrintLabel_FBAD61004(channelIndex, row60, "FBAD61004", token); }, "打印标签", row60, -1));
                stepRowIndex++;

                // 步骤62：验证打印
                int row61 = stepRowIndex;
                steps.Add((async (token) =>
                {
                    var ch = ProjectSettings.Channels.First(c => c.Index == channelIndex); string expectedPrintContent = $"FBAD61004 {ch.PrintedSN}";
                    return await CompareScannedCodes(channelIndex, row61, sn, expectedPrintContent, token);
                }, "条码比对", row61, -1));



                int totalSteps = steps.Count;
                int currentStep = 0;
                bool allPassed = true;

                foreach (var step in steps)
                {
                    currentStep++;

                    bool pass = await ExecuteTestStepAsync(
                        channelIndex,
                        step.Action,
                        step.Name,
                        step.RowIndex,
                        ct,
                        currentStep,
                        totalSteps,
                        maxRetries: step.MaxRetries);

                    if (!pass)
                    {
                        allPassed = false;
                        if (appSettings.StopOnFail) break;
                    }
                }
                await RelayController.SendCommandWithCrcAsync(CommandList.CloseAllRelay_01, 38400, ComName.rs485ComName, 1000, msg => AppendLog(msg));
                return allPassed;
            }
            catch (Exception ex)
            {
                AppendLog($"测试错误,错误类型：{ex.GetType().ToString()};错误信息：{ex.Message}");
                return false;
            }
        }


        /// <summary>
        /// 识别测试串口（先并行打开所有串口并开始读取，再发送继电器命令，最后收集响应并解析）
        /// </summary>
        private async Task<bool> IdentifyTestComPort_FBAD61004(int channelIndex, int startRowIndex, CancellationToken cancellationToken)
        {
            try
            {
                // 1. 获取所有可用串口（排除 RS485 串口）
                string[] allPorts = SerialPort.GetPortNames();
                var candidatePorts = allPorts.Where(p => p != ComName.rs485ComName).ToList();
                if (candidatePorts.Count == 0)
                {
                    AppendLog("未找到任何可用串口", LogError);
                    return false;
                }

                // 2. 从 DataGrid 读取预期值（仅用于后面比较）
                DataTable dt = ProjectSettings.testDataTable;
                if (dt == null || startRowIndex < 0 || startRowIndex >= dt.Rows.Count)
                {
                    AppendLog("无效的行索引或 DataTable 为空", LogError);
                    return false;
                }

                // 定义设备信息的顺序（与 DataGrid 中从 startRowIndex 开始的顺序一致）
                string[] orderedKeys = { "Revive II Type", "Parameter set", "HAL Rev", "MNLib", "STM32 Library", "Date", "HW-Rev." };
                int deviceInfoCount = orderedKeys.Length;

                // 读取预期值（仅针对这7个键）
                Dictionary<string, string> expectedValues = new Dictionary<string, string>();
                for (int i = 0; i < deviceInfoCount; i++)
                {
                    int rowIndex = startRowIndex + i;
                    if (rowIndex >= dt.Rows.Count) break;
                    DataRow row = dt.Rows[rowIndex];
                    string testItem = row["TestItem"]?.ToString().Trim();
                    string key = orderedKeys[i];
                    if (!string.Equals(testItem, key, StringComparison.OrdinalIgnoreCase))
                    {
                        AppendLog($"注意：第 {rowIndex + 1} 行的测试项 '{testItem}' 与预期键 '{key}' 不匹配，将使用该行数据", LogWarning);
                    }
                    string expected = row["LowerLimit"]?.ToString().Trim();
                    if (!string.IsNullOrEmpty(expected))
                        expectedValues[key] = expected;
                }

                // 3. 并行打开所有串口并启动读取任务
                var readTasks = new List<Task<(string PortName, string Response)>>();
                var openedPorts = new List<SerialPort>();
                bool anyOpen = false;

                foreach (var portName in candidatePorts)
                {
                    try
                    {
                        var port = new SerialPort(portName, 57600, Parity.None, 8, StopBits.One);
                        port.ReadTimeout = 2000;
                        port.WriteTimeout = 1000;
                        port.Open();
                        port.DiscardInBuffer();
                        openedPorts.Add(port);
                        var task = Task.Run(async () =>
                        {
                            StringBuilder sb = new StringBuilder();
                            DateTime start = DateTime.Now;
                            while ((DateTime.Now - start).TotalMilliseconds < 3000)
                            {
                                try
                                {
                                    if (port.BytesToRead > 0)
                                    {
                                        string line = port.ReadLine();
                                        sb.AppendLine(line);
                                    }
                                    else
                                    {
                                        await Task.Delay(20);
                                    }
                                }
                                catch (TimeoutException) { break; }
                                catch { break; }
                            }
                            return (PortName: portName, Response: sb.ToString());
                        }, cancellationToken);
                        readTasks.Add(task);
                        anyOpen = true;
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"打开串口 {portName} 失败: {ex.Message}", LogError);
                    }
                }

                if (!anyOpen)
                {
                    AppendLog("无法打开任何串口", LogError);
                    return false;
                }

                // 4. 发送继电器命令（通过 RS485 串口触发设备输出）
                AppendLog("发送继电器命令，触发设备输出信息...", LogInfo);
                await RelayController.SendCommandAsync(1, 2, true, 1, 38400, null, msg => AppendLog(msg, LogInfo));
                await WaitDialog.WaitOrThrowAsync("请等待电流值稳定，请稍候...", 0.3, this);
                await RelayController.SendCommandAsync(1, 2, false, 1, 38400, null, msg => AppendLog(msg, LogInfo));

                // 等待设备输出
                await Task.Delay(1000, cancellationToken);

                // 5. 等待所有读取任务完成
                var results = await Task.WhenAll(readTasks);

                // 6. 关闭所有串口
                foreach (var port in openedPorts)
                {
                    try { port.Close(); port.Dispose(); } catch { }
                }

                // 7. 分析响应，查找包含设备信息的串口
                string successPort = null;
                Dictionary<string, string> actualValues = null;

                foreach (var result in results)
                {
                    if (string.IsNullOrEmpty(result.Response)) continue;
                    AppendLog($"串口 {result.PortName} 响应:\n{result.Response}", LogInfo);

                    var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    string[] lines = result.Response.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string line in lines)
                    {
                        string trimmedLine = line.Trim();
                        if (string.IsNullOrEmpty(trimmedLine)) continue;
                        if (trimmedLine.StartsWith("Warning") || trimmedLine.Contains("MT.DERM")) continue;

                        // Revive II Type
                        var match = Regex.Match(trimmedLine, @"Revive II Type (\d+) v([\d\.\-]+)");
                        if (match.Success)
                        {
                            parsed["Revive II Type"] = $"{match.Groups[1].Value} v{match.Groups[2].Value}";
                            continue;
                        }

                        // Parameter set + HAL Rev (同一行)
                        match = Regex.Match(trimmedLine, @"Parameter set:\s*([^\s]+)\s+HAL Rev:\s+([^\s]+)");
                        if (match.Success)
                        {
                            parsed["Parameter set"] = match.Groups[1].Value;
                            parsed["HAL Rev"] = match.Groups[2].Value;
                            continue;
                        }
                        // 单独 Parameter set
                        if (!parsed.ContainsKey("Parameter set"))
                        {
                            match = Regex.Match(trimmedLine, @"Parameter set:\s*([^\s]+)");
                            if (match.Success) parsed["Parameter set"] = match.Groups[1].Value;
                        }
                        // 单独 HAL Rev
                        if (!parsed.ContainsKey("HAL Rev"))
                        {
                            match = Regex.Match(trimmedLine, @"HAL Rev:\s+([^\s]+)");
                            if (match.Success) parsed["HAL Rev"] = match.Groups[1].Value;
                        }

                        // MNLib + STM32 Library (同一行)
                        match = Regex.Match(trimmedLine, @"MNLib:\s+([^\s]+)\s+STM32 Library:\s+([^\s]+)");
                        if (match.Success)
                        {
                            parsed["MNLib"] = match.Groups[1].Value;
                            parsed["STM32 Library"] = match.Groups[2].Value;
                            continue;
                        }
                        // 单独 MNLib
                        if (!parsed.ContainsKey("MNLib"))
                        {
                            match = Regex.Match(trimmedLine, @"MNLib:\s+([^\s]+)");
                            if (match.Success) parsed["MNLib"] = match.Groups[1].Value;
                        }
                        // 单独 STM32 Library
                        if (!parsed.ContainsKey("STM32 Library"))
                        {
                            match = Regex.Match(trimmedLine, @"STM32 Library:\s+([^\s]+)");
                            if (match.Success) parsed["STM32 Library"] = match.Groups[1].Value;
                        }

                        // Date + HW-Rev. (同一行)
                        match = Regex.Match(trimmedLine, @"Date:\s+([^\s]+)\s+HW-Rev.:\s+(\S+)");
                        if (match.Success)
                        {
                            parsed["Date"] = match.Groups[1].Value;
                            parsed["HW-Rev."] = match.Groups[2].Value;
                            continue;
                        }
                        // 单独 Date
                        if (!parsed.ContainsKey("Date"))
                        {
                            match = Regex.Match(trimmedLine, @"Date:\s+([^\s]+)");
                            if (match.Success) parsed["Date"] = match.Groups[1].Value;
                        }
                        // 单独 HW-Rev.
                        if (!parsed.ContainsKey("HW-Rev."))
                        {
                            match = Regex.Match(trimmedLine, @"HW-Rev.:\s+(\S+)");
                            if (match.Success) parsed["HW-Rev."] = match.Groups[1].Value;
                        }
                    }

                    // 打印解析结果
                    foreach (var kv in parsed)
                    {
                        AppendLog($"解析字段: {kv.Key} = {kv.Value}", LogInfo);
                    }

                    if (parsed.ContainsKey("Revive II Type") && parsed.ContainsKey("Parameter set"))
                    {
                        successPort = result.PortName;
                        actualValues = parsed;
                        break;
                    }
                }

                if (successPort == null)
                {
                    AppendLog("未找到包含设备信息的串口", LogError);
                    return false;
                }

                // 8. 保存串口并更新 DataGrid（只更新7项设备信息）
                ComName.testComName = successPort;
                AppendLog($"成功识别测试串口: {successPort}", LogSuccess);

                bool allMatch = true;
                for (int i = 0; i < deviceInfoCount; i++)
                {
                    int rowIndex = startRowIndex + i;
                    if (rowIndex >= dt.Rows.Count)
                    {
                        AppendLog($"警告：DataGrid 行数不足，预期需要 {deviceInfoCount} 行，实际只有 {dt.Rows.Count - startRowIndex} 行", LogWarning);
                        break;
                    }
                    DataRow row = dt.Rows[rowIndex];
                    string key = orderedKeys[i];
                    string expected = expectedValues.ContainsKey(key) ? expectedValues[key] : "";
                    string actual = actualValues.ContainsKey(key) ? actualValues[key] : "";
                    bool pass = false;

                    if (!string.IsNullOrEmpty(expected) && !string.IsNullOrEmpty(actual))
                    {
                        string expectedTrim = expected.Trim();
                        string actualTrim = actual.Trim();
                        pass = actualTrim.Equals(expectedTrim, StringComparison.OrdinalIgnoreCase) ||
                               actualTrim.Contains(expectedTrim);
                    }
                    else if (string.IsNullOrEmpty(expected) && !string.IsNullOrEmpty(actual))
                    {
                        pass = true;
                    }

                    if (!pass) allMatch = false;

                    string displayValue = string.IsNullOrEmpty(actual) ? "未获取" : actual;
                    await UpdateTestResult(channelIndex, rowIndex, row, displayValue, pass);
                }

                if (allMatch)
                    AppendLog("所有设备信息匹配成功", LogSuccess);
                else
                    AppendLog("部分设备信息不匹配", LogError);

                return allMatch;
            }
            catch (Exception ex)
            {
                AppendLog($"识别测试串口异常: {ex.Message}", LogError);
                return false;
            }
        }




        /// <summary>
        /// 打印标签（动态日期+递增序列号）
        /// </summary>
        /// <summary>
        /// 打印动态标签（支持日期+递增序列号）
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <param name="rowIndex">DataGrid 行索引</param>
        /// <param name="fixedPart">固定部分（如 "FBAD64202"）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否成功</returns>
        private async Task<bool> PrintLabel_FBAD61004(int channelIndex, int rowIndex, string fixedPart, CancellationToken cancellationToken)
        {
            // 检查在线打印开关
            if (!appSettings.OnlinePrint)
            {
                AppendLog("在线打印未启用，跳过打印标签", LogInfo);
                DataTable dt = ProjectSettings.testDataTable;
                if (dt != null && rowIndex >= 0 && rowIndex < dt.Rows.Count)
                {
                    DataRow row = dt.Rows[rowIndex];
                    await UpdatePrintResult(channelIndex, row, "在线打印未启用，跳过打印标签", true);
                }
                return true; // 跳过视为通过
            }

            // 原有的打印逻辑（保持不变）
            try
            {
                DataTable dt = ProjectSettings.testDataTable;
                if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
                {
                    AppendLog($"无效的行索引: {rowIndex}", LogError);
                    return false;
                }
                DataRow row = dt.Rows[rowIndex];
                bool isSelected = Convert.ToBoolean(row["Select"]);
                if (!isSelected)
                {
                    AppendLog($"第 {rowIndex + 1} 行未勾选，跳过打印", LogInfo);
                    await UpdatePrintResult(channelIndex, row, "跳过", true);
                    return true;
                }

                // 获取偏移量和序列号
                var (xOffset, yOffset) = ZplOffsetHelper.GetOffset(ProjectSettings.CurrentProjectName);
                int serial = ZplOffsetHelper.GetNextSerial(ProjectSettings.CurrentProjectName);
                string serialStr = serial.ToString("D5");
                string datePart = DateTime.Now.ToString("yyMMdd");
                string dynamicSN = $"{datePart}{serialStr}";

                string zplTemplate = @"^CT~~CD,~CC^~CT~
^XA
~TA000
~JSN
^LT0
^MNW
^MTT
^PON
^PMN
^LH0,0
^JMA
^PR2,2
~SD30
^JUS
^LRN
^CI27
^PA0,1,1,0
^XZ
^XA
^MMT
^PW177
^LL142
^LS0
^FT131,116^A@B,15,18,TT0003M_^FH\^CI28^FD{0}^FS^CI27
^FT105,114^BXB,5,200,0,0,1,_,1
^FH\^FD{0} {1}^FS
^FT155,132^A@B,15,18,TT0003M_^FH\^CI28^FDExceed_v1.2.3^FS^CI27
^PQ1,0,1,Y
^XZ";

                string zpl = string.Format(zplTemplate, fixedPart, dynamicSN);
                zpl = ZplOffsetHelper.ApplyOffset(zpl, xOffset, yOffset);

                bool success = await ZplPrinterHelper.PrintZplAsync(zpl, msg => AppendLog(msg, LogInfo));
                if (success)
                {
                    var ch = ProjectSettings.Channels.FirstOrDefault(c => c.Index == channelIndex);
                    if (ch != null) ch.PrintedSN = dynamicSN;
                }
                await UpdatePrintResult(channelIndex, row, success ? dynamicSN : "失败", success);
                AppendLog($"打印标签: {fixedPart} {dynamicSN} {(success ? "成功" : "失败")}", success ? LogSuccess : LogError);
                return success;
            }
            catch (Exception ex)
            {
                AppendLog($"打印标签异常: {ex.Message}", LogError);
                return false;
            }
        }


        #endregion ME MTD005 436_01-50-01（FBAD61004） END


        #region LS电机控制板D350打印

        private async Task<bool> LSD350Print(int channelIndex, string sn, CancellationToken ct)
        {
            try
            {
                int stepRowIndex = 0;

                /*
           * MaxRetries=定义单个步骤测试
          -1 = 跟随系统设置 appSettings.FailRetryCount
          0 = 不重试，只执行 1 次
          1 = 失败后重试 1 次，总共最多执行 2 次
          2 = 失败后重试 2 次，总共最多执行 3 次
          3 = 失败后重试 3 次，总共最多执行 4 次
          /// <summary>
  /// -1 = 跟随系统设置
  ///  0 = 不重试
  ///  1 = 失败后重试 1 次
  ///  2 = 失败后重试 2 次
  /// </summary>
           */
                var steps = new List<(Func<CancellationToken, Task<bool>> Action, string Name, int RowIndex, int MaxRetries)>();
                // 步骤1：SN输入
                int row0 = stepRowIndex;
                steps.Add((async (token) => { await SN_Input(channelIndex, row0, sn, token); return true; }, "SN输入", row0, -1));
                stepRowIndex++;
                // 步骤2：打印条码
                int row1 = stepRowIndex;
                steps.Add((async (token) => { return await PrintBarcodeAsync(channelIndex, row1, sn, token); }, "打印条码", row1, -1));
                stepRowIndex++;
                // 步骤3：条码比对（传入打印sn 作为参考值）
                int row2 = stepRowIndex;
                steps.Add((async (token) => { return await CompareBoardAndPrintBarcode(channelIndex, row2, sn, ProjectSettings.PrintRefSN, token); }, "条码比对", row2, -1));
                stepRowIndex++;



                int totalSteps = steps.Count;
                int currentStep = 0;
                bool allPassed = true;
                foreach (var step in steps)
                {
                    currentStep++;

                    bool pass = await ExecuteTestStepAsync(
                        channelIndex,
                        step.Action,
                        step.Name,
                        step.RowIndex,
                        ct,
                        currentStep,
                        totalSteps,
                        maxRetries: step.MaxRetries);

                    if (!pass)
                    {
                        allPassed = false;
                        if (appSettings.StopOnFail) break;
                    }
                }

                return allPassed;
            }
            catch (Exception ex)
            {
                AppendLog($"测试错误,错误类型：{ex.GetType().ToString()};错误信息：{ex.Message}");
                return false;
            }
        }

        private async Task<bool> PrintBarcodeAsync(int channelIndex, int rowIndex, string sn, CancellationToken cancellationToken)
        {
            try
            {
                // 确保 SN 长度至少为 11（示例 "22605X00007"）
                // 检查 SN 有效性
                ProjectSettings.PrintRefSN = "";
                if (string.IsNullOrWhiteSpace(sn))
                {
                    AppendLog("SN 为空，无法打印条码", LogError);
                    return false;
                }
                if (sn.Length < 11)
                {
                    AppendLog($"SN 长度不足11位（当前长度：{sn.Length}），无法生成条码", LogError);
                    return false;
                }
                sn = sn.Substring(0, 11);
                var (year2Digit, weekNumber) = GetCurrentYearWeek();
                string weekStr = weekNumber.ToString("00"); // 保证两位数，如 "05"

                // 前4位为年份+周数+机型名称
                string newSnPrefix = year2Digit + weekStr + sn.Substring(5, 1) + "B";  // 例如 "2520"

                sn = sn.Substring(7, 4);//截取主板序列号


                // 生成条码数据
                string number = newSnPrefix + sn;
                ProjectSettings.PrintRefSN = number;
                string barcodeData = $"{newSnPrefix.Substring(0, 4)}>6{newSnPrefix.Substring(4, 2)}>5{sn}";

                // 构建完整的 ZPL 字符串（不含坐标偏移）
                string originalZpl = $@"^CT~~CD,~CC^~CT~
^XA
~TA000
~JSN
^LT0
^MNW
^MTT
^PON
^PMN
^LH0,0
^JMA
^PR2,2
~SD25
^JUS
^LRN
^CI27
^PA0,1,1,0
^XZ
^XA
^MMT
^PW374
^LL136
^LS0
^BY2,3,63^FT48,69^BCN,,N,N
^FH\^FD>;{barcodeData}^FS
^FT108,94^A0N,21,25^FH\^CI28^FD{number}^FS^CI27
^FT15,2^A0R,16,15^FH\^CI28^FDMade in China^FS^CI27
^FO303,59^GE36,38,1^FS
^FT316,87^A0N,25,25^FH\^CI28^FDB^FS^CI27
^PQ1,0,1,Y
^XZ";

                // 4. 获取偏移量
                var offset = ZplOffsetHelper.GetOffset(ProjectSettings.CurrentProjectName);
                AppendLog($"应用偏移量: X={offset.XOffset}, Y={offset.YOffset}", LogInfo);

                // 5. 应用坐标偏移
                var builder = new ZplBuilder(originalZpl, offset.XOffset, offset.YOffset);
                string finalZpl = builder.Build();

                // 可选：打印调试日志，检查坐标是否变化
                AppendLog($"偏移后 ZPL (前200字符): {finalZpl.Substring(0, Math.Min(200, finalZpl.Length))}", LogInfo);
                bool success = await ZplPrinterHelper.PrintZplAsync(finalZpl, msg => AppendLog(msg, LogInfo));

                if (success)
                    AppendLog("打印成功", LogSuccess);
                else
                    AppendLog("打印失败", LogError);

                // 更新 DataGrid...
                DataTable dt = ProjectSettings.testDataTable;
                if (dt != null && rowIndex >= 0 && rowIndex < dt.Rows.Count)
                {
                    DataRow row = dt.Rows[rowIndex];
                    string valueColumn = $"Channel{channelIndex + 1}Value";
                    string resultColumn = $"Channel{channelIndex + 1}Result";
                    await Dispatcher.InvokeAsync(() =>
                    {
                        row[valueColumn] = success ? "成功" : "失败";
                        row[resultColumn] = success ? "PASS" : "FAIL";
                    });
                }

                return success;
            }
            catch (Exception ex)
            {
                AppendLog($"打印异常: {ex.Message}", LogError);
                return false;
            }
        }
        /// <summary>
        /// 获取当前年份的后两位和ISO周数
        /// </summary>
        /// <returns>(年份后两位, 周数)</returns>
        private (string year2Digit, int weekNumber) GetCurrentYearWeek()
        {
            // 年份后两位
            string year2Digit = DateTime.Now.ToString("yy");  // 例如 "25" 表示2025年

            // 计算ISO周数（周一为一周的第一天，且第一周至少包含4天）
            var culture = CultureInfo.CurrentCulture;
            int weekNumber = culture.Calendar.GetWeekOfYear(
                DateTime.Now,
                CalendarWeekRule.FirstFourDayWeek,
                DayOfWeek.Monday);

            // 如果你使用的是 .NET Core 3.0+ 或 .NET 5+，可以用更简洁的 ISOWeek：
            // int weekNumber = ISOWeek.GetWeekOfYear(DateTime.Now);

            return (year2Digit, weekNumber);
        }
        /// <summary>
        /// 条码比对：扫描主板码和打印码，分别与传入的主板SN和打印参考SN对比
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <param name="rowIndex">行索引</param>
        /// <param name="boardSn">主板镭雕SN（预期值）</param>
        /// <param name="printRefSn">打印参考SN（预期值）</param>
        /// <param name="cancellationToken">取消令牌</param>
        private async Task<bool> CompareBoardAndPrintBarcode(int channelIndex, int rowIndex, string expectedBoardSn, string expectedPrintSn, CancellationToken cancellationToken)
        {
            try
            {
                var dlg = new ScanCompareWindow();
                dlg.Owner = this;
                bool? result = dlg.ShowDialog();


                if (result != true || string.IsNullOrEmpty(dlg.FirstCode) || string.IsNullOrEmpty(dlg.SecondCode))
                {
                    AppendLog("条码比对取消或未完整扫描", LogWarning);
                    await UpdateCompareResult(channelIndex, rowIndex, false, "取消/未完整");
                    return false;
                }

                // 规范化：只取前11位（忽略末尾可能的多余字符，如A、B等）
                string Normalize(string code)
                {
                    if (string.IsNullOrEmpty(code)) return string.Empty;
                    return code.Length > 12 ? code.Substring(0, 12) : code;
                }

                string normalizedFirst = Normalize(dlg.FirstCode);   // 扫描的主板码
                string normalizedSecond = Normalize(dlg.SecondCode); // 扫描的打印码
                string normalizedBoard = Normalize(expectedBoardSn);
                string normalizedPrintRef = Normalize(expectedPrintSn);

                bool firstMatches = normalizedFirst.Equals(normalizedBoard, StringComparison.OrdinalIgnoreCase);
                bool secondMatches = normalizedSecond.Equals(normalizedPrintRef, StringComparison.OrdinalIgnoreCase);
                bool matched = firstMatches && secondMatches;

                string detail = matched ? "比对一致" :
                    $"主板码扫描={dlg.FirstCode} → {normalizedFirst}, 预期主板={expectedBoardSn} → {normalizedBoard}; " +
                    $"打印码扫描={dlg.SecondCode} → {normalizedSecond}, 预期打印={expectedPrintSn} → {normalizedPrintRef}";
                await UpdateCompareResult(channelIndex, rowIndex, matched, matched ? "比对一致" : detail);
                AppendLog($"条码比对结果: {detail}", matched ? LogSuccess : LogError);
                return matched;
            }
            catch (Exception ex)
            {
                AppendLog($"条码比对异常: {ex.Message}", LogError);
                await UpdateCompareResult(channelIndex, rowIndex, false, "异常");
                return false;
            }
        }

        private async Task UpdateCompareResult(int channelIndex, int rowIndex, bool success, string valueText)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                DataTable dt = ProjectSettings.testDataTable;
                if (dt != null && rowIndex >= 0 && rowIndex < dt.Rows.Count)
                {
                    DataRow row = dt.Rows[rowIndex];
                    string valueColumn = $"Channel{channelIndex + 1}Value";
                    string resultColumn = $"Channel{channelIndex + 1}Result";
                    row[valueColumn] = valueText;
                    row[resultColumn] = success ? "PASS" : "FAIL";
                }
            });
        }
        #endregion LS电机控制D350打印END

        #region LS电机控制D550打印

        private async Task<bool> LSD550Print(int channelIndex, string sn, CancellationToken ct)
        {
            try
            {
                int stepRowIndex = 0;

                /*
           * MaxRetries=定义单个步骤测试
          -1 = 跟随系统设置 appSettings.FailRetryCount
          0 = 不重试，只执行 1 次
          1 = 失败后重试 1 次，总共最多执行 2 次
          2 = 失败后重试 2 次，总共最多执行 3 次
          3 = 失败后重试 3 次，总共最多执行 4 次
          /// <summary>
  /// -1 = 跟随系统设置
  ///  0 = 不重试
  ///  1 = 失败后重试 1 次
  ///  2 = 失败后重试 2 次
  /// </summary>
           */
                var steps = new List<(Func<CancellationToken, Task<bool>> Action, string Name, int RowIndex, int MaxRetries)>();
                // 步骤1：SN输入
                int row0 = stepRowIndex;
                steps.Add((async (token) => { await SN_Input(channelIndex, row0, sn, token); return true; }, "SN输入", row0, -1));
                stepRowIndex++;
                // 步骤2：打印条码
                int row1 = stepRowIndex;
                steps.Add((async (token) => { return await PrintBarcodeAsync_D550(channelIndex, row1, sn, token); }, "打印条码", row1, -1));
                stepRowIndex++;
                // 步骤3：条码比对（传入打印sn 作为参考值）
                int row2 = stepRowIndex;
                steps.Add((async (token) => { return await CompareBoardAndPrintBarcode(channelIndex, row2, sn, ProjectSettings.PrintRefSN, token); }, "条码比对", row2, -1));
                stepRowIndex++;



                int totalSteps = steps.Count;
                int currentStep = 0;
                bool allPassed = true;
                foreach (var step in steps)
                {
                    currentStep++;

                    bool pass = await ExecuteTestStepAsync(
                        channelIndex,
                        step.Action,
                        step.Name,
                        step.RowIndex,
                        ct,
                        currentStep,
                        totalSteps,
                        maxRetries: step.MaxRetries);

                    if (!pass)
                    {
                        allPassed = false;
                        if (appSettings.StopOnFail) break;
                    }
                }

                return allPassed;
            }
            catch (Exception ex)
            {
                AppendLog($"测试错误,错误类型：{ex.GetType().ToString()};错误信息：{ex.Message}");
                return false;
            }
        }

        private async Task<bool> PrintBarcodeAsync_D550(int channelIndex, int rowIndex, string sn, CancellationToken cancellationToken)
        {
            try
            {
                // 确保 SN 长度至少为 11（示例 "22605X00007"）
                // 检查 SN 有效性
                ProjectSettings.PrintRefSN = "";
                if (string.IsNullOrWhiteSpace(sn))
                {
                    AppendLog("SN 为空，无法打印条码", LogError);
                    return false;
                }
                if (sn.Length < 11)
                {
                    AppendLog($"SN 长度不足11位（当前长度：{sn.Length}），无法生成条码", LogError);
                    return false;
                }
                sn = sn.Substring(0, 11);
                var (year2Digit, weekNumber) = GetCurrentYearWeek();
                string weekStr = weekNumber.ToString("00"); // 保证两位数，如 "05"

                // 前4位为年份+周数+机型名称
                string newSnPrefix = year2Digit + weekStr + sn.Substring(5, 1) + "B";  // 例如 "2520"

                sn = sn.Substring(7, 4);//截取主板序列号


                // 生成条码数据
                string number = newSnPrefix + sn;
                ProjectSettings.PrintRefSN = number;
                string barcodeData = $"{newSnPrefix.Substring(0, 4)}>6{newSnPrefix.Substring(4, 2)}>5{sn}";

                // 构建完整的 ZPL 字符串（不含坐标偏移）
                string originalZpl = $@"^CT~~CD,~CC^~CT~
^XA
~TA000
~JSN
^LT0
^MNW
^MTT
^PON
^PMN
^LH0,0
^JMA
^PR2,2
~SD25
^JUS
^LRN
^CI27
^PA0,1,1,0
^XZ
^XA
^MMT
^PW374
^LL136
^LS0
^BY2,3,63^FT48,69^BCN,,N,N
^FH\^FD>;{barcodeData}^FS
^FT108,94^A0N,21,25^FH\^CI28^FD{number}^FS^CI27
^FT15,2^A0R,16,15^FH\^CI28^FDMade in China^FS^CI27
^FO303,59^GE36,38,1^FS
^FT316,87^A0N,25,25^FH\^CI28^FDB^FS^CI27
^PQ1,0,1,Y
^XZ";

                // 4. 获取偏移量
                var offset = ZplOffsetHelper.GetOffset(ProjectSettings.CurrentProjectName);
                AppendLog($"应用偏移量: X={offset.XOffset}, Y={offset.YOffset}", LogInfo);

                // 5. 应用坐标偏移
                var builder = new ZplBuilder(originalZpl, offset.XOffset, offset.YOffset);
                string finalZpl = builder.Build();

                // 可选：打印调试日志，检查坐标是否变化
                AppendLog($"偏移后 ZPL (前200字符): {finalZpl.Substring(0, Math.Min(200, finalZpl.Length))}", LogInfo);
                bool success = await ZplPrinterHelper.PrintZplAsync(finalZpl, msg => AppendLog(msg, LogInfo));

                if (success)
                    AppendLog("打印成功", LogSuccess);
                else
                    AppendLog("打印失败", LogError);

                // 更新 DataGrid...
                DataTable dt = ProjectSettings.testDataTable;
                if (dt != null && rowIndex >= 0 && rowIndex < dt.Rows.Count)
                {
                    DataRow row = dt.Rows[rowIndex];
                    string valueColumn = $"Channel{channelIndex + 1}Value";
                    string resultColumn = $"Channel{channelIndex + 1}Result";
                    await Dispatcher.InvokeAsync(() =>
                    {
                        row[valueColumn] = success ? "成功" : "失败";
                        row[resultColumn] = success ? "PASS" : "FAIL";
                    });
                }

                return success;
            }
            catch (Exception ex)
            {
                AppendLog($"打印异常: {ex.Message}", LogError);
                return false;
            }
        }

        #endregion LS电机控制D550打印 END

        #region EI G4 UUI Controller EC-1031 REV2

        /// <summary>
        /// EI G4 UUI Controller EC-1032 REV2控制器测试序列
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <param name="sn">序列号</param>
        /// <param name="ct">取消令牌</param>
        /// <param name="expectedMode">期望的模式："G3" 或 "UUI"</param>
        private async Task<bool> ControllerTestSequence(int channelIndex, string sn, CancellationToken ct)
        {
            try
            {
                await DMM.EnableFunctionAsync(DMM.CMD_SET_CURRENT_AC, msg => AppendLog(msg, LogInfo));
                int stepRowIndex = 0;
                /*
           * MaxRetries=定义单个步骤测试
          -1 = 跟随系统设置 appSettings.FailRetryCount
          0 = 不重试，只执行 1 次
          1 = 失败后重试 1 次，总共最多执行 2 次
          2 = 失败后重试 2 次，总共最多执行 3 次
          3 = 失败后重试 3 次，总共最多执行 4 次
          /// <summary>
  /// -1 = 跟随系统设置
  ///  0 = 不重试
  ///  1 = 失败后重试 1 次
  ///  2 = 失败后重试 2 次
  /// </summary>
           */
                var steps = new List<(Func<CancellationToken, Task<bool>> Action, string Name, int RowIndex, int MaxRetries)>();

                // 步骤1：SN输入
                int row0 = stepRowIndex;
                steps.Add((async (token) => { await SN_Input(channelIndex, row0, sn, token); return true; }, "SN输入", row0, -1));
                stepRowIndex++;
                // 步骤2：读取波形最大值
                int rowMax = stepRowIndex;
                steps.Add((async (token) => await MeasureMaxVoltage(channelIndex, rowMax, 1, token), "波形比较-最大值", rowMax, -1));
                stepRowIndex++;
                // 步骤3：读取波形最小值
                int rowMin = stepRowIndex;
                steps.Add((async (token) => await MeasureMinVoltage(channelIndex, rowMin, 1, token), "波形比较-最小值", rowMin, -1));
                stepRowIndex++;
                // 步骤4：读取载波频率
                int rowFreq = stepRowIndex;
                steps.Add((async (token) => await MeasureFrequencyWithFixedScale(channelIndex, rowFreq, 1, token), "载波频率", rowFreq, -1));
                stepRowIndex++;
                // 步骤5：旋转读取载波频率
                int rowCable = stepRowIndex;
                steps.Add((async (token) => await CableRotationFrequencyTest(channelIndex, rowCable, 1, token), "旋转电缆载波频率测试", rowCable, -1));
                stepRowIndex++;
                // 在测试序列中添加步骤
                int rowFreqDuration = stepRowIndex;
                steps.Add((async (token) => await MeasureFrequencyDuration(channelIndex, rowFreqDuration, 1, 1900, 2100, token), "载玻频率开持续时间", rowFreqDuration, -1));
                stepRowIndex++;
                // 在测试序列中添加步骤
                int rowFreqDuration_off = stepRowIndex;
                steps.Add((async (token) => await MeasureFrequencyDuration(channelIndex, rowFreqDuration_off, 1, 0, 100, token, true), "载玻频率关持续时间", rowFreqDuration_off, -1));
                stepRowIndex++;
                // 步骤6：读取周期开
                int rowPeriod = stepRowIndex;
                steps.Add((async (token) => await MeasurePeriodWithDurationCheck(channelIndex, rowPeriod, 1, 1, token), "周期时间", rowPeriod, -1));
                stepRowIndex++;
                // 步骤3：打开烧录继电器Y13-Y16（开启）
                int row2 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row2, 1, 01, true, 2, 38400, token); }, "打开电流测试通道", row2, -1));
                stepRowIndex++;

                // 步骤4：等待稳定时间
                int row3 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row3, token); }, "等待稳定时间", row3, -1));
                stepRowIndex++;
                //电流测量
                int rowCurrent = stepRowIndex;
                steps.Add((async (token) => await MeasureCurrentStep(channelIndex, rowCurrent, 8, token), "电流测量", rowCurrent, -1));
                stepRowIndex++;

                int totalSteps = steps.Count;
                int currentStep = 0;
                bool allPassed = true;
                foreach (var step in steps)
                {
                    currentStep++;

                    bool pass = await ExecuteTestStepAsync(
                        channelIndex,
                        step.Action,
                        step.Name,
                        step.RowIndex,
                        ct,
                        currentStep,
                        totalSteps,
                        maxRetries: step.MaxRetries);

                    if (!pass)
                    {
                        allPassed = false;
                        if (appSettings.StopOnFail) break;
                    }
                }

                return allPassed;
            }
            catch (Exception ex)
            {
                AppendLog($"测试错误,错误类型：{ex.GetType().ToString()};错误信息：{ex.Message}");
                return false;
            }
            finally
            {
                //复位
                await _scope.ConfigureAndEnableMeasurementsAsync_EI(timebaseScale: 0.005, channelCount: 1, channelScale: 10, enableDutyCycle: false, enableOtherMeasurements: true, logAction: msg => AppendLog(msg));
                await RelayController.SendCommandWithCrcAsync(CommandList.CloseAllRelay_01, 38400, ComName.rs485ComName, 1000, msg => AppendLog(msg));
            }

        }

        /// <summary>
        /// 读取最大值：垂直刻度设为50V/div，持续8秒，若值在上下限内则立即返回PASS
        /// </summary>
        private async Task<bool> MeasureMaxVoltage(int channelIndex, int rowIndex, int scopeChannel, CancellationToken token)
        {
            DataTable dt = ProjectSettings.testDataTable;
            if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
            {
                AppendLog($"无效的行索引: {rowIndex}", LogError);
                return false;
            }
            DataRow row = dt.Rows[rowIndex];
            if (!Convert.ToBoolean(row["Select"]))
            {
                AppendLog($"第 {rowIndex + 1} 行未勾选，跳过最大值测量", LogInfo);
                await UpdateMeasureResult(channelIndex, row, "跳过", true);
                return true;
            }

            // 解析上下限
            double lower = 0, upper = 0;
            bool hasLimit = false;
            try
            {
                string lowerStr = row["LowerLimit"]?.ToString().Trim();
                string upperStr = row["UpperLimit"]?.ToString().Trim();
                if (!string.IsNullOrEmpty(lowerStr) && !string.IsNullOrEmpty(upperStr))
                {
                    lower = Convert.ToDouble(lowerStr);
                    upper = Convert.ToDouble(upperStr);
                    hasLimit = true;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"上下限解析失败: {ex.Message}", LogError);
            }

            IMessageBasedSession session = null;
            try
            {
                session = await _scope.OpenSessionAsync(msg => AppendLog(msg, LogInfo));
                if (session == null) return false;
                //设置时基为0
                await _scope.SendCommandAsync(session, ":TIMebase:SCALe 0.005");
                // 设置垂直刻度为50V/div，并设置偏移-25V（使中心在-25V，显示负电压范围）
                AppendLog($"设置通道{scopeChannel}垂直刻度为50V/div，偏移为-25V");
                await _scope.SendCommandAsync(session, $":CHANnel{scopeChannel}:SCALe 50");
                await _scope.SendCommandAsync(session, $":CHANnel{scopeChannel}:OFFSet -20");
                await Task.Delay(300, token);

                double lastValue = 0;
                DateTime start = DateTime.Now;
                bool found = false;
                while ((DateTime.Now - start).TotalSeconds < 8 && !found)
                {
                    if (token.IsCancellationRequested) break;
                    string resp = await _scope.QueryAsync(session, $":MEASure:VMAX? CHANnel{scopeChannel}", msg => AppendLog(msg, LogInfo));
                    if (double.TryParse(resp, System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out double value))
                    {
                        lastValue = value;
                        AppendLog($"读取最大值: {value:F4} V");
                        if (hasLimit && value >= lower && value <= upper)
                        {
                            found = true;
                            break;
                        }
                    }
                    await Task.Delay(500, token); // 间隔500ms，共16次，确保8秒内至少读16次
                }

                if (found)
                {
                    AppendLog($"最大值在上下限内，立即返回: {lastValue:F4} V");
                    await UpdateMeasureResult(channelIndex, row, $"{lastValue:F4} V", true);
                    return true;
                }
                else
                {
                    if (Math.Abs(lastValue) < 0.001 && lastValue != 0)
                        AppendLog("8秒内未读取到有效最大值", LogError);
                    else
                        AppendLog($"8秒内最大值未在上下限内，最后值: {lastValue:F4} V", LogWarning);
                    await UpdateMeasureResult(channelIndex, row, $"{lastValue:F4} V", false);
                    return false;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"读取最大值失败: {ex.Message}", LogError);
                await UpdateMeasureResult(channelIndex, row, "异常", false);
                return false;
            }
            finally { session?.Dispose(); }
        }

        /// <summary>
        /// 读取最小值：垂直刻度设为50V/div，持续8秒，若值在上下限内则立即返回PASS
        /// </summary>
        private async Task<bool> MeasureMinVoltage(int channelIndex, int rowIndex, int scopeChannel, CancellationToken token)
        {
            DataTable dt = ProjectSettings.testDataTable;
            if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
            {
                AppendLog($"无效的行索引: {rowIndex}", LogError);
                return false;
            }
            DataRow row = dt.Rows[rowIndex];
            if (!Convert.ToBoolean(row["Select"]))
            {
                AppendLog($"第 {rowIndex + 1} 行未勾选，跳过最小值测量", LogInfo);
                await UpdateMeasureResult(channelIndex, row, "跳过", true);
                return true;
            }

            double lower = 0, upper = 0;
            bool hasLimit = false;
            try
            {
                string lowerStr = row["LowerLimit"]?.ToString().Trim();
                string upperStr = row["UpperLimit"]?.ToString().Trim();
                if (!string.IsNullOrEmpty(lowerStr) && !string.IsNullOrEmpty(upperStr))
                {
                    lower = Convert.ToDouble(lowerStr);
                    upper = Convert.ToDouble(upperStr);
                    hasLimit = true;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"上下限解析失败: {ex.Message}", LogError);
            }

            IMessageBasedSession session = null;
            try
            {
                session = await _scope.OpenSessionAsync(msg => AppendLog(msg, LogInfo));
                if (session == null) return false;
                //设置时基
                await _scope.SendCommandAsync(session, ":TIMebase:SCALe 0.005");
                // 设置垂直刻度为50V/div，偏移设为-25V（与最大值一致，适合显示负压）
                AppendLog($"设置通道{scopeChannel}垂直刻度为50V/div，偏移为-50V");
                await _scope.SendCommandAsync(session, $":CHANnel{scopeChannel}:SCALe 50");
                await _scope.SendCommandAsync(session, $":CHANnel{scopeChannel}:OFFSet 20");
                await Task.Delay(300, token);

                double lastValue = 0;
                DateTime start = DateTime.Now;
                bool found = false;
                while ((DateTime.Now - start).TotalSeconds < 8 && !found)
                {
                    if (token.IsCancellationRequested) break;
                    string resp = await _scope.QueryAsync(session, $":MEASure:VMIN? CHANnel{scopeChannel}", msg => AppendLog(msg, LogInfo));
                    if (double.TryParse(resp, System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out double value))
                    {
                        lastValue = value;
                        AppendLog($"读取最小值: {value:F4} V");
                        if (hasLimit && value >= lower && value <= upper)
                        {
                            found = true;
                            break;
                        }
                    }
                    await Task.Delay(500, token);
                }

                if (found)
                {
                    AppendLog($"最小值在上下限内，立即返回: {lastValue:F4} V");
                    await UpdateMeasureResult(channelIndex, row, $"{lastValue:F4} V", true);
                    return true;
                }
                else
                {
                    if (Math.Abs(lastValue) < 0.001 && lastValue != 0)
                        AppendLog("8秒内未读取到有效最小值", LogError);
                    else
                        AppendLog($"8秒内最小值未在上下限内，最后值: {lastValue:F4} V", LogWarning);
                    await UpdateMeasureResult(channelIndex, row, $"{lastValue:F4} V", false);
                    return false;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"读取最小值失败: {ex.Message}", LogError);
                await UpdateMeasureResult(channelIndex, row, "异常", false);
                return false;
            }
            finally { session?.Dispose(); }
        }

        // 辅助更新方法
        private async Task UpdateMeasureResult(int channelIndex, DataRow row, string value, bool pass)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                string valueColumn = $"Channel{channelIndex + 1}Value";
                string resultColumn = $"Channel{channelIndex + 1}Result";
                row[valueColumn] = value;
                row[resultColumn] = pass ? "PASS" : "FAIL";
            });
        }
        /// <summary>
        /// 读取频率：垂直刻度设为10V/div（±30V范围），持续读取8秒，若频率在上下限内则立即返回PASS
        /// </summary>
        private async Task<bool> MeasureFrequencyWithFixedScale(int channelIndex, int rowIndex, int scopeChannel, CancellationToken token, bool openSend = false)
        {
            DataTable dt = ProjectSettings.testDataTable;
            if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
            {
                AppendLog($"无效的行索引: {rowIndex}", LogError);
                return false;
            }
            DataRow row = dt.Rows[rowIndex];
            if (!Convert.ToBoolean(row["Select"]))
            {
                AppendLog($"第 {rowIndex + 1} 行未勾选，跳过频率测量", LogInfo);
                await UpdateMeasureResult(channelIndex, row, "跳过", true);
                return true;
            }

            // 解析上下限（频率）
            double lower = 0, upper = 0;
            bool hasLimit = false;
            try
            {
                string lowerStr = row["LowerLimit"]?.ToString().Trim();
                string upperStr = row["UpperLimit"]?.ToString().Trim();
                if (!string.IsNullOrEmpty(lowerStr) && !string.IsNullOrEmpty(upperStr))
                {
                    lower = Convert.ToDouble(lowerStr);
                    upper = Convert.ToDouble(upperStr);
                    hasLimit = true;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"上下限解析失败: {ex.Message}", LogError);
            }

            IMessageBasedSession session = null;
            try
            {
                session = await _scope.OpenSessionAsync(msg => AppendLog(msg, LogInfo));
                if (session == null) return false;

                // 设置垂直刻度为10V/div（显示±30V范围），偏移0V
                AppendLog($"设置通道{scopeChannel}垂直刻度为10V/div，偏移0V");
                await _scope.SendCommandAsync(session, $":CHANnel{scopeChannel}:SCALe 10");
                await _scope.SendCommandAsync(session, $":CHANnel{scopeChannel}:OFFSet 0");
                await Task.Delay(300, token);

                double lastValue = 0;
                DateTime start = DateTime.Now;
                bool found = false;
                while ((DateTime.Now - start).TotalSeconds < 8 && !found)
                {
                    if (token.IsCancellationRequested) break;
                    string resp = await _scope.QueryAsync(session, $":MEASure:FREQuency? CHANnel{scopeChannel}", msg => AppendLog(msg, LogInfo));
                    if (double.TryParse(resp, System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out double value))
                    {
                        lastValue = value;
                        AppendLog($"读取频率: {value:F4} Hz");
                        if (hasLimit && value >= lower && value <= upper)
                        {
                            found = true;
                            break;
                        }
                    }
                    await Task.Delay(500, token);
                }
                if (openSend)
                {
                    await _scope.SendCommandAsync(session, ":TIMebase:SCALe 0.001");
                }
                if (found)
                {

                    AppendLog($"频率在上下限内，立即返回: {lastValue:F4} Hz");
                    await UpdateMeasureResult(channelIndex, row, $"{lastValue:F4} Hz", true);
                    return true;
                }
                else
                {
                    AppendLog($"8秒内频率未在上下限内，最后值: {lastValue:F4} Hz", LogWarning);
                    await UpdateMeasureResult(channelIndex, row, $"{lastValue:F4} Hz", false);
                    return false;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"读取频率失败: {ex.Message}", LogError);
                await UpdateMeasureResult(channelIndex, row, "异常", false);
                return false;
            }
            finally { session?.Dispose(); }
        }

        /// <summary>
        /// 持续读取频率，统计在范围内的累计持续时间（允许最多5次无效），当累计时间达到时间上下限范围内时立即返回PASS（30秒超时）
        /// </summary>
        private async Task<bool> MeasureFrequencyDuration(int channelIndex, int rowIndex, int scopeChannel, double freqLower, double freqUpper, CancellationToken token, bool opensend = false)
        {
            DataTable dt = ProjectSettings.testDataTable;
            if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
            {
                AppendLog($"无效的行索引: {rowIndex}", LogError);
                return false;
            }
            DataRow row = dt.Rows[rowIndex];
            if (!Convert.ToBoolean(row["Select"]))
            {
                AppendLog($"第 {rowIndex + 1} 行未勾选，跳过频率持续时间测量", LogInfo);
                await UpdateMeasureResult(channelIndex, row, "跳过", true);
                return true;
            }

            // 从DataGrid读取时间上下限（单位：秒）
            double timeLower = 0, timeUpper = 0;
            bool hasTimeLimit = false;
            try
            {
                string lowerStr = row["LowerLimit"]?.ToString().Trim();
                string upperStr = row["UpperLimit"]?.ToString().Trim();
                if (!string.IsNullOrEmpty(lowerStr) && !string.IsNullOrEmpty(upperStr))
                {
                    timeLower = Convert.ToDouble(lowerStr);
                    timeUpper = Convert.ToDouble(upperStr);
                    hasTimeLimit = true;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"时间上下限解析失败: {ex.Message}", LogError);
                await UpdateMeasureResult(channelIndex, row, "时间上下限错误", false);
                return false;
            }

            if (!hasTimeLimit)
            {
                AppendLog($"第 {rowIndex + 1} 行未设置时间上下限", LogError);
                await UpdateMeasureResult(channelIndex, row, "无时间上下限", false);
                return false;
            }

            IMessageBasedSession session = null;
            try
            {
                session = await _scope.OpenSessionAsync(msg => AppendLog(msg, LogInfo));
                if (session == null) return false;
                if (opensend)
                {
                    await _scope.SendCommandAsync(session, ":TIMebase:SCALe 0.001");
                }
                // 设置垂直刻度10V/div，偏移0V（±30V）
                AppendLog($"设置通道{scopeChannel}垂直刻度为10V/div，偏移0V");
                await _scope.SendCommandAsync(session, $":CHANnel{scopeChannel}:SCALe 10");
                await _scope.SendCommandAsync(session, $":CHANnel{scopeChannel}:OFFSet 0");
                await Task.Delay(300, token);

                // 累计计时
                double inRangeDuration = 0;
                bool inRange = false;
                int invalidCount = 0;                    // 新增：无效计数
                const int maxInvalidCount = 5;           // 允许的最大无效次数
                var stopwatch = new System.Diagnostics.Stopwatch();
                DateTime start = DateTime.Now;
                const double timeoutSeconds = 30.0;

                AppendLog($"开始监控频率范围 [{freqLower}, {freqUpper}] Hz，超时 {timeoutSeconds} 秒，目标时间 [{timeLower}, {timeUpper}] 秒");

                while ((DateTime.Now - start).TotalSeconds < timeoutSeconds && !token.IsCancellationRequested)
                {
                    string resp = await _scope.QueryAsync(session, $":MEASure:FREQuency? CHANnel{scopeChannel}", msg => AppendLog(msg, LogInfo));

                    // ========== 处理无信号 9.9000E+37 ==========
                    double freq = 0;
                    if (resp.Contains("9.9E+37") || resp.Contains("9.9000E+37"))
                    {
                        freq = 0.0;
                        AppendLog("无信号，频率视为 0 Hz", LogWarning);
                        // 不再在这里处理无效计数和 continue，让代码继续向下执行
                    }
                    // ========== 解析失败 ==========
                    else if (!double.TryParse(resp, System.Globalization.NumberStyles.Float,
                                              System.Globalization.CultureInfo.InvariantCulture, out freq))
                    {
                        freq = 0.0;
                        AppendLog($"解析频率失败，视为 0 Hz: {resp}", LogError);
                        // 不再 continue，继续执行后续判断
                    }
                    else if (!double.TryParse(resp, System.Globalization.NumberStyles.Float,
                                              System.Globalization.CultureInfo.InvariantCulture, out freq))
                    {
                        AppendLog($"解析频率失败: {resp}", LogError);
                        // 解析失败视为无效
                        invalidCount++;
                        AppendLog($"无效计数: {invalidCount}/{maxInvalidCount}");
                        if (invalidCount >= maxInvalidCount)
                        {
                            AppendLog($"无效次数达到 {maxInvalidCount}，重置计时", LogWarning);
                            inRangeDuration = 0;
                            inRange = false;
                            stopwatch.Reset();
                            invalidCount = 0;
                        }
                        await Task.Delay(60, token);
                        continue;
                    }

                    bool isInFreqRange = (freq >= freqLower && freq <= freqUpper);
                    if (isInFreqRange)
                    {
                        // 有效：重置无效计数
                        invalidCount = 0;
                        if (!inRange)
                        {
                            inRange = true;
                            stopwatch.Restart();
                        }
                        inRangeDuration = stopwatch.Elapsed.TotalSeconds;
                        AppendLog($"频率 {freq:F2} Hz 在范围内，已持续 {inRangeDuration:F2} 秒");


                    }
                    else
                    {
                        if (opensend && inRangeDuration < timeLower)
                        {
                            inRangeDuration += 0.5;
                        }
                        AppendLog($"频率超出，当前累计时间 inRangeDuration={inRangeDuration:F4} 秒，目标区间 [{timeLower}, {timeUpper}] 秒");
                        // 如果累计时间在时间上下限内，立即 PASS
                        if (inRangeDuration >= timeLower && inRangeDuration <= timeUpper)
                        {
                            AppendLog($"累计时间 {inRangeDuration:F2} 秒 已在 [{timeLower}, {timeUpper}] 内，立即 PASS");
                            await UpdateMeasureResult(channelIndex, row, $"{inRangeDuration:F2} s", true);
                            return true;
                        }
                        else
                        {
                            // 无效：增加无效计数
                            invalidCount++;
                            AppendLog($"频率 {freq:F2} Hz 超出范围，无效计数: {invalidCount}/{maxInvalidCount}");
                            if (inRange)
                            {
                                // 暂停计时（但保持累计时间不变）
                                inRange = false;
                                stopwatch.Stop();
                            }
                            // 检查无效次数是否达到阈值
                            if (invalidCount >= maxInvalidCount)
                            {
                                AppendLog($"无效次数达到 {maxInvalidCount}，重置计时", LogWarning);
                                inRangeDuration = 0;
                                inRange = false;
                                stopwatch.Reset();
                                invalidCount = 0;
                            }
                        }

                    }

                    await Task.Delay(60, token);
                }

                // 超时未达到时间目标
                AppendLog($"超时 {timeoutSeconds} 秒，累计时间 {inRangeDuration:F2} 秒 未在 [{timeLower}, {timeUpper}] 内", LogError);
                await UpdateMeasureResult(channelIndex, row, $"{inRangeDuration:F2} s", false);
                return false;
            }
            catch (Exception ex)
            {
                AppendLog($"频率持续时间测量异常: {ex.Message}", LogError);
                await UpdateMeasureResult(channelIndex, row, "异常", false);
                return false;
            }
            finally { session?.Dispose(); }
        }

        /// <summary>
        /// 旋转电缆频率测试：提示操作员旋转电缆，8秒内连续15次频率在范围内则通过
        /// </summary>
        private async Task<bool> CableRotationFrequencyTest(int channelIndex, int rowIndex, int scopeChannel, CancellationToken token)
        {
            DataTable dt = ProjectSettings.testDataTable;
            if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
            {
                AppendLog($"无效的行索引: {rowIndex}", LogError);
                return false;
            }
            DataRow row = dt.Rows[rowIndex];
            if (!Convert.ToBoolean(row["Select"]))
            {
                AppendLog($"第 {rowIndex + 1} 行未勾选，跳过旋转电缆测试", LogInfo);
                await UpdateMeasureResult(channelIndex, row, "跳过", true);
                return true;
            }

            double lower = 0, upper = 0;
            try
            {
                string lowerStr = row["LowerLimit"]?.ToString().Trim();
                string upperStr = row["UpperLimit"]?.ToString().Trim();
                if (string.IsNullOrEmpty(lowerStr) || string.IsNullOrEmpty(upperStr))
                {
                    AppendLog($"上下限未设置", LogError);
                    return false;
                }
                lower = Convert.ToDouble(lowerStr);
                upper = Convert.ToDouble(upperStr);
            }
            catch (Exception ex)
            {
                AppendLog($"上下限解析失败: {ex.Message}", LogError);
                return false;
            }

            var win = new CableRotationMonitorWindow(_scope, scopeChannel, lower, upper);
            win.Owner = this;
            bool? result = win.ShowDialog();
            bool success = result == true && win.IsSuccess;
            string displayValue = success ? $"{win.FinalFrequency:F4} Hz" : $"{win.FinalFrequency:F4} Hz(未达标)";
            await UpdateMeasureResult(channelIndex, row, displayValue, success);
            AppendLog($"旋转电缆测试: 最终频率 {win.FinalFrequency:F4} Hz, {(success ? "通过" : "失败")}", success ? LogSuccess : LogError);
            return success;
        }


        /// <summary>
        /// 持续读取周期，判断在范围内的持续时间是否达到目标值（周期单位：秒）
        /// DataGrid 显示最后一次读取的周期值，结果列显示 PASS/FAIL
        /// </summary>
        /// <param name="channelIndex">测试通道索引（用于DataGrid列）</param>
        /// <param name="rowIndex">DataGrid行索引</param>
        /// <param name="scopeChannel">示波器通道号（1~4）</param>
        /// <param name="targetDurationSeconds">目标持续时间（秒）</param>
        /// <param name="token">取消令牌</param>
        /// <param name="openSet">是否设置时基和垂直刻度</param>
        /// <returns>是否达到目标持续时间</returns>
        private async Task<bool> MeasurePeriodWithDurationCheck(
            int channelIndex,
            int rowIndex,
            int scopeChannel,
            double targetDurationSeconds,
            CancellationToken token,
            bool openSet = true)
        {
            DataTable dt = ProjectSettings.testDataTable;
            if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
            {
                AppendLog($"无效的行索引: {rowIndex}", LogError);
                return false;
            }
            DataRow row = dt.Rows[rowIndex];
            if (!Convert.ToBoolean(row["Select"]))
            {
                AppendLog($"第 {rowIndex + 1} 行未勾选，跳过周期持续时间检测", LogInfo);
                await UpdateMeasureResult(channelIndex, row, "跳过", true);
                return true;
            }

            // 从当前行读取周期上下限（单位：秒）
            double lowerLimit = 0, upperLimit = 0;
            bool hasLimit = false;
            try
            {
                string lowerStr = row["LowerLimit"]?.ToString().Trim();
                string upperStr = row["UpperLimit"]?.ToString().Trim();
                if (!string.IsNullOrEmpty(lowerStr) && !string.IsNullOrEmpty(upperStr))
                {
                    lowerLimit = Convert.ToDouble(lowerStr);
                    upperLimit = Convert.ToDouble(upperStr);
                    hasLimit = true;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"解析上下限失败: {ex.Message}", LogError);
                await UpdateMeasureResult(channelIndex, row, "上下限错误", false);
                return false;
            }

            if (!hasLimit)
            {
                AppendLog($"第 {rowIndex + 1} 行未设置上下限", LogError);
                await UpdateMeasureResult(channelIndex, row, "无上下限", false);
                return false;
            }

            // 打开示波器会话
            IMessageBasedSession session = null;
            try
            {
                session = await _scope.OpenSessionAsync(msg => AppendLog(msg, LogInfo));
                if (session == null) return false;

                if (openSet)
                {
                    // 1. 设置时基为5s/div
                    AppendLog($"设置时基为5s/div");
                    await _scope.SendCommandAsync(session, ":TIMebase:SCALe 5");
                    await Task.Delay(500, token);

                    // 2. 设置垂直刻度为10V/div，偏移0V（±30V范围）
                    AppendLog($"设置通道{scopeChannel}垂直刻度为10V/div，偏移0V");
                    await _scope.SendCommandAsync(session, $":CHANnel{scopeChannel}:SCALe 10");
                    await _scope.SendCommandAsync(session, $":CHANnel{scopeChannel}:OFFSet 0");
                    await Task.Delay(300, token);
                }

                // 准备计时
                var stopwatch = new System.Diagnostics.Stopwatch();
                double inRangeDuration = 0;
                bool inRange = false;
                DateTime startTime = DateTime.Now;
                const double timeoutSeconds = 40;
                double lastPeriod = 0; // 保存最后一次读取的周期值

                AppendLog($"开始监控周期，范围 [{lowerLimit}, {upperLimit}] 秒，目标持续 {targetDurationSeconds:F2} 秒，超时 {timeoutSeconds} 秒");

                while ((DateTime.Now - startTime).TotalSeconds < timeoutSeconds && !token.IsCancellationRequested)
                {
                    // 读取周期Cur值（单位：秒）
                    string resp = await _scope.QueryAsync(session, $":MEASure:PERiod? CHANnel{scopeChannel}", msg => AppendLog(msg, LogInfo));
                    if (double.TryParse(resp, System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out double period))
                    {
                        lastPeriod = period; // 保存最后读取的周期
                        bool isInRange = (period >= lowerLimit && period <= upperLimit);

                        if (isInRange)
                        {
                            if (!inRange)
                            {
                                inRange = true;
                                stopwatch.Restart();
                            }
                            inRangeDuration = stopwatch.Elapsed.TotalSeconds;
                            AppendLog($"周期 {period:F4} s 在范围内，已持续 {inRangeDuration:F2} 秒");
                            if (inRangeDuration >= targetDurationSeconds)
                            {
                                AppendLog($"达到目标持续时间 {targetDurationSeconds:F2} 秒，PASS");
                                // 成功时显示周期值
                                await UpdateMeasureResult(channelIndex, row, $"{period:F4} s", true);
                                return true;
                            }
                        }
                        else
                        {
                            if (inRange)
                            {
                                inRange = false;
                                stopwatch.Reset();
                                AppendLog($"周期 {period:F4} s 超出范围，重置计时");
                            }
                            else
                            {
                                AppendLog($"周期 {period:F4} s 超出范围，等待信号");
                            }
                        }
                    }
                    else
                    {
                        AppendLog($"读取周期失败: {resp}", LogError);
                    }

                    await Task.Delay(200, token);
                }

                // 超时或取消
                if (token.IsCancellationRequested)
                {
                    AppendLog("操作被取消", LogWarning);
                }
                else
                {
                    AppendLog($"超时 {timeoutSeconds} 秒，未达到目标持续时间 (累计 {inRangeDuration:F2} 秒)", LogError);
                }

                // 失败时显示最后一次读取的周期值（如果没有则显示"无数据"）
                string displayValue = lastPeriod > 0 ? $"{lastPeriod:F4} s" : "无数据";
                await UpdateMeasureResult(channelIndex, row, displayValue, false);
                return false;
            }
            catch (Exception ex)
            {
                AppendLog($"周期持续时间检测异常: {ex.Message}", LogError);
                await UpdateMeasureResult(channelIndex, row, "异常", false);
                return false;
            }
            finally
            {
                session?.Dispose();
            }
        }

        /// <summary>
        /// 连续读取万用表电流值（自动转换为 mA），直到值在上下限范围内或超时
        /// </summary>
        /// <param name="channelIndex">测试通道索引</param>
        /// <param name="rowIndex">DataGrid 行索引</param>
        /// <param name="durationSeconds">持续读取时间（秒），默认8秒</param>
        /// <param name="token">取消令牌</param>
        /// <returns>是否通过</returns>
        private async Task<bool> MeasureCurrentStep(
            int channelIndex,
            int rowIndex,
            double durationSeconds = 8,
            CancellationToken token = default)
        {
            DataTable dt = ProjectSettings.testDataTable;
            if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
            {
                AppendLog($"无效的行索引: {rowIndex}", LogError);
                return false;
            }
            DataRow row = dt.Rows[rowIndex];
            if (!Convert.ToBoolean(row["Select"]))
            {
                AppendLog($"第 {rowIndex + 1} 行未勾选，跳过电流测量", LogInfo);
                await UpdateDMMResult(channelIndex, row, "跳过", true);
                return true;
            }

            // 从 DataGrid 读取上下限（单位：毫安 mA）
            double lower_mA = 0, upper_mA = 0;
            bool hasLimit = false;
            try
            {
                string lowerStr = row["LowerLimit"]?.ToString().Trim();
                string upperStr = row["UpperLimit"]?.ToString().Trim();
                if (!string.IsNullOrEmpty(lowerStr) && !string.IsNullOrEmpty(upperStr))
                {
                    lower_mA = Convert.ToDouble(lowerStr);
                    upper_mA = Convert.ToDouble(upperStr);
                    hasLimit = true;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"解析上下限失败: {ex.Message}", LogError);
                await UpdateDMMResult(channelIndex, row, "上下限错误", false);
                return false;
            }

            if (!hasLimit)
            {
                AppendLog($"第 {rowIndex + 1} 行未设置上下限", LogError);
                await UpdateDMMResult(channelIndex, row, "无上下限", false);
                return false;
            }

            try
            {
                bool connected = await DMM.ConnectAsync(msg => AppendLog(msg, LogInfo), token);
                if (!connected)
                {
                    AppendLog("万用表连接失败", LogError);
                    await UpdateDMMResult(channelIndex, row, "连接失败", false);
                    return false;
                }

                bool funcSet = await DMM.EnableFunctionAsync(DMM.CMD_SET_CURRENT_AC, msg => AppendLog(msg, LogInfo), token);
                if (!funcSet)
                {
                    AppendLog("设置电流模式失败", LogError);
                    await UpdateDMMResult(channelIndex, row, "模式设置失败", false);
                    return false;
                }

                double lastCurrent_mA = 0;
                DateTime startTime = DateTime.Now;
#pragma warning disable CS0219
                bool found = false;
#pragma warning restore CS0219

                while ((DateTime.Now - startTime).TotalSeconds < durationSeconds && !token.IsCancellationRequested)
                {
                    string response = await DMM.WriteAndReadAsync(DMM.CMD_GET_CURRENT_AC, msg => AppendLog(msg, LogInfo), token);
                    if (double.TryParse(response, System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out double current_A))
                    {
                        double current_mA = current_A * 1000.0;
                        lastCurrent_mA = current_mA;
                        AppendLog($"读取电流: {current_mA:F3} mA");

                        if (current_mA >= lower_mA && current_mA <= upper_mA)
                        {
                            found = true;
                            AppendLog($"电流值 {current_mA:F3} mA 在上下限范围内，立即返回 PASS");
                            await UpdateDMMResult(channelIndex, row, $"{current_mA:F3} mA", true);
                            return true;
                        }
                    }
                    else
                    {
                        AppendLog("读取电流失败，继续重试...", LogWarning);
                    }

                    // 等待200ms再继续读取，避免过于频繁
                    await Task.Delay(200, token);
                }

                // 超时或取消
                if (token.IsCancellationRequested)
                {
                    AppendLog("电流测量被取消", LogWarning);
                    await UpdateDMMResult(channelIndex, row, "取消", false);
                    return false;
                }

                // 超时未找到在范围内的值
                AppendLog($"超时 {durationSeconds} 秒，电流值 {lastCurrent_mA:F3} mA 未在范围内", LogError);
                await UpdateDMMResult(channelIndex, row, $"{lastCurrent_mA:F3} mA", false);
                return false;
            }
            catch (OperationCanceledException)
            {
                AppendLog("电流测量被取消", LogWarning);
                await UpdateDMMResult(channelIndex, row, "取消", false);
                return false;
            }
            catch (Exception ex)
            {
                AppendLog($"电流测量异常: {ex.Message}", LogError);
                await UpdateDMMResult(channelIndex, row, "异常", false);
                return false;
            }
            finally
            {
                // 不自动断开连接，以便后续步骤复用
            }
        }
        /// <summary>
        /// 更新 DataGrid 中万用表结果
        /// </summary>
        private async Task UpdateDMMResult(int channelIndex, DataRow row, string value, bool pass)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                string valueColumn = $"Channel{channelIndex + 1}Value";
                string resultColumn = $"Channel{channelIndex + 1}Result";
                row[valueColumn] = value;
                row[resultColumn] = pass ? "PASS" : "FAIL";
            });
        }


        #endregion EI G4 UUI Controller EC-1031 REV2 END


        #region EI G4 SUI Controller EC-1031 REV2

        private async Task<bool> ControllerTestSequence_SUI(int channelIndex, string sn, CancellationToken ct)
        {
            try
            {
                await DMM.EnableFunctionAsync(DMM.CMD_SET_CURRENT_AC, msg => AppendLog(msg, LogInfo));
                int stepRowIndex = 0;
                /*
          * MaxRetries=定义单个步骤测试
         -1 = 跟随系统设置 appSettings.FailRetryCount
         0 = 不重试，只执行 1 次
         1 = 失败后重试 1 次，总共最多执行 2 次
         2 = 失败后重试 2 次，总共最多执行 3 次
         3 = 失败后重试 3 次，总共最多执行 4 次
         /// <summary>
 /// -1 = 跟随系统设置
 ///  0 = 不重试
 ///  1 = 失败后重试 1 次
 ///  2 = 失败后重试 2 次
 /// </summary>
          */
                var steps = new List<(Func<CancellationToken, Task<bool>> Action, string Name, int RowIndex, int MaxRetries)>();

                // 步骤1：SN输入
                int row0 = stepRowIndex;
                steps.Add((async (token) => { await SN_Input(channelIndex, row0, sn, token); return true; }, "SN输入", row0, -1));
                stepRowIndex++;
                // 步骤2：读取波形最大值
                int rowMax = stepRowIndex;
                steps.Add((async (token) => await MeasureMaxVoltage(channelIndex, rowMax, 1, token), "波形比较-最大值", rowMax, -1));
                stepRowIndex++;
                // 步骤3：读取波形最小值
                int rowMin = stepRowIndex;
                steps.Add((async (token) => await MeasureMinVoltage(channelIndex, rowMin, 1, token), "波形比较-最小值", rowMin, -1));
                stepRowIndex++;
                // 步骤4：读取载波频率
                int rowFreq = stepRowIndex;
                steps.Add((async (token) => await MeasureFrequencyWithFixedScale(channelIndex, rowFreq, 1, token, true), "载波频率", rowFreq, -1));
                stepRowIndex++;
                // 步骤5：旋转读取载波频率
                int rowCable = stepRowIndex;
                steps.Add((async (token) => await CableRotationFrequencyTest(channelIndex, rowCable, 1, token), "旋转电缆载波频率测试", rowCable, -1));
                stepRowIndex++;
                // 在测试序列中添加步骤
                int rowFreqDuration = stepRowIndex;
                steps.Add((async (token) => await MeasureFrequencyDuration(channelIndex, rowFreqDuration, 1, 1900, 2100, token, true), "载玻频率开持续时间", rowFreqDuration, -1));
                stepRowIndex++;
                // 在测试序列中添加步骤
                int rowFreqDuration_off = stepRowIndex;
                steps.Add((async (token) => await MeasureFrequencyDuration(channelIndex, rowFreqDuration_off, 1, 0, 100, token), "载玻频率关持续时间", rowFreqDuration_off, -1));
                stepRowIndex++;
                // 步骤6：读取周期开
                int rowPeriod = stepRowIndex;
                steps.Add((async (token) => await MeasurePeriodWithDurationCheck(channelIndex, rowPeriod, 1, 1, token), "周期时间", rowPeriod, -1));
                stepRowIndex++;
                // 步骤3：打开烧录继电器Y13-Y16（开启）
                int row2 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row2, 1, 01, true, 2, 38400, token); }, "打开电流测试通道", row2, -1));
                stepRowIndex++;

                // 步骤4：等待稳定时间
                int row3 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row3, token); }, "等待稳定时间", row3, -1));
                stepRowIndex++;
                //电流测量
                int rowCurrent = stepRowIndex;
                steps.Add((async (token) => await MeasureCurrentStep(channelIndex, rowCurrent, 15, token), "电流测量", rowCurrent, -1));
                stepRowIndex++;

                int totalSteps = steps.Count;
                int currentStep = 0;
                bool allPassed = true;
                foreach (var step in steps)
                {
                    currentStep++;

                    bool pass = await ExecuteTestStepAsync(
                        channelIndex,
                        step.Action,
                        step.Name,
                        step.RowIndex,
                        ct,
                        currentStep,
                        totalSteps,
                        maxRetries: step.MaxRetries);

                    if (!pass)
                    {
                        allPassed = false;
                        if (appSettings.StopOnFail) break;
                    }
                }

                return allPassed;
            }
            catch (Exception ex)
            {
                AppendLog($"测试错误,错误类型：{ex.GetType().ToString()};错误信息：{ex.Message}");
                return false;
            }
            finally
            {
                //复位
                await _scope.ConfigureAndEnableMeasurementsAsync_EI(timebaseScale: 0.005, channelCount: 1, channelScale: 10, enableDutyCycle: false, enableOtherMeasurements: true, logAction: msg => AppendLog(msg));
                await RelayController.SendCommandWithCrcAsync(CommandList.CloseAllRelay_01, 38400, ComName.rs485ComName, 1000, msg => AppendLog(msg));
            }

        }



        #endregion EI G4 SUI Controller EC-1031 REV2 END



        #region FC 840300-52 REV3高压测试
        /// <summary>
        /// FC 840300-52 REV3高压测试测试序列
        /// </summary>
        /// <param name="channelIndex"></param>
        /// <param name="sn"></param>
        /// <param name="ct"></param>
        /// <param name="expectedMode"></param>
        /// <returns></returns>
        private async Task<bool> FC84030052REV3HighTest(int channelIndex, string sn, CancellationToken ct)
        {
            try
            { // 发送初始化命令（关闭所有继电器）
                await RelayController.SendCommandWithCrcAsync(CommandList.CloseAllRelay_01, 38400, ComName.rs485ComName, 1000, msg => AppendLog(msg));
                int stepRowIndex = 0;
                /*
         * MaxRetries=定义单个步骤测试
        -1 = 跟随系统设置 appSettings.FailRetryCount
        0 = 不重试，只执行 1 次
        1 = 失败后重试 1 次，总共最多执行 2 次
        2 = 失败后重试 2 次，总共最多执行 3 次
        3 = 失败后重试 3 次，总共最多执行 4 次
        /// <summary>
/// -1 = 跟随系统设置
///  0 = 不重试
///  1 = 失败后重试 1 次
///  2 = 失败后重试 2 次
/// </summary>
         */
                var steps = new List<(Func<CancellationToken, Task<bool>> Action, string Name, int RowIndex, int MaxRetries)>();

                // 步骤1：SN输入
                int row0 = stepRowIndex;
                steps.Add((async (token) => { await SN_Input(channelIndex, row0, sn, token); return true; }, "SN输入", row0, -1));
                stepRowIndex++;
                // 步骤2：治具下压确认
                int row1 = stepRowIndex;
                steps.Add((async (token) => { return await ConfirmFixtureDownward_FC(channelIndex, row1, token); }, "治具下压确认", row1, -1));
                stepRowIndex++;
                // 步骤3：打开烧录继电器Y1-Y2（闭合）
                int row2 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row2, 1, 1, true, 2, 38400, token); }, "闭合继电器Y1Y2", row2, -1));
                stepRowIndex++;
                // 步骤4：治具下压确认
                int row3 = stepRowIndex;
                steps.Add((async (token) => { return await ConfirmFixtureDownward_FC(channelIndex, row3, token, false); }, "治具下压确认", row3, -1));
                stepRowIndex++;
                // 步骤5：发送 TEST 8 命令，预期响应为 "TEST"
                int row4 = stepRowIndex;
                steps.Add((async (token) => await ExecuteCommandStep(channelIndex, row4, "TEST 8", ComName.uartComName, 19200, token), "发送 TEST 8,开始输出高压...", row4, -1));
                stepRowIndex++;
                // 步骤6：高压测试中...
                int row5 = stepRowIndex;
                steps.Add((async (token) => { return await WaitWithRowTimeout(channelIndex, row5, "1700V高压测试中请勿触摸带电部位！\r\nDuring the 1700V high-voltage test, please do not touch the electrified parts!", token); }, "1700V高压测试中请勿触摸带电部位！\r\nDuring the 1700V high-voltage test, please do not touch the electrified parts!", row5, -1));
                stepRowIndex++;

                // 步骤7：高压测试参数（占用多行，从 rowStart 开始）
                int row6 = stepRowIndex;
                steps.Add((async (token) => await ExecuteTdParametersStep(channelIndex, row6, ComName.uartComName, 19200, token), "高压测试参数", row6, -1));
                stepRowIndex += 4; // 根据参数个数增加（电压、电流、时间、综合结论）

                // 步骤8：开启烧录继电器Y1-Y2（开启）
                int row7 = stepRowIndex;
                steps.Add((async (token) => { return await ControlRelayAndUpdate(channelIndex, row7, 1, 1, false, 2, 38400, token); }, "打开继电器Y1Y2", row7, -1));
                stepRowIndex++;

                int totalSteps = steps.Count;
                int currentStep = 0;
                bool allPassed = true;
                foreach (var step in steps)
                {
                    currentStep++;

                    bool pass = await ExecuteTestStepAsync(
                        channelIndex,
                        step.Action,
                        step.Name,
                        step.RowIndex,
                        ct,
                        currentStep,
                        totalSteps,
                        maxRetries: step.MaxRetries);

                    if (!pass)
                    {
                        allPassed = false;
                        if (appSettings.StopOnFail) break;
                    }
                }
                // 发送初始化命令（关闭所有继电器）
                await RelayController.SendCommandWithCrcAsync(CommandList.CloseAllRelay_01, 38400, ComName.rs485ComName, 1000, msg => AppendLog(msg));
                return allPassed;
            }
            catch (Exception ex)
            {
                AppendLog($"测试错误,错误类型：{ex.GetType().ToString()};错误信息：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 执行自定义命令测试步骤：发送命令，比较响应与预期值（从 DataGrid 的 UpperLimit 读取）
        /// </summary>
        /// <param name="channelIndex">测试通道索引</param>
        /// <param name="rowIndex">DataGrid 行索引</param>
        /// <param name="command">要发送的命令</param>
        /// <param name="portName">串口号（如 ComName.uartComName）</param>
        /// <param name="baudRate">波特率</param>
        /// <param name="token">取消令牌</param>
        /// <returns>是否通过</returns>
        private async Task<bool> ExecuteCommandStep(
            int channelIndex,
            int rowIndex,
            string command,
            string portName,
            int baudRate,
            CancellationToken token)
        {
            DataTable dt = ProjectSettings.testDataTable;
            if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
            {
                AppendLog($"无效的行索引: {rowIndex}", LogError);
                return false;
            }
            DataRow row = dt.Rows[rowIndex];
            if (!Convert.ToBoolean(row["Select"]))
            {
                AppendLog($"第 {rowIndex + 1} 行未勾选，跳过命令执行", LogInfo);
                await UpdateCommandResult(channelIndex, row, "跳过", true);
                return true;
            }

            // 从 UpperLimit 列读取预期响应（或 LowerLimit，可根据需要调整）
            string expectedResponse = row["UpperLimit"]?.ToString().Trim();
            if (string.IsNullOrEmpty(expectedResponse))
            {
                AppendLog($"第 {rowIndex + 1} 行未设置预期响应", LogError);
                await UpdateCommandResult(channelIndex, row, "无预期值", false);
                return false;
            }

            // 发送命令并读取响应
            string actualResponse = await SerialPortHelper.SendCommandAndReadResponseAsync(
                portName, baudRate, command,
                msg => AppendLog(msg, LogInfo), 2000, true);

            if (actualResponse == null)
            {
                await UpdateCommandResult(channelIndex, row, "无响应", false);
                return false;
            }

            // 判断是否匹配（不区分大小写）
            bool pass = actualResponse.Equals(expectedResponse, StringComparison.OrdinalIgnoreCase);
            string displayValue = $"{command} → {actualResponse}";
            await UpdateCommandResult(channelIndex, row, displayValue, pass);
            AppendLog($"命令 '{command}' 响应: {actualResponse}，预期: {expectedResponse}，{(pass ? "PASS" : "FAIL")}",
                      pass ? LogSuccess : LogError);
            return pass;
        }

        private async Task UpdateCommandResult(int channelIndex, DataRow row, string value, bool pass)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                string valueColumn = $"Channel{channelIndex + 1}Value";
                string resultColumn = $"Channel{channelIndex + 1}Result";
                row[valueColumn] = value;
                row[resultColumn] = pass ? "PASS" : "FAIL";
            });
        }

        /// <summary>
        /// 执行 TD? 命令，将各参数与 DataGrid 中连续行的上下限比较，并更新显示
        /// </summary>
        /// <param name="channelIndex">测试通道索引</param>
        /// <param name="startRowIndex">起始行索引（对应第一个参数）</param>
        /// <param name="portName">串口号</param>
        /// <param name="baudRate">波特率</param>
        /// <param name="token">取消令牌</param>
        /// <returns>所有参数是否全部通过</returns>
        private async Task<bool> ExecuteTdParametersStep(
    int channelIndex,
    int startRowIndex,
    string portName,
    int baudRate,
    CancellationToken token)
        {
            DataTable dt = ProjectSettings.testDataTable;
            if (dt == null)
            {
                AppendLog("DataTable 为空", LogError);
                return false;
            }

            // 发送 TD? 命令
            string response = await SerialPortHelper.SendCommandAndReadResponseAsync(
                portName, baudRate, "TD?",
                msg => AppendLog(msg, LogInfo), 2000, true);

            if (response == null)
            {
                AppendLog("TD? 命令无响应", LogError);
                return false;
            }

            // 解析响应
            TdResponseData tdData = HighVoltageTestParser.ParseTdResponse(response, msg => AppendLog(msg, LogInfo));
            if (tdData == null)
            {
                AppendLog("TD? 响应解析失败", LogError);
                return false;
            }

            // 提取参数列表（包含原始值字符串）
            var parameters = HighVoltageTestParser.ExtractNumericParameters(tdData);
            if (parameters.Count == 0)
            {
                AppendLog("未提取到有效参数", LogError);
                return false;
            }

            bool allPass = true;
            for (int i = 0; i < parameters.Count; i++)
            {
                int rowIndex = startRowIndex + i;
                if (rowIndex >= dt.Rows.Count)
                {
                    AppendLog($"行索引 {rowIndex} 超出范围，剩余 {parameters.Count - i} 个参数未处理", LogWarning);
                    break;
                }

                DataRow row = dt.Rows[rowIndex];
                var (displayName, rawValueWithUnit, numericValue, unit) = parameters[i];

                // 检查该行是否勾选
                if (!Convert.ToBoolean(row["Select"]))
                {
                    AppendLog($"第 {rowIndex + 1} 行未勾选，跳过参数: {displayName}", LogInfo);
                    await UpdateParameterResult(channelIndex, row, "跳过", true);
                    continue;
                }

                // 读取上下限
                double lower = 0, upper = 0;
                bool hasLimit = false;
                try
                {
                    string lowerStr = row["LowerLimit"]?.ToString().Trim();
                    string upperStr = row["UpperLimit"]?.ToString().Trim();
                    if (!string.IsNullOrEmpty(lowerStr) && !string.IsNullOrEmpty(upperStr))
                    {
                        lower = Convert.ToDouble(lowerStr);
                        upper = Convert.ToDouble(upperStr);
                        hasLimit = true;
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"第 {rowIndex + 1} 行上下限解析失败: {ex.Message}", LogError);
                }

                // 判断是否通过
                bool pass;
                if (hasLimit)
                {
                    // 特殊处理：综合结论使用整数值比较（0/1），其他参数用 double
                    if (displayName.Contains("综合结论"))
                    {
                        int intValue = numericValue > 0.5 ? 1 : 0;
                        int lowerInt = (int)lower;
                        int upperInt = (int)upper;
                        pass = intValue >= lowerInt && intValue <= upperInt;
                    }
                    else
                    {
                        pass = numericValue >= lower && numericValue <= upper;
                    }
                }
                else
                {
                    pass = true; // 未设置上下限则默认通过
                }

                if (!pass) allPass = false;

                // 构造显示字符串：例如 "直流耐电压测试 电压: 1.700kV"
                string displayValue = $"{displayName}: {rawValueWithUnit}";
                await UpdateParameterResult(channelIndex, row, displayValue, pass);
                AppendLog($"参数: {displayValue} 上下限[{lower}, {upper}] {(pass ? "PASS" : "FAIL")}",
                          pass ? LogSuccess : LogError);
            }

            return allPass;
        }

        /// <summary>
        /// 更新单个参数结果到 DataGrid
        /// </summary>
        private async Task UpdateParameterResult(int channelIndex, DataRow row, string value, bool pass)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                string valueColumn = $"Channel{channelIndex + 1}Value";
                string resultColumn = $"Channel{channelIndex + 1}Result";
                row[valueColumn] = value;
                row[resultColumn] = pass ? "PASS" : "FAIL";
            });
        }
        private async Task<bool> WaitWithRowTimeout(int channelIndex, int rowIndex, string displayinfo, CancellationToken cancellationToken)
        {
            DataTable dt = ProjectSettings.testDataTable;
            if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
            {
                AppendLog($"[通道{channelIndex + 1}] 无效的行索引: {rowIndex}", LogError);
                return false;
            }
            DataRow row = dt.Rows[rowIndex];
            bool isSelected = Convert.ToBoolean(row["Select"]);
            if (!isSelected)
            {
                AppendLog($"[通道{channelIndex + 1}] 第 {rowIndex + 1} 行未勾选，跳过等待", LogInfo);
                await Dispatcher.InvokeAsync(() =>
                {
                    string valueColumn = $"Channel{channelIndex + 1}Value";
                    string resultColumn = $"Channel{channelIndex + 1}Result";
                    row[valueColumn] = "Skip";
                    row[resultColumn] = "PASS";
                });
                return true;
            }

            double milliseconds = 0;
            string upperStr = row["UpperLimit"]?.ToString().Trim();
            string lowerStr = row["LowerLimit"]?.ToString().Trim();

            if (!string.IsNullOrEmpty(upperStr) && double.TryParse(upperStr, out double upperVal))
                milliseconds = upperVal;
            else if (!string.IsNullOrEmpty(lowerStr) && double.TryParse(lowerStr, out double lowerVal))
                milliseconds = lowerVal;
            else
            {
                AppendLog($"[通道{channelIndex + 1}] 第 {rowIndex + 1} 行未找到有效的时间值（毫秒）", LogError);
                return false;
            }

            double seconds = milliseconds / 1000.0;
            AppendLog($"[通道{channelIndex + 1}] 读取到等待时间: {milliseconds} ms → {seconds:F2} 秒", LogInfo);
            if (String.IsNullOrEmpty(displayinfo))
            {
                displayinfo = "请等待延时完成....";
            }
            try
            {
                await WaitDialog.WaitOrThrowAsync(displayinfo, seconds, this);
                // 等待成功（未取消），更新 DataGrid 为 PASS
                await Dispatcher.InvokeAsync(() =>
                {
                    string valueColumn = $"Channel{channelIndex + 1}Value";
                    string resultColumn = $"Channel{channelIndex + 1}Result";
                    row[valueColumn] = $"{seconds} 秒";
                    row[resultColumn] = "PASS";
                });
                return true;
            }
            catch (OperationCanceledException)
            {
                AppendLog($"[通道{channelIndex + 1}] 等待被用户取消", LogWarning);
                await Dispatcher.InvokeAsync(() =>
                {
                    string valueColumn = $"Channel{channelIndex + 1}Value";
                    string resultColumn = $"Channel{channelIndex + 1}Result";
                    row[valueColumn] = $"{seconds} 秒";
                    row[resultColumn] = "FAIL";
                });
                return false;
            }
            catch (Exception ex)
            {
                AppendLog($"[通道{channelIndex + 1}] 等待异常: {ex.Message}", LogError);
                await Dispatcher.InvokeAsync(() =>
                {
                    string valueColumn = $"Channel{channelIndex + 1}Value";
                    string resultColumn = $"Channel{channelIndex + 1}Result";
                    row[valueColumn] = "异常";
                    row[resultColumn] = "FAIL";
                });
                return false;
            }
        }


        #endregion FC 840300-52 REV3高压测试 END

        #region LO LR4-2912-0D测试
        /// <summary>
        /// LO LR4-2912-0D测试一拖三序列
        /// </summary>
        /// <param name="channelIndex"></param>
        /// <param name="sn"></param>
        /// <param name="ct"></param>
        /// <param name="expectedMode"></param>
        /// <returns></returns>
        private async Task<bool> LR4_2912Test(int channelIndex, string sn, CancellationToken ct)
        {
            try
            { // 发送初始化命令（关闭所有继电器）
                //await RelayController.SendCommandWithCrcAsync(CommandList.CloseAllRelay_01, 38400, ComName.rs485ComName, 1000, msg => AppendLog(msg));
                int stepRowIndex = 0;
                var steps = new List<(Func<CancellationToken, Task<bool>> Action, string Name, int RowIndex)>();

                // 步骤1：SN输入
                int row0 = stepRowIndex;
                steps.Add((async (token) => { await SN_Input(channelIndex, row0, sn, token); return true; }, "SN输入", row0));
                stepRowIndex++;
                // 步骤2：治具下压确认
                int row1 = stepRowIndex;
                steps.Add((async (token) => { return await ConfirmFixtureDownward_FC(channelIndex, row1, token); }, "治具下压确认", row1));
                stepRowIndex++;



                int totalSteps = steps.Count;
                int currentStep = 0;
                bool allPassed = true;
                foreach (var step in steps)
                {
                    currentStep++;
                    bool pass = await ExecuteTestStepAsync(channelIndex, step.Action, step.Name, step.RowIndex, ct, currentStep, totalSteps, maxRetries: 1);
                    if (!pass)
                    {
                        allPassed = false;
                        if (appSettings.StopOnFail) break;
                    }
                }
                // 发送初始化命令（关闭所有继电器）
                //await RelayController.SendCommandWithCrcAsync(CommandList.CloseAllRelay_01, 38400, ComName.rs485ComName, 1000, msg => AppendLog(msg));
                return allPassed;
            }
            catch (Exception ex)
            {
                AppendLog($"测试错误,错误类型：{ex.GetType().ToString()};错误信息：{ex.Message}");
                return false;
            }
        }
        #endregion LO LR4-2912-0D测试 END

        #region 测试项目配置
        private class TestProjectConfig
        {
            public string Name { get; set; }

            /// <summary>
            /// Legacy / XmlRunner / TemplateRunner / PluginRunner
            /// 当前阶段默认 Legacy。
            /// </summary>
            public string Runner { get; set; } = "Legacy";

            /// <summary>
            /// 测试流程映射 Key，例如 VC_DOCKING。
            /// </summary>
            public string SequenceKey { get; set; }

            /// <summary>
            /// 串口初始化映射 Key，当前阶段可以先预留。
            /// </summary>
            public string ComInitKey { get; set; }

            public List<string> RequiredComPorts { get; set; } = new List<string>();

            public Func<IProgress<string>, Task<bool>> InitializeComPortsAsync { get; set; }

            public Func<int, string, CancellationToken, Task<bool>> RunTestSequenceAsync { get; set; }

            public Func<string, string> GetTestFilePath { get; set; }

            public Func<string> GetFtpBasePath { get; set; }
        }
        private Dictionary<string, TestProjectConfig> _projectConfigs;
        /// <summary>
        /// SequenceKey 到测试流程方法的映射表。
        /// ProjectList.xml 中的 SequenceKey 会通过这里找到真正的测试方法。
        /// </summary>
        private Dictionary<string, Func<int, string, CancellationToken, Task<bool>>> _sequenceHandlers;
        /// <summary>
        /// 独立测试序列类注册表。
        /// 用于把测试流程从 MainWindow.xaml.cs 中逐步迁移出去。
        /// </summary>
        /// 
        private readonly object _fixtureDownSessionLock = new object();

        private readonly Dictionary<string, FixtureDownWaitSession> _fixtureDownSessions =
            new Dictionary<string, FixtureDownWaitSession>();

        private class FixtureDownParticipant
        {
            public int ChannelIndex { get; set; }
            public int RowIndex { get; set; }
        }

        private class FixtureDownWaitSession
        {
            public string Key { get; set; }

            public int RowIndex { get; set; }

            public bool OpenRelay { get; set; }

            public int ExpectedParticipants { get; set; }

            public DateTime CreatedAt { get; set; } = DateTime.Now;

            public readonly object SyncRoot = new object();

            public List<FixtureDownParticipant> Participants { get; } =
                new List<FixtureDownParticipant>();

            public Task<bool> WaitTask { get; set; }

            public bool IsCompleted { get; set; }
        }
        private Dictionary<string, Func<ITestSequence>> _sequenceClassFactories;
        private RigolDHO804Scope _scope;
        private class ProjectListRuntimeConfig
        {
            public string DisplayName { get; set; }
            public string FilePath { get; set; }
            public string FtpBasePath { get; set; }
            public List<string> RequiredComPorts { get; set; } = new List<string>();
            public string Runner { get; set; }
            public string SequenceKey { get; set; }
            public string ComInitKey { get; set; }
        }
        private void InitializeProjectConfigs()
        {
            _projectConfigs = new Dictionary<string, TestProjectConfig>();
            // 注册旧的 MainWindow 内部测试方法映射
            InitializeSequenceHandlers();

            // 注册新的独立测试序列类
            InitializeSequenceClasses();

            // ----------------------------------------------
            // 项目1: VC Docking Station Board测试
            // ----------------------------------------------
            _projectConfigs["VC Docking Station Board测试"] = new TestProjectConfig
            {
                Name = "VC Docking Station Board测试",
                RequiredComPorts = new List<string> { "rs485ComName", "powerSupplyComName", "ledComName" },
                InitializeComPortsAsync = async (progress) =>
                {
                    progress?.Report("正在识别 RS485 串口...");
                    ComName.rs485ComName = await SerialPortHelper.GetComNameAsync(
                        CommandList.CloseAllRelay_01, 38400, "01 06 00 34 00 00 C8 04",
                        msg => AppendLog(msg, LogInfo), timeoutMs: 3000);

                    if (string.IsNullOrEmpty(ComName.rs485ComName))
                    {
                        AppendLog("RS485 串口识别失败", LogError);
                        await Dispatcher.InvokeAsync(() => MessageBox.Show("自动识别 RS485 串口失败，请检查硬件连接，或进入系统设置手动选择串口。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                        return false;
                    }

                    progress?.Report("正在识别电源串口...");
                    ComName.powerSupplyComName = await SerialPortHelper.GetComNameByE1QueryAsync(9600, msg => AppendLog(msg, LogInfo));
                    if (string.IsNullOrEmpty(ComName.powerSupplyComName))
                    {
                        AppendLog("电源串口识别失败", LogError);
                        await Dispatcher.InvokeAsync(() => MessageBox.Show("自动识别电源串口失败，请检查硬件连接，或进入系统设置手动选择串口。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                        return false;
                    }

                    progress?.Report("正在识别LED串口...");
                    ComName.ledComName = await SerialPortHelper.GetSerialPort232Async(msg => AppendLog(msg, LogInfo));
                    if (string.IsNullOrEmpty(ComName.ledComName))
                    {
                        AppendLog("LED串口识别失败", LogError);
                        await Dispatcher.InvokeAsync(() => MessageBox.Show("自动识别LED串口失败，请检查硬件连接，或进入系统设置手动选择串口。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                        return false;
                    }

                    progress?.Report("正在初始化示波器...");
                    _scope = new RigolDHO804Scope();
                    bool configSuccess = await _scope.ConfigureScopeAsync(timebaseScale: 0.00003, channelCount: 4, channelScale: 2.0, enableDutyCycle: true, logAction: msg => AppendLog(msg));
                    if (!configSuccess)
                    {
                        AppendLog("示波器配置失败，请检查连接", LogError);
                        await Dispatcher.InvokeAsync(() => MessageBox.Show("示波器配置失败，请检查连接或手动配置。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                        return false;
                    }

                    await _scope.MeasureAllChannelsAsync(msg => AppendLog(msg, LogInfo));
                    AppendLog("示波器已就绪", LogSuccess);

                    progress?.Report("串口初始化完成");
                    AppendLog($"初始化成功：RS485={ComName.rs485ComName}, 电源={ComName.powerSupplyComName}, LED={ComName.ledComName}, 示波器已连接", LogSuccess);
                    return true;
                },
                RunTestSequenceAsync = async (channelIndex, sn, ct) =>
                {
                    return await RunVCDockingStationBoard_TestSequence(channelIndex, sn, ct);
                },
                GetTestFilePath = (projectName) => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProjectConfig", "VC Docking Station Board TestConfig", "TestFile_VC Docking Station Board.xml"),
                GetFtpBasePath = () => "/VC/VC Docking Station Board"
            };

            // ----------------------------------------------
            // 项目2: FC 840300-52 REV3烧录
            // ----------------------------------------------
            _projectConfigs["FC 840300-52 REV3烧录"] = new TestProjectConfig
            {
                Name = "FC 840300-52 REV3烧录",
                RequiredComPorts = new List<string> { "rs485ComName" },
                InitializeComPortsAsync = async (progress) =>
                {
                    progress?.Report("正在识别 RS485 串口...");
                    ComName.rs485ComName = await SerialPortHelper.GetComNameAsync(
                        CommandList.CloseAllRelay_01, 38400, "01 06 00 34 00 00 C8 04",
                        msg => AppendLog(msg, LogInfo), timeoutMs: 3000);
                    if (string.IsNullOrEmpty(ComName.rs485ComName))
                    {
                        AppendLog("RS485 串口识别失败", LogError);
                        await Dispatcher.InvokeAsync(() => MessageBox.Show("自动识别 RS485 串口失败，请检查硬件连接，或进入系统设置手动选择串口。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                        return false;
                    }
                    AppendLog($"RS485 串口识别成功: {ComName.rs485ComName}", LogSuccess);
                    return true;
                },
                RunTestSequenceAsync = async (channelIndex, sn, ct) =>
                {
                    return await RunFC_840300_52_REV3_TestSequence(channelIndex, sn, ct);
                },
                GetTestFilePath = (projectName) => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProjectConfig", "FC 840300-52 REV3 BrunConfig", "FC 840300-52 REV3 BrunConfig.xml"),
                GetFtpBasePath = () => "/FC/FC 840300-52 REV3 Programme"
            };

            // ----------------------------------------------
            // 项目3: ME MTD500测试
            // ----------------------------------------------
            _projectConfigs["ME MTD005 436_01-50-01(FBAD64202)"] = new TestProjectConfig
            {
                Name = "ME MTD005 436_01-50-01(FBAD64202)",
                RequiredComPorts = new List<string> { "rs485ComName", "uartComName", "powerSupplyComName" },
                InitializeComPortsAsync = async (progress) =>
                {
                    progress?.Report("正在识别 RS485 串口...");
                    ComName.rs485ComName = await SerialPortHelper.GetComNameAsync(
                        CommandList.CloseAllRelay_01, 38400, "01 06 00 34 00 00 C8 04",
                        msg => AppendLog(msg, LogInfo), timeoutMs: 3000);
                    if (string.IsNullOrEmpty(ComName.rs485ComName))
                    {
                        AppendLog("RS485 串口识别失败", LogError);
                        await Dispatcher.InvokeAsync(() => MessageBox.Show("自动识别 RS485 串口失败，请检查硬件连接，或进入系统设置手动选择串口。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                        return false;
                    }
                    AppendLog($"RS485 串口识别成功: {ComName.rs485ComName}", LogSuccess);

                    progress?.Report("正在识别声音频率计串口...");
                    ComName.uartComName = await SerialPortHelper.GetComNameAsync(
                        CommandList.ReadAddress, 115200, "01 03 02 00 01 79 84",
                        msg => AppendLog(msg, LogInfo));
                    if (string.IsNullOrEmpty(ComName.uartComName))
                    {
                        AppendLog("声音频率计串口识别失败", LogError);
                        await Dispatcher.InvokeAsync(() => MessageBox.Show("自动识别声音频率计串口失败，请检查硬件连接，或进入系统设置手动选择串口。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                        return false;
                    }
                    AppendLog($"声音频率计串口识别成功: {ComName.uartComName}", LogSuccess);

                    progress?.Report("正在识别电源串口...");
                    ComName.powerSupplyComName = await SerialPortHelper.GetComNameByE1QueryAsync(9600, msg => AppendLog(msg, LogInfo));
                    if (string.IsNullOrEmpty(ComName.powerSupplyComName))
                    {
                        AppendLog("电源串口识别失败", LogError);
                        await Dispatcher.InvokeAsync(() => MessageBox.Show("自动识别电源串口失败，请检查硬件连接，或进入系统设置手动选择串口。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                        return false;
                    }

                    AppendLog($"串口识别成功：RS485={ComName.rs485ComName}, 声音频率计={ComName.uartComName}, 电源={ComName.powerSupplyComName}", LogSuccess);
                    return true;
                },
                RunTestSequenceAsync = async (channelIndex, sn, ct) =>
                {
                    return await ME_MTD005_436_01_50_01_FBAD64202(channelIndex, sn, ct);
                },
                GetTestFilePath = (projectName) => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProjectConfig", "ME MTD500TestConfig", "ME MTD005 436_01-50-01(FBAD64202)TestConfig.xml"),
                GetFtpBasePath = () => "/ME/MTD500/ME MTD005 436_01-50-01(FBAD64202)"
            };


            _projectConfigs["ME MTD005 436_01-50-01(FBAD61004)"] = new TestProjectConfig
            {
                Name = "ME MTD005 436_01-50-01(FBAD61004)",
                RequiredComPorts = new List<string> { "rs485ComName", "uartComName", "powerSupplyComName" },
                InitializeComPortsAsync = async (progress) =>
                {
                    progress?.Report("正在识别 RS485 串口...");
                    ComName.rs485ComName = await SerialPortHelper.GetComNameAsync(
                        CommandList.CloseAllRelay_01, 38400, "01 06 00 34 00 00 C8 04",
                        msg => AppendLog(msg, LogInfo), timeoutMs: 3000);
                    if (string.IsNullOrEmpty(ComName.rs485ComName))
                    {
                        AppendLog("RS485 串口识别失败", LogError);
                        await Dispatcher.InvokeAsync(() => MessageBox.Show("自动识别 RS485 串口失败，请检查硬件连接，或进入系统设置手动选择串口。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                        return false;
                    }
                    AppendLog($"RS485 串口识别成功: {ComName.rs485ComName}", LogSuccess);

                    progress?.Report("正在识别声音频率计串口...");
                    ComName.uartComName = await SerialPortHelper.GetComNameAsync(
                        CommandList.ReadAddress, 115200, "01 03 02 00 01 79 84",
                        msg => AppendLog(msg, LogInfo));
                    if (string.IsNullOrEmpty(ComName.uartComName))
                    {
                        AppendLog("声音频率计串口识别失败", LogError);
                        await Dispatcher.InvokeAsync(() => MessageBox.Show("自动识别声音频率计串口失败，请检查硬件连接，或进入系统设置手动选择串口。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                        return false;
                    }
                    AppendLog($"声音频率计串口识别成功: {ComName.uartComName}", LogSuccess);

                    progress?.Report("正在识别电源串口...");
                    ComName.powerSupplyComName = await SerialPortHelper.GetComNameByE1QueryAsync(9600, msg => AppendLog(msg, LogInfo));
                    if (string.IsNullOrEmpty(ComName.powerSupplyComName))
                    {
                        AppendLog("电源串口识别失败", LogError);
                        await Dispatcher.InvokeAsync(() => MessageBox.Show("自动识别电源串口失败，请检查硬件连接，或进入系统设置手动选择串口。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                        return false;
                    }

                    AppendLog($"串口识别成功：RS485={ComName.rs485ComName}, 声音频率计={ComName.uartComName}, 电源={ComName.powerSupplyComName}", LogSuccess);
                    return true;
                },
                RunTestSequenceAsync = async (channelIndex, sn, ct) =>
                {
                    return await ME_MTD005_436_01_50_01_FBAD61004(channelIndex, sn, ct);
                },
                GetTestFilePath = (projectName) => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProjectConfig", "ME MTD500TestConfig", "ME MTD005 436_01-50-01(FBAD61004).xml"),
                GetFtpBasePath = () => "/ME/MTD500/ME MTD005 436_01-50-01(FBAD61004)"
            };
            // ----------------------------------------------
            // 项目4: LS D350打印
            // ----------------------------------------------
            _projectConfigs["LS D350打印贴纸"] = new TestProjectConfig
            {
                Name = "LS D350打印贴纸",
                RequiredComPorts = new List<string>(),
                InitializeComPortsAsync = async (progress) =>
                {
                    AppendLog($"{Name}程序加载成功！", LogSuccess);
                    await Task.CompletedTask;
                    return true;
                },
                RunTestSequenceAsync = async (channelIndex, sn, ct) =>
                {
                    return await LSD350Print(channelIndex, sn, ct);
                },
                GetTestFilePath = (projectName) => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProjectConfig", "LS PrintConfig", "LS D350PrintConfig.xml"),
                GetFtpBasePath = () => "/LS/D350/打印数据"
            };

            // ----------------------------------------------
            // 项目5: LS D550打印
            // ----------------------------------------------
            _projectConfigs["LS D550打印贴纸"] = new TestProjectConfig
            {
                Name = "LS D550打印贴纸",
                RequiredComPorts = new List<string>(),
                InitializeComPortsAsync = async (progress) =>
                {
                    AppendLog($"{Name}程序加载成功！", LogSuccess);
                    await Task.CompletedTask;
                    return true;
                },
                RunTestSequenceAsync = async (channelIndex, sn, ct) =>
                {
                    return await LSD550Print(channelIndex, sn, ct);
                },
                GetTestFilePath = (projectName) => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProjectConfig", "LS PrintConfig", "LS D550PrintConfig.xml"),
                GetFtpBasePath = () => "/LS/D550/打印数据"
            };
            // ----------------------------------------------
            // EI G4 UUI Controller EC-1032 REV2
            // ----------------------------------------------
            // EI G4 UUI Controller EC-1032 REV2 控制器测试
            _projectConfigs["Ei G4 UUI Controller EC-1032 REV2 Test"] = new TestProjectConfig
            {
                Name = "Ei G4 UUI Controller EC-1032 REV2 Test",
                RequiredComPorts = new List<string> { "rs485ComName" },
                InitializeComPortsAsync = async (progress) =>
                {

                    progress?.Report("正在识别 RS485 串口...");
                    ComName.rs485ComName = await SerialPortHelper.GetComNameAsync(CommandList.CloseAllRelay_01, 38400, "01 06 00 34 00 00 C8 04", msg => AppendLog(msg, LogInfo), timeoutMs: 3000);
                    if (string.IsNullOrEmpty(ComName.rs485ComName))
                    {
                        AppendLog("RS485 串口识别失败", LogError);
                        await Dispatcher.InvokeAsync(() => MessageBox.Show("自动识别 RS485 串口失败，请检查硬件连接，或进入系统设置手动选择串口。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                        return false;
                    }
                    AppendLog($"RS485 串口识别成功: {ComName.rs485ComName}", LogSuccess);

                    AppendLog("正在初始化示波器DHO804...", LogInfo);
                    progress?.Report("正在初始化示波器DHO804...");
                    _scope = new RigolDHO804Scope();

                    // 尝试连接并配置示波器（时基 2s/div，用于滚动模式捕获通断周期）
                    bool configOk = await _scope.ConfigureAndEnableMeasurementsAsync_EI(timebaseScale: 0.005, channelCount: 1, channelScale: 10, enableDutyCycle: false, enableOtherMeasurements: true, logAction: msg => AppendLog(msg));

                    if (!configOk)
                    {
                        await Dispatcher.InvokeAsync(() => MessageBox.Show("示波器DHO804连接失败，请检查USB连接或确认驱动程序已安装。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                        AppendLog("示波器DHO804初始化失败", LogError);
                        return false;
                    }
                    AppendLog("示波器DHO804已就绪", LogSuccess);

                    AppendLog("正在初始化数字万用表DM3508...", LogInfo);
                    progress?.Report("正在初始化数字万用表DM3508...");
                    _scope = new RigolDHO804Scope();

                    // 连接数字万用表
                    bool connected = await DMM.ConnectAsync(msg => AppendLog(msg, LogInfo));

                    if (!connected)
                    {
                        await Dispatcher.InvokeAsync(() => MessageBox.Show("数字万用表DM3508连接失败，请检查USB连接或确认驱动程序已安装。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                        AppendLog("数字万用表DM3508连接失败，请检查USB连接或确认驱动程序已安装。", LogError);
                        return false;
                    }

                    AppendLog("数字万用表DM3508已就绪", LogSuccess);
                    return true;
                },
                RunTestSequenceAsync = async (channelIndex, sn, ct) =>
                {
                    return await ControllerTestSequence(channelIndex, sn, ct);
                },
                GetTestFilePath = (name) => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProjectConfig", "EI G4 UUI Controller EC-1032 REV2 Test.xml"),
                GetFtpBasePath = () => "/EI/G4 UUI Controller EC-1032 REV2 TestRport"
            };
            // ----------------------------------------------
            // EI G4 SUI Controller EC-1031 REV2控制器测试
            // ----------------------------------------------
            // EI G4 SUI Controller EC-1031 REV2
            _projectConfigs["Ei G4 SUI Controller EC-1031 REV2 Test"] = new TestProjectConfig
            {
                Name = "Ei G4 SUI Controller EC-1031 REV2 Test",
                RequiredComPorts = new List<string> { "rs485ComName" },
                InitializeComPortsAsync = async (progress) =>
                {

                    progress?.Report("正在识别 RS485 串口...");
                    ComName.rs485ComName = await SerialPortHelper.GetComNameAsync(CommandList.CloseAllRelay_01, 38400, "01 06 00 34 00 00 C8 04", msg => AppendLog(msg, LogInfo), timeoutMs: 3000);
                    if (string.IsNullOrEmpty(ComName.rs485ComName))
                    {
                        AppendLog("RS485 串口识别失败", LogError);
                        await Dispatcher.InvokeAsync(() => MessageBox.Show("自动识别 RS485 串口失败，请检查硬件连接，或进入系统设置手动选择串口。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                        return false;
                    }
                    AppendLog($"RS485 串口识别成功: {ComName.rs485ComName}", LogSuccess);

                    AppendLog("正在初始化示波器DHO804...", LogInfo);
                    progress?.Report("正在初始化示波器DHO804...");
                    _scope = new RigolDHO804Scope();

                    // 尝试连接并配置示波器（时基 2s/div，用于滚动模式捕获通断周期）
                    bool configOk = await _scope.ConfigureAndEnableMeasurementsAsync_EI(timebaseScale: 0.005, channelCount: 1, channelScale: 10, enableDutyCycle: false, enableOtherMeasurements: true, logAction: msg => AppendLog(msg));

                    if (!configOk)
                    {
                        await Dispatcher.InvokeAsync(() => MessageBox.Show("示波器DHO804连接失败，请检查USB连接或确认驱动程序已安装。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                        AppendLog("示波器DHO804初始化失败", LogError);
                        return false;
                    }
                    AppendLog("示波器DHO804已就绪", LogSuccess);

                    AppendLog("正在初始化数字万用表DM3508...", LogInfo);
                    progress?.Report("正在初始化数字万用表DM3508...");
                    _scope = new RigolDHO804Scope();

                    // 连接数字万用表
                    bool connected = await DMM.ConnectAsync(msg => AppendLog(msg, LogInfo));

                    if (!connected)
                    {
                        await Dispatcher.InvokeAsync(() => MessageBox.Show("数字万用表DM3508连接失败，请检查USB连接或确认驱动程序已安装。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                        AppendLog("数字万用表DM3508连接失败，请检查USB连接或确认驱动程序已安装。", LogError);
                        return false;
                    }

                    AppendLog("数字万用表DM3508已就绪", LogSuccess);
                    return true;
                },
                RunTestSequenceAsync = async (channelIndex, sn, ct) =>
                {
                    return await ControllerTestSequence_SUI(channelIndex, sn, ct);
                },
                GetTestFilePath = (name) => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProjectConfig", "EI G4 SUI Controller EC-1031 REV2 Test.xml"),
                GetFtpBasePath = () => "/EI/G4 SUI Controller EC-1031 REV2 TestReport"
            };
            // ----------------------------------------------
            // 项目7: KB BMU-KB52SA_A00R20烧录
            // ----------------------------------------------
            _projectConfigs["KB BMU-KB52SA_A00R20烧录"] = new TestProjectConfig
            {
                Name = "KB BMU-KB52SA_A00R20烧录",
                RequiredComPorts = new List<string> { "rs485ComName" },
                InitializeComPortsAsync = async (progress) =>
                {
                    progress?.Report("正在识别 RS485 串口...");
                    ComName.rs485ComName = await SerialPortHelper.GetComNameAsync(
                        CommandList.CloseAllRelay_01, 38400, "01 06 00 34 00 00 C8 04",
                        msg => AppendLog(msg, LogInfo), timeoutMs: 3000);
                    if (string.IsNullOrEmpty(ComName.rs485ComName))
                    {
                        AppendLog("RS485 串口识别失败", LogError);
                        await Dispatcher.InvokeAsync(() => MessageBox.Show("自动识别 RS485 串口失败，请检查硬件连接，或进入系统设置手动选择串口。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                        return false;
                    }
                    AppendLog($"RS485 串口识别成功: {ComName.rs485ComName}", LogSuccess);
                    return true;
                },
                RunTestSequenceAsync = async (channelIndex, sn, ct) =>
                {
                    return await RunKB_BMU_KB52SA_A00R20BRunSequence(channelIndex, sn, ct);
                },
                GetTestFilePath = (projectName) => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProjectConfig", "KB BMU-KB52SA_A00R20ProgrammeConfig", "BMU-KB52SA_A00R20ProgrammeConfig.xml"),
                GetFtpBasePath = () => "/KB/KB BMU-KB52SA_A00R20烧录"
            };
            // ----------------------------------------------
            // 项目8: FC 840300-52 REV3高压测试
            // ----------------------------------------------
            _projectConfigs["FC 840300-52 REV3高压测试"] = new TestProjectConfig
            {
                Name = "FC 840300-52 REV3高压测试",
                RequiredComPorts = new List<string> { "rs485ComName" },
                InitializeComPortsAsync = async (progress) =>
                {
                    progress?.Report("正在识别 RS485 串口...");
                    ComName.rs485ComName = await SerialPortHelper.GetComNameAsync(
                        CommandList.CloseAllRelay_01, 38400, "01 06 00 34 00 00 C8 04",
                        msg => AppendLog(msg, LogInfo), timeoutMs: 3000);
                    if (string.IsNullOrEmpty(ComName.rs485ComName))
                    {
                        AppendLog("RS485 串口识别失败", LogError);
                        await Dispatcher.InvokeAsync(() => MessageBox.Show("自动识别 RS485 串口失败，请检查硬件连接，或进入系统设置手动选择串口。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                        return false;
                    }
                    AppendLog($"RS485 串口识别成功: {ComName.rs485ComName}", LogSuccess);


                    progress?.Report("正在高压测试仪串口...");
                    ComName.uartComName = await SerialPortHelper.GetUartCom("ENTER-TEST", new[] { "ENTER-TEST", "CannotExecute" }, 19200, msg => AppendLog(msg, LogInfo));
                    if (string.IsNullOrEmpty(ComName.uartComName))
                    {
                        AppendLog("高压测试仪串口识别失败", LogError);
                        await Dispatcher.InvokeAsync(() => MessageBox.Show("高压测试仪串口失败，请检查硬件连接，或进入系统设置手动选择串口。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                        return false;
                    }
                    AppendLog($"高压测试仪串口识别成功: {ComName.uartComName}", LogSuccess);


                    return true;
                },
                RunTestSequenceAsync = async (channelIndex, sn, ct) =>
                {
                    return await FC84030052REV3HighTest(channelIndex, sn, ct);
                },
                GetTestFilePath = (projectName) => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProjectConfig", "FC 840300-52 REV3 BrunConfig", "FC 840300-52 REV3 HighTest.xml"),
                GetFtpBasePath = () => "/FC/FC 840300-52 REV3高压测试"
            };


            // ----------------------------------------------
            // 项目9: LO LR4-2912-0D一拖三并行测试
            // ----------------------------------------------
            _projectConfigs["LR4-2912-0D AutoTest"] = new TestProjectConfig
            {

                InitializeComPortsAsync = async (progress) =>
                {
                    progress?.Report("正在识别 RS485 串口...");
                    ComName.rs485ComName = await SerialPortHelper.GetComNameAsync(
                        CommandList.CloseAllRelay_01, 38400, "01 06 00 34 00 00 C8 04",
                        msg => AppendLog(msg, LogInfo), timeoutMs: 3000);

                    if (string.IsNullOrEmpty(ComName.rs485ComName))
                    {
                        AppendLog("RS485 串口识别失败", LogError);
                        await Dispatcher.InvokeAsync(() => MessageBox.Show("• 自动识别 RS485 串口失败，将禁止测试！\r\n• 请检查硬件连接，或进入系统设置手动选择串口。\r\n• Failed to automatically detect the RS485 serial port; testing will be disabled\r\n• Please check the hardware connection or go to system settings to manually select the serial port.", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                        return false;
                    }
                    AppendLog($"RS485 串口识别成功: {ComName.rs485ComName}", LogSuccess);
                    return true;
                },

            };
            // // 关键：让 ProjectList.xml 中的配置覆盖 Main 里的固定配置
            ApplyProjectListOverrides();

        }

        private void InitializeSequenceHandlers()
        {
            _sequenceHandlers = new Dictionary<string, Func<int, string, CancellationToken, Task<bool>>>
            {
                ["VC_DOCKING"] = async (channelIndex, sn, ct) =>
                    await RunVCDockingStationBoard_TestSequence(channelIndex, sn, ct),

                ["FC_840300_REV3_BURN"] = async (channelIndex, sn, ct) =>
                    await RunFC_840300_52_REV3_TestSequence(channelIndex, sn, ct),

                ["FC_840300_REV3_HIGH"] = async (channelIndex, sn, ct) =>
                    await FC84030052REV3HighTest(channelIndex, sn, ct),

                ["ME_MTD005_FBAD64202"] = async (channelIndex, sn, ct) =>
                    await ME_MTD005_436_01_50_01_FBAD64202(channelIndex, sn, ct),

                ["ME_MTD005_FBAD61004"] = async (channelIndex, sn, ct) =>
                    await ME_MTD005_436_01_50_01_FBAD61004(channelIndex, sn, ct),

                ["EI_G4_UUI_CONTROLLER"] = async (channelIndex, sn, ct) =>
                    await ControllerTestSequence(channelIndex, sn, ct),

                ["EI_G4_SUI_CONTROLLER"] = async (channelIndex, sn, ct) =>
                    await ControllerTestSequence(channelIndex, sn, ct),

                ["LR4_2912_AUTO"] = async (channelIndex, sn, ct) =>
                    await LR4_2912Test(channelIndex, sn, ct)
            };
        }

        private void InitializeSequenceClasses()
        {
            _sequenceClassFactories = new Dictionary<string, Func<ITestSequence>>
            {
                ["LR4_2912_CLASS"] = () =>
                    new LR4_2912Sequence(
                        new DataTableTestGridService(
                            () => ProjectSettings.testDataTable,
                            Dispatcher,
                            ScrollToTestRowAsync,
                            UpdateChannelStepResultDisplay
                        ),
                        Dispatcher,
                        this
                    )
                ,
                ["SK_MPS250_Sequence"] = () => new TestPlatform.TestSequences.SKMPS250Sequence(
                    new DataTableTestGridService(
                        () => ProjectSettings.testDataTable,
                        Dispatcher,
                        ScrollToTestRowAsync,
                        UpdateChannelStepResultDisplay
                    ),
                    ShowSequenceConfirmAsync
                ),
                ["SK_MPS125_Sequence"] = () => new TestPlatform.TestSequences.SKMPS125Sequence(
                    new DataTableTestGridService(
                        () => ProjectSettings.testDataTable,
                        Dispatcher,
                        ScrollToTestRowAsync,
                        UpdateChannelStepResultDisplay
                    ),
                    ShowSequenceConfirmAsync
                ),
                ["SK_BCM250_Sequence"] = () => new TestPlatform.TestSequences.SKBCM250Sequence(
                    new DataTableTestGridService(
                        () => ProjectSettings.testDataTable,
                        Dispatcher,
                        ScrollToTestRowAsync,
                        UpdateChannelStepResultDisplay
                    ),
                    ShowSequenceConfirmAsync
                ),
                ["SK_BCM125_Sequence"] = () => new TestPlatform.TestSequences.SKBCM125Sequence(
                    new DataTableTestGridService(
                        () => ProjectSettings.testDataTable,
                        Dispatcher,
                        ScrollToTestRowAsync,
                        UpdateChannelStepResultDisplay
                    ),
                    ShowSequenceConfirmAsync
                )
            };
        }

        private async Task<bool> ShowSequenceConfirmAsync(string message)
        {
            return await Dispatcher.InvokeAsync(() =>
            {
                var dialog = new ConfirmDisplayWindow(message)
                {
                    Owner = this
                };

                return dialog.ShowDialog() == true;
            });
        }
        private async Task<bool> RunIndependentSequenceAsync(
     ITestSequence sequence,
     int channelIndex,
     string sn,
     CancellationToken ct)
        {
            Action<string> info = msg => AppendLog(msg, LogInfo);
            Action<string> warning = msg => AppendLog(msg, LogWarning);
            Action<string> error = msg => AppendLog(msg, LogError);
            Action<string> success = msg => AppendLog(msg, LogSuccess);

            sequence.LogInfo += info;
            sequence.LogWarning += warning;
            sequence.LogError += error;
            sequence.LogSuccess += success;

            try
            {
                ResolvedTestPlan resolvedPlan = null;
                if (sequence is SKBCM125Sequence)
                {
                    resolvedPlan = TestPlanService.Resolve(
                        ProjectSettings.TestFikePath,
                        TestPlanRuntimeState.ActiveProfileId,
                        TestPlanRuntimeState.GetStepOverridesSnapshot());
                }

                var context = new TestSequenceContext
                {
                    ChannelIndex = channelIndex,
                    SN = sn,
                    CancellationToken = ct,
                    StopOnFail = appSettings.StopOnFail,
                    ParallelTestCount = appSettings.ParallelTestCount,
                    FailRetryCount = appSettings.FailRetryCount,
                    TestPlan = resolvedPlan
                };

                return await sequence.RunAsync(context);
            }
            finally
            {
                sequence.LogInfo -= info;
                sequence.LogWarning -= warning;
                sequence.LogError -= error;
                sequence.LogSuccess -= success;
            }
        }

        private Dictionary<string, ProjectListRuntimeConfig> LoadProjectListRuntimeConfigs()
        {
            var result = new Dictionary<string, ProjectListRuntimeConfig>();

            try
            {
                string projectListPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,"ProjectList.xml"
                );

                if (!File.Exists(projectListPath))
                {
                    AppendLog($"ProjectList.xml 不存在，继续使用 Main 中默认项目配置：{projectListPath}", LogInfo);
                    return result;
                }

                XDocument doc = XDocument.Load(projectListPath);

                foreach (var elem in doc.Root.Elements("Project"))
                {
                    string displayName = (string)elem.Element("DisplayName") ?? "";

                    if (string.IsNullOrWhiteSpace(displayName))
                        continue;

                    List<string> requiredComPorts = elem.Element("RequiredComPorts")?
                        .Elements("ComPort")
                        .Select(x => ((string)x ?? "").Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList() ?? new List<string>();

                    result[displayName] = new ProjectListRuntimeConfig
                    {
                        DisplayName = displayName,
                        FilePath = (string)elem.Element("FilePath") ?? "",
                        FtpBasePath = (string)elem.Element("FtpBasePath") ?? "",
                        RequiredComPorts = requiredComPorts,

                        Runner = (string)elem.Element("Runner") ?? "Legacy",
                        SequenceKey = (string)elem.Element("SequenceKey") ?? "",
                        ComInitKey = (string)elem.Element("ComInitKey") ?? ""
                    };
                }
            }
            catch (Exception ex)
            {
                AppendLog($"读取 ProjectList.xml 运行配置失败，继续使用 Main 中默认配置：{ex.Message}", LogError);
            }

            return result;
        }
        private string ResolveProjectConfigPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return filePath;

            if (Path.IsPathRooted(filePath))
                return filePath;

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filePath);
        }
        private void ApplyProjectListOverrides()
        {
            var xmlConfigs = LoadProjectListRuntimeConfigs();

            if (xmlConfigs.Count == 0)
            {
                AppendLog("ProjectList.xml 未提供可覆盖的运行配置，继续使用 Main 中默认配置。", LogInfo);
                return;
            }

            foreach (var item in xmlConfigs.Values)
            {
                bool existsInMain = _projectConfigs.TryGetValue(item.DisplayName, out var config);

                if (!existsInMain)
                {
                    // 关键变化：
                    // 如果 ProjectList.xml 中有项目，并且它配置了有效 SequenceKey，
                    // 则可以自动创建 TestProjectConfig。
                    config = new TestProjectConfig
                    {
                        Name = item.DisplayName
                    };

                    _projectConfigs[item.DisplayName] = config;
                }

                config.Runner = string.IsNullOrWhiteSpace(item.Runner) ? "Legacy" : item.Runner;

                config.SequenceKey = item.SequenceKey ?? "";
                config.ComInitKey = item.ComInitKey ?? "";

                // 1. 覆盖测试 XML 文件路径
                if (!string.IsNullOrWhiteSpace(item.FilePath))
                {
                    string resolvedFilePath = ResolveProjectConfigPath(item.FilePath);
                    config.GetTestFilePath = _ => resolvedFilePath;
                }

                // 2. 覆盖 FTP 上传路径
                if (!string.IsNullOrWhiteSpace(item.FtpBasePath))
                {
                    string ftpBasePath = item.FtpBasePath;
                    config.GetFtpBasePath = () => ftpBasePath;
                }

                // 3. 覆盖必需串口列表
                if (item.RequiredComPorts != null && item.RequiredComPorts.Count > 0)
                {
                    config.RequiredComPorts = new List<string>(item.RequiredComPorts);
                }

                // 4. 根据 SequenceKey 自动绑定测试流程
                if (!string.IsNullOrWhiteSpace(item.SequenceKey))
                {
                    if (_sequenceHandlers != null &&
                        _sequenceHandlers.TryGetValue(item.SequenceKey, out var sequenceHandler))
                    {
                        config.RunTestSequenceAsync = sequenceHandler;

                        AppendLog(
                            $"项目 {item.DisplayName} 已通过 SequenceKey={item.SequenceKey} 绑定 Main 内部测试流程。",
                            LogInfo
                        );
                    }
                    else if (_sequenceClassFactories != null && _sequenceClassFactories.TryGetValue(item.SequenceKey, out var sequenceFactory))
                    {
                        config.RunTestSequenceAsync = async (channelIndex, sn, ct) =>
                        {
                            ITestSequence sequence = sequenceFactory();
                            return await RunIndependentSequenceAsync(sequence, channelIndex, sn, ct);
                        };

                        AppendLog(
                            $"项目 {item.DisplayName} 已通过 SequenceKey={item.SequenceKey} 绑定独立测试序列类。",
                            LogInfo
                        );
                    }
                }

                // 5. 保底默认值，避免空引用
                if (config.GetTestFilePath == null)
                {
                    config.GetTestFilePath = _ => "";
                }

                if (config.GetFtpBasePath == null)
                {
                    config.GetFtpBasePath = () => "";
                }

                if (config.RequiredComPorts == null)
                {
                    config.RequiredComPorts = new List<string>();
                }
            }
        }
        #endregion 测试项目配置END
        #region 窗口关闭
        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            // 取消所有正在进行的测试
            foreach (var channel in ProjectSettings.Channels)
            {
                channel.CancelToken?.Cancel();
                channel.CancelToken?.Dispose();
            }

            if (appSettings.RememberWindowPos)
            {
                Rect bounds = WindowState == WindowState.Normal
                    ? new Rect(Left, Top, Width, Height)
                    : RestoreBounds;

                appSettings.WindowLeft = bounds.Left;
                appSettings.WindowTop = bounds.Top;
                appSettings.WindowWidth = bounds.Width;
                appSettings.WindowHeight = bounds.Height;
                SaveSettings();
            }
        }


        #endregion


    }

    public sealed class RuntimeGroupNode : INotifyPropertyChanged
    {
        private string _status;
        private string _progressText;
        private Brush _statusBrush = Brushes.Gray;
        private bool _isSelected;

        public string GroupId { get; set; }
        public string DisplayName { get; set; }
        public string FirstStepId { get; set; }
        public int CompletedCount { get; private set; }
        public int TotalCount { get; private set; }

        public string Status
        {
            get => _status;
            private set
            {
                if (_status == value)
                    return;
                _status = value;
                OnPropertyChanged();
            }
        }

        public string ProgressText
        {
            get => _progressText;
            set
            {
                if (_progressText == value)
                    return;
                _progressText = value;
                OnPropertyChanged();
            }
        }

        public Brush StatusBrush
        {
            get => _statusBrush;
            private set
            {
                if (Equals(_statusBrush, value))
                    return;
                _statusBrush = value;
                OnPropertyChanged();
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public void Update(string status, string progressText, int completed, int total)
        {
            Status = status;
            ProgressText = progressText;
            CompletedCount = completed;
            TotalCount = total;
            StatusBrush = GetStatusBrush(status);
            OnPropertyChanged(nameof(CompletedCount));
            OnPropertyChanged(nameof(TotalCount));
        }

        private static Brush GetStatusBrush(string status)
        {
            switch (status)
            {
                case "PASS": return new SolidColorBrush(Color.FromRgb(24, 115, 60));
                case "FAIL":
                case "收尾失败": return new SolidColorBrush(Color.FromRgb(180, 35, 24));
                case "执行中": return new SolidColorBrush(Color.FromRgb(20, 92, 158));
                case "收尾中": return new SolidColorBrush(Color.FromRgb(91, 67, 153));
                case "已取消": return Brushes.DimGray;
                case "已跳过": return Brushes.DarkGray;
                default: return Brushes.Gray;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

}
