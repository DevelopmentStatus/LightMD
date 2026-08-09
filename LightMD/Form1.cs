using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace LightMD
{
    public partial class Form1 : Form
    {
        /// <summary>
        /// Editors often save by writing, truncating and renaming in quick
        /// succession. Waiting out a short quiet period turns that burst into a
        /// single reload, and gives the writer time to finish.
        /// </summary>
        private const int ReloadDebounceMs = 250;

        private readonly string[]? _args;
        private readonly List<string> _mappedHosts = new();

        private readonly FileSystemWatcher _watcher = new();
        private readonly System.Windows.Forms.Timer _reloadTimer = new();

        private string? _currentFile;
        private ViewerTheme _theme = ViewerTheme.Light;

        /// <summary>Heading to jump to once the next document has loaded.</summary>
        private string? _pendingFragment;

        /// <summary>Scroll offset to restore once the next document has loaded.</summary>
        private double? _pendingScrollY;

        public Form1(string[]? args = null)
        {
            _args = args;
            InitializeComponent();

            // The embedded .ico carries every size, so the title bar and the
            // taskbar each pick the resolution they want.
            using var iconStream = System.Reflection.Assembly
                .GetExecutingAssembly()
                .GetManifestResourceStream("LightMD.lightmd.ico");
            if (iconStream is not null)
            {
                Icon = new Icon(iconStream);
            }

            _reloadTimer.Interval = ReloadDebounceMs;
            _reloadTimer.Tick += OnReloadTimerTick;

            _watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;
            _watcher.Changed += OnWatchedFileChanged;
            _watcher.Created += OnWatchedFileChanged;
            _watcher.Renamed += OnWatchedFileChanged;
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            try
            {
                _theme = DetectSystemTheme();
                ApplyThemeToChrome();

                // WebView2 defaults its user-data folder to a directory beside
                // the executable, which is not writable once installed under
                // Program Files. Keep it in the user's profile instead.
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LightMD",
                    "WebView2");
                var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, null);
                await webView.EnsureCoreWebView2Async(environment);

                webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                webView.CoreWebView2.Settings.IsScriptEnabled = false;

                webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
                webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

                if (_args is { Length: > 0 })
                {
                    LoadMarkdownFile(_args[0]);
                }
                else
                {
                    ShowWelcomePage();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to initialize the viewer:\n\n{ex.Message}",
                    "LightMD - Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            base.OnFormClosed(e);
        }

        // --- theme ---------------------------------------------------------

        /// <summary>
        /// Reads the per-user "app" theme Windows exposes for desktop apps.
        /// Absent or unreadable means light, which is the Windows default.
        /// </summary>
        private static ViewerTheme DetectSystemTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int light && light == 0
                    ? ViewerTheme.Dark
                    : ViewerTheme.Light;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
            {
                return ViewerTheme.Light;
            }
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle))
                return;

            var theme = DetectSystemTheme();
            if (theme == _theme)
                return;

            _theme = theme;
            ApplyThemeToChrome();

            // ColorCode's palette is baked into the HTML as inline styles, so
            // the document has to be rendered again rather than restyled.
            Rerender();
        }

        /// <summary>
        /// Matches the window and the WebView2 background to the theme so a
        /// reload doesn't flash white before the document paints.
        /// </summary>
        private void ApplyThemeToChrome()
        {
            var background = _theme == ViewerTheme.Dark
                ? Color.FromArgb(0x0D, 0x11, 0x17)
                : Color.White;

            BackColor = background;
            webView.DefaultBackgroundColor = background;
        }

        // --- loading -------------------------------------------------------

        private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (e.Uri == "about:blank")
                return;

            // A link to another local file. Handle it here rather than letting
            // the WebView navigate: markdown opens in the viewer, anything else
            // goes to whichever app owns it.
            if (e.Uri.StartsWith($"https://{MarkdownRenderer.DocumentLinkHost}/", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                // BeginInvoke: the confirmation is modal, and blocking inside a
                // NavigationStarting handler stalls the WebView. Let it unwind.
                var uri = e.Uri;
                BeginInvoke(() => OpenLinkedFile(uri));
                return;
            }

            if (e.Uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                e.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                var uri = e.Uri;
                BeginInvoke(() =>
                {
                    if (ConfirmFollow(uri))
                        OpenExternally(uri);
                });
            }
        }

        /// <summary>
        /// Asks the reader whether to follow a link. Nothing is opened until
        /// they say yes.
        /// </summary>
        private bool ConfirmFollow(string target)
        {
            return MessageBox.Show(
                this,
                $"Open this link?\n\n{target}",
                "LightMD",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        /// <summary>
        /// Follows a rewritten link: markdown files replace the current
        /// document, everything else is handed to the shell.
        /// </summary>
        private void OpenLinkedFile(string uri)
        {
            string path;
            string? fragment;
            try
            {
                var query = System.Web.HttpUtility.ParseQueryString(new Uri(uri).Query);
                path = query["path"] ?? string.Empty;
                fragment = query["fragment"];
            }
            catch (UriFormatException)
            {
                return;
            }

            if (path.Length == 0)
                return;

            if (!ConfirmFollow(path))
                return;

            if (IsMarkdownFile(path))
            {
                LoadMarkdownFile(path, fragment);
            }
            else
            {
                OpenExternally(path);
            }
        }

        /// <summary>
        /// Restores the reader's position once a document has painted: the
        /// heading a link pointed at, or the offset held across a reload.
        /// <para>
        /// <c>ExecuteScriptAsync</c> keeps working while
        /// <c>IsScriptEnabled</c> is false — that setting governs script the
        /// document itself carries, not script the host injects. So this
        /// costs nothing in terms of what an opened file is allowed to run.
        /// </para>
        /// </summary>
        private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            // Cancelling a navigation also raises this event. Leave the pending
            // state alone in that case: the document the reader actually asked
            // for is on its way, and it is the one that should consume it.
            if (!e.IsSuccess)
                return;

            var fragment = _pendingFragment;
            var scrollY = _pendingScrollY;
            _pendingFragment = null;
            _pendingScrollY = null;

            try
            {
                if (!string.IsNullOrEmpty(fragment))
                {
                    var id = JsonSerializer.Serialize(fragment);
                    await webView.CoreWebView2.ExecuteScriptAsync(
                        $"document.getElementById({id})?.scrollIntoView()");
                }
                else if (scrollY is > 0)
                {
                    var y = scrollY.Value.ToString(CultureInfo.InvariantCulture);
                    await webView.CoreWebView2.ExecuteScriptAsync($"window.scrollTo(0,{y})");
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                // The view went away mid-navigation; nothing to restore onto.
            }
        }

        private async Task<double?> GetScrollOffsetAsync()
        {
            try
            {
                var raw = await webView.CoreWebView2.ExecuteScriptAsync("window.scrollY");
                return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                    ? y
                    : null;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                return null;
            }
        }

        private void OpenExternally(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
            {
                MessageBox.Show(
                    $"Could not open:\n\n{ex.Message}",
                    "LightMD",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private static bool IsMarkdownFile(string path)
        {
            var extension = Path.GetExtension(path)?.ToLowerInvariant();
            return !string.IsNullOrEmpty(extension) && MarkdownExtensions.Contains(extension);
        }

        private static readonly HashSet<string> MarkdownExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".md", ".markdown", ".mdown", ".mkd", ".mkdn", ".mdwn", ".mdtxt", ".text"
        };

        private void LoadMarkdownFile(string filePath, string? fragment = null)
        {
            try
            {
                _pendingFragment = fragment;

                if (!File.Exists(filePath))
                {
                    ShowError($"File does not exist:\n\n{filePath}");
                    return;
                }

                if (!IsMarkdownFile(filePath))
                {
                    var extension = Path.GetExtension(filePath);
                    ShowError($"Unsupported file type: {(string.IsNullOrEmpty(extension) ? "(none)" : extension)}\n\nSupported: {string.Join(", ", MarkdownExtensions.Order())}");
                    return;
                }

                var markdown = ReadAllTextShared(filePath);
                var document = MarkdownRenderer.Render(markdown, filePath, _theme);
                ApplyFolderMappings(document.FolderMappings);
                webView.NavigateToString(document.Html);

                _currentFile = filePath;
                Text = $"{Path.GetFileName(filePath)} - LightMD";
                WatchCurrentFile();
            }
            catch (UnauthorizedAccessException)
            {
                ShowError($"Access denied:\n\n{filePath}");
            }
            catch (IOException ex)
            {
                ShowError($"Could not read file:\n\n{ex.Message}");
            }
        }

        /// <summary>
        /// Reads with sharing left wide open. A plain File.ReadAllText fails if
        /// the editor that just saved still holds the file, which is exactly the
        /// moment the watcher fires.
        /// </summary>
        private static string ReadAllTextShared(string filePath)
        {
            using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private void ShowWelcomePage()
        {
            var document = MarkdownRenderer.Render(
                "# LightMD\n\nDrag and drop a Markdown file here, or open one from the command line.",
                null,
                _theme);
            ApplyFolderMappings(document.FolderMappings);
            webView.NavigateToString(document.Html);

            _currentFile = null;
            _watcher.EnableRaisingEvents = false;
            Text = "LightMD";
        }

        /// <summary>Re-renders the document already on screen, in place.</summary>
        private async void Rerender()
        {
            if (_currentFile is null)
            {
                ShowWelcomePage();
                return;
            }

            var scrollY = await GetScrollOffsetAsync();
            LoadMarkdownFile(_currentFile);
            _pendingScrollY = scrollY;
        }

        /// <summary>
        /// Republishes the set of folders WebView2 may serve local images from.
        /// Mappings are per-document, so the previous document's folders are
        /// withdrawn first — a file never keeps access after it is closed.
        /// </summary>
        private void ApplyFolderMappings(IReadOnlyDictionary<string, string> mappings)
        {
            foreach (var host in _mappedHosts)
            {
                webView.CoreWebView2.ClearVirtualHostNameToFolderMapping(host);
            }
            _mappedHosts.Clear();

            foreach (var (host, folder) in mappings)
            {
                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    host, folder, CoreWebView2HostResourceAccessKind.Allow);
                _mappedHosts.Add(host);
            }
        }

        // --- live reload ---------------------------------------------------

        /// <summary>
        /// Watches the folder rather than the file itself: editors that save by
        /// replacing the file would otherwise break the handle and stop events.
        /// </summary>
        private void WatchCurrentFile()
        {
            if (_currentFile is null)
                return;

            var folder = Path.GetDirectoryName(Path.GetFullPath(_currentFile));
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                _watcher.EnableRaisingEvents = false;
                return;
            }

            _watcher.EnableRaisingEvents = false;
            _watcher.Path = folder;
            _watcher.Filter = Path.GetFileName(_currentFile);
            _watcher.EnableRaisingEvents = true;
        }

        private void OnWatchedFileChanged(object sender, FileSystemEventArgs e)
        {
            // Watcher events arrive on a pool thread.
            BeginInvoke(() =>
            {
                _reloadTimer.Stop();
                _reloadTimer.Start();
            });
        }

        private async void OnReloadTimerTick(object? sender, EventArgs e)
        {
            _reloadTimer.Stop();

            if (_currentFile is null || !File.Exists(_currentFile))
                return;

            // Hold the reader's place: a save shouldn't throw them back to the
            // top of the document they were part-way through.
            var scrollY = await GetScrollOffsetAsync();
            LoadMarkdownFile(_currentFile);
            _pendingScrollY = scrollY;
        }

        // --- misc ----------------------------------------------------------

        private void ShowError(string message)
        {
            MessageBox.Show(message, "LightMD - Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void Form1_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            {
                e.Effect = DragDropEffects.Copy;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void Form1_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                if (IsMarkdownFile(files[0]))
                {
                    LoadMarkdownFile(files[0]);
                }
            }
        }
    }
}
