#nullable enable
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MyBrowserShell
{
    public class Tab : UserControl
    {
        public WebView2 WebView { get; private set; }
        public TabState State { get; } = new();
        public bool IsSuspended => _isSuspended;
        public bool EffectiveShieldsEnabled => _effectiveShieldsEnabled;

        private bool _isSuspended;
        private bool _privacyHandlersAttached;
        private bool _shieldsFiltersEnabled;
        private string? _privacyScriptId;
        private bool _effectiveShieldsEnabled;
        private bool _settingsApplied;
        private bool _appliedShields;
        private readonly Dictionary<ulong, int> _redirectCountsByNavigation = new();
        private string _lastUrl = "";

        public event EventHandler? PrivacyStatsChanged;
        public event EventHandler<BrowserDownloadRequestedEventArgs>? DownloadRequested;
        public event EventHandler<BrowserDownloadStartedEventArgs>? DownloadStarted;

        public Tab()
        {
            Dock = DockStyle.Fill;

            WebView = new WebView2
            {
                Dock = DockStyle.Fill,
                CreationProperties = new CoreWebView2CreationProperties
                {
                    UserDataFolder = BrowserRuntime.UserDataFolder,
                    AdditionalBrowserArguments = BrowserRuntime.AdditionalBrowserArguments,
                    IsInPrivateModeEnabled = true
                }
            };

            Controls.Add(WebView);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    WebView?.CoreWebView2?.Stop();
                }
                catch { }

                try
                {
                    WebView?.Dispose();
                }
                catch { }
            }

            base.Dispose(disposing);
        }


        public async Task InitializeAsync(string url, bool shieldsEnabled)
        {
            var environment = await BrowserRuntime.GetEnvironmentAsync();
            var controllerOptions = environment.CreateCoreWebView2ControllerOptions();
            controllerOptions.IsInPrivateModeEnabled = true;
            await WebView.EnsureCoreWebView2Async(environment, controllerOptions);
            AttachPrivacyHandlers();
            await ApplyShieldsAsync(shieldsEnabled);

            // ⭐ FIX: Allow local HTML files
            var normalizedUrl = NormalizeNavigationUrl(url);
            if (normalizedUrl == null)
                return;

            _lastUrl = normalizedUrl;
            WebView.Source = new Uri(normalizedUrl);
        }

        /// <summary>
        /// Initializes this tab using the Tor-proxied WebView2 environment.
        /// All traffic (including DNS) is routed through the local Tor SOCKS5 proxy.
        /// </summary>
        public async Task InitializeTorAsync(string url)
        {
            var environment = await BrowserRuntime.GetTorEnvironmentAsync();
            var controllerOptions = environment.CreateCoreWebView2ControllerOptions();
            controllerOptions.IsInPrivateModeEnabled = true;
            await WebView.EnsureCoreWebView2Async(environment, controllerOptions);
            AttachPrivacyHandlers();
            await ApplyShieldsAsync(true); // Shields always on in Tor windows

            var normalizedUrl = NormalizeNavigationUrl(url);
            if (normalizedUrl == null)
                return;

            _lastUrl = normalizedUrl;
            WebView.Source = new Uri(normalizedUrl);
        }


        public async Task ApplyShieldsAsync(bool shields)
        {
            if (WebView.CoreWebView2 == null)
                return;

            if (_settingsApplied && _appliedShields == shields)
                return;

            var core = WebView.CoreWebView2;
            await PrivacyPolicy.ApplyProfileSettingsAsync(core.Profile, shields);
            PrivacyPolicy.ApplyBrowserSettings(core.Settings, shields);
            _effectiveShieldsEnabled = shields;
            _settingsApplied = true;
            _appliedShields = shields;

            core.Settings.UserAgent =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

            UpdateShieldsFilters(shields);

            if (shields && _privacyScriptId == null)
            {
                _privacyScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(
                    PrivacyPolicy.DocumentPrivacyScript);
            }
            else if (!shields && _privacyScriptId != null)
            {
                try
                {
                    core.RemoveScriptToExecuteOnDocumentCreated(_privacyScriptId);
                }
                catch { }

                _privacyScriptId = null;
            }
        }

        public void SetShieldsForNavigation(bool shields)
        {
            if (_effectiveShieldsEnabled == shields)
                return;

            _effectiveShieldsEnabled = shields;
            UpdateShieldsFilters(shields);
        }

        private void AttachPrivacyHandlers()
        {
            if (_privacyHandlersAttached)
                return;

            var core = WebView.CoreWebView2;

            core.WebResourceRequested += OnWebResourceRequested;
            core.PermissionRequested += OnPermissionRequested;
            core.DownloadStarting += OnDownloadStarting;
            core.NewWindowRequested += OnNewWindowRequested;
            core.NavigationStarting += OnCoreNavigationStarting;
            core.NavigationCompleted += OnCoreNavigationCompleted;

            _privacyHandlersAttached = true;
        }

        /// <summary>
        /// Adds or removes shield resource filters when shields are toggled at runtime.
        /// </summary>
        public void UpdateShieldsFilters(bool shieldsEnabled)
        {
            var core = WebView?.CoreWebView2;
            if (core == null) return;
            if (_shieldsFiltersEnabled == shieldsEnabled)
                return;

            foreach (var context in PrivacyPolicy.BlockableResourceContexts)
            {
                foreach (var pattern in PrivacyPolicy.TrackerPatterns)
                {
                    try
                    {
                        if (shieldsEnabled)
                            core.AddWebResourceRequestedFilter(pattern, context);
                        else
                            core.RemoveWebResourceRequestedFilter(pattern, context);
                    }
                    catch { }
                }

                foreach (var pattern in PrivacyPolicy.ShieldsOnlyPatterns)
                {
                    try
                    {
                        if (shieldsEnabled)
                            core.AddWebResourceRequestedFilter(pattern, context);
                        else
                            core.RemoveWebResourceRequestedFilter(pattern, context);
                    }
                    catch { }
                }
            }

            _shieldsFiltersEnabled = shieldsEnabled;
        }

        private void OnCoreNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (!e.IsRedirected)
            {
                _redirectCountsByNavigation[e.NavigationId] = 0;
                State.Url = e.Uri;
                State.LastAccessed = DateTime.UtcNow;
                State.ResetPrivacyReport();
                PrivacyStatsChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (!_effectiveShieldsEnabled)
                return;

            _redirectCountsByNavigation.TryGetValue(e.NavigationId, out int redirects);
            redirects++;
            _redirectCountsByNavigation[e.NavigationId] = redirects;

            if (PrivacyPolicy.ShouldBlockRedirect(redirects, _effectiveShieldsEnabled))
            {
                State.BlockedRedirects++;
                State.BlockedItems.Add("Redirect blocked: " + e.Uri);
                PrivacyStatsChanged?.Invoke(this, EventArgs.Empty);
                e.Cancel = true;
            }
        }

        private void OnCoreNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            _redirectCountsByNavigation.Remove(e.NavigationId);
        }

        private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            if (!PrivacyPolicy.ShouldBlockPopup(_effectiveShieldsEnabled))
                return;

            e.Handled = true;
            State.BlockedPopups++;
            State.BlockedItems.Add("Popup blocked: " + e.Uri);
            PrivacyStatsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
        {
            if (!PrivacyPolicy.ShouldDenyPermission(e.PermissionKind, _effectiveShieldsEnabled))
                return;

            e.State = CoreWebView2PermissionState.Deny;
            e.Handled = true;
            State.DeniedPermissions++;
            State.BlockedItems.Add("Permission denied: " + e.PermissionKind);
            PrivacyStatsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
        {
            var request = new BrowserDownloadRequestedEventArgs(
                e.DownloadOperation.Uri,
                e.ResultFilePath,
                _effectiveShieldsEnabled);
            if (PrivacyPolicy.ConsumeDownloadAllowOnce(e.DownloadOperation.Uri))
                request.Cancel = false;

            DownloadRequested?.Invoke(this, request);

            if (request.Cancel)
            {
                e.Cancel = true;
                e.Handled = true;
                State.BlockedDownloads++;
                State.BlockedItems.Add("Download blocked: " + e.DownloadOperation.Uri);
                PrivacyStatsChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            e.ResultFilePath = request.ResultFilePath;
            e.Handled = true;
            DownloadStarted?.Invoke(this, new BrowserDownloadStartedEventArgs(
                e.DownloadOperation.Uri,
                e.ResultFilePath,
                e.DownloadOperation));
        }

        private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            PrivacyPolicy.ApplyRequestPrivacyHeaders(e.Request, _effectiveShieldsEnabled);

            string? sourceUri = TryGetHeader(e.Request.Headers, "Referer");
            if (!PrivacyPolicy.ShouldBlockUri(
                    e.Request.Uri,
                    e.ResourceContext,
                    _effectiveShieldsEnabled,
                    sourceUri))
                return;

            try
            {
                e.Response = WebView.CoreWebView2.Environment.CreateWebResourceResponse(
                    Stream.Null, 204, "Blocked", "Content-Type: text/plain");
                State.BlockedTrackers++;
                State.BlockedItems.Add("Tracker blocked: " + e.Request.Uri);
                PrivacyStatsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch { }
        }

        private static string? TryGetHeader(CoreWebView2HttpRequestHeaders headers, string name)
        {
            try
            {
                return headers.GetHeader(name);
            }
            catch
            {
                return null;
            }
        }

        public void Navigate(string url)
        {
            var normalizedUrl = NormalizeNavigationUrl(url);
            if (normalizedUrl == null)
                return;

            _lastUrl = normalizedUrl;
            WebView.Source = new Uri(normalizedUrl);
        }

        private static string? NormalizeNavigationUrl(string url)
        {
            return NormalizeNavigationUrlForTests(url);
        }

        internal static string? NormalizeNavigationUrlForTests(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            url = url.Trim();

            if (url.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                return url;

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return "https://" + url;
            }

            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                return "https://" + url[7..];

            return url;
        }

        public void ShowNavigationError(CoreWebView2WebErrorStatus status, string homeUrl)
        {
            // Guard: WebView2 must be fully initialized before calling NavigateToString
            if (WebView.CoreWebView2 == null || WebView.IsDisposed)
                return;

            string attemptedUrl = System.Net.WebUtility.HtmlEncode(_lastUrl);
            string attemptedUrlLiteral = JsonSerializer.Serialize(_lastUrl);
            string homeUrlLiteral = JsonSerializer.Serialize(homeUrl);
            string statusText = System.Net.WebUtility.HtmlEncode(status.ToString());

            WebView.NavigateToString($@"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
  <title>Page not found</title>
  <style>
    :root {{ color-scheme: dark; font-family: ""Segoe UI"", system-ui, sans-serif; background: #121317; color: #f0f3f6; }}
    body {{ margin: 0; min-height: 100vh; display: grid; place-items: center; background: #121317; }}
    main {{ width: min(620px, calc(100vw - 48px)); }}
    h1 {{ margin: 0 0 12px; font-size: 30px; font-weight: 650; }}
    p {{ margin: 0 0 18px; color: #aeb7c2; font-size: 15px; line-height: 1.55; }}
    code {{ display: block; overflow-wrap: anywhere; padding: 12px 14px; border: 1px solid #48505d; border-radius: 8px; color: #d6f8ec; background: #1c1e24; }}
    .status {{ margin-top: 12px; color: #8f99a8; font-size: 13px; }}
    .actions {{ display: flex; flex-wrap: wrap; gap: 10px; margin-top: 18px; }}
    button {{ border: 1px solid #48505d; border-radius: 8px; padding: 10px 14px; background: #272a32; color: #f0f3f6; font: inherit; cursor: pointer; }}
    button.primary {{ border-color: #24b885; }}
  </style>
</head>
<body>
  <main>
    <h1>Couldn't open this page</h1>
    <p>The app couldn't reach that address. Check the spelling, or search from the address bar with DuckDuckGo.</p>
    <code>{attemptedUrl}</code>
    <p class=""status"">Status: {statusText}</p>
    <div class=""actions"">
      <button class=""primary"" onclick=""location.href = attemptedUrl"">Retry</button>
      <button onclick=""location.href = homeUrl"">Go home</button>
      <button onclick=""navigator.clipboard && navigator.clipboard.writeText(attemptedUrl)"">Copy address</button>
    </div>
  </main>
  <script>
    const attemptedUrl = {attemptedUrlLiteral};
    const homeUrl = {homeUrlLiteral};
  </script>
</body>
</html>");
        }

        public void Suspend()
        {
            if (_isSuspended)
                return;

            _lastUrl = WebView.Source?.ToString() ?? _lastUrl;
            _ = WebView.CoreWebView2?.TrySuspendAsync();
            WebView.Visible = false;
            _isSuspended = true;
        }

        public void Resume()
        {
            if (!_isSuspended)
                return;

            WebView.CoreWebView2?.Resume();
            WebView.Visible = true;
            _isSuspended = false;
        }

        public async Task ClearPrivateDataAsync()
        {
            if (WebView.CoreWebView2 == null)
                return;

            try
            {
                await WebView.CoreWebView2.Profile.ClearBrowsingDataAsync(
                    PrivacyPolicy.AllBrowsingDataKinds);
            }
            catch { }
        }

        public async Task ApplyDarkModeAsync(bool enabled)
        {
            if (WebView.CoreWebView2 == null)
                return;

            const string css = @"
html, body {
    background-color: #121212 !important;
    color: #e0f7ff !important;
}
img, video {
    filter: brightness(0.95) contrast(1.05);
}";

            string script = enabled
                ? "var s=document.getElementById('jacob-dark');" +
                  "if(!s){s=document.createElement('style');s.id='jacob-dark';s.innerHTML=`" + css + "`;document.head.appendChild(s);}"
                : "var s=document.getElementById('jacob-dark'); if(s) s.remove();";

            await WebView.CoreWebView2.ExecuteScriptAsync(script);
        }

        private bool _readerModeActive;

        public async Task ApplyReaderModeAsync(bool isDark = true)
        {
            if (WebView.CoreWebView2 == null)
                return;

            // Toggle off: reload the page to restore original content
            if (_readerModeActive)
            {
                _readerModeActive = false;
                WebView.CoreWebView2.Reload();
                return;
            }

            _readerModeActive = true;

            string bg      = isDark ? "#121212" : "#ffffff";
            string bodyBg  = isDark ? "#050505" : "#f3f4f6";
            string color   = isDark ? "#e0f7ff" : "#1a1c22";
            string shadow  = isDark ? "0 0 24px rgba(0,0,0,0.6)" : "0 2px 12px rgba(0,0,0,0.12)";

            string bgLit      = System.Text.Json.JsonSerializer.Serialize(bg);
            string bodyBgLit  = System.Text.Json.JsonSerializer.Serialize(bodyBg);
            string colorLit   = System.Text.Json.JsonSerializer.Serialize(color);
            string shadowLit  = System.Text.Json.JsonSerializer.Serialize(shadow);

            string script = $@"
(function(bg, bodyBg, color, shadow) {{
    try {{
        var article = document.querySelector('article');
        if (!article) {{
            var ps = Array.from(document.querySelectorAll('p'));
            if (ps.length > 5) {{
                article = document.createElement('div');
                ps.forEach(p => article.appendChild(p.cloneNode(true)));
            }} else {{
                article = document.body.cloneNode(true);
            }}
        }}
        document.body.innerHTML = '';
        var container = document.createElement('div');
        container.style.cssText = 'max-width:800px;margin:40px auto;font-family:Segoe UI,sans-serif;' +
            'font-size:18px;line-height:1.6;color:' + color + ';background-color:' + bg + ';' +
            'padding:24px;border-radius:12px;box-shadow:' + shadow + ';';
        container.appendChild(article);
        document.body.style.backgroundColor = bodyBg;
        document.body.style.margin = '0';
        document.body.style.padding = '16px';
        document.body.appendChild(container);
    }} catch(e) {{}}
}})({bgLit}, {bodyBgLit}, {colorLit}, {shadowLit});";

            await WebView.CoreWebView2.ExecuteScriptAsync(script);
        }

        public bool IsReaderModeActive => _readerModeActive;

        public async Task EnterPictureInPictureAsync()
        {
            if (WebView.CoreWebView2 == null)
                return;

            const string script = @"
(async function() {
    try {
        const vids = document.querySelectorAll('video');
        if (vids.length === 0) return;
        const v = vids[0];
        if (document.pictureInPictureElement) {
            await document.exitPictureInPicture();
        } else if (v.requestPictureInPicture) {
            await v.requestPictureInPicture();
        }
    } catch(e) {}
})();";

            await WebView.CoreWebView2.ExecuteScriptAsync(script);
        }

        public async Task<int> FindOnPageAsync(string query, bool reverse)
        {
            if (WebView.CoreWebView2 == null || string.IsNullOrWhiteSpace(query))
                return 0;

            string queryLiteral = JsonSerializer.Serialize(query);
            string reverseLiteral = reverse ? "true" : "false";
            string script = @"
(function(q, reverse) {
    try {
        var text = document.body ? document.body.innerText || '' : '';
        var lowerText = text.toLocaleLowerCase();
        var lowerQuery = q.toLocaleLowerCase();
        var count = 0;
        var pos = 0;
        while (lowerQuery && (pos = lowerText.indexOf(lowerQuery, pos)) !== -1) {
            count++;
            pos += lowerQuery.length;
        }
        window.find(q, false, reverse, true, false, true, false);
        return count;
    } catch(e) {
        return 0;
    }
})(" + queryLiteral + ", " + reverseLiteral + ");";

            string result = await WebView.CoreWebView2.ExecuteScriptAsync(script);
            return int.TryParse(result, out int count) ? count : 0;
        }

        public async Task SetMutedAsync(bool muted)
        {
            if (WebView.CoreWebView2 == null)
                return;

            string value = muted ? "true" : "false";
            await WebView.CoreWebView2.ExecuteScriptAsync(
                "document.querySelectorAll('audio,video').forEach(function(m){ m.muted = " + value + "; });");
        }

        public string? GetCurrentUrl() => WebView.Source?.ToString();
    }

    public sealed class BrowserDownloadRequestedEventArgs : EventArgs
    {
        public BrowserDownloadRequestedEventArgs(string sourceUrl, string resultFilePath, bool shieldsEnabled)
        {
            SourceUrl = sourceUrl;
            ResultFilePath = resultFilePath;
            ShieldsEnabled = shieldsEnabled;
            Cancel = shieldsEnabled;
        }

        public string SourceUrl { get; }
        public string ResultFilePath { get; set; }
        public bool ShieldsEnabled { get; }
        public bool Cancel { get; set; }
    }

    public sealed class BrowserDownloadStartedEventArgs : EventArgs
    {
        public BrowserDownloadStartedEventArgs(
            string sourceUrl,
            string resultFilePath,
            CoreWebView2DownloadOperation operation)
        {
            SourceUrl = sourceUrl;
            ResultFilePath = resultFilePath;
            Operation = operation;
        }

        public string SourceUrl { get; }
        public string ResultFilePath { get; }
        public CoreWebView2DownloadOperation Operation { get; }
    }
}
