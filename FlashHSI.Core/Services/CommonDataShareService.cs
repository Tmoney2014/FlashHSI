using System.Collections.ObjectModel;
using FlashHSI.Core;

namespace FlashHSI.Core.Services
{
    /// <summary>
    /// <ai>AI가 작성함</ai>
    /// ViewModels 간에 공유되는 데이터를 관리하는 서비스입니다.
    /// </summary>
    public class CommonDataShareService
    {
        // 예: 현재 로드된 모델의 클래스 정보 등
        public ObservableCollection<ClassInfo> CurrentClassStats { get; } = new ObservableCollection<ClassInfo>();
        
        public void ClearStats()
        {
            foreach(var c in CurrentClassStats) c.Count = 0;
        }
    }
}
