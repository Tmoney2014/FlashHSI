using System.Globalization;
using System.Windows.Data;

namespace FlashHSI.UI.Converters;

/// <summary>
///     퍼센트(0~100)와 부모 너비를 받아 실제 픽셀 너비를 계산하는 MultiValueConverter
///     바인딩: [0]=퍼센트(double), [1]=부모 ActualWidth(double)
/// </summary>
/// <ai>AI가 작성함</ai>
public class PercentageWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 
            && values[0] is double percent 
            && values[1] is double totalWidth)
        {
            var clampedPercent = Math.Clamp(percent, 0.0, 100.0);
            return totalWidth * (clampedPercent / 100.0);
        }
        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
