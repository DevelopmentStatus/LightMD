using System.Web;
using Markdig;

namespace LightMD
{
    public static class MarkdownRenderer
    {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseAutoIdentifiers()
            .Build();

        public static string RenderHtml(string markdown, string? filePath)
        {
            var title = string.IsNullOrEmpty(filePath)
                ? "LightMD"
                : HttpUtility.HtmlEncode(Path.GetFileName(filePath));

            var bodyHtml = Markdown.ToHtml(markdown, Pipeline);

            return $@"<!DOCTYPE html>
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
        }
    }
}
