using CommunityToolkit.Mvvm.ComponentModel;

namespace FlashHSI.Core.Models;

/// <summary>
/// 피더 모델 — 개별 피더의 번호와 값을 관리
/// </summary>
/// <ai>AI가 작성함</ai>
public partial class Feeder : ObservableObject
{
    // 피더 번호 (0-based)
    [ObservableProperty] private int _feederNumber;
    // 피더 출력 값 (1~99)
    [ObservableProperty] private int _feederValue;

    public Feeder()
    {
    }

    public Feeder(int feederNumber, int feederValue)
    {
        _feederNumber = feederNumber;
        _feederValue = feederValue;
    }

    // 화면 전시용 번호 (0기반 -> 1기반)
    public int DisplayNumber => FeederNumber + 1;

    public override string ToString()
    {
        return $"Feeder Number : {FeederNumber}, Feeder Value : {FeederValue}";
    }
}
