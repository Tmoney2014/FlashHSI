using CommunityToolkit.Mvvm.ComponentModel;

namespace FlashHSI.Core.Models
{
    /// <summary>
    /// <ai>AI가 작성함</ai>
    /// 분류 클래스의 에어건 타겟 여부를 관리하는 모델.
    /// 레거시 HSIClient의 SortClass와 동등한 역할.
    /// </summary>
    public partial class SortClass : ObservableObject
    {
        /// <summary>모델 내 클래스 인덱스 (0-based)</summary>
        public int Index { get; }

        /// <summary>클래스 이름 (ModelConfig.Labels에서 추출)</summary>
        public string Name { get; }

        /// <summary>클래스 색상 (ModelConfig.Colors에서 추출, Hex 형식)</summary>
        public string ColorHex { get; }

        /// <summary>에어건으로 쳐낼 대상인지 여부</summary>
        [ObservableProperty] private bool _isSelected;

        public SortClass(int index, string name, string colorHex, bool isSelected = false)
        {
            Index = index;
            Name = name;
            ColorHex = colorHex;
            _isSelected = isSelected;
        }
    }
}
