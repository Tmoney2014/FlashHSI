using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace FlashHSI.UI.ViewModels
{
    public partial class ClassInfo : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _colorHex = "#888888";

        [ObservableProperty]
        private long _count;

        public int Index { get; set; }

        public SolidColorBrush ColorBrush 
        {
            get
            {
                try
                {
                    return (SolidColorBrush)(new BrushConverter().ConvertFrom(ColorHex) ?? Brushes.Gray);
                }
                catch
                {
                    return Brushes.Gray;
                }
            }
        }

        partial void OnColorHexChanged(string value)
        {
            OnPropertyChanged(nameof(ColorBrush));
        }
    }
}
