using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Serilog;

namespace FlashHSI.UI.Converters;

/// <summary>
///     하드웨어/에러 상태값(int)을 색상으로 변환하는 범용 컨버터
///     0=회색(정지/데이터없음), 1=초록(정상/동작), 2=주황(경고), 3=빨강(에러)
///     ConverterParameter로 투명도 지정 가능 (예: "0.3")
/// </summary>
/// <ai>AI가 작성함</ai>
public class StatusToColorConverter : IValueConverter
{
    /// <summary>정지/데이터없음 (0) - 회색</summary>
    public Brush OffColor { get; set; } = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)); // Material Grey 500

    /// <summary>정상/동작 (1) - 초록색</summary>
    public Brush OnColor { get; set; } = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // Material Green 500

    /// <summary>경고 (2) - 주황색</summary>
    public Brush WarningColor { get; set; } = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)); // Material Orange 500

    /// <summary>에러 (3) - 빨간색</summary>
    public Brush ErrorColor { get; set; } = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)); // Material Red 500

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try
        {
            // AI가 추가함: parameter로 투명도 지정 가능
            double opacity = 1.0;
            if (parameter is string opacityParam && double.TryParse(opacityParam, out var parsedOpacity))
            {
                opacity = parsedOpacity;
            }

            if (value is int intValue)
            {
                var brush = intValue switch
                {
                    0 => OffColor,      // 정지/데이터없음 - 회색
                    1 => OnColor,       // 정상/동작 - 초록색
                    2 => WarningColor,  // 경고 - 주황색
                    3 => ErrorColor,    // 에러 - 빨간색
                    _ => OffColor       // 기본값
                };

                if (opacity < 1.0 && brush is SolidColorBrush solidBrush)
                {
                    var color = solidBrush.Color;
                    return new SolidColorBrush(Color.FromArgb((byte)(color.A * opacity), color.R, color.G, color.B));
                }
                return brush;
            }

            Log.Warning("StatusToColorConverter: 지원하지 않는 값 타입 {ValueType}, 값: {Value}",
                value?.GetType(), value);
            return OffColor;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "StatusToColorConverter 변환 중 오류 발생. 값: {Value}", value);
            return OffColor;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
