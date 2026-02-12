using System;

namespace FlashHSI.Core.Settings
{
    public class SystemSettings
    {
        public string LastHeaderPath { get; set; } = "";
        public string LastWhiteRefPath { get; set; } = "";
        public string LastDarkRefPath { get; set; } = "";
        public string LastModelPath { get; set; } = "";

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
    }
}
