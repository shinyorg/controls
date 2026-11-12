# Markdown Controls

[← All Shiny Controls](../../README.md)

> Separate NuGet packages: `Shiny.Maui.Controls.Markdown` / `Shiny.Blazor.Controls.Markdown`

Render and edit markdown content using native MAUI controls — no WebView required on MAUI. Auto-resolves Light/Dark theming. Available for both MAUI and Blazor.

| Viewer | Editor |
|:---:|:---:|
| ![Viewer](../../assets/markdown-view.png) | ![Editor](../../assets/markdown-editor.png) |

**MarkdownView** — Read-only markdown renderer:

```xml
<md:MarkdownView Markdown="{Binding DocumentContent}" Padding="16" />
```

| Property | Type | Description |
|---|---|---|
| Markdown | string | Markdown content to render |
| Theme | MarkdownTheme? | Rendering theme (auto Light/Dark if null) |
| IsScrollEnabled | bool | `true` (default): the view scrolls its own content. `false`: no scroller, the view sizes to its content, for use inside a list or page that already scrolls |

On Blazor a long unbroken word in the content (a namespace in a heading, a bare URL) wraps instead of
widening the view, so the view never turns into a sideways scroller that moves only a few pixels. Code
blocks still scroll sideways on their own.

**Streaming.** Setting `Markdown` again rebuilds only the top-level blocks whose source changed (both hosts), so a reply that grows a token at a time re-renders its last paragraph rather than the whole document. AgentView relies on this; coalesce updates to ~30 per second all the same.

Events: `LinkTapped` — fired when a link is tapped; set `Handled = true` to prevent default browser launch.

**MarkdownEditor** — Editor with formatting toolbar and live preview:

```xml
<md:MarkdownEditor Markdown="{Binding NoteContent, Mode=TwoWay}"
                   Placeholder="Start writing..."
                   Padding="8" />
```

| Property | Type | Description |
|---|---|---|
| Markdown | string | Markdown content (TwoWay) |
| Theme | MarkdownTheme? | Preview theme (auto Light/Dark if null) |
| Placeholder | string | Placeholder text |
| ToolbarItems | IReadOnlyList\<MarkdownToolbarItem\>? | Toolbar buttons (default set provided) |
| IsPreviewVisible | bool | Toggle preview pane (TwoWay) |
| ToolbarBackgroundColor | Color? | Toolbar background |
| EditorBackgroundColor | Color? | Editor background |
| ShowToolbarInKeyboard | bool | Repeat `ToolbarItems` on the keyboard accessory bar (default `true`; MAUI iOS/Android) |
| Accessory | KeyboardAccessoryView? | Your own accessory bar instead of the generated one |

**Keyboard toolbar (MAUI, iOS + Android)** — on a phone the toolbar above the editor is covered by the
soft keyboard the moment you start typing, so the same `ToolbarItems` are also rendered as icons on a
`KeyboardAccessoryView` docked to the top of the keyboard: a horizontally scrolling row of the enabled
items (grouped exactly like the toolbar) with a pinned **Done** — the only way to dismiss a multi-line
keyboard, whose return key inserts a newline. On by default; set `ShowToolbarInKeyboard="False"` to opt
out, or set `Accessory` to supply your own bar.

```xml
<md:MarkdownEditor Markdown="{Binding NoteContent, Mode=TwoWay}"
                   ToolbarItems="{Binding MyItems}"
                   ShowToolbarInKeyboard="True" />
```

**Features:**
- Formatting toolbar: bold, italic, headings, lists, code, links, blockquotes
- The same toolbar on the keyboard accessory bar (iOS/Android)
- Live preview toggle
- Blazor side-by-side mode (`IsSplit`, on by default): when each pane would be narrower than 260px the preview wraps under the source, so it stays usable on a phone
- Auto-growing editor
- Full Markdig support: tables, task lists, strikethrough, fenced code blocks
- Tables (MAUI) draw one rounded outer border in `TableBorderColor` and single 1px dividers between rows and columns — no per-cell boxes, so inner lines are never doubled; the header row is shaded with `TableHeaderBackgroundColor`
- Customizable themes with colors, font sizes, and spacing
- Custom toolbar item support

## Dark mode

Left unset, `Theme` resolves to `MarkdownTheme.Themed` — every colour in it is a Shiny theme token,
so the rendered markdown follows the app's scheme *and* its theme pack. `MarkdownTheme.Light` and
`MarkdownTheme.Dark` stay literal palettes, for a preview that has to look the same whatever the app
is doing. See [Styling & theming](styling.md#dark-mode).
