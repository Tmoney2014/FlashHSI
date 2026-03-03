using System.Globalization;
using System.Windows.Data;
using FlashHSI.Core.Engine;

namespace FlashHSI.UI.Converters
{
    /// <summary>
    /// MaskMode enum을 받아서 지정된 모드와 일치하는지 Boolean으로 반환
    /// ConverterParameter: "Mean", "BandPixel", "MaskRule", "MeanOrBandPixel"
    /// </summary>
    public class MaskModeToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is MaskMode currentMode && parameter is string targetMode)
            {
                return targetMode switch
                {
                    "Mean" => currentMode == MaskMode.Mean,
                    "BandPixel" => currentMode == MaskMode.BandPixel,
                    "MaskRule" => currentMode == MaskMode.MaskRule,
                    "MeanOrBandPixel" => currentMode == MaskMode.Mean || currentMode == MaskMode.BandPixel,
                    _ => false
                };
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
