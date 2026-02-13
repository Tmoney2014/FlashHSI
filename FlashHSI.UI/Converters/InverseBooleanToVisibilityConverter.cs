using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FlashHSI.UI.Converters
{
    /// <ai>AI가 작성함</ai>
    /// <summary>
    /// bool 값을 반전하여 Visibility로 변환합니다.
    /// true → Collapsed, false → Visible
    /// 듀얼 ItemsControl 패턴에서 비선택 항목 표시에 사용됩니다.
    /// </summary>
    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? Visibility.Collapsed : Visibility.Visible;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility v)
                return v != Visibility.Visible;
            return false;
        }
    }
}
