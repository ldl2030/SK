using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TestPlatform
{
    public static class ZplOffsetHelper
    {
        private static Dictionary<string, (int X, int Y)> _offsets;
        private static string SerialFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PrintConfig", "SerialNumber.txt");
        private static readonly object _lock = new object();

        static ZplOffsetHelper()
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PrintConfig", "ZplOffsetConfig.json");
            _offsets = new Dictionary<string, (int, int)>();
            if (!File.Exists(configPath)) return;

            try
            {
                string json = File.ReadAllText(configPath);
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    // 支持 {"Projects":{"项目名":{"X":0,"Y":0}}}
                    if (doc.RootElement.TryGetProperty("Projects", out JsonElement projects))
                    {
                        foreach (var prop in projects.EnumerateObject())
                        {
                            string name = prop.Name;
                            if (prop.Value.TryGetProperty("X", out JsonElement xElem) &&
                                prop.Value.TryGetProperty("Y", out JsonElement yElem))
                            {
                                _offsets[name] = (xElem.GetInt32(), yElem.GetInt32());
                            }
                        }
                    }
                    else
                    {
                        // 兼容直接 {"项目名":{"X":0,"Y":0}}
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            string name = prop.Name;
                            if (prop.Value.TryGetProperty("X", out JsonElement xElem) &&
                                prop.Value.TryGetProperty("Y", out JsonElement yElem))
                            {
                                _offsets[name] = (xElem.GetInt32(), yElem.GetInt32());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"解析偏移配置失败: {ex.Message}");
            }
        }

        public static (int XOffset, int YOffset) GetOffset(string projectName)
        {
            if (_offsets.TryGetValue(projectName, out var offset))
                return offset;
            if (_offsets.TryGetValue("Default", out var defaultOffset))
                return defaultOffset;
            return (0, 0);
        }

        public static int GetNextSerial(string projectName)
        {
            lock (_lock)
            {
                int lastUsed = 0;
                if (File.Exists(SerialFile))
                {
                    var lines = File.ReadAllLines(SerialFile);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('=');
                        if (parts.Length == 2 && parts[0] == projectName && int.TryParse(parts[1], out int saved))
                        {
                            lastUsed = saved;
                            break;
                        }
                    }
                }
                int next = lastUsed + 1;
                SaveSerial(projectName, next);
                return next;
            }
        }

        private static void SaveSerial(string projectName, int serial)
        {
            var dict = new Dictionary<string, int>();
            if (File.Exists(SerialFile))
            {
                foreach (var line in File.ReadAllLines(SerialFile))
                {
                    var parts = line.Split('=');
                    if (parts.Length == 2 && int.TryParse(parts[1], out int val))
                        dict[parts[0]] = val;
                }
            }
            dict[projectName] = serial;
            File.WriteAllLines(SerialFile, dict.Select(kv => $"{kv.Key}={kv.Value}"));
        }

        public static string ApplyOffset(string zpl, int xOffset, int yOffset)
        {
            if (xOffset == 0 && yOffset == 0) return zpl;
            return Regex.Replace(zpl, @"\^FT(\d+),(\d+)", match =>
            {
                int x = int.Parse(match.Groups[1].Value) + xOffset;
                int y = int.Parse(match.Groups[2].Value) + yOffset;
                if (x < 0) x = 0;
                if (y < 0) y = 0;
                return $"^FT{x},{y}";
            });
        }
    }
}