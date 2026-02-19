using System;
using System.Collections.Generic;
using System.Linq;
using FlashHSI.Core.Analysis;
using FlashHSI.Core.Settings;

namespace FlashHSI.Core.Control
{
    /// <summary>
    /// <ai>AI가 작성함</ai>
    /// Logical controller for calculating ejection timing and mapping pixels to valves.
    /// User-Centric Design: System provides Coordinates, User controls Timing/Duration/Range.
    /// </summary>
    public class EjectionService
    {
        // AI가 추가함: 사출 대상 클래스 필터 (null이면 전체 사출)
        private HashSet<int>? _targetClasses;

        public event Action<EjectionLogItem>? OnEjectionSignal;

        /// <summary>
        /// <ai>AI가 작성함</ai>
        /// 에어건 타겟 클래스를 설정해요. null이면 모든 클래스를 사출해요.
        /// </summary>
        public void SetTargetClasses(HashSet<int>? targets)
        {
            _targetClasses = targets;
        }

        /// <summary>
        /// Process a blob and generate ejection command based on User Settings.
        /// </summary>
        /// <param name="blob">The active blob to eject.</param>
        public void Process(ActiveBlob blob)
        {
            if (blob == null) return;

            int bestClass = blob.GetBestClass();
            if (bestClass < 0) return; // Background or Invalid

            // AI가 수정함: 선택된 클래스만 사출 (타겟 필터링, O(1) HashSet 조회)
            if (_targetClasses != null && !_targetClasses.Contains(bestClass)) return;

            var settings = SettingsService.Instance.Settings;
            double currentFps = settings.CameraFrameRate; // Use Configured FPS

            // 1. Spatial Mapping (Where to hit)
            // User Setting: EjectionBlowMargin
            // System Data: Centroid X
            double centerX = blob.CenterX;
            
            // AI가 수정함: 레거시 매핑 공식 적용 (FOV 기반 + 채널 반전)
            // 레거시: mappedX = x / sensorSize * fov; channel = mappedX / (fov / chCount);
            int sensorWidth = (int)settings.CameraSensorSize;
            if (sensorWidth == 0) sensorWidth = 1024; // Default fallback

            int channelCount = settings.AirGunChannelCount;
            if (channelCount <= 0) channelCount = 32;

            int fov = settings.FieldOfView;
            int centerChannel;

            if (fov > 0)
            {
                // FOV 설정됨: 센서 픽셀 → 물리 위치(mm) → 채널
                float mappedX = (float)(centerX / sensorWidth * fov);
                float channelFloat = mappedX / ((float)fov / channelCount);
                centerChannel = (int)MathF.Round(channelFloat);
            }
            else
            {
                // FOV 미설정: 단순 선형 매핑 (센서 픽셀 = 채널)
                double pixelsPerValve = (double)sensorWidth / channelCount;
                centerChannel = (int)(centerX / pixelsPerValve);
            }

            // 채널 반전 (하드웨어 설치 방향에 따라)
            if (settings.IsChannelReverse)
                centerChannel = channelCount - centerChannel + 1;

            // Channel Margin Calculation
            var channels = MarginBlowCalculator(centerChannel, settings.EjectionBlowMargin, channelCount);

            // 2. Temporal Calculation (When to hit)
            // System Data: Centroid Y (blob.CenterY), Current Line (blob.EndLine)
            // User Setting: EjectionDelayMs, EjectionDelayOffsetMs
            
            double linesSinceCentroid = blob.EndLine - blob.CenterY;
            double msPerLine = 1000.0 / Math.Max(1.0, currentFps);
            double timeSinceCentroidPassed = linesSinceCentroid * msPerLine;

            // Final Delay = (User Base Delay + Y-Correction) - (Time Already Passed)
            int yCorrection = 0;
            if (settings.YCorrectionRules != null)
            {
                // Find matching rule with highest threshold
                var rule = settings.YCorrectionRules
                    .Where(r => blob.CenterY >= r.ThresholdY)
                    .OrderByDescending(r => r.ThresholdY)
                    .FirstOrDefault();

                if (rule != null)
                {
                    yCorrection = rule.CorrectionMs;
                }
            }

            int totalUserDelay = settings.EjectionDelayMs + yCorrection;
            int finalDelayMs = (int)(totalUserDelay - timeSinceCentroidPassed);

            if (finalDelayMs < 0) finalDelayMs = 0; // Fire immediately if late

            // 3. Duration Calculation (How long to hit)
            // User Setting: EjectionDurationMs (Fixed)
            int durationMs = settings.EjectionDurationMs;

            // Log & Event
            // AI 수정: margin이 설정된 경우 ValveId = 0으로 설정하여 HomeViewModel에서 각 채널 개별 fire하도록 함
            var log = new EjectionLogItem
            {
                Timestamp = DateTime.Now,
                BlobId = blob.Id,
                ClassId = bestClass,
                ValveId = settings.EjectionBlowMargin > 0 ? 0 : centerChannel, // margin 사용 시 0으로 설정하여 ValveIds 경로로 유도
                ValveIds = channels,      // All channels
                Delay = finalDelayMs,     // In MS now
                DurationMs = durationMs,  // In MS
                HitType = "User-Centric"
            };

            OnEjectionSignal?.Invoke(log);
        }

        /// <summary>
        /// Calculates the list of channels to fire based on center and margin.
        /// </summary>
        public List<int> MarginBlowCalculator(int centerChannel, int margin, int maxChannels)
        {
            var list = new List<int>();
            int start = Math.Max(1, centerChannel - margin);
            int end = Math.Min(maxChannels, centerChannel + margin);

            for (int i = start; i <= end; i++)
            {
                list.Add(i);
            }
            return list;
        }
    }
}
