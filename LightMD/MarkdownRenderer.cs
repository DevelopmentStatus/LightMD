using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using ColorCode.Styling;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdownColorCode = Markdown.ColorCode.MarkdownPipelineBuilderExtensions;

namespace LightMD
{
    /// <summary>Which palette a document is rendered with.</summary>
    public enum ViewerTheme
    {
        Light,
        Dark
    }

    /// <summary>
    /// The rendered document, plus the folder mappings WebView2 needs in order
    /// to serve the local images the document refers to.
    /// </summary>
    /// <param name="Html">A complete, self-contained HTML document.</param>
    /// <param name="FolderMappings">Virtual host name to local folder.</param>
    public sealed record RenderedDocument(
        string Html,
        IReadOnlyDictionary<string, string> FolderMappings);

    public static class MarkdownRenderer
    {
        /// <summary>
        /// Suffix for the virtual host names local images are served from.
        /// Not a real TLD, so it can never collide with a site on the network.
        /// </summary>
        private const string VirtualHostSuffix = ".lightmd.local";

        /// <summary>
        /// Links to local files are pointed here. Nothing ever serves this host —
        /// <see cref="Form1"/> cancels the navigation and handles the target
        /// itself, which is why the path travels in the URL rather than needing a
        /// folder mapping. Keeping links unmapped means following one grants no
        /// read access to the folder it lives in.
        /// </summary>
        public const string DocumentLinkHost = "open" + VirtualHostSuffix;

        private static readonly MarkdownPipeline LightPipeline = BuildPipeline(StyleDictionary.DefaultLight);
        private static readonly MarkdownPipeline DarkPipeline = BuildPipeline(StyleDictionary.DefaultDark);

        private static MarkdownPipeline BuildPipeline(StyleDictionary styles) =>
            MarkdownColorCode.UseColorCode(
                new MarkdownPipelineBuilder()
                    .UseAdvancedExtensions()
                    .UseAutoIdentifiers(),
                // ColorCode bakes colours in as inline styles, so the palette has
                // to be chosen now rather than switched in CSS later. That is why
                // a theme change re-renders instead of restyling.
                styleDictionary: styles)
            .Build();

        public static RenderedDocument Render(string markdown, string? filePath, ViewerTheme theme)
        {
            var title = string.IsNullOrEmpty(filePath)
                ? "LightMD"
                : HttpUtility.HtmlEncode(Path.GetFileName(filePath));

            var pipeline = theme == ViewerTheme.Dark ? DarkPipeline : LightPipeline;
            var (bodyHtml, folderMappings) = ToHtmlWithGitHubAnchors(markdown, filePath, pipeline);

            var html = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
    <title>{title}</title>
    <style>
{ThemeVariables(theme)}

        *, *::before, *::after {{
            box-sizing: border-box;
        }}

        body {{
            font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, ""Helvetica Neue"", Arial, sans-serif;
            font-size: 16px;
            line-height: 1.7;
            color: var(--text);
            background-color: var(--bg);
            max-width: 900px;
            margin: 0 auto;
            padding: 32px 40px;
            word-wrap: break-word;
            overflow-wrap: break-word;
            caret-color: transparent;
        }}

        h1, h2, h3, h4, h5, h6 {{
            margin-top: 1.5em;
            margin-bottom: 0.5em;
            font-weight: 600;
            line-height: 1.3;
            color: var(--heading);
        }}

        h1 {{ font-size: 2em; border-bottom: 1px solid var(--rule-strong); padding-bottom: 0.3em; }}
        h2 {{ font-size: 1.5em; border-bottom: 1px solid var(--rule); padding-bottom: 0.25em; }}
        h3 {{ font-size: 1.25em; }}
        h4 {{ font-size: 1.1em; }}

        p {{
            margin-top: 0;
            margin-bottom: 1em;
        }}

        a {{
            color: var(--link);
            text-decoration: none;
        }}

        a:hover {{
            text-decoration: underline;
        }}

        ul, ol {{
            margin-top: 0;
            margin-bottom: 1em;
            padding-left: 2em;
        }}

        li {{
            margin-bottom: 0.25em;
        }}

        blockquote {{
            margin: 1em 0;
            padding: 0.5em 1em;
            color: var(--muted);
            border-left: 4px solid var(--border);
        }}

        blockquote > :last-child {{
            margin-bottom: 0;
        }}

        code {{
            font-family: ""Cascadia Code"", ""Fira Code"", Consolas, ""Courier New"", monospace;
            font-size: 0.9em;
        }}

        :not(pre) > code {{
            background-color: var(--inline-code-bg);
            border-radius: 3px;
            padding: 0.2em 0.4em;
            color: var(--inline-code-text);
        }}

        pre {{
            background-color: var(--code-bg);
            border: 1px solid var(--code-border);
            border-radius: 6px;
            padding: 16px;
            overflow-x: auto;
            margin-bottom: 1em;
            line-height: 1.45;
        }}

        pre > code {{
            background: none;
            border: none;
            padding: 0;
            color: var(--text);
            font-size: 0.9em;
        }}

        table {{
            border-collapse: collapse;
            width: 100%;
            margin-bottom: 1em;
        }}

        table th, table td {{
            border: 1px solid var(--border);
            padding: 8px 12px;
            text-align: left;
        }}

        table th {{
            background-color: var(--surface);
            font-weight: 600;
        }}

        table tr:nth-child(even) {{
            background-color: var(--surface);
        }}

        img {{
            max-width: 100%;
            height: auto;
        }}

        hr {{
            border: none;
            border-top: 1px solid var(--code-border);
            margin: 2em 0;
        }}

        strong {{
            font-weight: 600;
        }}

        em {{
            font-style: italic;
        }}

        html {{
            scroll-behavior: smooth;
            background-color: var(--bg);
        }}
    </style>
</head>
<body>
{bodyHtml}
</body>
</html>";

            return new RenderedDocument(html, folderMappings);
        }

        /// <summary>
        /// The palette for a theme. Everything else in the stylesheet refers to
        /// these, so the two themes never drift apart structurally.
        /// </summary>
        private static string ThemeVariables(ViewerTheme theme) => theme == ViewerTheme.Dark
            ? @"        :root {
            --bg: #0d1117;
            --surface: #161b22;
            --text: #c9d1d9;
            --heading: #e6edf3;
            --muted: #8b949e;
            --link: #58a6ff;
            --border: #30363d;
            --rule: #21262d;
            --rule-strong: #30363d;
            --code-bg: #161b22;
            --code-border: #30363d;
            --inline-code-bg: rgba(110,118,129,0.4);
            --inline-code-text: #ff7b72;
        }"
            : @"        :root {
            --bg: #ffffff;
            --surface: #f6f8fa;
            --text: #1a1a1a;
            --heading: #111111;
            --muted: #6a737d;
            --link: #0366d6;
            --border: #dfe2e5;
            --rule: #ececec;
            --rule-strong: #e8e8e8;
            --code-bg: #f6f8fa;
            --code-border: #e1e4e8;
            --inline-code-bg: rgba(27,31,35,0.05);
            --inline-code-text: #e01e5a;
        }";

        /// <summary>
        /// Renders markdown, replacing Markdig's heading ids with GitHub-style slugs.
        /// Markdig drops the leading number from headings like "## 1. Fix the editor"
        /// and collapses runs of separators, so links written against GitHub's scheme
        /// (as generated by GitHub itself and by most Markdown tooling) don't resolve.
        /// </summary>
        private static (string Html, IReadOnlyDictionary<string, string> FolderMappings)
            ToHtmlWithGitHubAnchors(string markdown, string? filePath, MarkdownPipeline pipeline)
        {
            // Fully qualified: the Markdown.ColorCode namespace shadows Markdig.Markdown.
            var document = Markdig.Markdown.Parse(markdown, pipeline);

            var used = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var heading in document.Descendants<HeadingBlock>())
            {
                var slug = GitHubSlug(GetHeadingText(heading));
                if (slug.Length == 0)
                    continue;

                // GitHub disambiguates repeated headings with -1, -2, ...
                if (used.TryGetValue(slug, out var count))
                {
                    used[slug] = count + 1;
                    slug = $"{slug}-{count}";
                }
                else
                {
                    used[slug] = 1;
                }

                heading.GetAttributes().Id = slug;
            }

            using var writer = new StringWriter();
            var renderer = new HtmlRenderer(writer);
            pipeline.Setup(renderer);
            renderer.Render(document);
            writer.Flush();

            var html = RewriteLocalReferences(writer.ToString(), filePath, out var folderMappings);
            return (html, folderMappings);
        }

        /// <summary>
        /// Points local images at a virtual host WebView2 can serve, and local
        /// links at the host <see cref="Form1"/> intercepts.
        /// <para>
        /// The document is displayed with <c>NavigateToString</c>, which gives the
        /// page a <c>data:</c> URI — there is no base path for a relative
        /// <c>src</c> to resolve against, and a <c>data:</c> page may not read
        /// <c>file:///</c> URLs, so local images simply fail to load. Mapping
        /// each referenced folder to its own virtual host fixes that without
        /// copying images or embedding them in the page, so size and format are
        /// unconstrained — whatever the browser can decode, LightMD shows.
        /// </para>
        /// <para>
        /// Only folders the document actually references are exposed, and each
        /// gets its own host, so an image next to the file and one several
        /// directories away both work.
        /// </para>
        /// </summary>
        private static string RewriteLocalReferences(
            string html, string? filePath,
            out IReadOnlyDictionary<string, string> folderMappings)
        {
            var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            folderMappings = mappings;

            if (string.IsNullOrEmpty(filePath))
                return html;

            var baseFolder = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (string.IsNullOrEmpty(baseFolder))
                return html;

            // folder -> virtual host, so repeated folders reuse one mapping.
            var hostsByFolder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string ServeFile(string fullPath)
            {
                var folder = Path.GetDirectoryName(fullPath)!;
                if (!hostsByFolder.TryGetValue(folder, out var host))
                {
                    host = $"asset{hostsByFolder.Count}{VirtualHostSuffix}";
                    hostsByFolder[folder] = host;
                    mappings[host] = folder;
                }
                return $"https://{host}/{Uri.EscapeDataString(Path.GetFileName(fullPath))}";
            }

            html = LocalSourceAttribute.Replace(html, match =>
            {
                if (!TryResolveLocalFile(match.Groups["url"].Value, baseFolder, out var fullPath))
                    return match.Value;

                return Rebuild(match, ServeFile(fullPath));
            });

            // srcset carries a comma-separated candidate list, each entry a URL
            // followed by an optional width or density descriptor.
            html = SrcsetAttribute.Replace(html, match =>
            {
                var candidates = match.Groups["url"].Value.Split(',');
                var rewritten = new List<string>(candidates.Length);
                var changed = false;

                foreach (var candidate in candidates)
                {
                    var entry = candidate.Trim();
                    if (entry.Length == 0)
                        continue;

                    var split = entry.IndexOfAny(new[] { ' ', '\t' });
                    var url = split < 0 ? entry : entry[..split];
                    var descriptor = split < 0 ? string.Empty : entry[split..];

                    if (TryResolveLocalFile(url, baseFolder, out var fullPath))
                    {
                        rewritten.Add(ServeFile(fullPath) + descriptor);
                        changed = true;
                    }
                    else
                    {
                        rewritten.Add(entry);
                    }
                }

                return changed ? Rebuild(match, string.Join(", ", rewritten)) : match.Value;
            });

            html = LocalHrefAttribute.Replace(html, match =>
            {
                var url = match.Groups["url"].Value;
                if (!TryResolveLocalFile(url, baseFolder, out var fullPath))
                    return match.Value;

                // The fragment survives the round trip so links like
                // "other.md#usage" still land on the right heading.
                var hash = url.IndexOf('#');
                var fragment = hash >= 0 ? url[hash..] : string.Empty;

                var target = $"https://{DocumentLinkHost}/?path={Uri.EscapeDataString(fullPath)}"
                           + (fragment.Length > 0 ? $"&fragment={Uri.EscapeDataString(fragment[1..])}" : string.Empty);
                return Rebuild(match, target);
            });

            return html;

            static string Rebuild(Match match, string value) =>
                $"{match.Groups["attr"].Value}{match.Groups["q"].Value}{value}{match.Groups["q"].Value}";
        }

        /// <summary>
        /// Matches the source attribute of an image or media element. Applied to
        /// rendered HTML rather than the syntax tree so that images written as
        /// raw HTML (<c>&lt;img src="..."&gt;</c>, common in READMEs for sizing
        /// and alignment) are handled alongside Markdown <c>![](...)</c> syntax.
        /// Escaped markup inside code blocks can't match: its quotes are
        /// already entities by this point.
        /// </summary>
        private static readonly Regex LocalSourceAttribute =
            new(@"(?<attr>\b(?:src|poster)\s*=\s*)(?<q>[""'])(?<url>[^""']*)\k<q>",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SrcsetAttribute =
            new(@"(?<attr>\bsrcset\s*=\s*)(?<q>[""'])(?<url>[^""']*)\k<q>",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LocalHrefAttribute =
            new(@"(?<attr>\bhref\s*=\s*)(?<q>[""'])(?<url>[^""']*)\k<q>",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Resolves a Markdown URL to an existing local file, or returns false
        /// for remote URLs, data URIs, bare fragments and anything not on disk.
        /// </summary>
        private static bool TryResolveLocalFile(string url, string baseFolder, out string fullPath)
        {
            fullPath = string.Empty;

            if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
            {
                // Remote and inline references already work as written.
                if (!absolute.IsFile)
                    return false;

                fullPath = absolute.LocalPath;
                return File.Exists(fullPath);
            }

            // Strip any ?query / #fragment a relative path may carry. A link that
            // is only a fragment leaves nothing behind and is left alone.
            var path = url.Split('?', '#')[0];
            if (path.Length == 0)
                return false;

            path = Uri.UnescapeDataString(path.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                fullPath = Path.GetFullPath(Path.Combine(baseFolder, path));
            }
            catch (ArgumentException)
            {
                return false;   // invalid characters in the path
            }

            return File.Exists(fullPath);
        }

        private static string GetHeadingText(HeadingBlock heading)
        {
            if (heading.Inline is null)
                return string.Empty;

            var text = new StringBuilder();
            foreach (var inline in heading.Inline.Descendants())
            {
                switch (inline)
                {
                    case LiteralInline literal:
                        text.Append(literal.Content.AsSpan());
                        break;
                    case CodeInline code:
                        text.Append(code.Content);
                        break;
                }
            }
            return text.ToString();
        }

        /// <summary>
        /// GitHub's slug rules: lower-case, drop everything that isn't a letter,
        /// digit, underscore, hyphen or space, then turn spaces into hyphens.
        /// Removed characters leave their surrounding spaces behind, so
        /// "UI framework / replicate PZ's UI" becomes "ui-framework--replicate-pzs-ui".
        /// </summary>
        private static string GitHubSlug(string text)
        {
            var slug = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                if (c == ' ')
                    slug.Append('-');
                else if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    slug.Append(char.ToLowerInvariant(c));
            }
            return slug.ToString();
        }
    }
}
