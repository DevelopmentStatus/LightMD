using System.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace LightMD
{
    public partial class Form1 : Form
    {
        private readonly string[]? _args;
        private readonly List<string> _mappedHosts = new();

        public Form1(string[]? args = null)
        {
            _args = args;
            InitializeComponent();
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

        private void OnNavigationStarting(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
        {
            if (e.Uri == "about:blank")
                return;

            if (e.Uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                e.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = e.Uri,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Could not open link:\n\n{ex.Message}",
                        "LightMD",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
        }

        private void LoadMarkdownFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    ShowError($"File does not exist:\n\n{filePath}");
                    return;
                }

                var extension = Path.GetExtension(filePath)?.ToLowerInvariant();
                var validExtensions = new HashSet<string>
                {
                    ".md", ".markdown", ".mdown", ".mkd", ".mkdn", ".mdwn", ".mdtxt", ".text"
                };

                if (string.IsNullOrEmpty(extension) || !validExtensions.Contains(extension))
                {
                    ShowError($"Unsupported file type: {extension ?? "(none)"}\n\nSupported: .md, .markdown, .mdown, .mkd, .mkdn, .mdwn, .mdtxt, .text");
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
                var extension = Path.GetExtension(files[0])?.ToLowerInvariant();
                var validExtensions = new HashSet<string>
                {
                    ".md", ".markdown", ".mdown", ".mkd", ".mkdn", ".mdwn", ".mdtxt", ".text"
                };

                if (!string.IsNullOrEmpty(extension) && validExtensions.Contains(extension))
                {
                    LoadMarkdownFile(files[0]);
                }
            }
        }
    }
}
