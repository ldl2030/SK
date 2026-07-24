using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Xml.Serialization;

namespace TestPlatform
{
    /// <summary>
    /// 单个通道的LED参数配置（上下限）
    /// </summary>
    public class LEDChannelConfig : INotifyPropertyChanged
    {
        private int _channelIndex;
        private double _freqLower, _freqUpper;
        private int _countLower, _countUpper;
        private int _redLower, _redUpper;
        private int _greenLower, _greenUpper;
        private int _blueLower, _blueUpper;
        private double _hueLower, _hueUpper;
        private int _brightnessLower, _brightnessUpper;

        public int ChannelIndex
        {
            get => _channelIndex;
            set { _channelIndex = value; OnPropertyChanged(); }
        }

        // 频率 (Hz)
        public double FreqLower { get => _freqLower; set { _freqLower = value; OnPropertyChanged(); } }
        public double FreqUpper { get => _freqUpper; set { _freqUpper = value; OnPropertyChanged(); } }

        // 闪烁计数值 (无单位)
        public int CountLower { get => _countLower; set { _countLower = value; OnPropertyChanged(); } }
        public int CountUpper { get => _countUpper; set { _countUpper = value; OnPropertyChanged(); } }

        // 红分量 (0-255)
        public int RedLower { get => _redLower; set { _redLower = value; OnPropertyChanged(); } }
        public int RedUpper { get => _redUpper; set { _redUpper = value; OnPropertyChanged(); } }

        // 绿分量 (0-255)
        public int GreenLower { get => _greenLower; set { _greenLower = value; OnPropertyChanged(); } }
        public int GreenUpper { get => _greenUpper; set { _greenUpper = value; OnPropertyChanged(); } }

        // 蓝分量 (0-255)
        public int BlueLower { get => _blueLower; set { _blueLower = value; OnPropertyChanged(); } }
        public int BlueUpper { get => _blueUpper; set { _blueUpper = value; OnPropertyChanged(); } }

        // 色调 (Hue, 0-360)
        public double HueLower { get => _hueLower; set { _hueLower = value; OnPropertyChanged(); } }
        public double HueUpper { get => _hueUpper; set { _hueUpper = value; OnPropertyChanged(); } }

        // 亮度相对值 (0-65535)
        public int BrightnessLower { get => _brightnessLower; set { _brightnessLower = value; OnPropertyChanged(); } }
        public int BrightnessUpper { get => _brightnessUpper; set { _brightnessUpper = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// 8通道LED配置集合
    /// </summary>
    public class LEDConfigSet
    {
        public List<LEDChannelConfig> Channels { get; set; } = new List<LEDChannelConfig>();

        public LEDConfigSet()
        {
            for (int i = 0; i < 8; i++)
            {
                Channels.Add(new LEDChannelConfig { ChannelIndex = i + 1 });
            }
        }
    }

    /// <summary>
    /// 配置文件读写
    /// </summary>
    public static class LEDConfigManager
    {
        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LEDConfig", "LEDAnalyzerConfig.xml");

        public static void Save(LEDConfigSet config)
        {
            try
            {
                string dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var serializer = new XmlSerializer(typeof(LEDConfigSet));
                using (var writer = new StreamWriter(ConfigPath))
                    serializer.Serialize(writer, config);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存LED配置失败: {ex.Message}");
            }
        }

        public static LEDConfigSet Load()
        {
            if (!File.Exists(ConfigPath))
                return new LEDConfigSet();

            try
            {
                var serializer = new XmlSerializer(typeof(LEDConfigSet));
                using (var reader = new StreamReader(ConfigPath))
                    return (LEDConfigSet)serializer.Deserialize(reader);
            }
            catch
            {
                return new LEDConfigSet();
            }
        }
    }

    /// <summary>
    /// 单个通道LED检测结果
    /// </summary>
    public class LEDChannelResult
    {
        public int ChannelIndex { get; set; }
        public bool AllPass { get; set; }

        public double FreqValue { get; set; }
        public int CountValue { get; set; }
        public int RValue { get; set; }
        public int GValue { get; set; }
        public int BValue { get; set; }
        public double HueValue { get; set; }
        public int BrightnessValue { get; set; }

        public bool FreqPass { get; set; }
        public bool CountPass { get; set; }
        public bool RPass { get; set; }
        public bool GPass { get; set; }
        public bool BPass { get; set; }
        public bool HuePass { get; set; }
        public bool BrightnessPass { get; set; }

        public string GetDetailString()
        {
            return $"CH{ChannelIndex}: F={FreqValue:F1}/{FreqPass}, C={CountValue}/{CountPass}, R={RValue}/{RPass}, G={GValue}/{GPass}, B={BValue}/{BPass}, H={HueValue:F2}/{HuePass}, Br={BrightnessValue}/{BrightnessPass}";
        }
    }
}