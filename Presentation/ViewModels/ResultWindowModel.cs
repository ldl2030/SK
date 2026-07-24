using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace TestPlatform
{
    public class ResultWindowModel : INotifyPropertyChanged
    {
        private string _displayText;
        private Brush _displayForeground;
        private Brush _background;

        public string DisplayText
        {
            get => _displayText;
            set
            {
                if (_displayText != value)
                {
                    _displayText = value;
                    OnPropertyChanged();
                }
            }
        }

        public Brush Background
        {
            get => _background;
            set
            {
                if (_background != value)
                {
                    _background = value;
                    OnPropertyChanged();
                }
            }
        }

        public Brush DisplayForeground
        {
            get => _displayForeground;
            set
            {
                _displayForeground = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
