#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MyBrowserShell
{
    internal sealed class SavedSession
    {
        public List<SavedTabState> Tabs { get; set; } = new();
    }

    internal sealed class SavedTabState
    {
        public string Url { get; set; } = "";
        public bool IsPinned { get; set; }
        public bool IsMuted { get; set; }
        public bool IsSuspended { get; set; }
    }

    internal sealed class SessionStore
    {
        private readonly string filePath;

        public SessionStore(string appDataFolder)
        {
            filePath = Path.Combine(appDataFolder, "saved-session.json");
        }

        public bool Exists => File.Exists(filePath);

        public async Task SaveAsync(IEnumerable<SavedTabState> tabs)
        {
            var session = new SavedSession
            {
                Tabs = tabs.Where(t => !string.IsNullOrWhiteSpace(t.Url)).ToList()
            };

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(session, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
            }
            catch { }
        }

        public async Task<SavedSession> LoadAsync()
        {
            try
            {
                if (!File.Exists(filePath))
                    return new SavedSession();

                var json = await File.ReadAllTextAsync(filePath);
                return JsonSerializer.Deserialize<SavedSession>(json) ?? new SavedSession();
            }
            catch
            {
                return new SavedSession();
            }
        }

        public void Delete()
        {
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch { }
        }
    }
}
