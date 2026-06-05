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
                return null;

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return null;

            // Strip "www." prefix so that exceptions apply to the bare domain.
            string host = uri.Host.ToLowerInvariant();
            if (host.StartsWith("www.", StringComparison.Ordinal))
                host = host.Substring(4);

            return string.IsNullOrEmpty(host) ? null : host;
        }

        /// <summary>
        /// Returns true if <paramref name="host"/> (or null) is in the exceptions list.
        /// </summary>
        public static bool IsHostExcepted(string? host, List<string> exceptions)
        {
            if (host == null || exceptions == null || exceptions.Count == 0)
                return false;

            return exceptions.Contains(host);
        }

        /// <summary>
        /// Adds or removes <paramref name="host"/> from the exceptions list.
        /// </summary>
        public static void SetException(List<string> exceptions, string host, bool disabled)
        {
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
