using System.Globalization;
using System.Windows;
using System.Windows.Data;
using FlashHSI.Core.Engine;

namespace FlashHSI.UI.Converters
{
    /// <summary>
    /// MaskMode enum을 받아서 Visibility로 변환
    /// ConverterParameter: "Mean", "BandPixel", "MaskRule", "MeanOrBandPixel"
    /// </summary>
    public class MaskModeToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is MaskMode currentMode && parameter is string targetMode)
            {
                bool isVisible = targetMode switch
                {
                    "Mean" => currentMode == MaskMode.Mean,
                    "BandPixel" => currentMode == MaskMode.BandPixel,
                    "MaskRule" => currentMode == MaskMode.MaskRule,
                    "MeanOrBandPixel" => currentMode == MaskMode.Mean || currentMode == MaskMode.BandPixel,
                    "MeanOrMaskRule" => currentMode == MaskMode.Mean || currentMode == MaskMode.MaskRule,
                    _ => false
                };
                return isVisible ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
