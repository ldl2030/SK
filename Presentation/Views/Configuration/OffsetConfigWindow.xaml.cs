using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace TestPlatform
{
    public partial class OffsetConfigWindow : Window
    {
        public ObservableCollection<ProjectOffset> Projects { get; set; }
        private readonly string _configPath;

        public OffsetConfigWindow()
        {
            InitializeComponent();
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PrintConfig", "ZplOffsetConfig.json");
            LoadConfig();
            dgProjects.ItemsSource = Projects;
        }

        private void LoadConfig()
        {
            Projects = new ObservableCollection<ProjectOffset>();
            if (!File.Exists(_configPath))
            {
                Projects.Add(new ProjectOffset { ProjectName = "LS D350打印贴纸", XOffset = 0, YOffset = 0 });
                Projects.Add(new ProjectOffset { ProjectName = "Default", XOffset = 0, YOffset = 0 });
                return;
            }

            try
            {
                string json = File.ReadAllText(_configPath);
                var root = JsonSerializer.Deserialize<RootConfig>(json);
                if (root?.Projects != null)
                {
                    foreach (var kv in root.Projects)
                    {
                        Projects.Add(new ProjectOffset
                        {
                            ProjectName = kv.Key,
                            XOffset = kv.Value.X,
                            YOffset = kv.Value.Y
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"读取配置文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SaveConfig()
        {
            // 显示加载动画
            loadingOverlay.Visibility = Visibility.Visible;

            await Task.Delay(300); // 模拟保存过程

            var dict = new Dictionary<string, OffsetValue>();
            foreach (var item in Projects)
            {
                dict[item.ProjectName] = new OffsetValue { X = item.XOffset, Y = item.YOffset };
            }
            var root = new RootConfig { Projects = dict };

            // 配置 JSON 序列化选项：保留中文，不转义
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            string json = JsonSerializer.Serialize(root, options);

            try
            {
                File.WriteAllText(_configPath, json);
                loadingOverlay.Visibility = Visibility.Collapsed;
                MessageBox.Show("配置已保存", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                loadingOverlay.Visibility = Visibility.Collapsed;
                MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            SaveConfig();
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // 数据模型（必须实现 INotifyPropertyChanged）
        public class ProjectOffset : INotifyPropertyChanged
        {
            private string _projectName;
            private int _xOffset;
            private int _yOffset;

            public string ProjectName
            {
                get => _projectName;
                set { _projectName = value; OnPropertyChanged(); }
            }
            public int XOffset
            {
                get => _xOffset;
                set { _xOffset = value; OnPropertyChanged(); }
            }
            public int YOffset
            {
                get => _yOffset;
                set { _yOffset = value; OnPropertyChanged(); }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged(string name = null) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // JSON 反序列化辅助类
        private class RootConfig
        {
            public Dictionary<string, OffsetValue> Projects { get; set; }
        }
        private class OffsetValue
        {
            public int X { get; set; }
            public int Y { get; set; }
        }
    }
}