using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Xml.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace TestPlatform
{
    /// <summary>
    /// ProjectEditWindow.xaml 的交互逻辑
    /// </summary>
    public partial class ProjectEditWindow : Window
    {
        public string GroupName { get; private set; }
        public string ModelName { get; private set; }
        public string DisplayName { get; private set; }
        public string FilePath { get; private set; }
        public string UpdateUrl { get; private set; }

        public string Runner { get; private set; }
        public string SequenceKey { get; private set; }
        public string ComInitKey { get; private set; }
        public string FtpBasePath { get; private set; }
        public List<string> RequiredComPorts { get; private set; } = new List<string>();




        public ProjectEditWindow(
     string displayName = "",
     string filePath = "",
     string updateUrl = "",
     string groupName = "",
     string modelName = "",
     string runner = "Legacy",
     string sequenceKey = "",
     string comInitKey = "",
     string ftpBasePath = "",
     List<string> requiredComPorts = null)
        {
            InitializeComponent();

            txtDisplayName.Text = displayName;
            txtFilePath.Text = filePath;
            txtUpdateUrl.Text = updateUrl;

            txtGroupName.Text = string.IsNullOrWhiteSpace(groupName)
                ? GetGroupNameFromDisplayName(displayName)
                : groupName;

            txtModelName.Text = string.IsNullOrWhiteSpace(modelName)
                ? GetModelNameFromDisplayName(displayName)
                : modelName;

            cmbRunner.Text = string.IsNullOrWhiteSpace(runner) ? "Legacy" : runner;
            txtSequenceKey.Text = sequenceKey ?? "";
            txtComInitKey.Text = comInitKey ?? "";
            txtFtpBasePath.Text = ftpBasePath ?? "";

            txtRequiredComPorts.Text = requiredComPorts == null
                ? ""
                : string.Join(Environment.NewLine, requiredComPorts);
        }
        private string GetGroupNameFromDisplayName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                return "默认";

            string name = displayName.Trim();

            if (name.Length <= 2)
                return name;

            return name.Substring(0, 2).Trim();
        }

        private string GetModelNameFromDisplayName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                return "";

            string name = displayName.Trim();

            if (name.Length <= 2)
                return name;

            return name.Substring(2).Trim();
        }
        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Filter = "XML文件 (*.xml)|*.xml";
            dlg.DefaultExt = "xml";
            dlg.AddExtension = true;
            dlg.OverwritePrompt = false;
            dlg.CheckPathExists = true;
            dlg.FileName = BuildDefaultXmlFileName();

            string groupName = string.IsNullOrWhiteSpace(txtGroupName.Text)
                ? GetGroupNameFromDisplayName(txtDisplayName.Text.Trim())
                : txtGroupName.Text.Trim();

            string modelName = string.IsNullOrWhiteSpace(txtModelName.Text)
                ? GetModelNameFromDisplayName(txtDisplayName.Text.Trim())
                : txtModelName.Text.Trim();

            dlg.InitialDirectory = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "ProjectConfig",
                GetSafePathPart(groupName),
                GetSafePathPart(modelName));

            Directory.CreateDirectory(dlg.InitialDirectory);

            if (dlg.ShowDialog() == true)
            {
                txtFilePath.Text = dlg.FileName;
            }
        }
        private void TxtDisplayName_LostFocus(object sender, RoutedEventArgs e)
        {
            string displayName = txtDisplayName.Text.Trim();

            if (string.IsNullOrWhiteSpace(txtGroupName.Text))
            {
                txtGroupName.Text = GetGroupNameFromDisplayName(displayName);
            }

            if (string.IsNullOrWhiteSpace(txtModelName.Text))
            {
                txtModelName.Text = GetModelNameFromDisplayName(displayName);
            }
        }
        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtDisplayName.Text))
            {
                MessageBox.Show("请输入项目完整名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DisplayName = txtDisplayName.Text.Trim();

            GroupName = string.IsNullOrWhiteSpace(txtGroupName.Text)
                ? GetGroupNameFromDisplayName(DisplayName)
                : txtGroupName.Text.Trim();

            ModelName = string.IsNullOrWhiteSpace(txtModelName.Text)
                ? GetModelNameFromDisplayName(DisplayName)
                : txtModelName.Text.Trim();

            FilePath = string.IsNullOrWhiteSpace(txtFilePath.Text)
                ? BuildDefaultXmlFilePath(DisplayName, GroupName, ModelName)
                : txtFilePath.Text.Trim();

            EnsureTestXmlFileExists(FilePath);

            UpdateUrl = txtUpdateUrl.Text.Trim();

            Runner = string.IsNullOrWhiteSpace(cmbRunner.Text)
                ? "Legacy"
                : cmbRunner.Text.Trim();

            SequenceKey = txtSequenceKey.Text.Trim();
            ComInitKey = txtComInitKey.Text.Trim();
            FtpBasePath = txtFtpBasePath.Text.Trim();

            RequiredComPorts = txtRequiredComPorts.Text
                .Split(new[] { "\r\n", "\n", "\r", ";", "," }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            DialogResult = true;
            Close();
        }


        private string BuildDefaultXmlFileName()
        {
            string modelName = string.IsNullOrWhiteSpace(txtModelName.Text)
                ? GetModelNameFromDisplayName(txtDisplayName.Text.Trim())
                : txtModelName.Text.Trim();

            if (string.IsNullOrWhiteSpace(modelName))
                modelName = txtDisplayName.Text.Trim();

            if (string.IsNullOrWhiteSpace(modelName))
                modelName = "NewProject";

            return GetSafePathPart(modelName) + "_autoTest.xml";
        }

        private string BuildDefaultXmlFilePath(string displayName, string groupName, string modelName)
        {
            string safeGroup = GetSafePathPart(string.IsNullOrWhiteSpace(groupName)
                ? GetGroupNameFromDisplayName(displayName)
                : groupName);

            string safeModel = GetSafePathPart(string.IsNullOrWhiteSpace(modelName)
                ? GetModelNameFromDisplayName(displayName)
                : modelName);

            if (string.IsNullOrWhiteSpace(safeModel))
                safeModel = GetSafePathPart(displayName);

            string dir = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "ProjectConfig",
                safeGroup,
                safeModel);

            return System.IO.Path.Combine(dir, safeModel + "_autoTest.xml");
        }

        private string GetSafePathPart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "NewProject";

            string result = value.Trim();

            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            {
                result = result.Replace(c, '_');
            }

            foreach (char c in System.IO.Path.GetInvalidPathChars())
            {
                result = result.Replace(c, '_');
            }

            return result.Trim();
        }

        private void EnsureTestXmlFileExists(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new InvalidOperationException("XML 文件路径为空，无法创建测试配置文件。");

            string fullPath = System.IO.Path.IsPathRooted(filePath)
                ? filePath
                : System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filePath);

            string dir = System.IO.Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(fullPath))
                return;

            XDocument doc = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("TestItems",
                    new XElement("TestItem",
                        new XElement("Enabled", true),
                        new XElement("Name", "新测试项"),
                        new XElement("UpperLimit", ""),
                        new XElement("LowerLimit", ""),
                        new XElement("Unit", "")
                    )
                )
            );

            doc.Save(fullPath);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
