using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using FlashHSI.Core.Settings;

namespace FlashHSI.UI.Converters
{
    /// <summary>
    /// Converts MaskRuleLogicalOperator to a color brush.
    /// AND = Green, OR = Orange.
    /// </summary>
    public class OperatorToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is MaskRuleLogicalOperator op)
            {
                return op == MaskRuleLogicalOperator.AND 
                    ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))  // Green
                    : new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)); // Orange
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
