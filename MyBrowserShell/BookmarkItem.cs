#nullable enable
using System;

namespace MyBrowserShell
{
    internal sealed class BookmarkItem
    {
        public string Title { get; set; } = "Untitled";
        public string Url { get; set; } = "";
        public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
