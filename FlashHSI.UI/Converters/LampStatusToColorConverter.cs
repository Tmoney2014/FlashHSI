using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Serilog;

namespace FlashHSI.UI.Converters;

/// <summary>
///     램프 색상 상태값을 색상으로 변환하는 컨버터
///     0=회색(정지), 1=노랑(가열중), 2=주황(경고), 3=빨강(에러), 4=초록(준비완료)
/// </summary>
/// <ai>AI가 작성함</ai>
public class LampStatusToColorConverter : IValueConverter
{
    /// <summary>정지 상태 (0) - 회색</summary>
    public Brush StoppedColor { get; set; } = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)); // Material Grey 500

    /// <summary>가열 중 상태 (1) - 노란색</summary>
    public Brush HeatingColor { get; set; } = new SolidColorBrush(Color.FromRgb(0xFD, 0xD8, 0x35)); // Material Yellow 600

    /// <summary>경고 상태 (2) - 주황색</summary>
    public Brush WarningColor { get; set; } = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)); // Material Orange 500

    /// <summary>에러 상태 (3) - 빨간색</summary>
    public Brush ErrorColor { get; set; } = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)); // Material Red 500

    /// <summary>준비 완료 상태 (4) - 초록색</summary>
    public Brush ReadyColor { get; set; } = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // Material Green 500

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
                    0 => StoppedColor,  // 정지 - 회색
                    1 => HeatingColor,  // 가열 중 - 노란색
                    2 => WarningColor,  // 경고 - 주황색
                    3 => ErrorColor,    // 에러 - 빨간색
                    4 => ReadyColor,    // 준비 완료 - 초록색
                    _ => StoppedColor   // 기본값
                };

                // AI가 추가함: opacity가 1이 아니면 투명도 적용된 새 Brush 생성
                if (opacity < 1.0 && brush is SolidColorBrush solidBrush)
                {
                    var color = solidBrush.Color;
                    return new SolidColorBrush(Color.FromArgb((byte)(color.A * opacity), color.R, color.G, color.B));
                }
                return brush;
            }

            Log.Warning("램프 색상 컨버터: 지원하지 않는 값 타입 {ValueType}, 값: {Value}",
                value?.GetType(), value);
            return StoppedColor;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "LampStatusToColorConverter 변환 중 오류 발생. 값: {Value}", value);
            return StoppedColor;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
