using System;
using System.IO;
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
    }
}
