#nullable enable
using System;
using System.IO;

namespace MyBrowserShell
{
    internal sealed class BrowserSettings
    {
        public bool DarkTheme { get; set; } = true;
        public bool ShieldsEnabled { get; set; } = true;
        public string SearchUrl { get; set; } = "https://duckduckgo.com/?q=";
        public string HomeUrl { get; set; } = "";
        public string DefaultDownloadFolder { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        public bool RestoreSavedSession { get; set; }
    }
}
