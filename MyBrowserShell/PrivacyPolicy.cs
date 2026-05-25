#nullable enable
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MyBrowserShell
{
    internal static class PrivacyPolicy
    {
        public static bool ShieldsEnabled { get; private set; } = true;
        private static readonly HashSet<string> DownloadAllowOnceHosts = new(StringComparer.OrdinalIgnoreCase);

        public static readonly CoreWebView2WebResourceContext[] BlockableResourceContexts =
        {
            CoreWebView2WebResourceContext.Document,
            CoreWebView2WebResourceContext.Image,
            CoreWebView2WebResourceContext.Script,
            CoreWebView2WebResourceContext.XmlHttpRequest,
            CoreWebView2WebResourceContext.Fetch,
            CoreWebView2WebResourceContext.Other
        };

        // Always-active tracker patterns (applied regardless of shields state)
        private static readonly Lazy<IReadOnlyList<string>> TrackerPatternsLazy =
            new(BuildTrackerPatterns);

        // Shields-only patterns (only registered when shields are enabled)
        private static readonly Lazy<IReadOnlyList<string>> ShieldsPatternsLazy =
            new(BuildShieldsPatterns);

        /// <summary>Patterns that are always registered (core tracker list).</summary>
        public static IReadOnlyList<string> TrackerPatterns => TrackerPatternsLazy.Value;

        /// <summary>Patterns only registered when Shields are enabled (ads, telemetry, etc).</summary>
        public static IReadOnlyList<string> ShieldsOnlyPatterns => ShieldsPatternsLazy.Value;

        // Keep the combined list for backwards compat (used nowhere else)
        public static IReadOnlyList<string> BlockableResourcePatterns =>
            TrackerPatternsLazy.Value;

        internal const string DocumentPrivacyScript = @"
(function() {
    try {
        Object.defineProperty(navigator, 'doNotTrack', { get: function() { return '1'; }, configurable: true });
        Object.defineProperty(navigator, 'globalPrivacyControl', { get: function() { return true; }, configurable: true });
        var meta = document.querySelector('meta[name=""referrer""]');
        if (!meta) {
            meta = document.createElement('meta');
            meta.name = 'referrer';
            meta.content = 'strict-origin-when-cross-origin';
            (document.head || document.documentElement).appendChild(meta);
        }
        window.open = function() { return null; };
        navigator.sendBeacon = function() { return false; };
    } catch (e) {}
})();";

        private static readonly HashSet<string> DefaultBlockedTrackerHosts = new(StringComparer.OrdinalIgnoreCase)
        {
            "google-analytics.com",
            "analytics.google.com",
            "googletagmanager.com",
            "ssl.google-analytics.com",
            "facebook.net",
            "connect.facebook.net",
            "scorecardresearch.com",
            "quantserve.com",
            "quantcount.com",
            "hotjar.com",
            "segment.io",
            "mixpanel.com",
            "amplitude.com",
            "matomo.cloud",
            "newrelic.com",
            "nr-data.net",
            "bugsnag.com",
            "sentry.io"
        };

        private static readonly HashSet<string> ShieldBlockedHosts = new(StringComparer.OrdinalIgnoreCase)
        {
            "googlesyndication.com",
            "doubleclick.net",
            "ads.twitter.com",
            "ads.linkedin.com",
            "adservice.google.com",
            "googleadservices.com",
            "pagead2.googlesyndication.com",
            "securepubads.g.doubleclick.net",
            "taboola.com",
            "outbrain.com",
            "criteo.com",
            "criteo.net",
            "adnxs.com",
            "rubiconproject.com",
            "pubmatic.com",
            "openx.net",
            "adform.net",
            "smartadserver.com",
            "media.net",
            "amazon-adsystem.com",
            "yieldmo.com",
            "zedo.com",
            "popads.net",
            "propellerads.com",
            "coinhive.com",
            "coin-hive.com",
            "cryptoloot.pro"
        };

        private static readonly string[] ShieldBlockedHostTokens =
        {
            ".ads.",
            ".adserver.",
            ".adservice.",
            ".analytics.",
            ".tracking.",
            ".track.",
            ".telemetry.",
            ".metrics."
        };

        private static readonly string[] ShieldBlockedLeadingHostTokens =
        {
            "ads.",
            "adserver.",
            "adservice.",
            "analytics.",
            "tracking.",
            "track.",
            "telemetry.",
            "metrics."
        };

        private static readonly string[] ShieldBlockedPathTokens =
        {
            "/ads/",
            "/adserver/",
            "/advertising/",
            "/banner/",
            "/banners/",
            "/popunder/",
            "/popup/",
            "/tracking/",
            "/track/",
            "/telemetry/",
            "/metrics/",
            "/fingerprint",
            "/beacon",
            "/pixel"
        };

        public static void SetShieldsEnabled(bool enabled) => ShieldsEnabled = enabled;

        public static void AllowDownloadOnceForHost(string host)
        {
            if (!string.IsNullOrWhiteSpace(host))
                DownloadAllowOnceHosts.Add(host);
        }

        public static bool ConsumeDownloadAllowOnce(string uriString)
        {
            if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
                return false;

            return DownloadAllowOnceHosts.Remove(uri.Host);
        }

        public static void ClearSessionExceptions() => DownloadAllowOnceHosts.Clear();

        public static bool ShouldBlockUri(string uriString, CoreWebView2WebResourceContext context)
        {
            if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
                return false;

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return false;

            string host = uri.Host;
            if (MatchesHost(host, DefaultBlockedTrackerHosts))
                return true;

            if (!ShieldsEnabled)
                return false;

            if (MatchesHost(host, ShieldBlockedHosts))
                return true;

            if (context is CoreWebView2WebResourceContext.Image
                or CoreWebView2WebResourceContext.Script
                or CoreWebView2WebResourceContext.XmlHttpRequest
                or CoreWebView2WebResourceContext.Fetch
                or CoreWebView2WebResourceContext.Other)
            {
                foreach (var token in ShieldBlockedHostTokens)
                {
                    if (host.Contains(token, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                foreach (var token in ShieldBlockedLeadingHostTokens)
                {
                    if (host.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                string path = uri.AbsolutePath;
                foreach (var token in ShieldBlockedPathTokens)
                {
                    if (path.Contains(token, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        public static bool ShouldBlockPopup() => ShieldsEnabled;

        public static bool ShouldBlockRedirect(int redirectCount) =>
            ShieldsEnabled && redirectCount > 1;

        private static IReadOnlyList<string> BuildTrackerPatterns()
        {
            var patterns = new List<string>();
            foreach (var host in DefaultBlockedTrackerHosts)
            {
                patterns.Add("*://" + host + "/*");
                patterns.Add("*://*." + host + "/*");
            }
            return patterns;
        }

        private static IReadOnlyList<string> BuildShieldsPatterns()
        {
            var patterns = new List<string>();
            foreach (var host in ShieldBlockedHosts)
            {
                patterns.Add("*://" + host + "/*");
                patterns.Add("*://*." + host + "/*");
            }
            foreach (var token in ShieldBlockedHostTokens)
                patterns.Add("*://*" + token + "*/*");
            foreach (var token in ShieldBlockedLeadingHostTokens)
                patterns.Add("*://" + token + "*/*");
            foreach (var token in ShieldBlockedPathTokens)
                patterns.Add("*://*" + token + "*");
            return patterns;
        }

        private static bool MatchesHost(string host, HashSet<string> blockedHosts)
        {
            if (blockedHosts.Contains(host))
                return true;

            foreach (var blocked in blockedHosts)
            {
                if (host.EndsWith("." + blocked, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static bool ShouldDenyPermission(CoreWebView2PermissionKind kind)
        {
            if (!ShieldsEnabled)
                return false;

            return kind switch
            {
                CoreWebView2PermissionKind.Notifications => true,
                CoreWebView2PermissionKind.Geolocation => true,
                _ => false
            };
        }

        public static async Task ApplyProfileSettingsAsync(CoreWebView2Profile profile, bool shields)
        {
            profile.PreferredTrackingPreventionLevel = shields
                ? CoreWebView2TrackingPreventionLevel.Balanced
                : CoreWebView2TrackingPreventionLevel.Basic;

            profile.IsGeneralAutofillEnabled = false;
            profile.IsPasswordAutosaveEnabled = false;

            if (shields)
            {
                await SetPermissionStateAsync(profile, CoreWebView2PermissionKind.Notifications,
                    CoreWebView2PermissionState.Deny);
                await SetPermissionStateAsync(profile, CoreWebView2PermissionKind.Geolocation,
                    CoreWebView2PermissionState.Deny);
            }
            else
            {
                await SetPermissionStateAsync(profile, CoreWebView2PermissionKind.Notifications,
                    CoreWebView2PermissionState.Default);
                await SetPermissionStateAsync(profile, CoreWebView2PermissionKind.Geolocation,
                    CoreWebView2PermissionState.Default);
            }
        }

        public static void ApplyBrowserSettings(CoreWebView2Settings settings, bool shields)
        {
            settings.IsGeneralAutofillEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;
            settings.IsReputationCheckingRequired = !shields;
            settings.IsStatusBarEnabled = false;
            // IsWebMessageEnabled must stay true — it is the JS<->host bridge used by the new-tab
            // page to receive bookmark/theme data via ExecuteScriptAsync. It is not a privacy surface.
            settings.IsWebMessageEnabled = true;
            settings.AreDevToolsEnabled = false;
            // AreBrowserAcceleratorKeysEnabled = false would disable Ctrl+C/V/A/Z which are
            // standard editing shortcuts — always keep them enabled.
            settings.AreBrowserAcceleratorKeysEnabled = true;
            settings.IsSwipeNavigationEnabled = !shields;
            settings.IsPinchZoomEnabled = true;
            settings.IsZoomControlEnabled = false;
        }

        private static async Task SetPermissionStateAsync(
            CoreWebView2Profile profile,
            CoreWebView2PermissionKind kind,
            CoreWebView2PermissionState state)
        {
            try
            {
                await profile.SetPermissionStateAsync(kind, "*", state);
            }
            catch { }
        }

        public static CoreWebView2BrowsingDataKinds AllBrowsingDataKinds { get; } =
            CoreWebView2BrowsingDataKinds.AllSite |
            CoreWebView2BrowsingDataKinds.Cookies |
            CoreWebView2BrowsingDataKinds.DiskCache |
            CoreWebView2BrowsingDataKinds.DownloadHistory |
            CoreWebView2BrowsingDataKinds.BrowsingHistory |
            CoreWebView2BrowsingDataKinds.GeneralAutofill |
            CoreWebView2BrowsingDataKinds.PasswordAutosave |
            CoreWebView2BrowsingDataKinds.AllDomStorage |
            CoreWebView2BrowsingDataKinds.ServiceWorkers;
    }
}
