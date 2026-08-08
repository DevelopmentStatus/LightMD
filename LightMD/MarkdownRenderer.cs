using System.Text;
using System.Web;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdownColorCode = Markdown.ColorCode.MarkdownPipelineBuilderExtensions;

namespace LightMD
{
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

        private static readonly MarkdownPipeline Pipeline =
            MarkdownColorCode.UseColorCode(
                new MarkdownPipelineBuilder()
                    .UseAdvancedExtensions()
                    .UseAutoIdentifiers(),
                // ColorCode defaults to its dark palette, which renders as pale
                // grey on this stylesheet's light code background.
                styleDictionary: ColorCode.Styling.StyleDictionary.DefaultLight)
            .Build();

        public static RenderedDocument Render(string markdown, string? filePath)
        {
            var title = string.IsNullOrEmpty(filePath)
                ? "LightMD"
                : HttpUtility.HtmlEncode(Path.GetFileName(filePath));

            var (bodyHtml, folderMappings) = ToHtmlWithGitHubAnchors(markdown, filePath);

            var html = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
    <title>{title}</title>
    <style>
        *, *::before, *::after {{
            box-sizing: border-box;
        }}

        body {{
            font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, ""Helvetica Neue"", Arial, sans-serif;
            font-size: 16px;
            line-height: 1.7;
            color: #1a1a1a;
            background-color: #ffffff;
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
            color: #111;
        }}

        h1 {{ font-size: 2em; border-bottom: 1px solid #e8e8e8; padding-bottom: 0.3em; }}
        h2 {{ font-size: 1.5em; border-bottom: 1px solid #ececec; padding-bottom: 0.25em; }}
        h3 {{ font-size: 1.25em; }}
        h4 {{ font-size: 1.1em; }}

        p {{
            margin-top: 0;
            margin-bottom: 1em;
        }}

        a {{
            color: #0366d6;
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
            color: #6a737d;
            border-left: 4px solid #dfe2e5;
        }}

        blockquote > :last-child {{
            margin-bottom: 0;
        }}

        code {{
            font-family: ""Cascadia Code"", ""Fira Code"", Consolas, ""Courier New"", monospace;
            font-size: 0.9em;
        }}

        :not(pre) > code {{
            background-color: rgba(27,31,35,0.05);
            border-radius: 3px;
            padding: 0.2em 0.4em;
            color: #e01e5a;
        }}

        pre {{
            background-color: #f6f8fa;
            border: 1px solid #e1e4e8;
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
            color: #24292e;
            font-size: 0.9em;
        }}

        table {{
            border-collapse: collapse;
            width: 100%;
            margin-bottom: 1em;
        }}

        table th, table td {{
            border: 1px solid #dfe2e5;
            padding: 8px 12px;
            text-align: left;
        }}

        table th {{
            background-color: #f6f8fa;
            font-weight: 600;
        }}

        table tr:nth-child(even) {{
            background-color: #f6f8fa;
        }}

        img {{
            max-width: 100%;
            height: auto;
        }}

        hr {{
            border: none;
            border-top: 1px solid #e1e4e8;
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
        /// Renders markdown, replacing Markdig's heading ids with GitHub-style slugs.
        /// Markdig drops the leading number from headings like "## 1. Fix the editor"
        /// and collapses runs of separators, so links written against GitHub's scheme
        /// (as generated by GitHub itself and by most Markdown tooling) don't resolve.
        /// </summary>
        private static (string Html, IReadOnlyDictionary<string, string> FolderMappings)
            ToHtmlWithGitHubAnchors(string markdown, string? filePath)
        {
            // Fully qualified: the Markdown.ColorCode namespace shadows Markdig.Markdown.
            var document = Markdig.Markdown.Parse(markdown, Pipeline);

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
            Pipeline.Setup(renderer);
            renderer.Render(document);
            writer.Flush();

            var html = RewriteLocalImages(writer.ToString(), filePath, out var folderMappings);
            return (html, folderMappings);
        }

        /// <summary>
        /// Points every local image at a virtual host WebView2 can serve.
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
        private static string RewriteLocalImages(
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

            return LocalSourceAttribute.Replace(html, match =>
            {
                var url = match.Groups["url"].Value;

                if (!TryResolveLocalFile(url, baseFolder, out var fullPath))
                    return match.Value;

                var folder = Path.GetDirectoryName(fullPath);
                if (string.IsNullOrEmpty(folder))
                    return match.Value;

                if (!hostsByFolder.TryGetValue(folder, out var host))
                {
                    host = $"asset{hostsByFolder.Count}{VirtualHostSuffix}";
                    hostsByFolder[folder] = host;
                    mappings[host] = folder;
                }

                var served = $"https://{host}/{Uri.EscapeDataString(Path.GetFileName(fullPath))}";
                return $"{match.Groups["attr"].Value}{match.Groups["q"].Value}{served}{match.Groups["q"].Value}";
            });
        }

        /// <summary>
        /// Matches the source attribute of an image or media element. Applied to
        /// rendered HTML rather than the syntax tree so that images written as
        /// raw HTML (<c>&lt;img src="..."&gt;</c>, common in READMEs for sizing
        /// and alignment) are handled alongside Markdown <c>![](...)</c> syntax.
        /// Escaped markup inside code blocks can't match: its quotes are
        /// already entities by this point.
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex LocalSourceAttribute =
            new(@"(?<attr>\b(?:src|poster)\s*=\s*)(?<q>[""'])(?<url>[^""']*)\k<q>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
                | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Resolves a Markdown image URL to an existing local file, or returns
        /// false for remote URLs, data URIs and anything that isn't on disk.
        /// </summary>
        private static bool TryResolveLocalFile(string url, string baseFolder, out string fullPath)
        {
            fullPath = string.Empty;

            if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
            {
                // Remote and inline images already work as written.
                if (!absolute.IsFile)
                    return false;

                fullPath = absolute.LocalPath;
                return File.Exists(fullPath);
            }

            // Strip any ?query / #fragment a relative path may carry.
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
