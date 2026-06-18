#nullable enable
using System;
using System.Collections.Generic;

namespace MyBrowserShell
{
    /// <summary>
    /// Helpers for per-site shield exceptions (stored as a list of host strings in BrowserSettings).
    /// </summary>
    internal static class SiteShieldPolicy
    {
        /// <summary>
        /// Extracts and normalises the hostname from a URL string.
        /// Returns null if the URL is null, empty, or not a valid http/https URL.
        /// </summary>
        public static string? NormalizeHost(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                // Not an absolute URL — treat as a bare hostname
                string bare = url.Trim().ToLowerInvariant();
                if (bare.StartsWith("www.", StringComparison.Ordinal))
                    bare = bare.Substring(4);
                return string.IsNullOrEmpty(bare) ? null : bare;
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return null;

            // Strip "www." prefix so that exceptions apply to the bare domain.
            string host = uri.Host.ToLowerInvariant();
            if (host.StartsWith("www.", StringComparison.Ordinal))
                host = host.Substring(4);

            return string.IsNullOrEmpty(host) ? null : host;
        }

        /// <summary>
        /// Returns true if the host extracted from <paramref name="urlOrHost"/> matches
        /// any entry in <paramref name="exceptions"/>, including subdomains.
        /// </summary>
        public static bool IsHostExcepted(string? urlOrHost, List<string> exceptions)
        {
            if (urlOrHost == null || exceptions == null || exceptions.Count == 0)
                return false;

            string? host = NormalizeHost(urlOrHost);
            if (host == null) return false;

            foreach (var entry in exceptions)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                // Exact match (e.g. "example.com") or subdomain match (e.g. "sub.example.com")
                if (host == entry || host.EndsWith("." + entry, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Adds or removes the normalised host derived from <paramref name="urlOrHost"/>
        /// from the exceptions list.
        /// </summary>
        public static void SetException(List<string> exceptions, string urlOrHost, bool disabled)
        {
            string? host = NormalizeHost(urlOrHost);
            if (host == null) return;

            if (disabled)
            {
                if (!exceptions.Contains(host))
                    exceptions.Add(host);
            }
            else
            {
                exceptions.Remove(host);
            }
        }
    }
}
