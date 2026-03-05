using System.Collections.Generic;
using System.Threading.Tasks;
using FlashHSI.Core.Control.Camera;

namespace FlashHSI.Core.Services
{
    /// <summary>
    /// AI가 작성함: 여러 뷰모델에서 공통으로 사용할 초분광 데이터 캡처 기능(Breeze 호환)
    /// </summary>
    public interface ICaptureService
    {
        /// <summary>
        /// 캡처된 프레임 수가 변경될 때 발생하는 이벤트입니다.
        /// </summary>
        event Action<int>? CapturedFrameCountChanged;

        /// <summary>
        /// 현재까지 버퍼에 캡처된 프레임 수를 반환합니다.
        /// </summary>
        int CurrentCapturedFrameCount { get; }

        /// <summary>
        /// 프레임 버퍼를 초기화하고 캡처를 준비합니다.
        /// </summary>
        void ClearBuffer();

        /// <summary>
        /// 캡처 데이터를 버퍼에 추가합니다.
        /// </summary>
        void AddFrame(ushort[] data, int width, int height);

        /// <summary>
        /// 버퍼에 캡처된 모든 프레임 목록과 해상도(width, height)를 반환하고 버퍼를 비웁니다.
        /// </summary>
        (List<ushort[]> frames, int width, int height) GetCapturedDataAndClear();

        /// <summary>
        /// 프레임 데이터와 레퍼런스 원본 경로를 기반으로 단일 폴더에 완전한 초분광 데이터를 저장합니다.
        /// </summary>
        /// <param name="baseName">저장될 폴더와 대상 파일의 기본 이름 (예: capture)</param>
        /// <param name="captureDirectory">저장할 최상위 디렉터리 경로</param>
        /// <param name="frames">저장할 16-bit 초분광 프레임 원료 데이터</param>
        /// <param name="whiteRefPath">메모리에 적용 중인 White Reference 원본 Hdr 파일 경로</param>
        /// <param name="darkRefPath">메모리에 적용 중인 Dark Reference 원본 Hdr 파일 경로</param>
        /// <param name="width">프레임 너비 (Sample)</param>
        /// <param name="height">프레임 높이 (Band)</param>
        /// <param name="cameraService">카메라 메타데이터 접근용 서비스 참조</param>
        Task SaveCaptureAsync(string baseName, string captureDirectory, List<ushort[]> frames, string? whiteRefPath, string? darkRefPath, int width, int height, ICameraService cameraService);
    }
}
