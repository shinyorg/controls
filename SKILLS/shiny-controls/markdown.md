# Markdown Controls

Two controls for rendering and editing markdown content. Uses Markdig for parsing. Separate NuGet package from the main controls library.

**NuGet Package**: `Shiny.Maui.Controls.Markdown`
**Namespace**: `Shiny.Maui.Controls.Markdown`
**XAML Namespace**: `http://shiny.net/maui/markdown` (prefix: `md`)

```xml
xmlns:md="http://shiny.net/maui/markdown"
```

## MarkdownView

A read-only markdown renderer that converts markdown text to native MAUI controls (Labels, Grids, BoxViews, Borders) with theming and link handling.

```xml
<md:MarkdownView Markdown="{Binding Markdown}" Padding="16" />
```

Streaming text (an AI reply arriving a token at a time) can set `Markdown` on every update: only the top-level blocks whose source changed are rebuilt, so the earlier paragraphs are not re-parsed into new views each time. Still coalesce updates to ~30 per second.

```xml
<!-- inside a scrolling list: no inner scroller -->
<md:MarkdownView Markdown="{Binding Text}" IsScrollEnabled="False" />
```

### MarkdownView Properties

| Property | Type | Default | Binding Mode | Description |
|---|---|---|---|---|
| `Markdown` | `string` | `""` | OneWay | The markdown content to render |
| `Theme` | `MarkdownTheme?` | `null` | OneWay | Rendering theme; auto-resolves Light/Dark based on app theme if null |
| `IsScrollEnabled` | `bool` | `true` | OneWay | `true`: the view scrolls its own content. `false`: no scroller at all, the view sizes to its content (use inside a chat bubble or any page that already scrolls) |

### MarkdownView Events

| Event | EventArgs | Description |
|---|---|---|
| `LinkTapped` | `LinkTappedEventArgs` | Fired when a link is tapped; set `Handled = true` to prevent default browser launch |

### LinkTappedEventArgs

| Property | Type | Description |
|---|---|---|
| `Url` | `string` | The URL of the tapped link |
| `Handled` | `bool` | Set to `true` to prevent default browser launch |

## MarkdownEditor

A markdown editor with formatting toolbar, live preview toggle, and customizable toolbar items.

```xml
<md:MarkdownEditor Markdown="{Binding Markdown, Mode=TwoWay}"
                   Placeholder="Start writing markdown..."
                   Padding="8" />
```

### MarkdownEditor Properties

| Property | Type | Default | Binding Mode | Description |
|---|---|---|---|---|
| `Markdown` | `string` | `""` | TwoWay | The markdown content being edited |
| `Theme` | `MarkdownTheme?` | `null` | OneWay | Theme for preview rendering |
| `Placeholder` | `string` | `"Write markdown here..."` | OneWay | Placeholder text in the editor |
| `ToolbarItems` | `IReadOnlyList<MarkdownToolbarItem>?` | `MarkdownToolbarItems.Default` | OneWay | Formatting toolbar buttons |
| `IsPreviewVisible` | `bool` | `false` | TwoWay | Show/hide the preview pane |
| `ToolbarBackgroundColor` | `Color?` | `null` | OneWay | Toolbar background color |
| `EditorBackgroundColor` | `Color?` | `null` | OneWay | Editor text area background color |
| `ShowToolbarInKeyboard` | `bool` | `true` | OneWay | Repeat `ToolbarItems` on the soft keyboard's accessory bar (iOS/Android) |
| `Accessory` | `KeyboardAccessoryView?` | `null` | OneWay | Your own accessory bar, instead of the generated one |

### MarkdownEditor Events

| Event | EventArgs | Description |
|---|---|---|
| `LinkTapped` | `LinkTappedEventArgs` | Forwarded from preview link taps |
| `TextChanged` | `TextChangedEventArgs` | Fired when editor text changes |

### MarkdownEditor Features

- **Formatting toolbar**: Buttons for bold, italic, headings, lists, code, links, etc.
- **Toolbar on the keyboard**: the same items as icons on the soft keyboard's accessory bar (iOS/Android)
- **Live preview**: Toggle between edit and preview modes with eye/pencil icon button
- **Auto-growing editor**: Editor height grows as you type
- **Custom toolbar**: Replace default toolbar items with a custom set
- **Blazor split view** (`IsSplit`, default `true`): source and preview side by side; a container query wraps the preview under the source when each pane would be under 260px, so don't turn `IsSplit` off just for phones

### Keyboard accessory bar (iOS + Android)

`ShowToolbarInKeyboard` is **on by default**. Whatever is in `ToolbarItems` is repeated as icons on a
`KeyboardAccessoryView` docked to the top of the soft keyboard while the editor has focus — a
horizontally scrolling row, grouped with the same separators as the toolbar, plus a pinned **Done**.
The Done matters: this is a multi-line editor, so the return key inserts a newline and there is
otherwise no way to dismiss the keyboard.

```xml
<!-- default: the enabled toolbar items appear on the keyboard -->
<md:MarkdownEditor Markdown="{Binding Notes, Mode=TwoWay}" />

<!-- toolbar above the editor only -->
<md:MarkdownEditor Markdown="{Binding Notes, Mode=TwoWay}"
                   ShowToolbarInKeyboard="False" />
```

Set `ToolbarItems` and the accessory bar follows it — the two never drift apart. Set `Accessory` to
supply a bar of your own and `ShowToolbarInKeyboard` is ignored.

Windows, macOS, Linux and Blazor have no soft keyboard to decorate: the properties compile and do
nothing there. Do **not** generate a hand-rolled "toolbar above the keyboard" for this — the accessory
bar is the supported way.

## MarkdownTheme

Comprehensive theming for rendered markdown appearance. Auto-resolves Light or Dark based on `Application.Current?.RequestedTheme` when set to null.

### Static Themes

```csharp
MarkdownTheme.Light    // Light color scheme
MarkdownTheme.Dark     // Dark color scheme
```

### Theme Color Properties

| Property | Light Default | Dark Default | Description |
|---|---|---|---|
| `TextColor` | Black | `#E5E7EB` | Main text color |
| `MutedTextColor` | `#6B7280` | `#9CA3AF` | Dimmed text |
| `LinkColor` | `#2563EB` | `#60A5FA` | Hyperlink color |
| `CodeBackgroundColor` | `#F3F4F6` | `#374151` | Inline code background |
| `CodeTextColor` | `#D946EF` | `#F472B6` | Inline code text |
| `CodeBlockBackgroundColor` | `#1F2937` | `#111827` | Code block background |
| `CodeBlockTextColor` | `#E5E7EB` | `#D1D5DB` | Code block text |
| `BlockquoteBorderColor` | `#D1D5DB` | `#4B5563` | Blockquote left border |
| `BlockquoteBackgroundColor` | `#F9FAFB` | `#1F2937` | Blockquote background |
| `HorizontalRuleColor` | `#E5E7EB` | `#374151` | Divider color |
| `TableBorderColor` | `#E5E7EB` | `#374151` | Table outer border and the single 1px row/column dividers (MAUI draws no per-cell borders) |
| `TableHeaderBackgroundColor` | `#F3F4F6` | `#1F2937` | Table header background |

### Theme Font/Spacing Properties

| Property | Default | Description |
|---|---|---|
| `BaseFontSize` | `16` | Default text size |
| `H1FontSize` | `32` | Heading 1 size |
| `H2FontSize` | `24` | Heading 2 size |
| `H3FontSize` | `20` | Heading 3 size |
| `H4FontSize` | `18` | Heading 4 size |
| `H5FontSize` | `16` | Heading 5 size |
| `H6FontSize` | `14` | Heading 6 size |
| `CodeFontSize` | `14` | Code font size |
| `BlockSpacing` | `12` | Vertical spacing between block elements |
| `ListIndent` | `24` | Left indent for list items |
| `CodeFontFamily` | `""` | Monospace font family |

## MarkdownToolbarItem

```csharp
public record MarkdownToolbarItem(
    string Label,      // Display name
    string Icon,       // Emoji/text icon
    string Prefix,     // Text before selection
    string Suffix,     // Text after selection
    bool IsBlockLevel = false
);
```

### Pre-defined Toolbar Items

Available via `MarkdownToolbarItems.*`:

| Item | Icon | Description |
|---|---|---|
| `Bold` | B | **Bold text** |
| `Italic` | I | *Italic text* |
| `Strikethrough` | S | ~~Strikethrough~~ |
| `InlineCode` | `</>` | `inline code` |
| `Link` | link | Hyperlink |
| `Image` | img | Image |
| `H1` | H1 | Heading 1 |
| `H2` | H2 | Heading 2 |
| `H3` | H3 | Heading 3 |
| `BulletList` | list | Unordered list |
| `NumberedList` | 1. | Ordered list |
| `TaskList` | check | Task list |
| `Quote` | " | Blockquote |
| `CodeBlock` | `{}` | Code block |
| `HorizontalRule` | --- | Horizontal divider |

### Toolbar Collections

```csharp
MarkdownToolbarItems.Default  // Recommended set: Bold, Italic, InlineCode, H1-H3, Lists, Link, Quote, CodeBlock
MarkdownToolbarItems.All      // All 15 items
```

## Supported Markdown Elements

Bold, italic, strikethrough, H1-H6 headings, unordered/ordered/task lists, inline code, fenced code blocks, links (with LinkTapped event), images (as placeholder text), blockquotes, tables, horizontal rules, line breaks.

## Complete Markdown Example

```xml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:md="http://shiny.net/maui/markdown"
             x:Class="MyApp.DocsPage"
             Title="Documentation">

    <md:MarkdownView Markdown="{Binding DocumentContent}" Padding="16" />
</ContentPage>
```

## Complete Markdown Editor Example

```xml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:md="http://shiny.net/maui/markdown"
             x:Class="MyApp.NotesPage"
             Title="Notes">

    <md:MarkdownEditor Markdown="{Binding NoteContent, Mode=TwoWay}"
                       Placeholder="Write your notes..."
                       Padding="8" />
</ContentPage>
```

### Dark mode

Leave `Theme` unset. It resolves to `MarkdownTheme.Themed`, whose colours are all Shiny theme
tokens, so the output follows the scheme *and* the theme pack. `MarkdownTheme.Light` / `.Dark` are
literal palettes for a preview that must look the same whatever the app is doing.
