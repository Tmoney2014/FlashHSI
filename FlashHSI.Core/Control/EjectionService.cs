using CommunityToolkit.Mvvm.Messaging;
using FlashHSI.Core.Analysis;
using FlashHSI.Core.Messages;
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

        // AI가 추가함: 캐시된 설정값 (메시지로 업데이트)
        private int _cachedFieldOfView;
        private bool _cachedIsChannelReverse;
        private int _cachedEjectionDelayMs;
        private int _cachedEjectionDurationMs;
        private int _cachedEjectionBlowMargin;
        private int _cachedAirGunChannelCount;
        private int _cachedCameraSensorSize;

        /// <summary>
        /// 생성자 - 메시지 구독
        /// </summary>
        public EjectionService()
        {
            // 메시지 구독
            WeakReferenceMessenger.Default.Register<EjectionService, SettingsChangedMessage<int>>(this, static (recipient, message) =>
            {
                switch (message.PropertyName)
                {
                    case nameof(SystemSettings.FieldOfView):
                        recipient._cachedFieldOfView = message.Value;
                        break;
                    case nameof(SystemSettings.EjectionDelayMs):
                        recipient._cachedEjectionDelayMs = message.Value;
                        break;
                    case nameof(SystemSettings.EjectionDurationMs):
                        recipient._cachedEjectionDurationMs = message.Value;
                        break;
                    case nameof(SystemSettings.EjectionBlowMargin):
                        recipient._cachedEjectionBlowMargin = message.Value;
                        break;
                    case nameof(SystemSettings.AirGunChannelCount):
                        recipient._cachedAirGunChannelCount = message.Value;
                        break;
                    case nameof(SystemSettings.CameraSensorSize):
                        recipient._cachedCameraSensorSize = message.Value;
                        break;
                }
            });
            
            WeakReferenceMessenger.Default.Register<EjectionService, SettingsChangedMessage<bool>>(this, static (recipient, message) =>
            {
                if (message.PropertyName == nameof(SystemSettings.IsChannelReverse))
                {
                    recipient._cachedIsChannelReverse = message.Value;
                }
            });
        }

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
            // Note: 캐시된 값을 사용 (런타임 변경 시 즉시 반영)
            int sensorWidth = _cachedCameraSensorSize;
            if (sensorWidth == 0) sensorWidth = 1024; // Default fallback

            int channelCount = _cachedAirGunChannelCount;
            if (channelCount <= 0) channelCount = 32;

            int fov = _cachedFieldOfView;
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
            if (_cachedIsChannelReverse)
                centerChannel = channelCount - centerChannel + 1;

            // Channel Margin Calculation
            var channels = MarginBlowCalculator(centerChannel, _cachedEjectionBlowMargin, channelCount);

            // 2. Temporal Calculation (When to hit) - 단순하게 Delay만 적용
            // Note: 캐시된 값을 사용 (런타임 변경 시 즉시 반영)
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

            // 단순하게: 사용자가 지정한 딜레이 + Y보정만 적용
            int finalDelayMs = _cachedEjectionDelayMs + yCorrection;

            // 3. Duration Calculation (How long to hit)
            int durationMs = _cachedEjectionDurationMs;

            // Log & Event
            // AI 수정: margin이 설정된 경우 ValveId = 0으로 설정하여 HomeViewModel에서 각 채널 개별 fire하도록 함
            var log = new EjectionLogItem
            {
                Timestamp = DateTime.Now,
                BlobId = blob.Id,
                ClassId = bestClass,
                ValveId = _cachedEjectionBlowMargin > 0 ? 0 : centerChannel, // margin 사용 시 0으로 설정하여 ValveIds 경로로 유도
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
