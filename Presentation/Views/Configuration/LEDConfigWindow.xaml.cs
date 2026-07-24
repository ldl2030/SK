using System.Windows;

namespace TestPlatform
{
    public partial class LEDConfigWindow : Window
    {
        /// <summary>
        /// 当前编辑的配置数据
        /// </summary>
        public LEDConfigSet Config { get; private set; }

        public LEDConfigWindow(LEDConfigSet config)
        {
            InitializeComponent();
            Config = config;
            this.DataContext = Config;   // 绑定到视图模型
        }

        /// <summary>
        /// 保存配置并关闭窗口
        /// </summary>
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            LEDConfigManager.Save(Config);
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// 取消编辑，不保存修改
        /// </summary>
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}