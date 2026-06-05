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
            // Privacy / startup noise suppression
            "--disable-background-networking " +
            "--disable-sync " +
            "--disable-client-side-phishing-detection " +
            "--disable-default-apps " +
            "--no-first-run " +

            // GPU / rendering acceleration
            "--enable-gpu-rasterization " +
            "--enable-zero-copy " +
            "--enable-oop-rasterization " +
            "--enable-accelerated-video-decode " +
            "--enable-accelerated-2d-canvas " +
            "--ignore-gpu-blocklist " +

            // Network performance
            "--enable-quic " +                                  // HTTP/3 for modern sites
            "--enable-tcp-fast-open " +                         // TCP Fast Open reduces RTT
            "--dns-prefetch-disable=false " +                   // keep DNS prefetch ON
            "--max-connections-per-proxy=16 " +                 // more parallel connections
            "--disk-cache-size=134217728 " +                    // 128 MB explicit disk cache

            // Renderer performance
            "--disable-hang-monitor " +                         // no 5 s hang detection stall
            "--disable-ipc-flooding-protection " +              // no IPC throttle on fast tabs
            "--disable-renderer-backgrounding " +               // bg tabs stay full speed
            "--disable-backgrounding-occluded-windows " +       // occluded windows don't throttle
            "--process-per-site " +                             // fewer renderer processes

            // Feature flags
            "--enable-features=BackForwardCache,NetworkServiceInProcess2,ThrottleDisplayNoneAndVisibilityHiddenCrossOriginIframes " +
            "--disable-features=msEdgeLinkedAccount,msWalletBuyNow,TranslateUI,HeavyAdIntervention,LowPriorityIframes";

        /// <summary>
        /// Eagerly starts initialising the WebView2 environment in the background.
        /// Call this as early as possible (e.g. in Form constructor) so the environment
        /// is ready by the time the first tab needs it, eliminating first-navigation latency.
        /// </summary>
        public static void Warmup() => _ = GetEnvironmentAsync();

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

        // Tor window flags — same perf set as normal windows but without flags
        // that interfere with SOCKS keepalive (--disable-background-networking, --disable-sync).
        private static readonly string TorBaseBrowserArguments =
            "--disable-client-side-phishing-detection " +
            "--disable-default-apps " +
            "--no-first-run " +
            "--enable-gpu-rasterization " +
            "--enable-zero-copy " +
            "--enable-oop-rasterization " +
            "--enable-accelerated-video-decode " +
            "--enable-accelerated-2d-canvas " +
            "--ignore-gpu-blocklist " +
            "--enable-quic " +
            "--disk-cache-size=67108864 " +                     // 64 MB cache for Tor tabs
            "--disable-hang-monitor " +
            "--disable-ipc-flooding-protection " +
            "--disable-renderer-backgrounding " +
            "--disable-backgrounding-occluded-windows " +
            "--enable-features=BackForwardCache,NetworkServiceInProcess2 " +
            "--disable-features=msEdgeLinkedAccount,msWalletBuyNow,TranslateUI,HeavyAdIntervention";

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
