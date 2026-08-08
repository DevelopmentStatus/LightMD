using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace LightMD
{
    public partial class Form1 : Form
    {
        private readonly string[]? _args;
        private readonly List<string> _mappedHosts = new();

        /// <summary>Heading to jump to once the next document has loaded.</summary>
        private string? _pendingFragment;

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
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            try
            {
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
                OpenLinkedFile(e.Uri);
                return;
            }

            if (e.Uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                e.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                OpenExternally(e.Uri);
            }
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

            if (IsMarkdownFile(path))
            {
                // BeginInvoke: navigating from inside a NavigationStarting
                // handler is not allowed, so let this one unwind first.
                BeginInvoke(() => LoadMarkdownFile(path, fragment));
            }
            else
            {
                OpenExternally(path);
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

        /// <summary>
        /// Restores the reader's position once a document has painted: the
        /// heading a link pointed at.
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
            _pendingFragment = null;

            if (string.IsNullOrEmpty(fragment))
                return;

            try
            {
                var id = JsonSerializer.Serialize(fragment);
                await webView.CoreWebView2.ExecuteScriptAsync(
                    $"document.getElementById({id})?.scrollIntoView()");
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                // The view went away mid-navigation; nothing to restore onto.
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

                var markdown = File.ReadAllText(filePath);
                var document = MarkdownRenderer.Render(markdown, filePath);
                ApplyFolderMappings(document.FolderMappings);
                webView.NavigateToString(document.Html);
                Text = $"{Path.GetFileName(filePath)} - LightMD";
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

        private void ShowWelcomePage()
        {
            var document = MarkdownRenderer.Render(
                "# LightMD\n\nDrag and drop a Markdown file here, or open one from the command line.",
                null);
            ApplyFolderMappings(document.FolderMappings);
            webView.NavigateToString(document.Html);
            Text = "LightMD";
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
