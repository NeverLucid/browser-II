#nullable enable
using System;
using System.Collections.Generic;

namespace MyBrowserShell
{
    internal enum BrowserResourceKind
    {
        Other,
        Image,
        Media,
        Script,
        XmlHttpRequest,
        Fetch,
        EventSource,
        WebSocket,
        Ping
    }

    internal enum BrowserPermissionKind
    {
        Other,
        Notifications,
        Geolocation,
        Camera,
        Microphone
    }

    internal static class PrivacyPolicy
    {
        public static bool ShieldsEnabled { get; private set; } = true;
        private static readonly HashSet<string> DownloadAllowOnceHosts = new(StringComparer.OrdinalIgnoreCase);

        private static readonly Lazy<IReadOnlyList<string>> TrackerPatternsLazy =
            new(BuildTrackerPatterns);
        private static readonly Lazy<IReadOnlyList<string>> ShieldsPatternsLazy =
            new(BuildShieldsPatterns);

        public static IReadOnlyList<string> TrackerPatterns => TrackerPatternsLazy.Value;
        public static IReadOnlyList<string> ShieldsOnlyPatterns => ShieldsPatternsLazy.Value;
        public static IReadOnlyList<string> BlockableResourcePatterns => TrackerPatternsLazy.Value;

        internal const string DocumentPrivacyScript = @"
(function() {
    try {
        Object.defineProperty(navigator, 'doNotTrack', { get: function() { return '1'; }, configurable: true });
        Object.defineProperty(navigator, 'globalPrivacyControl', { get: function() { return true; }, configurable: true });
        Object.defineProperty(navigator, 'webdriver', { get: function() { return false; }, configurable: true });
        var meta = document.querySelector('meta[name=""referrer""]');
        if (!meta) {
            meta = document.createElement('meta');
            meta.name = 'referrer';
            meta.content = 'strict-origin-when-cross-origin';
            (document.head || document.documentElement).appendChild(meta);
        }
        navigator.sendBeacon = function() { return false; };
    } catch (e) {}
})();";

        private static readonly HashSet<string> DefaultBlockedTrackerHosts = new(StringComparer.OrdinalIgnoreCase)
        {
            "google-analytics.com",
            "analytics.google.com",
            "googletagservices.com",
            "googletagmanager.com",
            "ssl.google-analytics.com",
            "facebook.net",
            "connect.facebook.net",
            "bat.bing.com",
            "clarity.ms",
            "static.hotjar.com",
            "scorecardresearch.com",
            "quantserve.com",
            "quantcount.com",
            "hotjar.com",
            "segment.io",
            "mixpanel.com",
            "amplitude.com",
            "fullstory.com",
            "heapanalytics.com",
            "matomo.cloud",
            "mc.yandex.ru",
            "metrika.yandex.ru",
            "snap.licdn.com",
            "analytics.tiktok.com",
            "static.ads-twitter.com",
            "cloudflareinsights.com",
            "plausible.io",
            "newrelic.com",
            "nr-data.net",
            "bugsnag.com",
            "sentry.io"
        };

        // Enhanced ad-blocking hosts - now includes 50+ ad networks
        private static readonly HashSet<string> ShieldBlockedHosts = new(StringComparer.OrdinalIgnoreCase)
        {
            // Google Ad Services
            "googlesyndication.com",
            "doubleclick.net",
            "adservice.google.com",
            "googleadservices.com",
            "pagead2.googlesyndication.com",
            "securepubads.g.doubleclick.net",
            "ads.google.com",
            "ad.doubleclick.net",
            "stats.g.doubleclick.net",
            "www-google-analytics.l.google.com",
            
            // Social Media Ads
            "ads.twitter.com",
            "ads.linkedin.com",
            "facebook.com",
            "instagram.com",
            
            // Content Ad Networks & Recommendation Engines
            "taboola.com",
            "outbrain.com",
            "criteo.com",
            "criteo.net",
            
            // Programmatic Ad Exchanges & Supply-Side Platforms
            "adnxs.com",
            "adsrvr.org",
            "bidswitch.net",
            "casalemedia.com",
            "indexww.com",
            "moatads.com",
            "rubiconproject.com",
            "pubmatic.com",
            "openx.net",
            "adform.net",
            "smartadserver.com",
            "media.net",
            "amazon-adsystem.com",
            "yieldmo.com",
            "zedo.com",
            
            // Pop-up & Malicious Ads
            "popads.net",
            "propellerads.com",
            
            // Crypto Mining
            "coinhive.com",
            "coin-hive.com",
            "cryptoloot.pro",
            
            // Additional Ad Networks & Services
            "aol.com",
            "oath.com",
            "verizon-media.com",
            "sonobi.com",
            "indexexchange.com",
            "appnexus.com",
            "improvado.io",
            "spotxchange.com",
            "telaria.com",
            "triplelift.com",
            "pubwise.io",
            "jivox.com",
            "smaato.com",
            "flurry.com",
            "applovin.com",
            "mopub.com",
            "unity3d.com",
            "unityads.unity3d.com"
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
            ".metrics.",
            ".ad.",
            ".advertisement.",
            ".advertise.",
            ".promotional.",
            ".monetize."
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
            "metrics.",
            "ad.",
            "advertisement.",
            "advertise.",
            "promotional.",
            "monetize."
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
            "/pixel",
            "/collect?",
            "/event?",
            "/events?",
            "/log?",
            "/logs?",
            "/stats?",
            "/adm/",
            "/advertisement/",
            "/sponsored/",
            "/promoted/",
            "/promo/",
            "/display/",
            "/click/",
            "/impression/",
            "/viewable/",
            "/vast/",
            "/vpaid/"
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

        public static bool ShouldBlockUri(string uriString, BrowserResourceKind context) =>
            ShouldBlockUri(uriString, context, ShieldsEnabled, null);

        public static bool ShouldBlockUri(
            string uriString,
            BrowserResourceKind context,
            bool shieldsEnabled,
            string? sourceUri)
        {
            if (!shieldsEnabled)
                return false;

            if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
                return false;

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return false;

            string host = uri.Host;
            if (MatchesHost(host, DefaultBlockedTrackerHosts))
                return true;

            if (MatchesHost(host, ShieldBlockedHosts))
                return true;

            bool thirdParty = IsLikelyThirdParty(uri, sourceUri);
            if (context is BrowserResourceKind.Image
                or BrowserResourceKind.Media
                or BrowserResourceKind.Script
                or BrowserResourceKind.XmlHttpRequest
                or BrowserResourceKind.Fetch
                or BrowserResourceKind.EventSource
                or BrowserResourceKind.WebSocket
                or BrowserResourceKind.Ping
                or BrowserResourceKind.Other)
            {
                foreach (var token in ShieldBlockedHostTokens)
                {
                    if (thirdParty && host.Contains(token, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                foreach (var token in ShieldBlockedLeadingHostTokens)
                {
                    if (thirdParty && host.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                string pathAndQuery = uri.PathAndQuery;
                foreach (var token in ShieldBlockedPathTokens)
                {
                    if (thirdParty && pathAndQuery.Contains(token, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                // Block common ad tracking parameters
                if (uri.Query.Contains("utm_", StringComparison.OrdinalIgnoreCase) ||
                    uri.Query.Contains("fbclid", StringComparison.OrdinalIgnoreCase) ||
                    uri.Query.Contains("gclid", StringComparison.OrdinalIgnoreCase) ||
                    uri.Query.Contains("msclkid", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool ShouldBlockPopup() => ShouldBlockPopup(ShieldsEnabled);
        public static bool ShouldBlockPopup(bool shieldsEnabled) => shieldsEnabled;

        public static bool ShouldBlockRedirect(int redirectCount) =>
            ShouldBlockRedirect(redirectCount, ShieldsEnabled);

        public static bool ShouldBlockRedirect(int redirectCount, bool shieldsEnabled) =>
            shieldsEnabled && redirectCount > 2;

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

        private static bool IsLikelyThirdParty(Uri resourceUri, string? sourceUri)
        {
            if (string.IsNullOrWhiteSpace(sourceUri) ||
                !Uri.TryCreate(sourceUri, UriKind.Absolute, out var source))
                return true;

            return !GetRegistrableHost(resourceUri.Host).Equals(
                GetRegistrableHost(source.Host),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string GetRegistrableHost(string host)
        {
            var parts = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 2)
                return host;

            return parts[^2] + "." + parts[^1];
        }

        public static bool ShouldDenyPermission(BrowserPermissionKind kind) =>
            ShouldDenyPermission(kind, ShieldsEnabled);

        public static bool ShouldDenyPermission(BrowserPermissionKind kind, bool shieldsEnabled)
        {
            if (!shieldsEnabled)
                return false;

            return kind switch
            {
                BrowserPermissionKind.Notifications => true,
                BrowserPermissionKind.Geolocation => true,
                BrowserPermissionKind.Camera => true,
                BrowserPermissionKind.Microphone => true,
                _ => false
            };
        }
    }
}
