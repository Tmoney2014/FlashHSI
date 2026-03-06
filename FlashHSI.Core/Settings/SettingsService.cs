using Newtonsoft.Json;

namespace FlashHSI.Core.Settings
{
    public class SettingsService
    {
        private static SettingsService? _instance;
        public static SettingsService Instance => _instance ??= new SettingsService();

        public SystemSettings Settings { get; private set; }

        private readonly string _filePath;

        private SettingsService()
        {
            // Save in the same directory as the executable for portability, or AppData.
            // For this project, local directory is preferred by user context usually.
            _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "systemsettings.json");
            Settings = new SystemSettings();
            Load();
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    var loaded = JsonConvert.DeserializeObject<SystemSettings>(json);
                    if (loaded != null)
                    {
                        Settings = loaded;
                    }
                }
            }
            catch (Exception)
            {
                // Ignore load errors, keep defaults
            }
        }

        public void Save()
        {
            try
            {
                string json = JsonConvert.SerializeObject(Settings, Formatting.Indented);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception)
            {
                // Ignore save errors
            }
        }

        /// <summary>
        /// 램프 온도 퍼센트를 계산하여 반환합니다.
        /// 마지막 업데이트 시간과 현재 시간의 차이를 기반으로 가열/냉각을 계산합니다.
        /// </summary>
        public double GetLampTemperaturePercent()
        {
            var settings = Settings;

            // 마지막 업데이트 시간이 없으면 0%
            if (!settings.LampLastUpdateTime.HasValue)
            {
                return 0.0;
            }

            var elapsed = DateTime.Now - settings.LampLastUpdateTime.Value;

            // 램프가 꺼져있으면 → 무조건 냉각 중
            if (!settings.IsLampOn)
            {
                var coolDownMinutes = settings.LampCoolDownTimeMinutes;
                if (coolDownMinutes <= 0) coolDownMinutes = 5.0; // 기본값

                // 냉각 속도: 100% / coolDownMinutes
                var percentPerSecond = 100.0 / (coolDownMinutes * 60.0);
                var newTemp = settings.LampTemperaturePercent - (elapsed.TotalSeconds * percentPerSecond);
                return Math.Max(0.0, newTemp);
            }

            // 램프가 켜져있으면 → 가열 중 (또는 유지)
            var heatUpMinutes = settings.LampHeatUpTimeMinutes;
            if (heatUpMinutes <= 0) heatUpMinutes = 10.0; // 기본값

            // 가열 속도: 100% / heatUpMinutes
            var heatPercentPerSecond = 100.0 / (heatUpMinutes * 60.0);
            var newHeatTemp = settings.LampTemperaturePercent + (elapsed.TotalSeconds * heatPercentPerSecond);
            return Math.Min(100.0, newHeatTemp);
        }

        /// <summary>
        /// 램프가 켜질 때 호출 (가열 시작)
        /// </summary>
        public void SetLampOn()
        {
            // 현재 온도 계산 후 저장 (마지막으로 켜지던 시점부터 지금까지)
            Settings.LampTemperaturePercent = GetLampTemperaturePercent();
            Settings.IsLampOn = true;
            Settings.LampLastUpdateTime = DateTime.Now;
        }

        /// <summary>
        /// 램프가 꺼질 때 호출 (냉각 시작)
        /// </summary>
        public void SetLampOff()
        {
            // 현재 온도 계산 후 저장 (마지막으로 켜지던 시점부터 지금까지)
            Settings.LampTemperaturePercent = GetLampTemperaturePercent();
            Settings.IsLampOn = false;
            Settings.LampLastUpdateTime = DateTime.Now;
        }

        /// <summary>
        /// 앱 시작 시 호출 - 저장된 값을 기반으로 현재 온도 복원
        /// </summary>
        public void RestoreLampState()
        {
            // 저장된 IsLampOn 상태를 기반으로 현재 온도 계산
            var currentTemp = GetLampTemperaturePercent();
            Settings.LampTemperaturePercent = currentTemp;
            Settings.LampLastUpdateTime = DateTime.Now;
        }

        /// <summary>
        /// 현재 램프 온도 퍼센트를 저장하고 업데이트 시간을 현재로 설정합니다.
        /// </summary>
        public void UpdateLampTemperature(double percent)
        {
            Settings.LampTemperaturePercent = Math.Clamp(percent, 0.0, 100.0);
            Settings.LampLastUpdateTime = DateTime.Now;
        }
    }
}
