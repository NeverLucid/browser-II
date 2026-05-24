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

        private static CoreWebView2EnvironmentOptions CreateEnvironmentOptions()
        {
            return new CoreWebView2EnvironmentOptions
            {
                AllowSingleSignOnUsingOSPrimaryAccount = false,
                AreBrowserExtensionsEnabled = false,
                EnableTrackingPrevention = true,
                AdditionalBrowserArguments = BrowserRuntime.AdditionalBrowserArguments
            };
        }
    }
}
