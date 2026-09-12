namespace Shiny.Blazor.Controls.Markdown;

public class MarkdownTheme
{
    public string TextColor { get; set; } = "#000000";
    public string MutedTextColor { get; set; } = "#6B7280";
    public string LinkColor { get; set; } = "#2563EB";
    public string CodeBackgroundColor { get; set; } = "#F3F4F6";
    public string CodeTextColor { get; set; } = "#D946EF";
    public string CodeBlockBackgroundColor { get; set; } = "#1F2937";
    public string CodeBlockTextColor { get; set; } = "#E5E7EB";
    public string BlockquoteBorderColor { get; set; } = "#D1D5DB";
    public string BlockquoteBackgroundColor { get; set; } = "#F9FAFB";
    public string HorizontalRuleColor { get; set; } = "#E5E7EB";
    public string TableBorderColor { get; set; } = "#E5E7EB";
    public string TableHeaderBackgroundColor { get; set; } = "#F3F4F6";

    public double BaseFontSize { get; set; } = 16;
    public double H1FontSize { get; set; } = 32;
    public double H2FontSize { get; set; } = 24;
    public double H3FontSize { get; set; } = 20;
    public double H4FontSize { get; set; } = 18;
    public double H5FontSize { get; set; } = 16;
    public double H6FontSize { get; set; } = 14;
    public double CodeFontSize { get; set; } = 14;
    public double BlockSpacing { get; set; } = 12;
    public double ListIndent { get; set; } = 24;
    public string CodeFontFamily { get; set; } = "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace";

    /// <summary>
    /// The default when <c>Theme</c> is left unset: every colour is a Shiny theme token, so the
    /// rendered markdown follows the app's light/dark scheme (and its theme pack) on its own.
    /// </summary>
    /// <remarks>
    /// <see cref="Light"/> and <see cref="Dark"/> remain literal palettes, for a host that wants
    /// one regardless of what the app around it is doing - rendering a document preview that must
    /// stay paper-white, say.
    /// </remarks>
    public static MarkdownTheme Themed => new()
    {
        TextColor = "var(--shiny-color-on-surface, #161C23)",
        MutedTextColor = "var(--shiny-color-on-surface-variant, #3B475B)",
        LinkColor = "var(--shiny-color-primary, #0055D9)",
        CodeBackgroundColor = "var(--shiny-color-surface-container-high, #E2E6EA)",
        CodeTextColor = "var(--shiny-color-tertiary, #6A4CAD)",
        CodeBlockBackgroundColor = "var(--shiny-color-inverse-surface, #161C23)",
        CodeBlockTextColor = "var(--shiny-color-inverse-on-surface, #F6FAFE)",
        BlockquoteBorderColor = "var(--shiny-color-outline, #6C788D)",
        BlockquoteBackgroundColor = "var(--shiny-color-surface-container, #E8EEF6)",
        HorizontalRuleColor = "var(--shiny-color-outline-variant, #BBC7DF)",
        TableBorderColor = "var(--shiny-color-outline-variant, #BBC7DF)",
        TableHeaderBackgroundColor = "var(--shiny-color-surface-container-high, #E2E6EA)"
    };

    public static MarkdownTheme Light => new();

    public static MarkdownTheme Dark => new()
    {
        TextColor = "#E5E7EB",
        MutedTextColor = "#9CA3AF",
        LinkColor = "#60A5FA",
        CodeBackgroundColor = "#374151",
        CodeTextColor = "#F472B6",
        CodeBlockBackgroundColor = "#111827",
        CodeBlockTextColor = "#D1D5DB",
        BlockquoteBorderColor = "#4B5563",
        BlockquoteBackgroundColor = "#1F2937",
        HorizontalRuleColor = "#374151",
        TableBorderColor = "#374151",
        TableHeaderBackgroundColor = "#1F2937"
    };
}
