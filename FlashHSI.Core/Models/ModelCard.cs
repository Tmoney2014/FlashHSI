using CommunityToolkit.Mvvm.ComponentModel;

namespace FlashHSI.Core.Models
{
    /// <summary>
    /// <ai>AI가 작성함</ai>
    /// 디렉터리 내 모델 JSON 파일을 카드 형태로 표현하는 모델.
    /// HomeView에서 수평 스크롤 카드 리스트로 표시돼요.
    /// </summary>
    public partial class ModelCard : ObservableObject
    {
        /// <summary>모델 파일명 (확장자 제외)</summary>
        public string Name { get; }

        /// <summary>모델 JSON 파일 전체 경로</summary>
        public string FilePath { get; }

        /// <summary>현재 선택(로드)된 모델인지 여부</summary>
        [ObservableProperty] private bool _isSelected;

        public ModelCard(string name, string filePath, bool isSelected = false)
        {
            Name = name;
            FilePath = filePath;
            _isSelected = isSelected;
        }
    }
}
