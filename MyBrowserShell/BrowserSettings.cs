#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace MyBrowserShell
{
    internal sealed class BrowserSettings
    {
        private const string BingSearchUrl = "https://www.bing.com/search?q=";
        private string searchUrl = "https://duckduckgo.com/?q=";

        public bool DarkTheme { get; set; } = true;
        public bool ShieldsEnabled { get; set; } = true;
        public string SearchUrl
        {
            get => searchUrl;
            set => searchUrl = string.Equals(value, BingSearchUrl, StringComparison.OrdinalIgnoreCase)
                ? "https://duckduckgo.com/?q="
                : value;
        }
        public string HomeUrl { get; set; } = "";
        public string DefaultDownloadFolder { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        public bool RestoreSavedSession { get; set; }
        public List<string> ShieldDisabledHosts { get; set; } = new();
    }
}
