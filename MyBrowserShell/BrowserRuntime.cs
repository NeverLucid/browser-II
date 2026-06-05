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

        // Browser flags shared by normal windows — excludes flags that interfere with Tor.
        // (--disable-background-networking breaks SOCKS keepalive; we strip it for Tor.)
        private static readonly string TorBaseBrowserArguments =
            "--disable-client-side-phishing-detection " +
            "--disable-default-apps " +
            "--no-first-run " +
            "--enable-gpu-rasterization " +
            "--enable-zero-copy " +
            "--enable-features=BackForwardCache " +
            "--disable-features=msEdgeLinkedAccount,msWalletBuyNow,TranslateUI";

        private static CoreWebView2EnvironmentOptions CreateTorEnvironmentOptions(int socksPort)
        {
            // Route ALL traffic (including DNS lookups) through the Tor SOCKS5 proxy.
            //
            // --proxy-server        : force every request through the local Tor SOCKS5 port.
            // --host-resolver-rules : block local DNS resolution so hostnames (inc. .onion) are
            //                         resolved by the proxy, not leaked to the OS resolver.
            //                         We EXCLUDE both "localhost" and "127.0.0.1" so that the
            //                         loopback address used to reach Tor itself still resolves.
            // --proxy-bypass-list   : empty string — nothing bypasses the proxy.
            string torArgs =
                TorBaseBrowserArguments +
                $" --proxy-server=socks5://127.0.0.1:{socksPort}" +
                " --host-resolver-rules=\"MAP * ~NOTFOUND , EXCLUDE localhost , EXCLUDE 127.0.0.1\"" +
                " --proxy-bypass-list=\"<-loopback>\"";

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
