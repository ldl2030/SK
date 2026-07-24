using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace TestPlatform
{
    public partial class ScanCompareWindow : Window
    {
        private string firstCode;
        private string secondCode;
        private int step = 1;

        public string FirstCode => firstCode;
        public string SecondCode => secondCode;

        public ScanCompareWindow()
        {
            InitializeComponent();
            txtBarcode.Focus();
        }

        private void TxtBarcode_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            string code = txtBarcode.Text.Trim();
            if (string.IsNullOrEmpty(code))
            {
                ShakeControl(txtBarcode);
                return;
            }

            if (step == 1)
            {
                firstCode = code;
                step = 2;
                tbStep.Text = "步骤2：请扫描打印的条码";
                tbStatus.Text = "已扫描主板码，请扫描打印条码...";
                IconText.Text = "🖨️";
                txtBarcode.Clear();
                txtBarcode.Focus();
            }
            else if (step == 2)
            {
                secondCode = code;
                DialogResult = true;
                Close();
            }
        }

        private void ShakeControl(UIElement element)
        {
            var shake = (Storyboard)FindResource("Shake");
            shake.Begin((FrameworkElement)element);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}