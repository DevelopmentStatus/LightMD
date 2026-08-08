# LightMD

A lightweight Windows Markdown viewer (WinForms + WebView2). Not a repo (no git yet).

## Architecture
- `LightMD/Program.cs` — entry point, launches `Form1` with CLI args (`args[0]` = file path to open).
- `LightMD/Form1.cs` — main window. Hosts a WebView2 control (`webView`, defined in the Designer file). Handles:
  - Loading a markdown file passed via CLI arg, or showing a welcome page.
  - Drag-and-drop of `.md`/`.markdown`/`.mdown`/`.mkd`/`.mkdn`/`.mdwn`/`.mdtxt`/`.text` files.
  - Intercepting navigation so `http(s)://` links open in the system browser instead of inside the WebView.
  - WebView2 is locked down: scripting disabled, dev tools disabled, no context menu.
- `LightMD/MarkdownRenderer.cs` — converts markdown to a full HTML document via Markdig (`UseAdvancedExtensions`, `UseAutoIdentifiers`), with an inlined CSS stylesheet (GitHub-like styling, light theme only). `NavigateToString` is used to display it — no temp files.
- `LightMD/Form1.Designer.cs` / `.resx` — WinForms designer-generated UI layout.

## Stack
- .NET 8, WinForms (`net8.0-windows`), `UseWindowsForms=true`.
- Markdig 0.40.0 for markdown → HTML.
- Microsoft.Web.WebView2 1.0.4129.50 for rendering.

## Conventions
- No tests currently exist.
- No dark mode / theming yet — single light stylesheet baked into `MarkdownRenderer.RenderHtml`.
- Keep it minimal — this is intentionally a "light" viewer, avoid scope creep (no editing, no plugins, no settings UI) unless asked.
