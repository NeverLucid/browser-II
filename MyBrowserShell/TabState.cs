#nullable enable
using System;
using System.Collections.Generic;

namespace MyBrowserShell
{
    public class TabState
    {
        public string Url { get; set; } = "about:blank";
        public int ScrollY { get; set; } = 0;
        public bool IsIncognito { get; set; } = false;
        public DateTime LastAccessed { get; set; } = DateTime.UtcNow;

        // Privacy report data
        public List<string> BlockedItems { get; } = new();
        public bool HttpsOnlyBlocked { get; set; } = false;
        public int BlockedTrackers { get; set; }
        public int BlockedPopups { get; set; }
        public int DeniedPermissions { get; set; }
        public int BlockedRedirects { get; set; }
        public int BlockedDownloads { get; set; }

        public void ResetPrivacyReport()
        {
            BlockedItems.Clear();
            HttpsOnlyBlocked = false;
            BlockedTrackers = 0;
            BlockedPopups = 0;
            DeniedPermissions = 0;
            BlockedRedirects = 0;
            BlockedDownloads = 0;
        }
    }
}
