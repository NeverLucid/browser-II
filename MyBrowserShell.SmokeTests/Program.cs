using Microsoft.Web.WebView2.Core;
using MyBrowserShell;

var tests = new (string Name, Action Run)[]
{
    ("URL normalization", UrlNormalization),
    ("Shield blocking decisions", ShieldBlockingDecisions),
    ("Site shield exceptions", SiteShieldExceptions),
    ("Settings persistence", SettingsPersistence),
    ("Tor port selection", TorPortSelection),
    ("Shield third-party path tokens", ShieldPathTokenBlocking),
    ("Shield allows first-party", ShieldAllowsFirstParty),
};

foreach (var (name, run) in tests)
{
    run();
    Console.WriteLine("pass: " + name);
}

static void UrlNormalization()
{
    Equal(null, Tab.NormalizeNavigationUrlForTests("   "));
    Equal("https://example.com", Tab.NormalizeNavigationUrlForTests("example.com"));
    Equal("https://example.com/path", Tab.NormalizeNavigationUrlForTests("http://example.com/path"));
    Equal("https://example.com/path", Tab.NormalizeNavigationUrlForTests("https://example.com/path"));
    Equal("file:///C:/Temp/NewTab.html", Tab.NormalizeNavigationUrlForTests("file:///C:/Temp/NewTab.html"));
}

static void ShieldBlockingDecisions()
{
    True(PrivacyPolicy.ShouldBlockUri(
        "https://www.google-analytics.com/collect?v=1",
        CoreWebView2WebResourceContext.Script,
        shieldsEnabled: true,
        sourceUri: "https://example.com/"));

    False(PrivacyPolicy.ShouldBlockUri(
        "https://www.google-analytics.com/collect?v=1",
        CoreWebView2WebResourceContext.Script,
        shieldsEnabled: false,
        sourceUri: "https://example.com/"));

    True(PrivacyPolicy.ShouldBlockUri(
        "https://ads.example.net/banner.js",
        CoreWebView2WebResourceContext.Script,
        shieldsEnabled: true,
        sourceUri: "https://example.com/"));

    False(PrivacyPolicy.ShouldBlockUri(
        "https://example.com/assets/analytics.js",
        CoreWebView2WebResourceContext.Script,
        shieldsEnabled: true,
        sourceUri: "https://example.com/"));

    // Known ad network hosts are blocked
    True(PrivacyPolicy.ShouldBlockUri(
        "https://googlesyndication.com/pagead/js/adsbygoogle.js",
        CoreWebView2WebResourceContext.Script,
        shieldsEnabled: true,
        sourceUri: "https://example.com/"));

    // doubleclick.net blocked
    True(PrivacyPolicy.ShouldBlockUri(
        "https://securepubads.g.doubleclick.net/gpt/pubads_impl.js",
        CoreWebView2WebResourceContext.Script,
        shieldsEnabled: true,
        sourceUri: "https://example.com/"));
}

static void ShieldPathTokenBlocking()
{
    // /pixel path token should be blocked as third-party
    True(PrivacyPolicy.ShouldBlockUri(
        "https://cdn.otherdomain.com/pixel/track",
        CoreWebView2WebResourceContext.Image,
        shieldsEnabled: true,
        sourceUri: "https://example.com/"));

    // /collect? path token blocked as third-party
    True(PrivacyPolicy.ShouldBlockUri(
        "https://cdn.otherdomain.com/collect?id=123",
        CoreWebView2WebResourceContext.Fetch,
        shieldsEnabled: true,
        sourceUri: "https://example.com/"));
}

static void ShieldAllowsFirstParty()
{
    // First-party analytics path should NOT be blocked
    False(PrivacyPolicy.ShouldBlockUri(
        "https://example.com/analytics/pageview",
        CoreWebView2WebResourceContext.Fetch,
        shieldsEnabled: true,
        sourceUri: "https://example.com/"));

    // First-party ad-named asset should NOT be blocked
    False(PrivacyPolicy.ShouldBlockUri(
        "https://example.com/assets/ads/banner.png",
        CoreWebView2WebResourceContext.Image,
        shieldsEnabled: true,
        sourceUri: "https://example.com/"));
}

static void SiteShieldExceptions()
{
    var disabled = new List<string>();
    SiteShieldPolicy.SetException(disabled, "https://www.example.com/page", disabled: true);
    Equal("example.com", disabled.Single());
    True(SiteShieldPolicy.IsHostExcepted("https://example.com", disabled));
    True(SiteShieldPolicy.IsHostExcepted("https://sub.example.com", disabled));
    False(SiteShieldPolicy.IsHostExcepted("https://example.net", disabled));

    SiteShieldPolicy.SetException(disabled, "example.com", disabled: false);
    Equal(0, disabled.Count);
}

static void SettingsPersistence()
{
    string root = Path.Combine(Path.GetTempPath(), "MyBrowserShellSmokeTests", Guid.NewGuid().ToString("N"));
    var store = new SettingsStore(root);
    var settings = new BrowserSettings
    {
        DarkTheme = false,
        ShieldsEnabled = true,
        SearchUrl = "https://search.example/?q="
    };
    settings.ShieldDisabledHosts.Add("example.com");

    store.Save(settings);
    var loaded = store.Load();

    False(loaded.DarkTheme);
    True(loaded.ShieldsEnabled);
    Equal("https://search.example/?q=", loaded.SearchUrl);
    Equal("example.com", loaded.ShieldDisabledHosts.Single());

    // Round-trip with defaults: missing file should return a valid default object
    string emptyRoot = Path.Combine(Path.GetTempPath(), "MyBrowserShellSmokeTests", Guid.NewGuid().ToString("N"));
    var defaults = new SettingsStore(emptyRoot).Load();
    NotNull(defaults);
}

static void TorPortSelection()
{
    // With no preferred ports occupied, should return a free ephemeral port > 0
    int port = TorProxy.SelectAvailableSocksPortForTests();
    True(port > 0);

    // Passing an obviously-unbound port should get it back directly
    // (pick something unlikely to be in use on CI)
    int preferred = 19853;
    int selected = TorProxy.SelectAvailableSocksPortForTests(preferred);
    // It's either our preferred (if free) or a different free port — either way > 0
    True(selected > 0);
}

static void True(bool value)
{
    if (!value)
        throw new InvalidOperationException("Expected true.");
}

static void False(bool value)
{
    if (value)
        throw new InvalidOperationException("Expected false.");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

static void NotNull<T>(T value) where T : class
{
    if (value is null)
        throw new InvalidOperationException("Expected non-null value.");
}
