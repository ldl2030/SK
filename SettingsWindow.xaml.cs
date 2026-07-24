using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Serialization;
using Microsoft.Win32;
using System.Linq;

namespace TestPlatform
{
    public partial class SettingsWindow : Window
    {
        public AppSettings CurrentSettings { get; private set; }

        public SettingsWindow(AppSettings currentSettings)
        {
            InitializeComponent();
            CurrentSettings = currentSettings ?? new AppSettings();
            LoadSettingsToUI();
        }

        private void LoadSettingsToUI()
        {
            // MES/FTP
            chkMESEnable.IsChecked = CurrentSettings.MESEnabled;
            txtWorkOrder.Text = CurrentSettings.WorkOrder;
            txtWorkStation.Text = CurrentSettings.WorkStation;
            chkFTPEnable.IsChecked = CurrentSettings.FTPEnabled;
            txtFTPServer.Text = CurrentSettings.FTPServer;
            txtFTPPort.Text = CurrentSettings.FTPPort.ToString();
            txtFTPUser.Text = CurrentSettings.FTPUser;
            txtFTPPassword.Password = CurrentSettings.FTPPassword;

            // 测试行为
            chkAutoClearSN.IsChecked = CurrentSettings.AutoClearSN;
            chkAutoSaveResult.IsChecked = CurrentSettings.AutoSaveResult;
            chkPlayBeep.IsChecked = CurrentSettings.PlayBeep;
            //并行测试计数
            txtParallelCount.Text = CurrentSettings.ParallelTestCount.ToString();

            // SN规则
            chkEnforcePrefix.IsChecked = CurrentSettings.EnforcePrefix;
            txtSNPrefix.Text = CurrentSettings.SNPrefix;
            chkEnforceLength.IsChecked = CurrentSettings.EnforceLength;
            txtSNLength.Text = CurrentSettings.SNLength.ToString();
            chkAutoUpper.IsChecked = CurrentSettings.AutoUpper;
            chkStopOnFail.IsChecked = CurrentSettings.StopOnFail;
            chkPromptOnFail.IsChecked = CurrentSettings.PromptOnFail;
            chkAllowEmptySN.IsChecked = CurrentSettings.AllowEmptySN;
            // 外观
            rbLightTheme.IsChecked = CurrentSettings.LightTheme;
            rbDarkTheme.IsChecked = !CurrentSettings.LightTheme;
            // 设置主色调下拉框
            if (!string.IsNullOrEmpty(CurrentSettings.AccentColor))
            {
                foreach (ComboBoxItem item in cmbAccentColor.Items)
                {
                    if (item.Tag.ToString() == CurrentSettings.AccentColor)
                    {
                        cmbAccentColor.SelectedItem = item;
                        break;
                    }
                }
            }
            // 图标样式
            if (!string.IsNullOrEmpty(CurrentSettings.IconStyle))
            {
                foreach (ComboBoxItem item in cmbIconStyle.Items)
                {
                    if (item.Tag.ToString() == CurrentSettings.IconStyle)
                    {
                        cmbIconStyle.SelectedItem = item;
                        break;
                    }
                }
            }
            //数据保存模式

            cmbReportSaveMode.SelectedIndex = CurrentSettings.ReportSaveMode == ReportSaveMode.SingleExcel ? 1 : 0;
            // 窗口行为
            chkRememberWindowPos.IsChecked = CurrentSettings.RememberWindowPos;
            chkAutoStart.IsChecked = CurrentSettings.AutoStart;

            // 操作员模式
            chkOperatorMode.IsChecked = CurrentSettings.OperatorMode;
            txtOperatorPassword.Password = CurrentSettings.OperatorPassword;

            // 高级设置
            txtMESIP.Text = CurrentSettings.MESIP;
            txtMESPort.Text = CurrentSettings.MESPort.ToString();
            txtMESPath.Text = CurrentSettings.MESPath;

            // 在线打印
            chkOnlinePrint.IsChecked = CurrentSettings.OnlinePrint;
            //SMB上传
            chkSMBEnable.IsChecked = CurrentSettings.SMBEnabled;
            txtSMBServerPath.Text = CurrentSettings.SMBServerPath;
            txtSMBUsername.Text = CurrentSettings.SMBUsername;
            txtSMBPassword.Password = CurrentSettings.SMBPassword;
            //失败后重测次数
            txtFailRetryCount.Text = CurrentSettings.FailRetryCount.ToString();
            // 操作员模式联动：若启用，则强制 MES 和 FTP 开启且不可修改
            chkOperatorMode.Checked += (s, e) => ApplyOperatorModeRestrictions();
            chkOperatorMode.Unchecked += (s, e) => ReleaseOperatorModeRestrictions();
            ApplyOperatorModeRestrictions(); // 初始调用
        }
        private void ApplyOperatorModeRestrictions()
        {
            bool isOperatorMode = chkOperatorMode.IsChecked == true;
            if (isOperatorMode)
            {
                // 强制 MES 和 FTP 开启
                chkMESEnable.IsChecked = true;
                chkFTPEnable.IsChecked = true;
                // 禁用这两个复选框，防止用户修改
                chkMESEnable.IsEnabled = false;
                chkFTPEnable.IsEnabled = false;
            }
            else
            {
                chkMESEnable.IsEnabled = true;
                chkFTPEnable.IsEnabled = true;
            }
        }

        private void ReleaseOperatorModeRestrictions()
        {
            // 当操作员模式取消时，恢复两个复选框可用
            chkMESEnable.IsEnabled = true;
            chkFTPEnable.IsEnabled = true;
        }
        private void SaveUIToSettings()
        {
            // MES/FTP
            CurrentSettings.MESEnabled = chkMESEnable.IsChecked ?? false;
            CurrentSettings.WorkOrder = txtWorkOrder.Text;
            CurrentSettings.WorkStation = txtWorkStation.Text;
            CurrentSettings.FTPEnabled = chkFTPEnable.IsChecked ?? false;
            CurrentSettings.FTPServer = txtFTPServer.Text;
            int.TryParse(txtFTPPort.Text, out int ftpPort);
            CurrentSettings.FTPPort = ftpPort;
            CurrentSettings.FTPUser = txtFTPUser.Text;
            CurrentSettings.FTPPassword = txtFTPPassword.Password;

            // 测试行为
            CurrentSettings.AutoClearSN = chkAutoClearSN.IsChecked ?? false;
            CurrentSettings.AutoSaveResult = chkAutoSaveResult.IsChecked ?? false;
            CurrentSettings.PlayBeep = chkPlayBeep.IsChecked ?? false;
            if (int.TryParse(txtParallelCount.Text, out int parallelCount) && parallelCount >= 1 && parallelCount <= 8)
                CurrentSettings.ParallelTestCount = parallelCount;
            else
                CurrentSettings.ParallelTestCount = 3;

            // SN规则
            CurrentSettings.EnforcePrefix = chkEnforcePrefix.IsChecked ?? false;
            CurrentSettings.SNPrefix = txtSNPrefix.Text;
            CurrentSettings.EnforceLength = chkEnforceLength.IsChecked ?? false;
            int.TryParse(txtSNLength.Text, out int snLen);
            CurrentSettings.SNLength = snLen;
            CurrentSettings.AutoUpper = chkAutoUpper.IsChecked ?? false;
            CurrentSettings.StopOnFail = chkStopOnFail.IsChecked ?? true;
            CurrentSettings.PromptOnFail = chkPromptOnFail.IsChecked ?? true;
            CurrentSettings.AllowEmptySN = chkAllowEmptySN.IsChecked ?? false;

            // 外观
            CurrentSettings.LightTheme = rbLightTheme.IsChecked ?? true;
            if (cmbAccentColor.SelectedItem is ComboBoxItem accentItem)
                CurrentSettings.AccentColor = accentItem.Tag.ToString();
            if (cmbIconStyle.SelectedItem is ComboBoxItem iconItem)
                CurrentSettings.IconStyle = iconItem.Tag.ToString();
            if (cmbAccentColor.SelectedItem is ComboBoxItem selectedItem)
                CurrentSettings.AccentColor = selectedItem.Tag.ToString();
            else
                CurrentSettings.AccentColor = "";

            // 窗口行为
            CurrentSettings.RememberWindowPos = chkRememberWindowPos.IsChecked ?? false;
            CurrentSettings.AutoStart = chkAutoStart.IsChecked ?? false;

            // 操作员模式
            CurrentSettings.OperatorMode = chkOperatorMode.IsChecked ?? false;
            CurrentSettings.OperatorPassword = txtOperatorPassword.Password;

            // 高级设置
            CurrentSettings.MESIP = txtMESIP.Text;
            int port = 0;
            if (!string.IsNullOrWhiteSpace(txtMESPort.Text))
                int.TryParse(txtMESPort.Text, out port);
            CurrentSettings.MESPort = port;
            
            CurrentSettings.MESPath = txtMESPath.Text;
            //保存报告的模式

            CurrentSettings.ReportSaveMode = cmbReportSaveMode.SelectedIndex == 1? ReportSaveMode.SingleExcel: ReportSaveMode.AppendCsv;
            // 在线打印
            CurrentSettings.OnlinePrint = chkOnlinePrint.IsChecked ?? false;
            //SMB上传
            CurrentSettings.SMBEnabled = chkSMBEnable.IsChecked ?? false;
            CurrentSettings.SMBServerPath = txtSMBServerPath.Text;
            CurrentSettings.SMBUsername = txtSMBUsername.Text;
            CurrentSettings.SMBPassword = txtSMBPassword.Password;
            //失败后重测次数
            int.TryParse(txtFailRetryCount.Text, out int retryCount);
            CurrentSettings.FailRetryCount = retryCount >= 0 ? retryCount : 0;
            // 操作员模式：强制 MES 和 FTP 开启
            if (chkOperatorMode.IsChecked == true)
            {
                CurrentSettings.MESEnabled = true;
                CurrentSettings.FTPEnabled = true;
            }
            else
            {
                CurrentSettings.MESEnabled = chkMESEnable.IsChecked ?? false;
                CurrentSettings.FTPEnabled = chkFTPEnable.IsChecked ?? false;
            }
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            SaveUIToSettings();
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            SaveUIToSettings(); // 先保存当前UI值
            SaveFileDialog dlg = new SaveFileDialog
            {
                Filter = "XML文件 (*.xml)|*.xml",
                DefaultExt = "xml",
                FileName = "TestPlatform_Settings.xml"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(AppSettings));
                    using (StreamWriter writer = new StreamWriter(dlg.FileName))
                    {
                        serializer.Serialize(writer, CurrentSettings);
                    }
                    MessageBox.Show("配置导出成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        private void BtnResetColor_Click(object sender, RoutedEventArgs e)
        {
            cmbAccentColor.SelectedItem = null;
        }
        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog
            {
                Filter = "XML文件 (*.xml)|*.xml",
                DefaultExt = "xml"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(AppSettings));
                    using (StreamReader reader = new StreamReader(dlg.FileName))
                    {
                        CurrentSettings = (AppSettings)serializer.Deserialize(reader);
                    }
                    LoadSettingsToUI(); // 刷新界面
                    MessageBox.Show("配置导入成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导入失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    [Serializable]
    public class AppSettings
    {
        // MES/FTP
        public bool MESEnabled { get; set; }
        public string WorkOrder { get; set; } = "";
        public string WorkStation { get; set; } = "";
        public bool FTPEnabled { get; set; }
        public string FTPServer { get; set; } = "";
        public int FTPPort { get; set; } = 21;
        public string FTPUser { get; set; } = "";
        public string FTPPassword { get; set; } = "";

        // 测试行为
        public bool AutoClearSN { get; set; }
        public bool AutoSaveResult { get; set; }
        public bool PlayBeep { get; set; }
        public int ParallelTestCount { get; set; } = 3;
        

        // SN规则
        public bool EnforcePrefix { get; set; }
        public string SNPrefix { get; set; } = "";
        public bool EnforceLength { get; set; }
        public int SNLength { get; set; }
        public bool AutoUpper { get; set; }
        //允许条码为空（跳过非空校验）
        public bool AllowEmptySN { get; set; } = false;

        // 外观
        public bool LightTheme { get; set; } = true;
        public string AccentColor { get; set; } = "#4CAF50";
        public string IconStyle { get; set; } = "FontIcon";

        // 窗口行为
        public bool RememberWindowPos { get; set; }
        public bool AutoStart { get; set; }

        // 操作员模式
        public bool OperatorMode { get; set; }
        public string OperatorPassword { get; set; } = "123456";

        // 高级设置
        public string MESIP { get; set; } = "";
        public int MESPort { get; set; } = 8080;
        public string MESPath { get; set; } = "/api/";

        // 在线打印
        public bool OnlinePrint { get; set; }

        // 窗口位置记忆
        public double WindowLeft { get; set; } = double.NaN;
        public double WindowTop { get; set; } = double.NaN;
        public double WindowWidth { get; set; } = double.NaN;
        public double WindowHeight { get; set; } = double.NaN;
        public string CurrentProjectName { get; set; } = "";
        /// <summary>
        /// 测试失败时是否立即停止（true:停止整个测试，false:继续）
        /// </summary>
        public bool StopOnFail { get; set; } = true;

        /// <summary>
        /// 测试失败时是否弹出重试提示（仅在 StopOnFail = false 时生效）
        /// </summary>
        public bool PromptOnFail { get; set; } = true;

        // SMB 相关设置
        public bool SMBEnabled { get; set; } = false;
        public string SMBServerPath { get; set; } = "\\\\192.168.30.7\\Testresult";
        public string SMBUsername { get; set; } = "";
        public string SMBPassword { get; set; } = "";
        /// <summary>
        /// 测试步骤失败时的自动重试次数（0表示不重试）
        /// </summary>
        public int FailRetryCount { get; set; } = 0;
        /// <summary>
        /// 报告保存模式
        /// </summary>
        public ReportSaveMode ReportSaveMode { get; set; } = ReportSaveMode.AppendCsv;

    }
}