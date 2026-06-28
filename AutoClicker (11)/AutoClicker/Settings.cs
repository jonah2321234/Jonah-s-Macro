using System;
using System.IO;
using System.Text.Json;

namespace AutoClicker
{
    /// <summary>
    /// User-configurable settings, persisted to a JSON file in %AppData%\AutoClicker
    /// so they survive between app launches.
    /// </summary>
    public class AppSettings
    {
        public double DelayMs { get; set; } = 0;
        public uint HotkeyVk { get; set; } = 117; // F6
        public bool ResyncEnabled { get; set; } = true;
        public int ResyncIntervalMinutes { get; set; } = 15;
        public string Theme { get; set; } = "Blue";

        private static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoClicker", "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null) return loaded;
                }
            }
            catch
            {
                // fall back to defaults on any read/parse error
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(dir);
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch
            {
                // saving is best-effort; ignore failures (e.g. no write permission)
            }
        }
    }
}
