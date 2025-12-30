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
    }
}
