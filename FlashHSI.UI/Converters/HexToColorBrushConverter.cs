using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FlashHSI.UI.Converters
{
    /// <summary>
    /// AI가 작성함: 16진수 색상 문자열을 SolidColorBrush로 변환
    /// </summary>
    public class HexToColorBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string hex && !string.IsNullOrEmpty(hex))
            {
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(hex);
                    return new SolidColorBrush(color);
                }
                catch
                {
                    return new SolidColorBrush(Colors.Gray);
                }
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
