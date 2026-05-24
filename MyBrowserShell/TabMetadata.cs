#nullable enable
using System;

namespace MyBrowserShell
{
    internal sealed class TabMetadata
    {
        public bool IsPinned { get; set; }
        public bool IsMuted { get; set; }
        public bool IsSuspended { get; set; }
        public DateTime LastActiveUtc { get; set; } = DateTime.UtcNow;
    }
}
