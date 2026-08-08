# LightMD

A lightweight Windows Markdown viewer (WinForms + WebView2).
See [README.md](README.md) for user-facing docs and build instructions.

## Architecture
- `LightMD/Program.cs` — entry point, launches `Form1` with CLI args (`args[0]` = file path to open).
- `LightMD/Form1.cs` — main window. Hosts a WebView2 control (`webView`, defined in the Designer file). Handles:
  - Loading a markdown file passed via CLI arg, or showing a welcome page.
  - Drag-and-drop of `.md`/`.markdown`/`.mdown`/`.mkd`/`.mkdn`/`.mdwn`/`.mdtxt`/`.text` files.
  - Intercepting navigation so `http(s)://` links open in the system browser instead of inside the WebView.
  - WebView2 is locked down: scripting disabled, dev tools disabled, no context menu.
- `LightMD/MarkdownRenderer.cs` — converts markdown to a full HTML document via Markdig, with an inlined CSS stylesheet (GitHub-like styling, light theme only). `NavigateToString` is used to display it — no temp files.
- `LightMD/Form1.Designer.cs` / `.resx` — WinForms designer-generated UI layout.
- `installer/` — WiX v5 package definition (`LightMD.wxs`) and build script (`build.ps1`).

## Stack
- .NET 8, WinForms (`net8.0-windows`), `UseWindowsForms=true`.
- Markdig 0.41.3 for markdown → HTML.
- Markdown.ColorCode + ColorCode.Core for server-side syntax highlighting.
- Microsoft.Web.WebView2 1.0.4129.50 for rendering.
- WiX v5 CLI (`dotnet tool install --global wix --version 5.0.2`) for the MSI. Pin to v5 — v6+ requires accepting the OSMF licence agreement.

## Non-obvious things worth knowing
- **Local images work via virtual host mappings, not embedding.** `NavigateToString` gives the page a `data:` URI, which has no base path and may not read `file:///`. `MarkdownRenderer.RewriteLocalImages` maps each referenced folder to its own `assetN.lightmd.local` host and rewrites the `src`. `Form1.ApplyFolderMappings` withdraws the previous document's mappings before applying the new ones. The rewrite is a regex over the *rendered HTML* rather than an AST walk, deliberately — raw `<img>` tags in Markdown are common and an AST walk over `LinkInline` misses them.
- **Syntax highlighting is server-side on purpose.** ColorCode runs in C#, so `IsScriptEnabled` stays `false`. highlight.js/Prism are more popular but run in the page, which would mean enabling JS for arbitrary opened files. ColorCode also defaults to its *dark* palette — `StyleDictionary.DefaultLight` is passed explicitly or code renders pale grey on the light background.
- **`Markdown.ColorCode` shadows `Markdig.Markdown`.** The namespace collides with the class, so `Markdig.Markdown.Parse` must be fully qualified.
- **Heading anchors are hand-rolled.** Markdig's auto-identifiers don't match GitHub's scheme (it drops the leading `1.` from `## 1. Foo` and collapses repeated separators), which breaks generated tables of contents. `MarkdownRenderer.ToHtmlWithGitHubAnchors` rewrites every heading id with GitHub's rule. Markdig's own `AutoIdentifierOptions.GitHub` does *not* match either — it was tested.
- **The WebView2 user-data folder is pinned** to `%LOCALAPPDATA%\LightMD\WebView2`. Its default is a directory beside the `.exe`, which is unwritable under `Program Files` — the installed app fails to launch without this.
- **`Form1.Designer.cs` must call `Controls.Add(webView)`.** It was missing originally: the control was constructed and sized but never attached to the form's visual tree, so the window rendered blank while the page loaded correctly underneath. Easy to reintroduce if the designer file is regenerated.
- **`installer/LightMD.wxs` uses registry values, not `ProgId`/`Verb` elements**, for file association — those reference a `File` id, and the payload is harvested by `<Files>`, which generates its own ids.
- **Don't change the `UpgradeCode`** in the WiX file; it's what makes upgrades replace rather than stack.
- **The MSI version comes from `build.ps1 -Version`**, injected as the `$(Version)` preprocessor variable. It was previously hardcoded in the `.wxs` while `-Version` only renamed the output file — which produced packages that all claimed 1.0.0.0 and therefore never triggered an upgrade. `build.ps1` now validates the four-part format. This is separate from the assembly `<Version>` in the csproj.
- **`AllowSameVersionUpgrades="yes"` is deliberate** — without it, reinstalling a rebuilt MSI at the same version number adds a *second* entry to Installed apps instead of replacing the first. Verified end-to-end: install 1.0.0.0 → install 1.1.0.0 → one entry; reinstall same version → one entry; downgrade → blocked.

## Conventions
- No tests currently exist.
- No dark mode / theming yet — single light stylesheet baked into `MarkdownRenderer.RenderHtml`.
- Keep it minimal — this is intentionally a "light" viewer, avoid scope creep (no editing, no plugins, no settings UI) unless asked.

## Known limitations (documented in README)
Relative *links* between documents still don't resolve — only images are rewritten. `srcset` is not rewritten either (only `src` and `poster`).
