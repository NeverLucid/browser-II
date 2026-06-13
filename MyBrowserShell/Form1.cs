#nullable enable
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MyBrowserShell
{
    public partial class Form1 : Form
    {
        private TableLayoutPanel rootLayout = null!;
        private Panel tabStripBar = null!;
        private FlowLayoutPanel tabFlow = null!;
        private Panel topBar = null!;
        private Panel addressContainer = null!;
        private Panel tabSearchContainer = null!;
        private Panel findPanel = null!;
        private TextBox addressBar = null!;
        private TextBox tabSearch = null!;
        private TextBox findBox = null!;
        private Label findStatusLabel = null!;

        private ChromeIconButton newTabButton = null!;
        private ChromeIconButton backButton = null!;
        private ChromeIconButton forwardButton = null!;
        private ChromeIconButton reloadButton = null!;
        private ChromeIconButton bookmarkButton = null!;
        private ChromeIconButton bookmarksButton = null!;
        private ChromeIconButton downloadsButton = null!;
        private ChromeIconButton readerModeButton = null!;
        private ChromeIconButton pipButton = null!;
        private ChromeIconButton saveSessionButton = null!;
        private ChromeIconButton loadSessionButton = null!;
        private ChromeIconButton clearDataButton = null!;
        private ChromeIconButton shieldsButton = null!;
        private ChromeIconButton themeButton = null!;
        private ChromeIconButton settingsButton = null!;

        private readonly List<TabPage> allPages = new();
        private readonly Dictionary<TabPage, TabMetadata> tabMetadata = new();
        private readonly List<BookmarkItem> bookmarks = new();
        private readonly Stack<string> closedTabUrls = new();
        private readonly HashSet<Keys> pressedWebViewShortcuts = new();
        private readonly List<ChromeIconButton> chromeButtons = new();
        private readonly ToolTip toolTip = new()
        {
            AutoPopDelay = 7000,
            InitialDelay = 350,
            ReshowDelay = 100,
            ShowAlways = true
        };

        private readonly string appDataFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyBrowserShell");
        private string homeUrl =
            new Uri(Path.Combine(AppContext.BaseDirectory, "NewTab.html")).AbsoluteUri;
        private SettingsStore settingsStore = null!;
        private BookmarkStore bookmarkStore = null!;
        private SessionStore sessionStore = null!;
        private DownloadManager downloadManager = null!;
        private BrowserSettings settings = new();

        private readonly Color Accent = Color.FromArgb(0, 120, 212);
        private readonly Color AccentGreen = Color.FromArgb(36, 184, 133);

        // Cached theme objects — rebuilt only on theme toggle, not per-draw
        private TabTheme? _cachedTabTheme;
        private ButtonTheme? _cachedButtonTheme;

        // Autocomplete dirty-tracking — only rebuild when bookmark URLs actually change
        private int _autoCompleteBookmarkHash = 0;
        private readonly Color DarkWindow = Color.FromArgb(18, 19, 23);
        private readonly Color DarkChrome = Color.FromArgb(28, 30, 36);
        private readonly Color DarkSurface = Color.FromArgb(39, 42, 50);
        private readonly Color DarkRaised = Color.FromArgb(52, 56, 66);
        private readonly Color DarkBorder = Color.FromArgb(72, 78, 91);
        private readonly Color DarkText = Color.FromArgb(240, 243, 246);
        private readonly Color DarkMuted = Color.FromArgb(161, 169, 181);
        private readonly Color LightWindow = Color.FromArgb(244, 246, 249);
        private readonly Color LightChrome = Color.FromArgb(252, 253, 255);
        private readonly Color LightSurface = Color.FromArgb(235, 239, 244);
        private readonly Color LightRaised = Color.White;
        private readonly Color LightBorder = Color.FromArgb(204, 212, 223);
        private readonly Color LightText = Color.FromArgb(28, 33, 40);
        private readonly Color LightMuted = Color.FromArgb(91, 101, 113);
        private bool darkTheme = true;
        private bool shieldsEnabled = true;
        private bool pageLoading;
        private bool isTorWindow;
        private TabPage? draggingTabPage;
        private bool isTrueFullscreen;
        private Tab? fullscreenTab;
        private FormBorderStyle restoreFormBorderStyle;
        private FormWindowState restoreWindowState;
        private Rectangle restoreBounds;
        private bool restoreTopMost;
        private bool restoreFindPanelVisible;
        private float restoreTabStripHeight;
        private float restoreToolbarHeight;
        private float restoreFindPanelHeight;

        // Hold-to-preview navigation history (Back/Forward buttons)
        private const int NavHoldPreviewDelayMs = 430;
        private bool _navHoldTriggered;
        private Timer? _backHoldTimer;
        private Timer? _forwardHoldTimer;
        private ContextMenuStrip? _navHistoryMenu;

        private bool IsDarkTheme => darkTheme;

        private Color WindowColor => IsDarkTheme ? DarkWindow : LightWindow;
        private Color ChromeColor => IsDarkTheme ? DarkChrome : LightChrome;
        private Color SurfaceColor => IsDarkTheme ? DarkSurface : LightSurface;
        private Color RaisedColor => IsDarkTheme ? DarkRaised : LightRaised;
        private Color BorderColor => IsDarkTheme ? DarkBorder : LightBorder;
        private Color TextColor => IsDarkTheme ? DarkText : LightText;
        private Color MutedTextColor => IsDarkTheme ? DarkMuted : LightMuted;

        public Form1(bool torWindow = false)
        {
            isTorWindow = torWindow;

            // Kick off WebView2 environment init immediately so it's warm by the time
            // the first tab calls InitializeAsync — eliminates first-navigation stall.
            if (!torWindow)
                BrowserRuntime.Warmup();
            InitializeComponent();
            ReplaceTabHost();

            settingsStore = new SettingsStore(appDataFolder);
            bookmarkStore = new BookmarkStore(appDataFolder);
            sessionStore = new SessionStore(appDataFolder);
            downloadManager = new DownloadManager();
            settings = settingsStore.Load();
            darkTheme = settings.DarkTheme;
            shieldsEnabled = isTorWindow || settings.ShieldsEnabled;
            PrivacyPolicy.SetShieldsEnabled(shieldsEnabled);
            if (!string.IsNullOrWhiteSpace(settings.HomeUrl))
                homeUrl = settings.HomeUrl;

            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(920, 590);
            DoubleBuffered = true;
            KeyPreview = true;

            BuildShell();
            ApplyShellTheme();
            LoadBookmarks();
            UpdateWindowTitle();

            if (settings.RestoreSavedSession && sessionStore.Exists)
                _ = LoadSessionAsync();
            else
                CreateNewTab(homeUrl);

        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                ClearRuntimePrivateDataAsync().GetAwaiter().GetResult();
            }
            catch { }

            base.OnFormClosed(e);
        }

        private void ReplaceTabHost()
        {
            Controls.Remove(tabControl1);
            tabControl1.Dispose();

            tabControl1 = new PageHostTabControl
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = new Point(0, 0)
            };

            tabControl1.SelectedIndexChanged += (s, e) =>
            {
                MarkCurrentTabActive();
                SuspendInactiveTabs();
                UpdateAddressFromCurrentTab();
                UpdateNavigationButtons();
                UpdateBookmarkButton();
                RefreshTabStrip();
            };
        }

        private void BuildShell()
        {
            rootLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            BuildTabStrip();
            BuildToolbar();
            BuildFindPanel();

            // Hook hold-to-preview for Back/Forward buttons after controls are built
            SetupNavHoldPreviewTimers();


            rootLayout.Controls.Add(tabStripBar, 0, 0);
            rootLayout.Controls.Add(topBar, 0, 1);
            rootLayout.Controls.Add(findPanel, 0, 2);
            rootLayout.Controls.Add(tabControl1, 0, 3);
            Controls.Add(rootLayout);
        }

        private void BuildTabStrip()
        {
            tabStripBar = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 6, 10, 4)
            };
            tabStripBar.Paint += PaintBottomBorder;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));

            tabFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            newTabButton = CreateIconButton(IconKind.Plus, "New tab (Ctrl+T)");
            newTabButton.Margin = new Padding(3, 0, 9, 0);
            newTabButton.Click += (s, e) => CreateNewTab(homeUrl);

            tabSearchContainer = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 1),
                Padding = new Padding(28, 4, 9, 0)
            };
            tabSearchContainer.Paint += PaintTabSearch;

            tabSearch = new TextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9f),
                PlaceholderText = "Filter tabs (Ctrl+Shift+F)",
                Margin = Padding.Empty
            };
            tabSearch.TextChanged += (s, e) => ApplyTabFilter();
            tabSearchContainer.Controls.Add(tabSearch);

            layout.Controls.Add(tabFlow, 0, 0);
            layout.Controls.Add(newTabButton, 1, 0);
            layout.Controls.Add(tabSearchContainer, 2, 0);
            tabStripBar.Controls.Add(layout);
        }

        private void BuildToolbar()
        {
            topBar = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 8, 10, 8)
            };
            topBar.Paint += PaintBottomBorder;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 520));

            var navPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = Padding.Empty,
                Padding = new Padding(0, 1, 0, 0)
            };

            backButton = CreateIconButton(IconKind.Back, "Back (Alt+Left)");
            forwardButton = CreateIconButton(IconKind.Forward, "Forward (Alt+Right)");
            reloadButton = CreateIconButton(IconKind.Reload, "Reload (F5)");

            backButton.Click += (s, e) =>
            {
                if (_navHoldTriggered)
                    return;
                GoBack();
            };
            forwardButton.Click += (s, e) =>
            {
                if (_navHoldTriggered)
                    return;
                GoForward();
            };
            reloadButton.Click += (s, e) => ReloadOrStop();

            navPanel.Controls.AddRange(new Control[] { backButton, forwardButton, reloadButton });

            addressContainer = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 0, 12, 0),
                Padding = new Padding(34, 8, 42, 7)  // right=42 reserves space for inline star button
            };
            addressContainer.Paint += PaintAddressContainer;

            addressBar = new TextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 10.25f),
                PlaceholderText = "Search or enter address",
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.CustomSource,
                Margin = Padding.Empty
            };
            addressBar.KeyDown += AddressBar_KeyDown;
            addressBar.Enter += (s, e) => addressContainer.Invalidate();
            addressBar.Leave += (s, e) => addressContainer.Invalidate();
            addressContainer.Controls.Add(addressBar);

            var actionPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = Padding.Empty,
                Padding = new Padding(0, 1, 0, 0)
            };

            // themeButton is kept as a field for icon tracking but not added to the toolbar
            // (theme is toggled from the new-tab page instead)
            themeButton = CreateIconButton(IconKind.Moon, "Toggle dark mode");
            settingsButton    = CreateIconButton(IconKind.Settings,  "Settings");
            bookmarksButton   = CreateIconButton(IconKind.Bookmarks, "Bookmarks");
            downloadsButton   = CreateIconButton(IconKind.Download,  "Downloads");
            readerModeButton  = CreateIconButton(IconKind.Reader,    "Reader mode");
            pipButton         = CreateIconButton(IconKind.Pip,       "Picture in picture");
            saveSessionButton = CreateIconButton(IconKind.Save,      "Save session (temp folder)");
            loadSessionButton = CreateIconButton(IconKind.Open,      "Load session");
            shieldsButton     = CreateIconButton(IconKind.Shield,    "Privacy shields on");
            clearDataButton   = CreateIconButton(IconKind.Trash,     "Clear private data");

            readerModeButton.Click += async (s, e) =>
            {
                if (CurrentTab != null)
                    await CurrentTab.ApplyReaderModeAsync(isDark: darkTheme);
            };
            pipButton.Click += async (s, e) =>
            {
                if (CurrentTab != null)
                    await CurrentTab.EnterPictureInPictureAsync();
            };
            saveSessionButton.Click += async (s, e) => await SaveSessionAsync();
            loadSessionButton.Click += async (s, e) => await LoadSessionAsync();
            clearDataButton.Click += async (s, e) => await ClearAllPrivateDataAsync();
            bookmarksButton.Click += (s, e) => ShowBookmarksMenu();
            downloadsButton.Click += (s, e) => ShowDownloadsMenu();
            shieldsButton.Click += (s, e) => ShowPrivacyReportPanel();
            settingsButton.Click += (s, e) => ShowSettingsMenu();

            actionPanel.Controls.AddRange(new Control[]
            {
                settingsButton, bookmarksButton, downloadsButton,
                readerModeButton, pipButton, shieldsButton, clearDataButton,
                saveSessionButton, loadSessionButton
            });
            // themeButton is intentionally omitted — theme is toggled from the new-tab page

            // Star button lives inside addressContainer as a right-side overlay (like Chrome)
            bookmarkButton = new ChromeIconButton(IconKind.Star)
            {
                Size        = new Size(28, 28),
                Cursor      = Cursors.Hand,
                TabStop     = false,
                Visible     = false,   // hidden until a real page loads (not new-tab)
            };
            toolTip.SetToolTip(bookmarkButton, "Save bookmark");
            bookmarkButton.Click += (s, e) => ToggleBookmarkForCurrentPage();
            addressContainer.Controls.Add(bookmarkButton);
            addressContainer.Resize += (s, e) => RepositionInlineBookmarkButton();
            RepositionInlineBookmarkButton();

            layout.Controls.Add(navPanel, 0, 0);
            layout.Controls.Add(addressContainer, 1, 0);
            layout.Controls.Add(actionPanel, 2, 0);
            topBar.Controls.Add(layout);
        }

        private void BuildFindPanel()
        {
            findPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 5, 10, 5),
                Visible = false
            };
            findPanel.Paint += PaintBottomBorder;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 420,
                ColumnCount = 5,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));

            findBox = new TextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10f),
                PlaceholderText = "Find in page"
            };
            findBox.TextChanged += async (s, e) => await RunFindAsync(false);
            findBox.KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    await RunFindAsync(e.Shift);
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    HideFindPanel();
                }
            };

            var previous = CreateFindButton("Up");
            previous.Click += async (s, e) => await RunFindAsync(true);
            var next = CreateFindButton("Down");
            next.Click += async (s, e) => await RunFindAsync(false);
            findStatusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9f),
                AutoEllipsis = true
            };
            var close = CreateFindButton("X");
            close.Click += (s, e) => HideFindPanel();

            layout.Controls.Add(findBox, 0, 0);
            layout.Controls.Add(previous, 1, 0);
            layout.Controls.Add(next, 2, 0);
            layout.Controls.Add(findStatusLabel, 3, 0);
            layout.Controls.Add(close, 4, 0);
            findPanel.Controls.Add(layout);
        }

        private Button CreateFindButton(string text)
        {
            return new Button
            {
                Dock = DockStyle.Fill,
                Text = text,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f),
                Margin = new Padding(4, 0, 0, 0),
                TabStop = false
            };
        }

        private ChromeIconButton CreateIconButton(IconKind icon, string tooltip)
        {
            var button = new ChromeIconButton(icon)
            {
                Width = 34,
                Height = 34,
                Margin = new Padding(0, 0, 7, 0)
            };

            button.Click += (s, e) => ActiveControl = null;
            chromeButtons.Add(button);
            toolTip.SetToolTip(button, tooltip);
            return button;
        }

        private void ApplyShellTheme()
        {
            // Tor windows get a subtle purple tint on the chrome to make them visually distinct
            Color effectiveChrome = isTorWindow
                ? Color.FromArgb(38, 28, 54)   // dark purple
                : ChromeColor;
            Color effectiveWindow = isTorWindow
                ? Color.FromArgb(22, 16, 34)   // deeper purple
                : WindowColor;

            BackColor = effectiveWindow;
            rootLayout.BackColor = effectiveWindow;
            tabControl1.BackColor = effectiveWindow;
            tabStripBar.BackColor = effectiveChrome;
            topBar.BackColor = effectiveChrome;
            findPanel.BackColor = effectiveChrome;
            tabFlow.BackColor = effectiveChrome;

            addressContainer.BackColor = Color.Transparent;
            addressBar.BackColor = RaisedColor;
            addressBar.ForeColor = TextColor;

            tabSearchContainer.BackColor = Color.Transparent;
            tabSearch.BackColor = SurfaceColor;
            tabSearch.ForeColor = TextColor;
            findBox.BackColor = RaisedColor;
            findBox.ForeColor = TextColor;
            findStatusLabel.ForeColor = MutedTextColor;

            themeButton.Icon = darkTheme ? IconKind.Moon : IconKind.Sun; // tracks state, not displayed
            bookmarkButton?.Invalidate(); // repaint inline star with updated theme colours
            UpdateShieldsButton();
            UpdateBookmarkButton();

            foreach (var button in chromeButtons)
            {
                button.Theme = CreateButtonTheme();
                button.Invalidate();
            }

            UpdateNavigationButtons();
            RefreshTabStrip();
            Invalidate(true);
        }

        private ButtonTheme CreateButtonTheme()
        {
            return _cachedButtonTheme ??= new ButtonTheme(
                SurfaceColor,
                RaisedColor,
                BorderColor,
                TextColor,
                MutedTextColor,
                Accent,
                AccentGreen);
        }

        private TabTheme CreateTabTheme()
        {
            return _cachedTabTheme ??= new TabTheme(
                ChromeColor,
                SurfaceColor,
                RaisedColor,
                BorderColor,
                TextColor,
                MutedTextColor,
                Accent,
                AccentGreen);
        }

        private void InvalidateThemeCache()
        {
            _cachedTabTheme = null;
            _cachedButtonTheme = null;
        }

        private void PaintBottomBorder(object? sender, PaintEventArgs e)
        {
            if (sender is not Control control)
                return;

            using var border = new Pen(BorderColor);
            e.Graphics.DrawLine(border, 0, control.Height - 1, control.Width, control.Height - 1);
        }

        private void RepositionInlineBookmarkButton()
        {
            if (bookmarkButton == null || addressContainer == null)
                return;
            int h = addressContainer.ClientSize.Height;
            int btnH = bookmarkButton.Height;
            bookmarkButton.Location = new Point(
                addressContainer.ClientSize.Width - bookmarkButton.Width - 6,
                (h - btnH) / 2);
        }

        private void PaintAddressContainer(object? sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = addressContainer.ClientRectangle;
            rect.Inflate(-1, -1);

            using var path = RoundedRect(rect, 10);
            using var fill = new SolidBrush(RaisedColor);
            using var border = new Pen(addressBar.Focused ? Accent : BorderColor, addressBar.Focused ? 1.8f : 1f);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);

            var securityColor = GetCurrentSecurityColor();
            DrawLockIcon(e.Graphics, new Rectangle(14, 13, 13, 13), addressBar.Focused ? Accent : securityColor);
        }

        private Color GetCurrentSecurityColor()
        {
            var url = CurrentTab?.GetCurrentUrl();
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                if (uri.Scheme == Uri.UriSchemeHttps)
                    return AccentGreen;
                if (uri.Scheme == Uri.UriSchemeHttp)
                    return Color.FromArgb(215, 80, 72);
            }

            return MutedTextColor;
        }

        private void PaintTabSearch(object? sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = tabSearchContainer.ClientRectangle;
            rect.Inflate(-1, -2);

            using var path = RoundedRect(rect, 9);
            using var fill = new SolidBrush(SurfaceColor);
            using var border = new Pen(BorderColor);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);

            using var pen = new Pen(MutedTextColor, 1.7f);
            e.Graphics.DrawEllipse(pen, 10, 12, 8, 8);
            e.Graphics.DrawLine(pen, 17, 19, 22, 24);
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void DrawLockIcon(Graphics graphics, Rectangle rect, Color color)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(color, 1.7f);
            using var brush = new SolidBrush(Color.FromArgb(20, color));
            var body = new Rectangle(rect.X + 1, rect.Y + 6, rect.Width - 2, rect.Height - 6);
            graphics.FillRectangle(brush, body);
            graphics.DrawRectangle(pen, body);
            graphics.DrawArc(pen, rect.X + 3, rect.Y, rect.Width - 6, rect.Height - 2, 190, 160);
        }

        private Tab? CurrentTab =>
            tabControl1.SelectedTab?.Tag as Tab;

        private async void CreateNewTab(string url, TabMetadata? initialMetadata = null)
        {
            var page = new TabPage("New Tab")
            {
                BackColor = WindowColor,
                Padding = Padding.Empty
            };
            var tab = new Tab();

            page.Controls.Add(tab);
            page.Tag = tab;
            allPages.Add(page);
            tabMetadata[page] = initialMetadata ?? new TabMetadata();

            // Eager suspension: once we have more than 5 tabs, immediately suspend
            // the least-recently-used unpinned background tabs to free renderer memory.
            const int EagerSuspendThreshold = 5;
            if (allPages.Count > EagerSuspendThreshold)
            {
                var candidates = allPages
                    .Where(p => p != page && p != tabControl1.SelectedTab && p.Tag is Tab t && !t.IsSuspended)
                    .OrderBy(p => tabMetadata.TryGetValue(p, out var m) ? m.LastActiveUtc : DateTime.MaxValue)
                    .Take(allPages.Count - EagerSuspendThreshold);

                foreach (var old in candidates)
                {
                    if (old.Tag is Tab oldTab && tabMetadata.TryGetValue(old, out var oldMeta) && !oldMeta.IsPinned)
                    {
                        oldTab.Suspend();
                        oldMeta.IsSuspended = true;
                    }
                }
            }

            tab.DownloadRequested += OnDownloadRequested;
            tab.DownloadStarted += OnDownloadStarted;
            tab.PrivacyStatsChanged += (s, e) => UpdateShieldsButton();
            ApplyTabFilter(selectPage: page);

            if (isTorWindow)
                await tab.InitializeTorAsync(url);
            else
                await tab.InitializeAsync(url, GetEffectiveShieldsForUrl(url));
            if (tabMetadata.TryGetValue(page, out var initial) && initial.IsMuted)
                await tab.SetMutedAsync(true);

            tab.WebView.CoreWebView2.NavigationStarting += (s, e) =>
            {
                if (IsDisposed)
                    return;

                if (TryHandleAppCommand(e.Uri))
                {
                    e.Cancel = true;
                    return;
                }

                bool effectiveShields = GetEffectiveShieldsForUrl(e.Uri);
                tab.SetShieldsForNavigation(effectiveShields);
                _ = tab.ApplyShieldsAsync(effectiveShields);

                BeginInvoke(new Action(() =>
                {
                    pageLoading = true;
                    page.Text = "Loading...";
                    addressBar.Text = e.Uri ?? "";
                    UpdateNavigationButtons();
                    RefreshTabStrip();
                }));
            };

            tab.WebView.CoreWebView2.NavigationCompleted += (s, e) =>
            {
                if (IsDisposed)
                    return;

                BeginInvoke(new Action(async () =>
                {
                    pageLoading = false;
                    if (!e.IsSuccess && ShouldShowLocalError(e.WebErrorStatus))
                    {
                        page.Text = "Page not found";
                        // Guard: only show error page if CoreWebView2 is still alive
                        if (tab.WebView.CoreWebView2 != null && !tab.WebView.IsDisposed)
                            tab.ShowNavigationError(e.WebErrorStatus, homeUrl);
                        // Single combined UI refresh on error path
                        UpdateAddressFromCurrentTab();
                        UpdateNavigationButtons();
                        RefreshTabStrip();
                        return;
                    }

                    page.Text = string.IsNullOrWhiteSpace(tab.WebView.CoreWebView2.DocumentTitle)
                        ? "New Tab"
                        : tab.WebView.CoreWebView2.DocumentTitle;
                    if (tabMetadata.TryGetValue(page, out var meta))
                    {
                        meta.LastActiveUtc = DateTime.UtcNow;
                        meta.IsSuspended = tab.IsSuspended;
                    }

                    // Single combined UI refresh on success path (was called 3+ times separately)
                    UpdateAddressFromCurrentTab();
                    UpdateNavigationButtons();
                    RefreshTabStrip();
                    UpdateBookmarkButton();

                    // These are async script injections — run in parallel to avoid sequential waits
                    await Task.WhenAll(
                        InjectNewTabDataAsync(tab),
                        tab.ApplyDarkModeAsync(darkTheme));
                }));
            };

            tab.WebView.CoreWebView2.HistoryChanged += (s, e) =>
            {
                if (!IsDisposed)
                    BeginInvoke(new Action(UpdateNavigationButtons));
            };

            tab.WebView.CoreWebView2.ContainsFullScreenElementChanged += (s, e) =>
            {
                if (IsDisposed)
                    return;

                BeginInvoke(new Action(() =>
                {
                    if (tab.WebView.CoreWebView2.ContainsFullScreenElement)
                    {
                        fullscreenTab = tab;
                        SetTrueFullscreen(true);
                    }
                    else if (fullscreenTab == tab)
                    {
                        fullscreenTab = null;
                        SetTrueFullscreen(false);
                    }
                }));
            };

            tab.WebView.KeyDown += WebView_KeyDown;
            tab.WebView.KeyUp += WebView_KeyUp;

            UpdateAddressFromCurrentTab();
            UpdateNavigationButtons();
        }

        private static bool ShouldShowLocalError(CoreWebView2WebErrorStatus status)
        {
            return status is CoreWebView2WebErrorStatus.HostNameNotResolved
                or CoreWebView2WebErrorStatus.CannotConnect
                or CoreWebView2WebErrorStatus.ServerUnreachable
                or CoreWebView2WebErrorStatus.Timeout
                or CoreWebView2WebErrorStatus.ConnectionAborted
                or CoreWebView2WebErrorStatus.ConnectionReset
                or CoreWebView2WebErrorStatus.OperationCanceled
                or CoreWebView2WebErrorStatus.Disconnected;
        }

        private bool TryHandleAppCommand(string? uri)
        {
            if (string.IsNullOrWhiteSpace(uri) ||
                !uri.StartsWith("mybrowsershell://", StringComparison.OrdinalIgnoreCase))
                return false;

            BeginInvoke(new Action(() =>
            {
                if (uri.Contains("toggle-theme", StringComparison.OrdinalIgnoreCase))
                {
                    darkTheme = !darkTheme;
                    settings.DarkTheme = darkTheme;
                    settingsStore.Save(settings);
                    InvalidateThemeCache();
                    ApplyShellTheme();
                    _ = ToggleDarkModeAsync(darkTheme);
                    // Push updated data directly to the new-tab page without a reload flash.
                    // The navigation to mybrowsershell:// has already been cancelled by WebView2
                    // (unknown scheme), so the page is still visible — just re-inject the payload.
                    _ = InjectNewTabDataAsync(CurrentTab);
                }
                else if (uri.Contains("downloads", StringComparison.OrdinalIgnoreCase))
                    ShowDownloadsMenu();
                else if (uri.Contains("settings", StringComparison.OrdinalIgnoreCase))
                    ShowSettingsMenu();
                else if (uri.Contains("bookmarks", StringComparison.OrdinalIgnoreCase))
                    ShowBookmarksMenu();
            }));
            return true;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (TryHandleBrowserShortcut(keyData))
                return true;

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void WebView_KeyDown(object? sender, KeyEventArgs e)
        {
            if (!pressedWebViewShortcuts.Add(e.KeyData))
                return;

            if (!MatchesBrowserShortcut(e.KeyData))
                return;

            e.Handled = true;
            e.SuppressKeyPress = true;
            var keyData = e.KeyData;
            BeginInvoke(() => ExecuteBrowserShortcut(keyData));
        }

        private void WebView_KeyUp(object? sender, KeyEventArgs e)
        {
            pressedWebViewShortcuts.Remove(e.KeyData);
        }

        private bool TryHandleBrowserShortcut(Keys keyData)
        {
            if (!MatchesBrowserShortcut(keyData))
                return false;

            ExecuteBrowserShortcut(keyData);
            return true;
        }

        private bool MatchesBrowserShortcut(Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            bool ctrl = keyData.HasFlag(Keys.Control);
            bool shift = keyData.HasFlag(Keys.Shift);
            bool alt = keyData.HasFlag(Keys.Alt);

            if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LMenu or Keys.RMenu)
                return false;

            if (ctrl && !shift && !alt && key == Keys.T)
                return true;

            if (ctrl && !shift && !alt && key == Keys.W)
                return tabControl1.SelectedTab != null;

            if (ctrl && shift && !alt && key == Keys.T)
                return closedTabUrls.Count > 0;

            if (ctrl && !alt && key == Keys.Tab)
                return tabControl1.TabPages.Count > 1;

            if (ctrl && !shift && !alt && key is >= Keys.D1 and <= Keys.D9)
                return tabControl1.TabPages.Count > 0;

            if (alt && !ctrl && !shift && key == Keys.Left)
                return CurrentTab?.WebView.CoreWebView2?.CanGoBack == true;

            if (alt && !ctrl && !shift && key == Keys.Right)
                return CurrentTab?.WebView.CoreWebView2?.CanGoForward == true;

            if ((!ctrl && !shift && !alt && key == Keys.F5) ||
                (ctrl && !shift && !alt && key == Keys.R))
                return CurrentTab?.WebView.CoreWebView2 != null;

            if ((ctrl && !shift && !alt && key == Keys.L) ||
                (alt && !ctrl && !shift && key == Keys.D) ||
                (!ctrl && !shift && !alt && key == Keys.F6))
                return true;

            if (ctrl && shift && !alt && key == Keys.F)
                return true;

            if (ctrl && !shift && !alt && key == Keys.F)
                return CurrentTab != null;

            if (!ctrl && !shift && !alt && key == Keys.Escape &&
                (addressBar.Focused || tabSearch.Focused || findBox.Focused || findPanel.Visible))
                return CurrentTab != null;

            if (!ctrl && !shift && !alt && key == Keys.F11)
                return true;

            // Zoom: Ctrl++, Ctrl+-, Ctrl+0
            if (ctrl && !shift && !alt && key is Keys.Oemplus or Keys.OemMinus or Keys.D0)
                return CurrentTab?.WebView.CoreWebView2 != null;

            return false;
        }

        private void ExecuteBrowserShortcut(Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            bool ctrl = keyData.HasFlag(Keys.Control);
            bool shift = keyData.HasFlag(Keys.Shift);
            bool alt = keyData.HasFlag(Keys.Alt);

            if (ctrl && !shift && !alt && key == Keys.T)
            {
                CreateNewTab(homeUrl);
                return;
            }

            if (ctrl && !shift && !alt && key == Keys.W)
            {
                if (tabControl1.SelectedTab != null)
                    CloseTab(tabControl1.SelectedTab);
                return;
            }

            if (ctrl && shift && !alt && key == Keys.T)
            {
                ReopenClosedTab();
                return;
            }

            if (ctrl && !alt && key == Keys.Tab)
            {
                CycleTab(shift ? -1 : 1);
                return;
            }

            if (ctrl && !shift && !alt && key is >= Keys.D1 and <= Keys.D9)
            {
                int index = key == Keys.D9
                    ? tabControl1.TabPages.Count - 1
                    : key - Keys.D1;
                SelectTabByIndex(index);
                return;
            }

            if (alt && !ctrl && !shift && key == Keys.Left)
            {
                GoBack();
                return;
            }

            if (alt && !ctrl && !shift && key == Keys.Right)
            {
                GoForward();
                return;
            }

            if ((!ctrl && !shift && !alt && key == Keys.F5) ||
                (ctrl && !shift && !alt && key == Keys.R))
            {
                Reload();
                return;
            }

            if ((ctrl && !shift && !alt && key == Keys.L) ||
                (alt && !ctrl && !shift && key == Keys.D) ||
                (!ctrl && !shift && !alt && key == Keys.F6))
            {
                FocusAddressBar();
                return;
            }

            if (ctrl && shift && !alt && key == Keys.F)
            {
                FocusTabSearch();
                return;
            }

            if (ctrl && !shift && !alt && key == Keys.F)
            {
                ShowFindPanel();
                return;
            }

            if (!ctrl && !shift && !alt && key == Keys.Escape &&
                (addressBar.Focused || tabSearch.Focused || findBox.Focused || findPanel.Visible))
            {
                if (findPanel.Visible)
                    HideFindPanel();
                else
                    CurrentTab?.WebView.Focus();
                return;
            }

            if (!ctrl && !shift && !alt && key == Keys.F11)
            {
                if (fullscreenTab != null)
                    return;

                SetTrueFullscreen(!isTrueFullscreen);
                return;
            }

            // Zoom
            if (ctrl && !shift && !alt && key == Keys.Oemplus)  { AdjustZoom(+0.1); return; }
            if (ctrl && !shift && !alt && key == Keys.OemMinus) { AdjustZoom(-0.1); return; }
            if (ctrl && !shift && !alt && key == Keys.D0)       { ResetZoom();      return; }
        }

        private void AdjustZoom(double delta)
        {
            var wv = CurrentTab?.WebView;
            if (wv?.CoreWebView2 == null) return;
            double next = Math.Round(Math.Clamp(wv.ZoomFactor + delta, 0.25, 5.0), 2);
            wv.ZoomFactor = next;
            UpdateWindowTitle();
        }

        private void ResetZoom()
        {
            var wv = CurrentTab?.WebView;
            if (wv?.CoreWebView2 == null) return;
            wv.ZoomFactor = 1.0;
            UpdateWindowTitle();
        }

        private void SetTrueFullscreen(bool enabled)
        {
            if (enabled == isTrueFullscreen)
                return;

            if (enabled)
            {
                restoreFormBorderStyle = FormBorderStyle;
                restoreWindowState = WindowState;
                restoreBounds = Bounds;
                restoreTopMost = TopMost;
                restoreFindPanelVisible = findPanel.Visible;
                restoreTabStripHeight = rootLayout.RowStyles[0].Height;
                restoreToolbarHeight = rootLayout.RowStyles[1].Height;
                restoreFindPanelHeight = rootLayout.RowStyles[2].Height;

                isTrueFullscreen = true;
                SuspendLayout();
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Normal;
                Bounds = Screen.FromControl(this).Bounds;
                TopMost = true;
                tabStripBar.Visible = false;
                topBar.Visible = false;
                findPanel.Visible = false;
                rootLayout.RowStyles[0].Height = 0;
                rootLayout.RowStyles[1].Height = 0;
                rootLayout.RowStyles[2].Height = 0;
                ResumeLayout(true);
                CurrentTab?.WebView.Focus();
                return;
            }

            isTrueFullscreen = false;
            SuspendLayout();
            TopMost = restoreTopMost;
            WindowState = FormWindowState.Normal;
            Bounds = restoreBounds;
            FormBorderStyle = restoreFormBorderStyle;
            WindowState = restoreWindowState;
            tabStripBar.Visible = true;
            topBar.Visible = true;
            findPanel.Visible = restoreFindPanelVisible;
            rootLayout.RowStyles[0].Height = restoreTabStripHeight;
            rootLayout.RowStyles[1].Height = restoreToolbarHeight;
            rootLayout.RowStyles[2].Height = restoreFindPanelVisible ? restoreFindPanelHeight : 0;
            ResumeLayout(true);
            CurrentTab?.WebView.Focus();
        }

        private void GoBack()
        {
            if (CurrentTab?.WebView.CoreWebView2?.CanGoBack == true)
                CurrentTab.WebView.CoreWebView2.GoBack();
        }

        private void GoForward()
        {
            if (CurrentTab?.WebView.CoreWebView2?.CanGoForward == true)
                CurrentTab.WebView.CoreWebView2.GoForward();
        }

        private void SetupNavHoldPreviewTimers()
        {
            if (_backHoldTimer == null)
            {
                _backHoldTimer = new Timer { Interval = NavHoldPreviewDelayMs };
                _backHoldTimer.Tick += (_, __) =>
                {
                    _backHoldTimer?.Stop();
                    _navHoldTriggered = true;
                    ShowNavHistoryMenu(isBack: true);
                };
            }

            if (_forwardHoldTimer == null)
            {
                _forwardHoldTimer = new Timer { Interval = NavHoldPreviewDelayMs };
                _forwardHoldTimer.Tick += (_, __) =>
                {
                    _forwardHoldTimer?.Stop();
                    _navHoldTriggered = true;
                    ShowNavHistoryMenu(isBack: false);
                };
            }

            backButton.MouseDown -= BackButton_MouseDown;
            backButton.MouseUp -= BackButton_MouseUp;
            backButton.MouseLeave -= BackButton_MouseLeave;

            forwardButton.MouseDown -= ForwardButton_MouseDown;
            forwardButton.MouseUp -= ForwardButton_MouseUp;
            forwardButton.MouseLeave -= ForwardButton_MouseLeave;

            backButton.MouseDown += BackButton_MouseDown;
            backButton.MouseUp += BackButton_MouseUp;
            backButton.MouseLeave += BackButton_MouseLeave;

            forwardButton.MouseDown += ForwardButton_MouseDown;
            forwardButton.MouseUp += ForwardButton_MouseUp;
            forwardButton.MouseLeave += ForwardButton_MouseLeave;
        }

        private void BackButton_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;
            _navHoldTriggered = false;
            _backHoldTimer?.Stop();
            _backHoldTimer?.Start();
        }

        private void BackButton_MouseUp(object? sender, MouseEventArgs e)
        {
            _backHoldTimer?.Stop();
        }

        private void BackButton_MouseLeave(object? sender, EventArgs e)
        {
            _backHoldTimer?.Stop();
        }

        private void ForwardButton_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;
            _navHoldTriggered = false;
            _forwardHoldTimer?.Stop();
            _forwardHoldTimer?.Start();
        }

        private void ForwardButton_MouseUp(object? sender, MouseEventArgs e)
        {
            _forwardHoldTimer?.Stop();
        }

        private void ForwardButton_MouseLeave(object? sender, EventArgs e)
        {
            _forwardHoldTimer?.Stop();
        }

        private void ShowNavHistoryMenu(bool isBack)
        {
            var core = CurrentTab?.WebView.CoreWebView2;
            if (core == null)
                return;

            if (isBack && !core.CanGoBack) return;
            if (!isBack && !core.CanGoForward) return;

            // Dispose old menu (and rebuild each time for freshest history)
            if (_navHistoryMenu != null)
            {
                try { _navHistoryMenu.Close(); } catch { }
                _navHistoryMenu.Dispose();
                _navHistoryMenu = null;
            }

            _navHistoryMenu = new ContextMenuStrip();

            // Attempt to use CoreWebView2.NavigationHistory APIs via reflection (keeps us resilient).
            try
            {
                var navHistoryObj = core.GetType().GetMethod("GetNavigationHistory")?.Invoke(core, null);
                if (navHistoryObj == null)
                    throw new MissingMethodException("GetNavigationHistory not available");

                int currentIndex = (int?)navHistoryObj.GetType().GetProperty("CurrentIndex")?.GetValue(navHistoryObj) ?? 0;
                var entriesObj = navHistoryObj.GetType().GetProperty("Entries")?.GetValue(navHistoryObj);
                if (entriesObj == null)
                    throw new MissingMemberException("Navigation history entries not found");

                var entries = (System.Collections.IEnumerable)entriesObj;
                var list = new System.Collections.Generic.List<object>();
                foreach (var it in entries) list.Add(it);

                if (list.Count == 0)
                    throw new InvalidOperationException("No navigation history entries");

                int start = isBack ? Math.Max(0, currentIndex - 10) : currentIndex + 1;
                int endExclusive = isBack ? currentIndex : Math.Min(list.Count, currentIndex + 11);

                if (isBack)
                    for (int i = currentIndex - 1; i >= start; i--)
                        AddHistoryItemToMenu(core, navHistoryObj, _navHistoryMenu, list[i], delta: i - currentIndex);
                else
                    for (int i = currentIndex + 1; i < endExclusive; i++)
                        AddHistoryItemToMenu(core, navHistoryObj, _navHistoryMenu, list[i], delta: i - currentIndex);

                if (_navHistoryMenu.Items.Count == 0)
                    _navHistoryMenu.Items.Add(new ToolStripMenuItem("No earlier pages") { Enabled = false });
            }
            catch
            {
                // Fallback: show minimal menu using GoBack/GoForward only.
                {
                    var item = new ToolStripMenuItem(isBack ? "Back" : "Forward")
                    {
                        Enabled = true
                    };
                    item.Click += (s, e) =>
                    {
                        if (isBack) GoBack(); else GoForward();
                    };
                    _navHistoryMenu.Items.Add(item);
                }
            }

            // Position near the pressed button
            var origin = isBack ? backButton : forwardButton;
            var screen = origin.PointToScreen(new Point(0, origin.Height));
            _navHistoryMenu.Show(screen);
        }

        private void AddHistoryItemToMenu(CoreWebView2 core, object navHistoryObj, ContextMenuStrip menu, object entry, int delta)
        {
            // entry has Url and/or DisplayName depending on WebView2 version.
            string? url = entry.GetType().GetProperty("Url")?.GetValue(entry)?.ToString();
            string? title = entry.GetType().GetProperty("Title")?.GetValue(entry)?.ToString();
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(url))
                return;

            string display = !string.IsNullOrWhiteSpace(title)
                ? title
                : url;

            if (string.IsNullOrWhiteSpace(display))
                return;

            display = TrimMenuText(display, 70);

            var item = new ToolStripMenuItem(display) { ToolTipText = url };
            item.Click += (_, __) =>
            {
                try
                {
                    // Prefer jump-style navigation if available
                    var jumpMethod = core.GetType().GetMethod("GoBackOrForward", new[] { typeof(int) });
                    if (jumpMethod != null)
                    {
                        jumpMethod.Invoke(core, new object[] { delta });
                        return;
                    }
                }
                catch { }

                // Fallback: sequential back/forward to approximate jump.
                if (delta < 0)
                {
                    int steps = Math.Abs(delta);
                    for (int i = 0; i < steps; i++)
                    {
                        if (core.CanGoBack)
                            core.GoBack();
                        else
                            break;
                    }
                }
                else if (delta > 0)
                {
                    int steps = delta;
                    for (int i = 0; i < steps; i++)
                    {
                        if (core.CanGoForward)
                            core.GoForward();
                        else
                            break;
                    }
                }
            };

            menu.Items.Add(item);
        }


        private void Reload()
        {
            CurrentTab?.WebView.CoreWebView2?.Reload();
        }

        private void ReloadOrStop()
        {
            var core = CurrentTab?.WebView.CoreWebView2;
            if (core == null)
                return;

            if (pageLoading)
                core.Stop();
            else
                core.Reload();
        }

        private void FocusAddressBar()
        {
            addressBar.Focus();
            addressBar.SelectAll();
        }

        private void FocusTabSearch()
        {
            tabSearch.Focus();
            tabSearch.SelectAll();
        }

        private void ShowFindPanel()
        {
            findPanel.Visible = true;
            rootLayout.RowStyles[2].Height = 42;
            findBox.Focus();
            findBox.SelectAll();
        }

        private void HideFindPanel()
        {
            findPanel.Visible = false;
            rootLayout.RowStyles[2].Height = 0;
            findStatusLabel.Text = "";
            CurrentTab?.WebView.Focus();
        }

        private async Task RunFindAsync(bool reverse)
        {
            if (!findPanel.Visible || CurrentTab == null)
                return;

            string query = findBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                findStatusLabel.Text = "";
                return;
            }

            int count = await CurrentTab.FindOnPageAsync(query, reverse);
            findStatusLabel.Text = count == 1 ? "1 match" : count + " matches";
        }

        private void CycleTab(int direction)
        {
            int count = tabControl1.TabPages.Count;
            if (count <= 1)
                return;

            int next = (tabControl1.SelectedIndex + direction + count) % count;
            tabControl1.SelectedIndex = next;
        }

        private void SelectTabByIndex(int index)
        {
            if (index < 0 || index >= tabControl1.TabPages.Count)
                return;

            tabControl1.SelectedIndex = index;
        }

        private void ReopenClosedTab()
        {
            if (closedTabUrls.Count == 0)
                return;

            CreateNewTab(closedTabUrls.Pop());
        }

        private void AddressBar_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
                return;

            e.SuppressKeyPress = true;
            CurrentTab?.Navigate(ResolveAddressInput(addressBar.Text));
        }

       private string ResolveAddressInput(string input)
    {
        input = input.Trim();

        if (input.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
            return input;

        if (string.IsNullOrWhiteSpace(input))
         return homeUrl;

        if (IsWebAddress(input))
            return NormalizeWebAddress(input);

        return settings.SearchUrl + Uri.EscapeDataString(input);
        }


        private static bool IsWebAddress(string input)
        {
            if (Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return true;
            }

            bool hasWhitespace = input.Any(char.IsWhiteSpace);

            return !hasWhitespace &&
                   (input.Contains('.') ||
                    input.Contains(':') ||
                    input.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                    input.StartsWith("localhost:", StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeWebAddress(string input)
        {
            if (!input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                input = "https://" + input;
            }

            if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                input = "https://" + input.Substring(7);

            return input;
        }

        private void ApplyTabFilter(TabPage? selectPage = null)
        {
            string query = tabSearch?.Text.Trim() ?? "";
            var selected = selectPage ?? tabControl1.SelectedTab;
            var visiblePages = string.IsNullOrWhiteSpace(query)
                ? allPages
                : allPages.Where(p => p.Text.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            visiblePages = visiblePages
                .OrderByDescending(p => tabMetadata.TryGetValue(p, out var meta) && meta.IsPinned)
                .ToList();

            tabControl1.SuspendLayout();
            tabControl1.TabPages.Clear();

            foreach (var page in visiblePages)
                tabControl1.TabPages.Add(page);

            if (selectPage != null && tabControl1.TabPages.Contains(selectPage))
                tabControl1.SelectedTab = selectPage;
            else if (selected != null && tabControl1.TabPages.Contains(selected))
                tabControl1.SelectedTab = selected;
            else if (tabControl1.TabPages.Count > 0)
                tabControl1.SelectedIndex = 0;

            tabControl1.ResumeLayout();
            UpdateAddressFromCurrentTab();
            UpdateNavigationButtons();
            RefreshTabStrip();
        }

        private void RefreshTabStrip()
        {
            if (tabFlow == null)
                return;

            string query = tabSearch?.Text.Trim() ?? "";
            var visiblePages = string.IsNullOrWhiteSpace(query)
                ? allPages
                : allPages.Where(p => p.Text.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            visiblePages = visiblePages
                .OrderByDescending(p => tabMetadata.TryGetValue(p, out var meta) && meta.IsPinned)
                .ToList();

            tabFlow.SuspendLayout();

            // ── Update-in-place: avoid destroying/recreating chips on every call ──
            // Remove chips whose pages are no longer visible
            var existingChips = tabFlow.Controls.OfType<TabChip>().ToList();
            foreach (var chip in existingChips)
            {
                if (!visiblePages.Contains(chip.Page))
                {
                    tabFlow.Controls.Remove(chip);
                    chip.Dispose();
                }
            }

            // Build lookup of currently alive chips
            var chipMap = tabFlow.Controls.OfType<TabChip>().ToDictionary(c => c.Page);

            for (int idx = 0; idx < visiblePages.Count; idx++)
            {
                var page = visiblePages[idx];
                tabMetadata.TryGetValue(page, out var meta);
                bool isPinned   = meta?.IsPinned   == true;
                bool isMuted    = meta?.IsMuted     == true;
                bool isSuspended = meta?.IsSuspended == true;
                bool isActive   = page == tabControl1.SelectedTab;

                int desiredWidth = isPinned
                    ? 96
                    : Math.Min(218, Math.Max(142, TextRenderer.MeasureText(page.Text, Font).Width + 54));

                if (chipMap.TryGetValue(page, out var existing))
                {
                    // Chip already exists — just update its properties
                    existing.Active    = isActive;
                    existing.Pinned    = isPinned;
                    existing.Muted     = isMuted;
                    existing.Suspended = isSuspended;
                    existing.Theme     = CreateTabTheme();
                    existing.Width     = desiredWidth;
                    existing.Invalidate(); // repaint only if changed

                    // Reorder if necessary
                    if (tabFlow.Controls.IndexOf(existing) != idx)
                        tabFlow.Controls.SetChildIndex(existing, idx);
                }
                else
                {
                    // New chip needed
                    var chip = new TabChip(page)
                    {
                        Active    = isActive,
                        Pinned    = isPinned,
                        Muted     = isMuted,
                        Suspended = isSuspended,
                        Theme     = CreateTabTheme(),
                        Width     = desiredWidth,
                        Height    = 31,
                        Margin    = new Padding(0, 0, 7, 0),
                    };
                    chip.Selected        += (s, e) => SelectPage(page);
                    chip.CloseRequested  += (s, e) => CloseTab(page);
                    chip.MouseDown       += (s, e) =>
                    {
                        if (e.Button == MouseButtons.Left)
                            draggingTabPage = page;
                    };
                    chip.MouseUp += (s, e) =>
                    {
                        if (e.Button == MouseButtons.Left && draggingTabPage != null && draggingTabPage != page)
                            MoveTab(draggingTabPage, page);
                        draggingTabPage = null;
                    };
                    // Rebuild on each open so Pin/Mute labels stay current
                    chip.MouseDown += (s, e) =>
                    {
                        if (e.Button == MouseButtons.Right)
                            chip.ContextMenuStrip = CreateTabContextMenu(page);
                    };
                    tabFlow.Controls.Add(chip);
                    tabFlow.Controls.SetChildIndex(chip, idx);
                }
            }

            tabFlow.ResumeLayout();
        }

        private void SelectPage(TabPage page)
        {
            if (!tabControl1.TabPages.Contains(page))
            {
                tabSearch.Clear();
                ApplyTabFilter(selectPage: page);
                return;
            }

            tabControl1.SelectedTab = page;
            MarkCurrentTabActive();
            SuspendInactiveTabs();
            UpdateAddressFromCurrentTab();
            UpdateNavigationButtons();
            RefreshTabStrip();
        }

        private void MarkCurrentTabActive()
        {
            if (tabControl1.SelectedTab is not TabPage page)
                return;

            if (!tabMetadata.TryGetValue(page, out var meta))
                tabMetadata[page] = meta = new TabMetadata();

            meta.LastActiveUtc = DateTime.UtcNow;
            meta.IsSuspended = false;
            (page.Tag as Tab)?.Resume();
        }

        private void SuspendInactiveTabs()
        {
            var now = DateTime.UtcNow;
            foreach (var page in allPages)
            {
                if (page == tabControl1.SelectedTab || page.Tag is not Tab tab)
                    continue;

                if (!tabMetadata.TryGetValue(page, out var meta))
                    tabMetadata[page] = meta = new TabMetadata();

                if (!meta.IsPinned && !meta.IsSuspended && now - meta.LastActiveUtc > TimeSpan.FromMinutes(5))
                {
                    tab.Suspend();
                    meta.IsSuspended = true;
                }
            }
        }

        private void MoveTab(TabPage source, TabPage target)
        {
            int sourceIndex = allPages.IndexOf(source);
            int targetIndex = allPages.IndexOf(target);
            if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
                return;

            allPages.RemoveAt(sourceIndex);
            allPages.Insert(targetIndex, source);
            ApplyTabFilter(selectPage: source);
        }

        private ContextMenuStrip CreateTabContextMenu(TabPage page)
        {
            tabMetadata.TryGetValue(page, out var meta);
            bool isPinned = meta?.IsPinned == true;
            bool isMuted = meta?.IsMuted == true;

            var menu = new ContextMenuStrip();
            menu.Items.Add(isPinned ? "Unpin tab" : "Pin tab", null, (s, e) => TogglePinned(page));
            menu.Items.Add(isMuted ? "Unmute tab" : "Mute tab", null, async (s, e) => await ToggleMutedAsync(page));
            menu.Items.Add("Duplicate tab", null, (s, e) => DuplicateTab(page));
            menu.Items.Add("Close other tabs", null, (s, e) => CloseOtherTabs(page));
            menu.Items.Add("Close tabs to the right", null, (s, e) => CloseTabsToRight(page));
            menu.Items.Add("Close tab", null, (s, e) => CloseTab(page, force: true));
            return menu;
        }

        private void TogglePinned(TabPage page)
        {
            if (!tabMetadata.TryGetValue(page, out var meta))
                tabMetadata[page] = meta = new TabMetadata();

            meta.IsPinned = !meta.IsPinned;
            ApplyTabFilter(selectPage: page);
        }

        private async Task ToggleMutedAsync(TabPage page)
        {
            if (!tabMetadata.TryGetValue(page, out var meta))
                tabMetadata[page] = meta = new TabMetadata();

            meta.IsMuted = !meta.IsMuted;
            if (page.Tag is Tab tab)
                await tab.SetMutedAsync(meta.IsMuted);

            RefreshTabStrip();
        }

        private void DuplicateTab(TabPage page)
        {
            var closingTab = page.Tag as Tab;
            if (fullscreenTab == closingTab)
            {
                fullscreenTab = null;
                SetTrueFullscreen(false);
            }

            var url = closingTab?.GetCurrentUrl();
            if (!string.IsNullOrWhiteSpace(url))
                CreateNewTab(url);
        }

        private void CloseOtherTabs(TabPage keepPage)
        {
            foreach (var page in allPages.ToList())
            {
                if (page != keepPage && (!tabMetadata.TryGetValue(page, out var meta) || !meta.IsPinned))
                    CloseTab(page);
            }

            SelectPage(keepPage);
        }

        private void CloseTabsToRight(TabPage page)
        {
            int index = allPages.IndexOf(page);
            if (index < 0)
                return;

            foreach (var trailingPage in allPages.Skip(index + 1).ToList())
            {
                if (!tabMetadata.TryGetValue(trailingPage, out var meta) || !meta.IsPinned)
                    CloseTab(trailingPage);
            }

            SelectPage(page);
        }

        private void LoadBookmarks()
        {
            bookmarks.Clear();
            bookmarks.AddRange(bookmarkStore.Load());
            UpdateAddressAutoComplete();
        }

        private void SaveBookmarks()
        {
            bookmarkStore.Save(bookmarks);
            UpdateAddressAutoComplete();
        }

        private void UpdateAddressAutoComplete()
        {
            if (addressBar == null)
                return;

            // Only rebuild if the bookmark URL set has actually changed
            var urls = bookmarks
                .Select(b => b.Url)
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            int newHash = 0;
            foreach (var u in urls)
                newHash = HashCode.Combine(newHash, u.GetHashCode(StringComparison.OrdinalIgnoreCase));

            if (newHash == _autoCompleteBookmarkHash)
                return; // nothing changed — skip the allocation
            _autoCompleteBookmarkHash = newHash;

            var source = new AutoCompleteStringCollection();
            source.AddRange(urls);
            addressBar.AutoCompleteCustomSource = source;
        }

        private void ToggleBookmarkForCurrentPage()
        {
            string? url = CurrentTab?.GetCurrentUrl();
            if (string.IsNullOrWhiteSpace(url) || IsNewTabUrl(url))
                return;

            int index = bookmarks.FindIndex(b => SameUrl(b.Url, url));
            if (index >= 0)
            {
                bookmarks.RemoveAt(index);
            }
            else
            {
                bookmarks.Add(new BookmarkItem
                {
                    Title = GetCurrentPageTitle(),
                    Url = url,
                    SavedAtUtc = DateTime.UtcNow
                });
            }

            SaveBookmarks();
            UpdateBookmarkButton();
            _ = InjectNewTabDataAsync(CurrentTab);
        }

        private string GetCurrentPageTitle()
        {
            string title = tabControl1.SelectedTab?.Text ?? "";
            return string.IsNullOrWhiteSpace(title) || title == "Loading..."
                ? CurrentTab?.GetCurrentUrl() ?? "Untitled"
                : title;
        }

        private void UpdateBookmarkButton()
        {
            if (bookmarkButton == null)
                return;

            string? url = CurrentTab?.GetCurrentUrl();

            // Hide star on the new-tab page and other internal pages
            bool isBookmarkable = !string.IsNullOrWhiteSpace(url)
                && (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                ||  url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

            bookmarkButton.Visible = isBookmarkable;

            if (!isBookmarkable)
                return;

            bool saved = bookmarks.Any(b => SameUrl(b.Url, url));
            bookmarkButton.Icon = saved ? IconKind.StarFilled : IconKind.Star;
            toolTip.SetToolTip(bookmarkButton, saved ? "Remove bookmark" : "Save bookmark");
            bookmarkButton.Invalidate();
        }

        private void ShowBookmarksMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Bookmark this page", null, (s, e) => ToggleBookmarkForCurrentPage());
            menu.Items.Add(new ToolStripSeparator());

            if (bookmarks.Count == 0)
            {
                menu.Items.Add(new ToolStripMenuItem("No bookmarks yet") { Enabled = false });
            }
            else
            {
                foreach (var bookmark in bookmarks.OrderBy(b => b.Title).ToList())
                {
                    var item = new ToolStripMenuItem(TrimMenuText(bookmark.Title, 48));
                    item.ToolTipText = bookmark.Url;
                    item.Click += (s, e) => CurrentTab?.Navigate(bookmark.Url);
                    menu.Items.Add(item);
                }
            }

            menu.Show(bookmarksButton, new Point(0, bookmarksButton.Height));
        }

        private void OnDownloadRequested(object? sender, BrowserDownloadRequestedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(settings.DefaultDownloadFolder))
            {
                try
                {
                    Directory.CreateDirectory(settings.DefaultDownloadFolder);
                    var fileName = Path.GetFileName(e.ResultFilePath);
                    if (!string.IsNullOrWhiteSpace(fileName))
                        e.ResultFilePath = Path.Combine(settings.DefaultDownloadFolder, fileName);
                }
                catch { }
            }

            if (!e.ShieldsEnabled)
            {
                e.Cancel = false;
                return;
            }

            var result = MessageBox.Show(
                "Privacy shields stopped a download from this page. Allow this download once?",
                "Download blocked",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (result == DialogResult.Yes)
            {
                e.Cancel = false;
            }
            else
            {
                e.Cancel = true;
            }
        }

        private void OnDownloadStarted(object? sender, BrowserDownloadStartedEventArgs e)
        {
            downloadManager.Add(e.SourceUrl, e.ResultFilePath, e.Operation);
        }

        private void ShowDownloadsMenu()
        {
            var menu = new ContextMenuStrip();

            if (downloadManager.Items.Count == 0)
            {
                menu.Items.Add(new ToolStripMenuItem("No downloads this session") { Enabled = false });
            }
            else
            {
                foreach (var item in downloadManager.Items.Take(10))
                {
                    var menuItem = new ToolStripMenuItem(
                        TrimMenuText($"{item.FileName} - {item.Status}{(item.TotalBytes > 0 ? $" - {item.ProgressPercent}%" : "")}", 58))
                    {
                        ToolTipText = item.ResultPath
                    };
                    if (item.FileExists)
                        menuItem.DropDownItems.Add("Open file", null, (s, e) => OpenPath(item.ResultPath));
                    if (!string.IsNullOrWhiteSpace(item.ResultPath))
                        menuItem.DropDownItems.Add("Show in folder", null, (s, e) => ShowInFolder(item.ResultPath));
                    if (item.CanCancel)
                        menuItem.DropDownItems.Add("Cancel", null, (s, e) => item.CancelAction?.Invoke());
                    if (!string.IsNullOrWhiteSpace(item.SourceUrl))
                        menuItem.DropDownItems.Add("Retry", null, (s, e) => CurrentTab?.Navigate(item.SourceUrl));
                    if (!string.IsNullOrWhiteSpace(item.FailureReason))
                        menuItem.DropDownItems.Add(new ToolStripMenuItem("Failed: " + item.FailureReason) { Enabled = false });
                    menu.Items.Add(menuItem);
                }
            }

            if (downloadManager.Items.Count > 0)
            {
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("Clear session download list", null, (s, e) => downloadManager.Clear());
            }

            menu.Show(downloadsButton, new Point(0, downloadsButton.Height));
        }

        private static void OpenPath(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return;

                Process.Start(new ProcessStartInfo(path)
                {
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private static void ShowInFolder(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;

                string argument = File.Exists(path)
                    ? "/select,\"" + path + "\""
                    : "\"" + Path.GetDirectoryName(path) + "\"";
                Process.Start(new ProcessStartInfo("explorer.exe", argument)
                {
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void ShowPrivacyReportMenu()
        {
            var state = CurrentTab?.State;
            var menu = new ContextMenuStrip();

            menu.Items.Add(new ToolStripMenuItem(isTorWindow ? "Tor shields on" : shieldsEnabled ? "Shields on" : "Shields off")
            {
                Enabled = false
            });

            if (state == null)
            {
                menu.Items.Add(new ToolStripMenuItem("No page loaded") { Enabled = false });
            }
            else
            {
                menu.Items.Add(new ToolStripMenuItem($"Trackers blocked: {state.BlockedTrackers}") { Enabled = false });
                menu.Items.Add(new ToolStripMenuItem($"Popups blocked: {state.BlockedPopups}") { Enabled = false });
                menu.Items.Add(new ToolStripMenuItem($"Permissions denied: {state.DeniedPermissions}") { Enabled = false });
                menu.Items.Add(new ToolStripMenuItem($"Redirects blocked: {state.BlockedRedirects}") { Enabled = false });
                menu.Items.Add(new ToolStripMenuItem($"Downloads blocked: {state.BlockedDownloads}") { Enabled = false });

                if (state.BlockedItems.Count > 0)
                {
                    menu.Items.Add(new ToolStripSeparator());
                    foreach (var blocked in state.BlockedItems.TakeLast(8).Reverse())
                        menu.Items.Add(new ToolStripMenuItem(TrimMenuText(blocked, 64)) { Enabled = false });
                }
            }

            menu.Items.Add(new ToolStripSeparator());
            if (isTorWindow)
            {
                menu.Items.Add(new ToolStripMenuItem("Tor windows keep shields on") { Enabled = false });
            }
            else
            {
                menu.Items.Add(shieldsEnabled ? "Disable shields and reload" : "Enable shields and reload",
                    null,
                    async (s, e) => await ToggleShieldsAsync());
            }
            menu.Show(shieldsButton, new Point(0, shieldsButton.Height));
        }

        private void ShowPrivacyReportPanel()
        {
            var tab = CurrentTab;
            var state = tab?.State;
            string? host = SiteShieldPolicy.NormalizeHost(tab?.GetCurrentUrl());
            bool hasSiteException = SiteShieldPolicy.IsHostExcepted(host, settings.ShieldDisabledHosts);
            bool effectiveShields = tab?.EffectiveShieldsEnabled ?? GetEffectiveShieldsForUrl(tab?.GetCurrentUrl());

            var form = new Form
            {
                Text = "Privacy report",
                Size = new Size(620, 520),
                MinimumSize = new Size(520, 420),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = WindowColor,
                ForeColor = TextColor,
                FormBorderStyle = FormBorderStyle.Sizable
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(14),
                BackColor = WindowColor
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

            string titleText = host == null ? "No site loaded" : host;
            var title = new Label
            {
                Dock = DockStyle.Fill,
                Text = titleText,
                Font = new Font("Segoe UI", 15f, FontStyle.Bold),
                ForeColor = TextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            string shieldText = isTorWindow
                ? "Tor window - shields locked on"
                : !shieldsEnabled
                    ? "Global shields are off"
                    : hasSiteException
                        ? "Shields are off for this site"
                        : effectiveShields ? "Shields are on for this site" : "Shields are off for this site";
            var summary = new Label
            {
                Dock = DockStyle.Fill,
                Text = state == null
                    ? shieldText
                    : $"{shieldText} - {state.BlockedTrackers} trackers, {state.BlockedPopups} popups, {state.DeniedPermissions} permissions, {state.BlockedRedirects} redirects, {state.BlockedDownloads} downloads blocked",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            var list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                BackColor = ChromeColor,
                ForeColor = TextColor,
                BorderStyle = BorderStyle.FixedSingle
            };
            list.Columns.Add("Type", 120);
            list.Columns.Add("Host or item", 420);

            foreach (var groupName in new[] { "Trackers", "Popups", "Permissions", "Redirects", "Downloads" })
                list.Groups.Add(groupName, groupName);

            if (state?.BlockedItems.Count > 0)
            {
                foreach (var item in state.BlockedItems.AsEnumerable().Reverse())
                {
                    string group = GetPrivacyItemGroup(item);
                    var row = new ListViewItem(group)
                    {
                        Group = list.Groups[group],
                        ToolTipText = item
                    };
                    row.SubItems.Add(TrimMenuText(GetPrivacyItemDisplay(item), 96));
                    list.Items.Add(row);
                }
            }
            else
            {
                var row = new ListViewItem("Report")
                {
                    Group = list.Groups["Trackers"]
                };
                row.SubItems.Add("Nothing blocked on this page yet");
                list.Items.Add(row);
            }

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 8, 0, 0)
            };

            var close = CreateReportButton("Close");
            close.Click += (s, e) => form.Close();

            var reload = CreateReportButton("Reload");
            reload.Enabled = tab?.WebView.CoreWebView2 != null;
            reload.Click += (s, e) =>
            {
                tab?.WebView.CoreWebView2?.Reload();
                form.Close();
            };

            var clear = CreateReportButton("Clear report");
            clear.Enabled = state != null;
            clear.Click += (s, e) =>
            {
                state?.ResetPrivacyReport();
                UpdateShieldsButton();
                form.Close();
            };

            var siteToggle = CreateReportButton(hasSiteException ? "Enable for site" : "Disable for site");
            siteToggle.Enabled = host != null && !isTorWindow && shieldsEnabled;
            siteToggle.Click += async (s, e) =>
            {
                await SetCurrentSiteShieldExceptionAsync(!hasSiteException, reload: true);
                form.Close();
            };

            var globalToggle = CreateReportButton(shieldsEnabled ? "Turn shields off" : "Turn shields on");
            globalToggle.Enabled = !isTorWindow;
            globalToggle.Click += async (s, e) =>
            {
                await ToggleShieldsAsync();
                form.Close();
            };

            actions.Controls.AddRange(new Control[] { close, reload, clear, siteToggle, globalToggle });

            layout.Controls.Add(title, 0, 0);
            layout.Controls.Add(summary, 0, 1);
            layout.Controls.Add(list, 0, 2);
            layout.Controls.Add(actions, 0, 3);
            form.Controls.Add(layout);
            form.Show(this);
        }

        private Button CreateReportButton(string text)
        {
            return new Button
            {
                Text = text,
                AutoSize = true,
                Height = 30,
                Margin = new Padding(6, 0, 0, 0),
                BackColor = RaisedColor,
                ForeColor = TextColor,
                FlatStyle = FlatStyle.Flat
            };
        }

        private static string GetPrivacyItemGroup(string item)
        {
            if (item.StartsWith("Popup", StringComparison.OrdinalIgnoreCase))
                return "Popups";
            if (item.StartsWith("Permission", StringComparison.OrdinalIgnoreCase))
                return "Permissions";
            if (item.StartsWith("Redirect", StringComparison.OrdinalIgnoreCase))
                return "Redirects";
            if (item.StartsWith("Download", StringComparison.OrdinalIgnoreCase))
                return "Downloads";
            return "Trackers";
        }

        private static string GetPrivacyItemDisplay(string item)
        {
            int index = item.LastIndexOf("http", StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && Uri.TryCreate(item[index..], UriKind.Absolute, out var uri))
                return uri.Host + uri.PathAndQuery;

            int colon = item.IndexOf(':');
            return colon >= 0 && colon + 1 < item.Length
                ? item[(colon + 1)..].Trim()
                : item;
        }

        private bool GetEffectiveShieldsForUrl(string? url)
        {
            if (isTorWindow)
                return true;

            return shieldsEnabled && !SiteShieldPolicy.IsHostExcepted(url, settings.ShieldDisabledHosts);
        }

        private async Task SetCurrentSiteShieldExceptionAsync(bool disabled, bool reload)
        {
            var tab = CurrentTab;
            string? host = SiteShieldPolicy.NormalizeHost(tab?.GetCurrentUrl());
            if (tab == null || host == null)
                return;

            SiteShieldPolicy.SetException(settings.ShieldDisabledHosts, host, disabled);
            settingsStore.Save(settings);
            await ApplyShieldsToTabAsync(tab, reload);
        }

        private async Task ApplyShieldsToTabAsync(Tab tab, bool reload)
        {
            bool effective = GetEffectiveShieldsForUrl(tab.GetCurrentUrl());
            await tab.ApplyShieldsAsync(effective);
            await InjectNewTabDataAsync(tab);

            if (reload)
                tab.WebView.CoreWebView2?.Reload();

            UpdateWindowTitle();
            UpdateShieldsButton();
        }

        private async Task InjectNewTabDataAsync(Tab? tab)
        {
            if (tab?.WebView.CoreWebView2 == null || !IsNewTabUrl(tab.GetCurrentUrl()))
                return;

            var payload = new
            {
                bookmarks = bookmarks
                    .OrderBy(b => b.Title)
                    .Select(b => new { title = b.Title, url = b.Url })
                    .ToArray(),
                shields    = isTorWindow ? "Tor shields on" : shieldsEnabled ? "Shields on" : "Shields off",
                tor        = isTorWindow,
                searchUrl  = settings.SearchUrl,
                darkTheme  = darkTheme,
            };
            string script = "window.MyBrowserShellNewTab && window.MyBrowserShellNewTab.applyData(" +
                JsonSerializer.Serialize(payload) + ");";
            await tab.WebView.CoreWebView2.ExecuteScriptAsync(script);
        }

        private bool IsNewTabUrl(string? url)
        {
            return !string.IsNullOrWhiteSpace(url) &&
                SameUrl(url, homeUrl);
        }

        private static bool SameUrl(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return false;

            return string.Equals(left.TrimEnd('/'), right.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }

        private static string TrimMenuText(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "Untitled";

            return text.Length <= maxLength
                ? text
                : text[..Math.Max(0, maxLength - 3)] + "...";
        }

        private async Task ToggleDarkModeAsync(bool enabled)
        {
            foreach (TabPage page in allPages)
            {
                page.BackColor = WindowColor;

                if (page.Tag is Tab tab)
                    await tab.ApplyDarkModeAsync(enabled);
            }
        }

        private async Task SaveSessionAsync()
        {
            var tabs = allPages
                .Select(p =>
                {
                    tabMetadata.TryGetValue(p, out var meta);
                    return new SavedTabState
                    {
                        Url = (p.Tag as Tab)?.GetCurrentUrl() ?? "",
                        IsPinned = meta?.IsPinned == true,
                        IsMuted = meta?.IsMuted == true,
                        IsSuspended = meta?.IsSuspended == true
                    };
                })
                .Where(t => !string.IsNullOrWhiteSpace(t.Url))
                .ToList();

            await sessionStore.SaveAsync(tabs);
            settings.RestoreSavedSession = true;
            settingsStore.Save(settings);
            UpdateNavigationButtons();
            MessageBox.Show("Saved this session. It can be restored from the open-session button.", "Session saved",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowSettingsMenu()
        {
            var menu = new ContextMenuStrip();

            menu.Items.Add(new ToolStripMenuItem("Settings") { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(settings.RestoreSavedSession ? "Restore saved session on launch: on" : "Restore saved session on launch: off",
                null,
                (s, e) =>
                {
                    settings.RestoreSavedSession = !settings.RestoreSavedSession;
                    settingsStore.Save(settings);
                });
            if (isTorWindow)
            {
                menu.Items.Add(new ToolStripMenuItem("Shields default: Tor always on") { Enabled = false });
            }
            else
            {
                menu.Items.Add(settings.ShieldsEnabled ? "Shields default: on" : "Shields default: off",
                    null,
                    async (s, e) =>
                    {
                        if (settings.ShieldsEnabled == shieldsEnabled)
                            await ToggleShieldsAsync();
                        else
                        {
                            settings.ShieldsEnabled = !settings.ShieldsEnabled;
                            settingsStore.Save(settings);
                        }
                    });
            }
            menu.Items.Add(new ToolStripSeparator());
            // ── Search engine submenu ────────────────────────────────────────
            var searchSubmenu = new ToolStripMenuItem("Search engine");

            var engines = new (string Name, string Url)[]
            {
                ("DuckDuckGo",  "https://duckduckgo.com/?q="),
                ("Google",      "https://www.google.com/search?q="),
                ("Bing",        "https://www.bing.com/search?q="),
                ("Brave",       "https://search.brave.com/search?q="),
                ("Ecosia",      "https://www.ecosia.org/search?q="),
            };

            string currentSearch = settings.SearchUrl ?? "";
            bool matchedBuiltIn = false;

            foreach (var (name, url) in engines)
            {
                bool isActive = currentSearch.Equals(url, StringComparison.OrdinalIgnoreCase);
                if (isActive) matchedBuiltIn = true;
                var item = new ToolStripMenuItem(name)
                {
                    Checked  = isActive,
                    CheckOnClick = false,
                };
                string capturedUrl = url;
                item.Click += (s, e) => SetSearchEngine(capturedUrl);
                searchSubmenu.DropDownItems.Add(item);
            }

            searchSubmenu.DropDownItems.Add(new ToolStripSeparator());

            // Custom engine entry
            bool isCustom = !matchedBuiltIn && !string.IsNullOrWhiteSpace(currentSearch);
            var customItem = new ToolStripMenuItem(isCustom ? $"Custom: {currentSearch}" : "Custom…")
            {
                Checked = isCustom,
            };
            customItem.Click += (s, e) =>
            {
                string initial = isCustom ? currentSearch : "https://kagi.com/search?q=";
                string? result = ShowInputDialog(
                    "Custom search engine",
                    "Enter a search URL ending with the query parameter (e.g. https://kagi.com/search?q=)",
                    initial);

                if (string.IsNullOrWhiteSpace(result))
                    return;

                // Accept %s placeholder or a bare trailing URL — normalise to bare
                string normalised = result.Contains("%s")
                    ? result.Replace("%s", "")
                    : result.TrimEnd();
                SetSearchEngine(normalised);
            };
            searchSubmenu.DropDownItems.Add(customItem);

            menu.Items.Add(searchSubmenu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Duplicate current tab", null, (s, e) =>
            {
                if (tabControl1.SelectedTab != null)
                    DuplicateTab(tabControl1.SelectedTab);
            });
            menu.Items.Add("Copy page link", null, (s, e) => CopyCurrentPageLink());
            menu.Items.Add("Open downloads folder", null, (s, e) => OpenDownloadsFolder());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Use current page as home", null, (s, e) =>
            {
                var url = CurrentTab?.GetCurrentUrl();
                if (!string.IsNullOrWhiteSpace(url))
                {
                    homeUrl = url;
                    settings.HomeUrl = url;
                    settingsStore.Save(settings);
                }
            });
            menu.Items.Add("Reset home to private new tab", null, (s, e) =>
            {
                homeUrl = new Uri(Path.Combine(AppContext.BaseDirectory, "NewTab.html")).AbsoluteUri;
                settings.HomeUrl = "";
                settingsStore.Save(settings);
            });
            menu.Items.Add("Choose download folder...", null, (s, e) => ChooseDownloadFolder());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("New Tor Window", null, async (s, e) => await OpenTorWindowAsync());
            menu.Show(settingsButton, new Point(0, settingsButton.Height));
        }

        /// <summary>
        /// Starts the Tor proxy (if not already running) then opens a new browser window
        /// that routes all traffic through Tor. The new window is visually distinguished
        /// by a purple chrome tint and a [Tor] title bar badge.
        /// </summary>
        private async Task OpenTorWindowAsync()
        {
            // Show a "connecting" hint while Tor bootstraps (can take a few seconds)
            var splash = new Form
            {
                Text = "Connecting to Tor…",
                Size = new Size(360, 110),
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                BackColor = Color.FromArgb(22, 16, 34),
                ForeColor = Color.FromArgb(220, 200, 255),
            };
            var label = new Label
            {
                Text = "Connecting to the Tor network…\nThis may take up to 90 seconds on first launch.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10f),
            };
            splash.Controls.Add(label);
            splash.Show(this);
            splash.Refresh();

            bool ok = await TorProxy.EnsureRunningAsync();
            splash.Close();

            if (!ok)
                return; // TorProxy already showed an error dialog

            var torForm = new Form1(torWindow: true);
            torForm.Show();
        }

        /// <summary>
        /// Simple single-line input prompt — avoids a Microsoft.VisualBasic dependency.
        /// Returns null/empty if the user cancels.
        /// </summary>
        private static string? ShowInputDialog(string title, string prompt, string initial = "")
        {
            var form = new Form
            {
                Text            = title,
                Width           = 500,
                Height          = 160,
                StartPosition   = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox     = false,
                MaximizeBox     = false,
                BackColor       = Color.FromArgb(28, 30, 36),
                ForeColor       = Color.FromArgb(240, 243, 246),
            };

            var label = new Label
            {
                Text      = prompt,
                AutoSize  = false,
                Width     = 460,
                Height    = 32,
                Location  = new Point(12, 10),
                ForeColor = Color.FromArgb(161, 169, 181),
                Font      = new Font("Segoe UI", 9f),
            };

            var textBox = new TextBox
            {
                Text      = initial,
                Width     = 460,
                Location  = new Point(12, 48),
                BackColor = Color.FromArgb(39, 42, 50),
                ForeColor = Color.FromArgb(240, 243, 246),
                BorderStyle = BorderStyle.FixedSingle,
                Font      = new Font("Segoe UI", 10f),
            };
            textBox.SelectAll();

            var ok = new Button
            {
                Text         = "OK",
                DialogResult = DialogResult.OK,
                Width        = 80,
                Height       = 28,
                Location     = new Point(300, 82),
                BackColor    = Color.FromArgb(0, 120, 212),
                ForeColor    = Color.White,
                FlatStyle    = FlatStyle.Flat,
            };
            ok.FlatAppearance.BorderSize = 0;

            var cancel = new Button
            {
                Text         = "Cancel",
                DialogResult = DialogResult.Cancel,
                Width        = 80,
                Height       = 28,
                Location     = new Point(392, 82),
                BackColor    = Color.FromArgb(52, 56, 66),
                ForeColor    = Color.FromArgb(240, 243, 246),
                FlatStyle    = FlatStyle.Flat,
            };
            cancel.FlatAppearance.BorderSize = 0;

            form.Controls.AddRange(new Control[] { label, textBox, ok, cancel });
            form.AcceptButton = ok;
            form.CancelButton = cancel;
            form.ActiveControl = textBox;

            return form.ShowDialog() == DialogResult.OK ? textBox.Text.Trim() : null;
        }

        private void SetSearchEngine(string searchUrl)
        {
            settings.SearchUrl = searchUrl;
            settingsStore.Save(settings);
            // Refresh the new-tab page so its search box picks up the new engine
            _ = InjectNewTabDataAsync(CurrentTab);
        }

        private void ChooseDownloadFolder()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Choose default download folder",
                SelectedPath = Directory.Exists(settings.DefaultDownloadFolder)
                    ? settings.DefaultDownloadFolder
                    : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                settings.DefaultDownloadFolder = dialog.SelectedPath;
                settingsStore.Save(settings);
            }
        }

        private void CopyCurrentPageLink()
        {
            var url = CurrentTab?.GetCurrentUrl();
            if (string.IsNullOrWhiteSpace(url))
                return;

            try
            {
                Clipboard.SetText(url);
            }
            catch { }
        }

        private void OpenDownloadsFolder()
        {
            string folder = Directory.Exists(settings.DefaultDownloadFolder)
                ? settings.DefaultDownloadFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"")
                {
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void UpdateWindowTitle()
        {
            var wv = CurrentTab?.WebView;
            double zoom = (wv?.CoreWebView2 != null) ? wv.ZoomFactor : 1.0;
            string zoomStr = Math.Abs(zoom - 1.0) > 0.01
                ? $" — {(int)Math.Round(zoom * 100)}%"
                : "";
            string shields = isTorWindow
                ? "Tor shields on"
                : GetEffectiveShieldsForUrl(CurrentTab?.GetCurrentUrl()) ? "Shields on" : "Shields off";
            string torTag = isTorWindow ? " [Tor]" : "";
            Text = $"MyBrowserShell{torTag} — {shields}{zoomStr}";
        }

        private void UpdateShieldsButton()
        {
            bool effective = GetEffectiveShieldsForUrl(CurrentTab?.GetCurrentUrl());
            shieldsButton.Icon = effective ? IconKind.Shield : IconKind.ShieldOff;
            toolTip.SetToolTip(shieldsButton, isTorWindow
                ? "Tor shields on"
                : effective ? "Privacy shields on for this site" : "Privacy shields off for this site");
            shieldsButton.Invalidate();
        }

        private async Task ToggleShieldsAsync()
        {
            if (isTorWindow)
            {
                shieldsEnabled = true;
                UpdateShieldsButton();
                await InjectNewTabDataAsync(CurrentTab);
                return;
            }

            shieldsEnabled = !shieldsEnabled;
            settings.ShieldsEnabled = shieldsEnabled;
            settingsStore.Save(settings);
            PrivacyPolicy.SetShieldsEnabled(shieldsEnabled);
            UpdateWindowTitle();
            UpdateShieldsButton();

            foreach (TabPage page in allPages)
            {
                if (page.Tag is Tab tab)
                {
                    bool effective = GetEffectiveShieldsForUrl(tab.GetCurrentUrl());
                    await tab.ApplyShieldsAsync(effective);
                    await InjectNewTabDataAsync(tab);
                    tab.WebView.CoreWebView2?.Reload();
                }
            }
        }

        private async Task ClearAllPrivateDataAsync()
        {
            await ClearRuntimePrivateDataAsync();
            sessionStore.Delete();
            settings.RestoreSavedSession = false;
            settingsStore.Save(settings);
            UpdateNavigationButtons();
            MessageBox.Show("Private data for this session was cleared.", "Private data cleared",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async Task ClearRuntimePrivateDataAsync()
        {
            foreach (TabPage page in allPages)
            {
                if (page.Tag is Tab tab)
                    await tab.ClearPrivateDataAsync();
            }

            closedTabUrls.Clear();
            downloadManager.Clear();
            PrivacyPolicy.ClearSessionExceptions();
        }

        private async Task LoadSessionAsync()
        {
            if (!sessionStore.Exists)
                return;

            var session = await sessionStore.LoadAsync();

            foreach (TabPage page in allPages)
                page.Dispose();

            allPages.Clear();
            tabMetadata.Clear();
            tabControl1.TabPages.Clear();
            tabSearch.Clear();

            foreach (var saved in session.Tabs.Where(t => !string.IsNullOrWhiteSpace(t.Url)))
                CreateNewTab(saved.Url, new TabMetadata
                {
                    IsPinned = saved.IsPinned,
                    IsMuted = saved.IsMuted,
                    IsSuspended = saved.IsSuspended
                });

            if (allPages.Count == 0)
                CreateNewTab(homeUrl);
        }

        private void UpdateAddressFromCurrentTab()
        {
            if (CurrentTab?.WebView.Source is Uri uri)
                addressBar.Text = uri.ToString();
            else if (tabControl1.TabPages.Count == 0)
                addressBar.Clear();

            addressContainer?.Invalidate();
        }

        private void UpdateNavigationButtons()
        {
            var core = CurrentTab?.WebView.CoreWebView2;
            backButton.Enabled = core?.CanGoBack == true;
            forwardButton.Enabled = core?.CanGoForward == true;
            reloadButton.Icon = pageLoading ? IconKind.Stop : IconKind.Reload;
            toolTip.SetToolTip(reloadButton, pageLoading ? "Stop loading" : "Reload (F5)");
            reloadButton.Enabled = core != null;
            readerModeButton.Enabled = core != null;
            pipButton.Enabled = core != null;
            bookmarkButton.Enabled = core != null;
            bookmarksButton.Enabled = true;
            downloadsButton.Enabled = true;
            saveSessionButton.Enabled = allPages.Count > 0;
            loadSessionButton.Enabled = sessionStore.Exists;
            UpdateBookmarkButton();

            foreach (var button in chromeButtons)
                button.Invalidate();
        }

        private void CloseTab(TabPage page, bool force = false)
        {
            if (!force && tabMetadata.TryGetValue(page, out var meta) && meta.IsPinned)
                return;

            var closingTab = page.Tag as Tab;
            var url = closingTab?.GetCurrentUrl();
            if (fullscreenTab == closingTab)
            {
                fullscreenTab = null;
                SetTrueFullscreen(false);
            }

            if (!string.IsNullOrWhiteSpace(url))
                closedTabUrls.Push(url);

            allPages.Remove(page);
            tabMetadata.Remove(page);
            tabControl1.TabPages.Remove(page);
            page.Dispose();

            if (allPages.Count == 0)
                CreateNewTab(homeUrl);
            else
                ApplyTabFilter();
        }

        private sealed class PageHostTabControl : TabControl
        {
            public override Rectangle DisplayRectangle => ClientRectangle;
        }
    }

    internal enum IconKind
    {
        Back,
        Forward,
        Reload,
        Stop,
        Plus,
        Reader,
        Pip,
        Save,
        Open,
        Moon,
        Sun,
        Shield,
        ShieldOff,
        Trash,
        Star,
        StarFilled,
        Bookmarks,
        Download,
        Settings
    }

    internal readonly record struct ButtonTheme(
        Color Surface,
        Color Raised,
        Color Border,
        Color Text,
        Color Muted,
        Color Accent,
        Color AccentGreen);

    internal sealed class ChromeIconButton : Control
    {
        private bool hovered;
        private bool pressed;

        public ChromeIconButton(IconKind icon)
        {
            Icon = icon;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        public IconKind Icon { get; set; }

        public ButtonTheme Theme { get; set; } = new(
            Color.FromArgb(39, 42, 50),
            Color.FromArgb(52, 56, 66),
            Color.FromArgb(72, 78, 91),
            Color.White,
            Color.FromArgb(161, 169, 181),
            Color.FromArgb(0, 120, 212),
            Color.FromArgb(36, 184, 133));

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                pressed = true;
                Invalidate();
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = ClientRectangle;
            rect.Inflate(-1, -1);

            var fillColor = Enabled
                ? pressed ? Theme.Accent : hovered ? Theme.Raised : Theme.Surface
                : Color.FromArgb(80, Theme.Surface);
            var borderColor = hovered && Enabled ? Theme.Accent : Theme.Border;
            var iconColor = Enabled ? Theme.Text : Theme.Muted;

            using (var path = RoundedRect(rect, 9))
            using (var fill = new SolidBrush(fillColor))
            using (var border = new Pen(borderColor))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            }

            DrawIcon(e.Graphics, rect, iconColor);
        }

        private void DrawIcon(Graphics graphics, Rectangle bounds, Color color)
        {
            using var pen = new Pen(color, 1.9f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            using var brush = new SolidBrush(color);

            int cx = bounds.Left + bounds.Width / 2;
            int cy = bounds.Top + bounds.Height / 2;

            switch (Icon)
            {
                case IconKind.Back:
                    graphics.DrawLines(pen, new[] { new Point(cx + 5, cy - 8), new Point(cx - 4, cy), new Point(cx + 5, cy + 8) });
                    graphics.DrawLine(pen, cx - 3, cy, cx + 9, cy);
                    break;
                case IconKind.Forward:
                    graphics.DrawLines(pen, new[] { new Point(cx - 5, cy - 8), new Point(cx + 4, cy), new Point(cx - 5, cy + 8) });
                    graphics.DrawLine(pen, cx - 9, cy, cx + 3, cy);
                    break;
                case IconKind.Reload:
                    graphics.DrawArc(pen, cx - 9, cy - 9, 18, 18, 35, 285);
                    graphics.FillPolygon(brush, new[] { new Point(cx + 9, cy - 8), new Point(cx + 10, cy - 1), new Point(cx + 4, cy - 4) });
                    break;
                case IconKind.Stop:
                    graphics.DrawLine(pen, cx - 7, cy - 7, cx + 7, cy + 7);
                    graphics.DrawLine(pen, cx + 7, cy - 7, cx - 7, cy + 7);
                    break;
                case IconKind.Plus:
                    graphics.DrawLine(pen, cx - 7, cy, cx + 7, cy);
                    graphics.DrawLine(pen, cx, cy - 7, cx, cy + 7);
                    break;
                case IconKind.Reader:
                    graphics.DrawRectangle(pen, cx - 10, cy - 8, 20, 16);
                    graphics.DrawLine(pen, cx, cy - 8, cx, cy + 8);
                    graphics.DrawLine(pen, cx - 7, cy - 4, cx - 3, cy - 4);
                    graphics.DrawLine(pen, cx + 3, cy - 4, cx + 7, cy - 4);
                    graphics.DrawLine(pen, cx - 7, cy, cx - 3, cy);
                    graphics.DrawLine(pen, cx + 3, cy, cx + 7, cy);
                    break;
                case IconKind.Pip:
                    graphics.DrawRectangle(pen, cx - 10, cy - 7, 20, 14);
                    graphics.DrawRectangle(pen, cx + 1, cy, 7, 5);
                    break;
                case IconKind.Save:
                    graphics.DrawRectangle(pen, cx - 9, cy - 9, 18, 18);
                    graphics.DrawLine(pen, cx - 5, cy - 9, cx - 5, cy - 3);
                    graphics.DrawLine(pen, cx - 5, cy - 3, cx + 5, cy - 3);
                    graphics.DrawRectangle(pen, cx - 5, cy + 3, 10, 5);
                    break;
                case IconKind.Open:
                    graphics.DrawLine(pen, cx - 10, cy - 4, cx - 4, cy - 4);
                    graphics.DrawLine(pen, cx - 4, cy - 4, cx - 2, cy - 8);
                    graphics.DrawLine(pen, cx - 2, cy - 8, cx + 9, cy - 8);
                    graphics.DrawLine(pen, cx - 10, cy - 4, cx - 7, cy + 8);
                    graphics.DrawLine(pen, cx - 7, cy + 8, cx + 9, cy + 8);
                    graphics.DrawLine(pen, cx + 9, cy + 8, cx + 11, cy - 2);
                    graphics.DrawLine(pen, cx - 10, cy - 4, cx + 11, cy - 4);
                    break;
                case IconKind.Moon:
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(cx - 8, cy - 9, 16, 18);
                        path.AddEllipse(cx - 2, cy - 10, 16, 18);
                        using var region = new Region(path);
                        region.Exclude(new Rectangle(cx - 1, cy - 11, 18, 21));
                        graphics.FillRegion(brush, region);
                    }
                    break;
                case IconKind.Sun:
                    graphics.DrawEllipse(pen, cx - 5, cy - 5, 10, 10);
                    for (int i = 0; i < 8; i++)
                    {
                        double angle = i * Math.PI / 4;
                        var inner = new Point(cx + (int)(Math.Cos(angle) * 8), cy + (int)(Math.Sin(angle) * 8));
                        var outer = new Point(cx + (int)(Math.Cos(angle) * 11), cy + (int)(Math.Sin(angle) * 11));
                        graphics.DrawLine(pen, inner, outer);
                    }
                    break;
                case IconKind.Shield:
                    graphics.DrawLines(pen, new[]
                    {
                        new Point(cx, cy - 9),
                        new Point(cx + 8, cy - 5),
                        new Point(cx + 8, cy + 2),
                        new Point(cx, cy + 9),
                        new Point(cx - 8, cy + 2),
                        new Point(cx - 8, cy - 5),
                        new Point(cx, cy - 9)
                    });
                    graphics.DrawLine(pen, cx, cy - 2, cx, cy + 5);
                    break;
                case IconKind.ShieldOff:
                    graphics.DrawLines(pen, new[]
                    {
                        new Point(cx, cy - 9),
                        new Point(cx + 8, cy - 5),
                        new Point(cx + 8, cy + 2),
                        new Point(cx, cy + 9),
                        new Point(cx - 8, cy + 2),
                        new Point(cx - 8, cy - 5),
                        new Point(cx, cy - 9)
                    });
                    graphics.DrawLine(pen, cx - 7, cy - 7, cx + 7, cy + 7);
                    break;
                case IconKind.Trash:
                    graphics.DrawLine(pen, cx - 7, cy - 6, cx + 7, cy - 6);
                    graphics.DrawLine(pen, cx - 4, cy - 6, cx - 3, cy + 7);
                    graphics.DrawLine(pen, cx + 4, cy - 6, cx + 3, cy + 7);
                    graphics.DrawLine(pen, cx - 6, cy + 7, cx + 6, cy + 7);
                    graphics.DrawLine(pen, cx - 2, cy - 9, cx + 2, cy - 9);
                    break;
                case IconKind.Star:
                case IconKind.StarFilled:
                    var points = CreateStarPoints(cx, cy, 10, 4);
                    if (Icon == IconKind.StarFilled)
                        graphics.FillPolygon(brush, points);
                    graphics.DrawPolygon(pen, points);
                    break;
                case IconKind.Bookmarks:
                    graphics.DrawRectangle(pen, cx - 9, cy - 9, 16, 18);
                    graphics.DrawLine(pen, cx - 5, cy - 5, cx + 4, cy - 5);
                    graphics.DrawLine(pen, cx - 5, cy, cx + 4, cy);
                    graphics.DrawLines(pen, new[]
                    {
                        new Point(cx + 7, cy - 9),
                        new Point(cx + 11, cy - 6),
                        new Point(cx + 11, cy + 9),
                        new Point(cx + 7, cy + 6)
                    });
                    break;
                case IconKind.Download:
                    graphics.DrawLine(pen, cx, cy - 9, cx, cy + 4);
                    graphics.DrawLines(pen, new[] { new Point(cx - 6, cy - 2), new Point(cx, cy + 5), new Point(cx + 6, cy - 2) });
                    graphics.DrawLine(pen, cx - 9, cy + 9, cx + 9, cy + 9);
                    break;
                case IconKind.Settings:
                    graphics.DrawEllipse(pen, cx - 7, cy - 7, 14, 14);
                    graphics.FillEllipse(brush, cx - 2, cy - 2, 4, 4);
                    graphics.DrawLine(pen, cx, cy - 11, cx, cy - 8);
                    graphics.DrawLine(pen, cx, cy + 8, cx, cy + 11);
                    graphics.DrawLine(pen, cx - 11, cy, cx - 8, cy);
                    graphics.DrawLine(pen, cx + 8, cy, cx + 11, cy);
                    graphics.DrawLine(pen, cx - 8, cy - 8, cx - 6, cy - 6);
                    graphics.DrawLine(pen, cx + 6, cy + 6, cx + 8, cy + 8);
                    graphics.DrawLine(pen, cx + 8, cy - 8, cx + 6, cy - 6);
                    graphics.DrawLine(pen, cx - 6, cy + 6, cx - 8, cy + 8);
                    break;
            }
        }

        private static Point[] CreateStarPoints(int cx, int cy, int outerRadius, int innerRadius)
        {
            var points = new Point[10];
            for (int i = 0; i < points.Length; i++)
            {
                double angle = -Math.PI / 2 + i * Math.PI / 5;
                int radius = i % 2 == 0 ? outerRadius : innerRadius;
                points[i] = new Point(
                    cx + (int)(Math.Cos(angle) * radius),
                    cy + (int)(Math.Sin(angle) * radius));
            }

            return points;
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal readonly record struct TabTheme(
        Color Chrome,
        Color Surface,
        Color Raised,
        Color Border,
        Color Text,
        Color Muted,
        Color Accent,
        Color AccentGreen);

    internal sealed class TabChip : Control
    {
        private readonly TabPage page;
        private bool hovered;
        private bool closeHovered;

        public TabPage Page => page; // exposed for update-in-place lookup in RefreshTabStrip

        public TabChip(TabPage page)
        {
            this.page = page;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        public bool Active { get; set; }
        public bool Pinned { get; set; }
        public bool Muted { get; set; }
        public bool Suspended { get; set; }

        public TabTheme Theme { get; set; } = new(
            Color.FromArgb(28, 30, 36),
            Color.FromArgb(39, 42, 50),
            Color.FromArgb(52, 56, 66),
            Color.FromArgb(72, 78, 91),
            Color.White,
            Color.FromArgb(161, 169, 181),
            Color.FromArgb(0, 120, 212),
            Color.FromArgb(36, 184, 133));

        public event EventHandler? Selected;
        public event EventHandler? CloseRequested;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool wasCloseHovered = closeHovered;
            closeHovered = !Pinned && CloseRect.Contains(e.Location);
            if (wasCloseHovered != closeHovered)
                Invalidate();

            base.OnMouseMove(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false;
            closeHovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (e.Button != MouseButtons.Left)
                return;

            if (!Pinned && CloseRect.Contains(e.Location))
                CloseRequested?.Invoke(this, EventArgs.Empty);
            else
                Selected?.Invoke(this, EventArgs.Empty);

            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = ClientRectangle;
            rect.Inflate(-1, -1);

            var fillColor = Active ? Theme.Raised : hovered ? Theme.Surface : Theme.Chrome;
            using (var path = RoundedRect(rect, 10))
            using (var fill = new SolidBrush(fillColor))
            using (var border = new Pen(Active ? Theme.Accent : Theme.Border))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            }

            if (Active)
            {
                using var accent = new Pen(Theme.AccentGreen, 2.5f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };
                e.Graphics.DrawLine(accent, rect.Left + 12, rect.Top + 2, rect.Right - 34, rect.Top + 2);
            }

            using (var dot = new SolidBrush(Suspended ? Theme.Border : Active ? Theme.AccentGreen : Theme.Muted))
                e.Graphics.FillEllipse(dot, rect.Left + 11, rect.Top + 12, 7, 7);

            if (Pinned)
            {
                using var pin = new Pen(Theme.AccentGreen, 1.7f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };
                e.Graphics.DrawLine(pin, rect.Left + 24, rect.Top + 9, rect.Left + 31, rect.Top + 16);
                e.Graphics.DrawLine(pin, rect.Left + 29, rect.Top + 7, rect.Left + 34, rect.Top + 12);
                e.Graphics.DrawLine(pin, rect.Left + 25, rect.Top + 18, rect.Left + 21, rect.Top + 22);
            }

            if (Muted)
            {
                using var mute = new Pen(Theme.Muted, 1.7f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };
                e.Graphics.DrawLine(mute, rect.Right - 42, rect.Top + 11, rect.Right - 34, rect.Top + 19);
                e.Graphics.DrawLine(mute, rect.Right - 34, rect.Top + 11, rect.Right - 42, rect.Top + 19);
            }

            var textLeft = Pinned ? rect.Left + 40 : rect.Left + 26;
            var textRightPadding = Muted ? 72 : 58;
            var textRect = new Rectangle(textLeft, rect.Top + 4, rect.Width - textRightPadding, rect.Height - 8);
            TextRenderer.DrawText(
                e.Graphics,
                string.IsNullOrWhiteSpace(page.Text) ? "New Tab" : page.Text,
                new Font("Segoe UI", 9f, Active ? FontStyle.Bold : FontStyle.Regular),
                textRect,
                Active ? Theme.Text : Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            if (!Pinned)
                DrawClose(e.Graphics);
        }

        private Rectangle CloseRect => new(Width - 28, 7, 18, 18);

        private void DrawClose(Graphics graphics)
        {
            var rect = CloseRect;
            if (closeHovered)
            {
                using var fill = new SolidBrush(Color.FromArgb(45, Theme.Accent));
                using var path = RoundedRect(rect, 6);
                graphics.FillPath(fill, path);
            }

            using var pen = new Pen(Active ? Theme.Text : Theme.Muted, 1.7f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawLine(pen, rect.Left + 6, rect.Top + 6, rect.Right - 6, rect.Bottom - 6);
            graphics.DrawLine(pen, rect.Right - 6, rect.Top + 6, rect.Left + 6, rect.Bottom - 6);
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
