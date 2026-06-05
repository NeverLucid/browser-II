#nullable enable
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Threading.Tasks;

namespace MyBrowserShell
{
    internal static class BrowserRuntime
    {
        internal static readonly string UserDataFolder =
            Path.Combine(Path.GetTempPath(), "MyBrowserShell", "PrivateWebView2");

        private static Task<CoreWebView2Environment>? _environmentTask;

        internal const string AdditionalBrowserArguments =
            "--disable-background-networking " +
            "--disable-sync " +
            "--disable-client-side-phishing-detection " +
            "--disable-default-apps " +
            "--no-first-run " +
            "--enable-gpu-rasterization " +
            "--enable-zero-copy " +
            "--enable-features=BackForwardCache " +
            "--disable-features=msEdgeLinkedAccount,msWalletBuyNow,TranslateUI";

        public static Task<CoreWebView2Environment> GetEnvironmentAsync()
        {
            return _environmentTask ??= CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: UserDataFolder,
                options: CreateEnvironmentOptions());
        }

        internal static readonly string TorUserDataFolder =
            Path.Combine(Path.GetTempPath(), "MyBrowserShell", "TorWebView2");

        // Cached per SOCKS port so the same environment is reused within one session.
        private static int _cachedTorPort;
        private static Task<CoreWebView2Environment>? _torEnvironmentTask;

        /// <summary>
        /// Returns (or creates) a WebView2 environment that routes all traffic through
        /// the Tor SOCKS5 proxy on <see cref="TorProxy.ActiveSocksPort"/>.
        /// Must be called only after <see cref="TorProxy.EnsureRunningAsync"/> succeeds.
        /// </summary>
        public static Task<CoreWebView2Environment> GetTorEnvironmentAsync()
        {
            int port = TorProxy.ActiveSocksPort;

            // If the port changed (e.g. a new Tor instance on a different port) drop the old cache.
            if (_torEnvironmentTask != null && _cachedTorPort != port)
                _torEnvironmentTask = null;

            _cachedTorPort = port;
            return _torEnvironmentTask ??= CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: TorUserDataFolder,
                options: CreateTorEnvironmentOptions(port));
        }

        private static CoreWebView2EnvironmentOptions CreateTorEnvironmentOptions(int socksPort)
        {
            // Route all traffic (including DNS) through the local Tor SOCKS5 proxy.
            // --host-resolver-rules prevents hostname leaks outside of Tor.
            string torArgs = AdditionalBrowserArguments +
                $" --proxy-server=socks5://127.0.0.1:{socksPort}" +
                " --host-resolver-rules=\"MAP * ~NOTFOUND , EXCLUDE 127.0.0.1\"";

            return new CoreWebView2EnvironmentOptions
            {
                AllowSingleSignOnUsingOSPrimaryAccount = false,
                AreBrowserExtensionsEnabled = false,
                EnableTrackingPrevention = true,
                AdditionalBrowserArguments = torArgs
            };
        }

        private static CoreWebView2EnvironmentOptions CreateEnvironmentOptions()
        {
            return new CoreWebView2EnvironmentOptions
            {
                AllowSingleSignOnUsingOSPrimaryAccount = false,
                AreBrowserExtensionsEnabled = false,
                EnableTrackingPrevention = true,
                AdditionalBrowserArguments = AdditionalBrowserArguments
            };
        }
    }
}
