namespace Shiny.Blazor.Controls.Agents.Renderers;

/// <summary>
/// The basic catalog's named icons as text glyphs, so the package needs no icon font. Every name in
/// the catalog's enum has one; an unknown name draws a neutral dot rather than nothing.
/// </summary>
static class A2UIIcons
{
    static readonly Dictionary<string, string> Glyphs = new(StringComparer.Ordinal)
    {
        ["accountCircle"] = "●", ["add"] = "+", ["arrowBack"] = "←", ["arrowForward"] = "→",
        ["attachFile"] = "\U0001F4CE", ["calendarToday"] = "\U0001F4C5", ["call"] = "☎", ["camera"] = "\U0001F4F7",
        ["check"] = "✓", ["close"] = "✕", ["delete"] = "\U0001F5D1", ["download"] = "⬇",
        ["edit"] = "✎", ["event"] = "\U0001F4C6", ["error"] = "⚠", ["fastForward"] = "⏩",
        ["favorite"] = "♥", ["favoriteOff"] = "♡", ["folder"] = "\U0001F4C1", ["help"] = "?",
        ["home"] = "⌂", ["info"] = "ℹ", ["locationOn"] = "\U0001F4CD", ["lock"] = "\U0001F512",
        ["lockOpen"] = "\U0001F513", ["mail"] = "✉", ["menu"] = "☰", ["moreVert"] = "⋮",
        ["moreHoriz"] = "⋯", ["notificationsOff"] = "\U0001F515", ["notifications"] = "\U0001F514", ["pause"] = "⏸",
        ["payment"] = "\U0001F4B3", ["person"] = "\U0001F464", ["phone"] = "\U0001F4F1", ["photo"] = "\U0001F5BC",
        ["play"] = "▶", ["print"] = "\U0001F5A8", ["refresh"] = "↻", ["rewind"] = "⏪",
        ["search"] = "\U0001F50D", ["send"] = "➤", ["settings"] = "⚙", ["share"] = "↪",
        ["shoppingCart"] = "\U0001F6D2", ["skipNext"] = "⏭", ["skipPrevious"] = "⏮", ["star"] = "★",
        ["starHalf"] = "⯨", ["starOff"] = "☆", ["stop"] = "⏹", ["upload"] = "⬆",
        ["visibility"] = "\U0001F441", ["visibilityOff"] = "⊘", ["volumeDown"] = "\U0001F509", ["volumeMute"] = "\U0001F508",
        ["volumeOff"] = "\U0001F507", ["volumeUp"] = "\U0001F50A", ["warning"] = "⚠"
    };

    public static string Glyph(string? name) => name is not null && Glyphs.TryGetValue(name, out var g) ? g : "•";
}
