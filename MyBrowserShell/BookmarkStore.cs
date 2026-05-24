#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MyBrowserShell
{
    internal sealed class BookmarkStore
    {
        private readonly string filePath;

        public BookmarkStore(string appDataFolder)
        {
            filePath = Path.Combine(appDataFolder, "bookmarks.json");
        }

        public List<BookmarkItem> Load()
        {
            try
            {
                if (!File.Exists(filePath))
                    return new List<BookmarkItem>();

                var saved = JsonSerializer.Deserialize<List<BookmarkItem>>(File.ReadAllText(filePath));
                return saved?
                    .Where(b => !string.IsNullOrWhiteSpace(b.Url))
                    .ToList() ?? new List<BookmarkItem>();
            }
            catch
            {
                return new List<BookmarkItem>();
            }
        }

        public void Save(IEnumerable<BookmarkItem> bookmarks)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                File.WriteAllText(filePath, JsonSerializer.Serialize(bookmarks, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
            }
            catch { }
        }
    }
}
