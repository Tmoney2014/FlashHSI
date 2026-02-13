using System;

namespace FlashHSI.Core.Settings
{
    public class SystemSettings
    {
        public string LastHeaderPath { get; set; } = "";
        public string LastWhiteRefPath { get; set; } = "";
        public string LastDarkRefPath { get; set; } = "";
        public string LastModelPath { get; set; } = "";

        // AI가 추가함: 에어건으로 쳐낼 클래스 인덱스 목록
        public List<int> SelectedSortClasses { get; set; } = new List<int>();

        public double TargetFps { get; set; } = 100.0;
        public double ConfidenceThreshold { get; set; } = 0.75;
        public double BackgroundThreshold { get; set; } = 0.0;

        public int AirGunChannelCount { get; set; } = 32;
        
        // AI가 추가함: Blob Tracking Parameters
        public int BlobMinPixels { get; set; } = 5;
        public int BlobLineGap { get; set; } = 5;
        public int BlobPixelGap { get; set; } = 10; // Default increased for stability

        // AI가 추가함: 카메라 파라미터 설정
        public double CameraExposureTime { get; set; } = 1000.0;   // μs
        public double CameraFrameRate { get; set; } = 100.0;       // FPS
        public int CameraSensorSize { get; set; } = 1024; // Default Sensor Width

        // AI가 추가함: Ejection Logic Parameters
        public int EjectionDelayMs { get; set; } = 300; // Legacy 'BlowDelay'
        public int EjectionDurationMs { get; set; } = 10; // Legacy 'BlowTime'
        public List<YCorrectionRule> YCorrectionRules { get; set; } = new List<YCorrectionRule>();
        public int EjectionBlowMargin { get; set; } = 0;

        // AI가 추가함: 센서→벨트→채널 매핑 설정 (레거시 SystemSetting 동등)
        /// <summary>실제 시야폭 (mm). 센서가 보는 벨트 위 물리적 폭. 0이면 센서픽셀=채널 단순매핑</summary>
        public int FieldOfView { get; set; } = 0;
        /// <summary>채널 번호 반전 여부 (하드웨어 설치 방향에 따라)</summary>
        public bool IsChannelReverse { get; set; } = false;

        // AI가 추가함: 피더(Feeder) 설정
        /// <summary>피더 개수 (0~9)</summary>
        public int FeederCount { get; set; } = 0;
        /// <summary>피더별 출력 값 목록 (각 1~99)</summary>
        public List<int> FeederValues { get; set; } = new List<int>();
        /// <summary>시리얼 통신에 사용할 COM 포트 이름</summary>
        public string SelectedSerialPort { get; set; } = "";

        // AI가 추가함: EtherCAT 연결 설정
        /// <summary>EtherCAT 연결에 사용할 네트워크 인터페이스 이름</summary>
        public string SelectedNetworkInterface { get; set; } = "";
        /// <summary>EtherCAT 마스터 IO 갱신 주기 (Hz)</summary>
        public uint EtherCATCycleFrequency { get; set; } = 500;

        // AI가 추가함: 램프 온도 모니터링 설정
        /// <summary>램프 온도 퍼센트 (0~100, 앱 종료 시 저장하여 다음 시작 시 복원)</summary>
        public double LampTemperaturePercent { get; set; } = 0.0;
        /// <summary>램프 온도 마지막 업데이트 시간 (가열/냉각 경과 계산용)</summary>
        public DateTime? LampLastUpdateTime { get; set; }
        /// <summary>램프 가열 소요 시간 (분, 0→100%까지)</summary>
        public double LampHeatUpTimeMinutes { get; set; } = 10.0;
        /// <summary>램프 냉각 소요 시간 (분, 100%→0%까지)</summary>
        public double LampCoolDownTimeMinutes { get; set; } = 5.0;
        
        // AI가 추가함: 카메라 재시도 타이머 설정
        /// <summary>카메라 재시도 타이머 (분, 앱 시작 시 카운트다운 후 카메라 초기화 재시도)</summary>
        public int CameraRetryTimerMinutes { get; set; } = 5;
    }
}
