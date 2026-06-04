#nullable enable
using System;
using System.IO;
using System.Text.Json;

namespace MyBrowserShell
{
    internal sealed class SettingsStore
    {
        private readonly string filePath;

        public SettingsStore(string appDataFolder)
        {
            filePath = Path.Combine(appDataFolder, "settings.json");
        }

        public BrowserSettings Load()
        {
            try
            {
                if (!File.Exists(filePath))
                    return new BrowserSettings();

                var settings = JsonSerializer.Deserialize<BrowserSettings>(File.ReadAllText(filePath));
                if (settings == null)
                    return new BrowserSettings();

                settings.ShieldDisabledHosts ??= new();
                return settings;
            }
            catch
            {
                return new BrowserSettings();
            }
        }

        public void Save(BrowserSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                File.WriteAllText(filePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
            }
            catch { }
        }
    }
}
