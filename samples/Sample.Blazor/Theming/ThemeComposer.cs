namespace Sample.Blazor.Theming;

/// <summary>One option on a knob: what it is called, and the tokens it writes.</summary>
/// <param name="Value">The stable identifier. Never reused for something else — preset codes encode indices.</param>
/// <param name="Label">What the dock shows.</param>
/// <param name="Light">Token overrides for the light scheme, without the <c>--shiny-</c> prefix.</param>
/// <param name="Dark">Token overrides for the dark scheme. Falls back to <paramref name="Light"/> when empty.</param>
public sealed record ThemeOption(
    string Value,
    string Label,
    IReadOnlyDictionary<string, string>? Light = null,
    IReadOnlyDictionary<string, string>? Dark = null
);

/// <summary>
/// The composer's knobs.
/// </summary>
/// <remarks>
/// <para>
/// Every option here writes <em>authoring</em> tokens and nothing else — there is no list of role
/// colours anywhere in this file. That is the whole point of the two-layer contract: an accent is one
/// token, and the container tint, the surface tint and the focus ring follow it because the stylesheet
/// derives them. Before the split, "change the accent" would have meant writing eight roles per scheme
/// and getting the tonal maths right by hand.
/// </para>
/// <para>
/// <b>Every list is append-only.</b> A preset code encodes option <em>indices</em>, so reordering or
/// removing an entry silently changes what an already-shared link resolves to.
/// </para>
/// </remarks>
public static class ThemeComposer
{
    static Dictionary<string, string> Map(params (string Token, string Value)[] pairs)
        => pairs.ToDictionary(x => x.Token, x => x.Value, StringComparer.Ordinal);

    /// <summary>The shipped packs. Applied through the theme service rather than as token overrides.</summary>
    public static readonly ThemeOption[] Packs =
    [
        new("", "Basic"),
        new("ocean", "Ocean"),
        new("material", "Material"),
        new("terminal", "Terminal"),
        new("aurora", "Aurora")
    ];

    /// <summary>
    /// The accent. Both halves of the pair are written: an accent light enough to need dark ink is
    /// unreadable under the pack's white <c>primary-foreground</c>, and the derived roles cannot fix
    /// that for you — the foreground is a decision, not a derivation.
    /// </summary>
    public static readonly ThemeOption[] Accents =
    [
        new("pack", "Pack default"),
        new("blue", "Blue",
            Map(("primary", "#2563EB"), ("primary-foreground", "#FFFFFF"), ("ring", "#2563EB")),
            Map(("primary", "#93B4FF"), ("primary-foreground", "#0A2A6B"), ("ring", "#93B4FF"))),
        new("violet", "Violet",
            Map(("primary", "#6D28D9"), ("primary-foreground", "#FFFFFF"), ("ring", "#6D28D9")),
            Map(("primary", "#C4B5FD"), ("primary-foreground", "#2E1065"), ("ring", "#C4B5FD"))),
        new("teal", "Teal",
            Map(("primary", "#0F766E"), ("primary-foreground", "#FFFFFF"), ("ring", "#0F766E")),
            Map(("primary", "#5EEAD4"), ("primary-foreground", "#04332F"), ("ring", "#5EEAD4"))),
        new("green", "Green",
            Map(("primary", "#15803D"), ("primary-foreground", "#FFFFFF"), ("ring", "#15803D")),
            Map(("primary", "#86EFAC"), ("primary-foreground", "#052E16"), ("ring", "#86EFAC"))),
        new("amber", "Amber",
            Map(("primary", "#B45309"), ("primary-foreground", "#FFFFFF"), ("ring", "#B45309")),
            Map(("primary", "#FCD34D"), ("primary-foreground", "#422006"), ("ring", "#FCD34D"))),
        new("rose", "Rose",
            Map(("primary", "#BE123C"), ("primary-foreground", "#FFFFFF"), ("ring", "#BE123C")),
            Map(("primary", "#FDA4AF"), ("primary-foreground", "#4C0519"), ("ring", "#FDA4AF"))),
        new("slate", "Slate",
            Map(("primary", "#334155"), ("primary-foreground", "#FFFFFF"), ("ring", "#334155")),
            Map(("primary", "#CBD5E1"), ("primary-foreground", "#0F172A"), ("ring", "#CBD5E1")))
    ];

    /// <summary>One token drives the whole corner ramp — the stylesheet expresses every step over it.</summary>
    public static readonly ThemeOption[] Radii =
    [
        new("pack", "Pack default"),
        new("none", "Square", Map(("radius", "0px"))),
        new("sm", "Small", Map(("radius", "6px"))),
        new("md", "Medium", Map(("radius", "12px"))),
        new("lg", "Large", Map(("radius", "18px"))),
        new("xl", "Pill", Map(("radius", "28px")))
    ];

    /// <summary>Scales the control heights and the spacing ramp together.</summary>
    public static readonly ThemeOption[] Densities =
    [
        new("pack", "Pack default"),
        new("compact", "Compact", Map(("density", "0.85"))),
        new("default", "Default", Map(("density", "1"))),
        new("comfortable", "Comfortable", Map(("density", "1.15")))
    ];

    public static readonly ThemeOption[] TextScales =
    [
        new("pack", "Pack default"),
        new("sm", "Small", Map(("text-scale", "0.9"))),
        new("md", "Default", Map(("text-scale", "1"))),
        new("lg", "Large", Map(("text-scale", "1.1"))),
        new("xl", "Extra large", Map(("text-scale", "1.25")))
    ];

    public static readonly ThemeOption[] Fonts =
    [
        new("pack", "Pack default"),
        new("system", "System", Map(("font-sans", "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"))),
        new("serif", "Serif", Map(("font-sans", "ui-serif, Georgia, 'Times New Roman', serif"))),
        new("mono", "Mono", Map(("font-sans", "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace"))),
        new("rounded", "Rounded", Map(("font-sans", "ui-rounded, 'SF Pro Rounded', 'Nunito', system-ui, sans-serif")))
    ];

    public static readonly ThemeOption[] Weights =
    [
        new("pack", "Pack default"),
        new("light", "Light", Map(("font-weight-offset", "-100"))),
        new("regular", "Regular", Map(("font-weight-offset", "0"))),
        new("bold", "Bold", Map(("font-weight-offset", "100"))),
        new("heavy", "Heavy", Map(("font-weight-offset", "200")))
    ];


    /// <summary>The knobs in preset-code order. Append only.</summary>
    public static readonly (string Name, ThemeOption[] Options)[] Knobs =
    [
        ("Pack", Packs),
        ("Accent", Accents),
        ("Radius", Radii),
        ("Density", Densities),
        ("Text", TextScales),
        ("Font", Fonts),
        ("Weight", Weights)
    ];
}


/// <summary>
/// One composed selection: an index per knob, plus the scheme it is being designed in.
/// </summary>
/// <remarks>
/// The code is one base-36 character per knob and a trailing <c>l</c>/<c>d</c> for the scheme — short
/// enough to live in a URL, a copy button and a docs deep link without looking like a hash. Indices
/// rather than names for the same reason: a name change would otherwise break every shared link.
/// </remarks>
public sealed record ThemeSelection(int[] Indexes, bool Dark)
{
    public static ThemeSelection Default => new(new int[ThemeComposer.Knobs.Length], false);

    public int this[int knob] => this.Indexes[knob];

    public ThemeSelection With(int knob, int option)
    {
        var next = (int[])this.Indexes.Clone();
        next[knob] = option;
        return this with { Indexes = next };
    }


    public string ToCode()
    {
        var chars = this.Indexes
            .Select((index, knob) => Base36(Math.Clamp(index, 0, ThemeComposer.Knobs[knob].Options.Length - 1)));

        return String.Concat(chars) + (this.Dark ? 'd' : 'l');
    }


    /// <summary>
    /// Reads a code back. Anything malformed — a short code, an out-of-range index, a code written by
    /// a newer build with more knobs — degrades to the default rather than throwing: a bad link should
    /// open the composer, not an error page.
    /// </summary>
    public static ThemeSelection FromCode(string? code)
    {
        if (String.IsNullOrWhiteSpace(code))
            return Default;

        var text = code.Trim();
        var dark = text.EndsWith('d');
        var body = text[..^1];

        var indexes = new int[ThemeComposer.Knobs.Length];

        for (var knob = 0; knob < indexes.Length && knob < body.Length; knob++)
        {
            var value = FromBase36(body[knob]);
            indexes[knob] = value >= 0 && value < ThemeComposer.Knobs[knob].Options.Length ? value : 0;
        }

        return new ThemeSelection(indexes, dark);
    }


    /// <summary>Every token this selection writes, in knob order so a later knob wins a shared token.</summary>
    public IReadOnlyDictionary<string, string> Tokens()
    {
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var knob = 0; knob < this.Indexes.Length; knob++)
        {
            var option = ThemeComposer.Knobs[knob].Options[this.Indexes[knob]];
            var map = (this.Dark ? option.Dark : option.Light) ?? option.Light;

            if (map is null)
                continue;

            foreach (var (token, value) in map)
                tokens[token] = value;
        }

        return tokens;
    }


    public string? Pack => ThemeComposer.Packs[this.Indexes[0]].Value is { Length: > 0 } slug ? slug : null;

    static char Base36(int value) => (char)(value < 10 ? '0' + value : 'a' + value - 10);

    static int FromBase36(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'z' => c - 'a' + 10,
        >= 'A' and <= 'Z' => c - 'A' + 10,
        _ => -1
    };
}
