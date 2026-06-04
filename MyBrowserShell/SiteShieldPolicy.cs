#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace MyBrowserShell
{
    internal static class SiteShieldPolicy
    {
        public static string? NormalizeHost(string? urlOrHost)
        {
            if (string.IsNullOrWhiteSpace(urlOrHost))
                return null;

            string value = urlOrHost.Trim();
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                    return null;

                value = uri.Host;
            }

            value = value.Trim().Trim('.').ToLowerInvariant();
            if (value.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                value = value[4..];

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        public static bool IsHostExcepted(string? urlOrHost, IEnumerable<string>? disabledHosts)
        {
            string? host = NormalizeHost(urlOrHost);
            if (host == null || disabledHosts == null)
                return false;

            foreach (var entry in disabledHosts)
            {
                string? disabled = NormalizeHost(entry);
                if (disabled == null)
                    continue;

                if (host.Equals(disabled, StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("." + disabled, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static void SetException(List<string> disabledHosts, string host, bool disabled)
        {
            string? normalized = NormalizeHost(host);
            if (normalized == null)
                return;

            disabledHosts.RemoveAll(item =>
                NormalizeHost(item)?.Equals(normalized, StringComparison.OrdinalIgnoreCase) == true);

            if (disabled)
                disabledHosts.Add(normalized);

            disabledHosts.Sort(StringComparer.OrdinalIgnoreCase);
        }
    }
}
