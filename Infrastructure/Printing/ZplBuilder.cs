using System.Text.RegularExpressions;

namespace TestPlatform
{
    public class ZplBuilder
    {
        private readonly string _originalZpl;
        private readonly int _xOffset;
        private readonly int _yOffset;

        public ZplBuilder(string zpl, int xOffset, int yOffset)
        {
            _originalZpl = zpl;
            _xOffset = xOffset;
            _yOffset = yOffset;
        }

        public string Build()
        {
            if (_xOffset == 0 && _yOffset == 0)
                return _originalZpl;

            return Regex.Replace(_originalZpl, @"\^FT(\d+),(\d+)", match =>
            {
                int x = int.Parse(match.Groups[1].Value) + _xOffset;
                int y = int.Parse(match.Groups[2].Value) + _yOffset;
                if (x < 0) x = 0;
                if (y < 0) y = 0;
                return $"^FT{x},{y}";
            });
        }
    }
}