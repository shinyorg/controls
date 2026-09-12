namespace Shiny.ThemeGen;

/// <summary>The complete, ordered Shiny token contract shared by MAUI and Blazor.</summary>
static class Tokens
{
    // ---- Color roles (PascalCase). Values are produced per-scheme by SchemeBuilder. ----
    public static readonly string[] ColorRoles =
    [
        "Primary", "OnPrimary", "PrimaryContainer", "OnPrimaryContainer",
        "Secondary", "OnSecondary", "SecondaryContainer", "OnSecondaryContainer",
        "Tertiary", "OnTertiary", "TertiaryContainer", "OnTertiaryContainer",
        "Error", "OnError", "ErrorContainer", "OnErrorContainer",
        "Background", "OnBackground",
        "Surface", "OnSurface", "SurfaceVariant", "OnSurfaceVariant",
        "SurfaceContainerLowest", "SurfaceContainerLow", "SurfaceContainer", "SurfaceContainerHigh", "SurfaceContainerHighest",
        "SurfaceTint",
        "Outline", "OutlineVariant",
        "Shadow", "Scrim",
        "InverseSurface", "InverseOnSurface", "InversePrimary",
        "Success", "OnSuccess", "SuccessContainer", "OnSuccessContainer",
        "Info", "OnInfo", "InfoContainer", "OnInfoContainer",
        "Warning", "OnWarning", "WarningContainer", "OnWarningContainer",
        "Caution", "OnCaution", "CautionContainer", "OnCautionContainer",
        "Critical", "OnCritical", "CriticalContainer", "OnCriticalContainer",
    ];

    // =============================================================================================
    // The authoring layer.
    //
    // The 55 roles above are the *contract* every control consumes, and they are not a thing anyone
    // wants to hand-edit: 55 names per scheme, most of them derivable from a handful of decisions.
    // So the roles become a derived layer, and what a theme author writes — and what the composer's
    // knobs drive — is this much smaller set, stated per scheme.
    //
    // Each entry names the role it is *seeded* from, which is how the existing seed-driven packs
    // migrate without anyone retyping them: the Material palette still produces a full scheme, and
    // the authoring value is read back out of it. A theme can then state any of these explicitly and
    // the seeds stop mattering for that token.
    // =============================================================================================

    /// <summary>One authoring colour token: its public name and the role it is seeded from.</summary>
    public sealed record AuthoringColor(string Name, string FromRole);

    public static readonly AuthoringColor[] AuthoringColors =
    [
        // Surfaces
        new("background", "Background"),
        new("foreground", "OnBackground"),
        new("card", "SurfaceContainerLowest"),
        new("popover", "SurfaceContainerLow"),
        new("muted", "SurfaceContainer"),
        new("muted-foreground", "OnSurfaceVariant"),

        // Semantic families. `accent` and `destructive` are the web's names for what Material calls
        // tertiary and error - the roles keep the Material name, the authoring token does not.
        new("primary", "Primary"),
        new("primary-foreground", "OnPrimary"),
        new("secondary", "Secondary"),
        new("secondary-foreground", "OnSecondary"),
        new("accent", "Tertiary"),
        new("accent-foreground", "OnTertiary"),
        new("destructive", "Error"),
        new("destructive-foreground", "OnError"),
        new("success", "Success"),
        new("success-foreground", "OnSuccess"),
        new("info", "Info"),
        new("info-foreground", "OnInfo"),
        new("warning", "Warning"),
        new("warning-foreground", "OnWarning"),
        new("caution", "Caution"),
        new("caution-foreground", "OnCaution"),
        new("critical", "Critical"),
        new("critical-foreground", "OnCritical"),

        // Lines
        new("border", "OutlineVariant"),
        new("input", "Outline"),
        new("ring", "Primary"),
        new("shadow-color", "Shadow"),
    ];

    /// <summary>How one role is produced from the authoring layer.</summary>
    public abstract record Derive;

    /// <summary>The role is the authoring token, unchanged.</summary>
    public sealed record Alias(string Token) : Derive;

    /// <summary><c>color-mix(in oklab, {Token} {Percent}%, {Into})</c>.</summary>
    public sealed record MixInto(string Token, double Percent, string Into) : Derive;

    /// <summary>
    /// Every role, expressed over the authoring layer. This is what makes one edit ripple: change
    /// <c>--shiny-primary</c> and the container tint, the tonal surface and the ring all move with it,
    /// which is exactly what the old flat dictionary could not do.
    /// </summary>
    public static readonly (string Role, Derive From)[] RoleDerivations = BuildRoleDerivations();

    static (string Role, Derive From)[] BuildRoleDerivations()
    {
        var list = new List<(string, Derive)>();

        // The nine accent families. A container is the accent laid over the page; the ink on it is the
        // accent pulled toward the page's own foreground - which is what makes both work in either
        // scheme without a second table: in light, foreground is near-black and the ink darkens; in
        // dark it is near-white and the ink lifts.
        void Family(string role, string token)
        {
            list.Add((role, new Alias(token)));
            list.Add(("On" + role, new Alias(token + "-foreground")));
            list.Add((role + "Container", new MixInto(token, 0.22, "background")));
            list.Add(("On" + role + "Container", new MixInto(token, 0.65, "foreground")));
        }

        Family("Primary", "primary");
        Family("Secondary", "secondary");
        Family("Tertiary", "accent");
        Family("Error", "destructive");
        Family("Success", "success");
        Family("Info", "info");
        Family("Warning", "warning");
        Family("Caution", "caution");
        Family("Critical", "critical");

        list.Add(("Background", new Alias("background")));
        list.Add(("OnBackground", new Alias("foreground")));
        list.Add(("Surface", new Alias("background")));
        list.Add(("OnSurface", new Alias("foreground")));
        list.Add(("SurfaceVariant", new Alias("muted")));
        list.Add(("OnSurfaceVariant", new Alias("muted-foreground")));

        // The container ramp: the two named steps a theme states, then a continuation of the same
        // idea - the page tinted toward its own ink - so the ladder stays monotonic in both schemes.
        list.Add(("SurfaceContainerLowest", new Alias("card")));
        list.Add(("SurfaceContainerLow", new Alias("popover")));
        list.Add(("SurfaceContainer", new Alias("muted")));
        list.Add(("SurfaceContainerHigh", new MixInto("foreground", 0.08, "background")));
        list.Add(("SurfaceContainerHighest", new MixInto("foreground", 0.12, "background")));

        list.Add(("SurfaceTint", new Alias("primary")));
        list.Add(("Outline", new Alias("input")));
        list.Add(("OutlineVariant", new Alias("border")));
        list.Add(("Shadow", new Alias("shadow-color")));
        list.Add(("Scrim", new Alias("shadow-color")));

        // Inverse is the scheme turned over: the page's ink becomes the surface and vice versa, which
        // is what a snackbar or a tooltip sits on.
        list.Add(("InverseSurface", new Alias("foreground")));
        list.Add(("InverseOnSurface", new Alias("background")));
        list.Add(("InversePrimary", new MixInto("primary", 0.6, "background")));

        return [.. list];
    }

    // ---- Density: control metrics (px) before the theme's density scale is applied. ----
    public const double ControlHeight = 44;
    public const double ControlHeightSmall = 32;
    public const double RowHeight = 48;
    public const double TouchTarget = 44;

    // ---- Shape (corner radii, px). May be overridden per-theme via the "shape" json block. ----
    public static readonly (string Name, double Value)[] Shape =
    [
        ("CornerNone", 0),
        ("CornerExtraSmall", 4),
        ("CornerSmall", 8),
        ("CornerMedium", 12),
        ("CornerLarge", 16),
        ("CornerExtraLarge", 28),
        ("CornerFull", 9999),
    ];

    // ---- State layer opacities ----
    public static readonly (string Name, double Value)[] State =
    [
        ("HoverOpacity", 0.08),
        ("FocusOpacity", 0.10),
        ("PressedOpacity", 0.10),
        ("DraggedOpacity", 0.16),
    ];

    // ---- Spacing scale (px) ----
    public static readonly (string Name, double Value)[] Spacing =
    [
        ("Space0", 0),
        ("Space1", 4),
        ("Space2", 8),
        ("Space3", 12),
        ("Space4", 16),
        ("Space5", 24),
        ("Space6", 32),
        ("Space7", 48),
        ("Space8", 64),
    ];

    // ---- Type scale (Material 3). Size/LineHeight/Tracking in px, Weight numeric. ----
    public static readonly (string Role, double Size, double LineHeight, int Weight, double Tracking)[] Type =
    [
        ("DisplayLarge", 57, 64, 400, -0.25),
        ("DisplayMedium", 45, 52, 400, 0),
        ("DisplaySmall", 36, 44, 400, 0),
        ("HeadlineLarge", 32, 40, 400, 0),
        ("HeadlineMedium", 28, 36, 400, 0),
        ("HeadlineSmall", 24, 32, 400, 0),
        ("TitleLarge", 22, 28, 400, 0),
        ("TitleMedium", 16, 24, 500, 0.15),
        ("TitleSmall", 14, 20, 500, 0.1),
        ("BodyLarge", 16, 24, 400, 0.5),
        ("BodyMedium", 14, 20, 400, 0.25),
        ("BodySmall", 12, 16, 400, 0.4),
        ("LabelLarge", 14, 20, 500, 0.1),
        ("LabelMedium", 12, 16, 500, 0.5),
        ("LabelSmall", 11, 16, 500, 0.5),
    ];

    // ---- Elevation (Material 3 tonal elevation), as the layers each level is built from.
    // Kept structured rather than as literal box-shadow strings so a theme's elevation style and
    // intensity can rebuild them. Index = level; level 0 is deliberately empty ("none").
    public static readonly (double OffsetY, double Blur, double Spread, double Alpha)[][] ElevationLayers =
    [
        [],
        [(1, 2, 0, 0.30), (1, 3, 1, 0.15)],
        [(1, 2, 0, 0.30), (2, 6, 2, 0.15)],
        [(4, 8, 3, 0.15), (1, 3, 0, 0.30)],
        [(6, 10, 4, 0.15), (2, 3, 0, 0.30)],
        [(8, 12, 6, 0.15), (4, 4, 0, 0.30)],
    ];

    // ---- Elevation as MAUI Shadow. Index = level; level 0 is "no shadow". ----
    public static readonly (double OffsetX, double OffsetY, double Radius, double Opacity)[] MauiShadowLevels =
    [
        (0, 0, 0, 0),
        (0, 1, 3, 0.20),
        (0, 2, 6, 0.20),
        (0, 4, 8, 0.22),
        (0, 6, 12, 0.24),
        (0, 8, 16, 0.26),
    ];

    public static readonly string[] ElevationNames = ["Level0", "Level1", "Level2", "Level3", "Level4", "Level5"];

    public static readonly string[] BorderNames = ["Thin", "Medium", "Thick"];

    public static readonly string[] DensityNames = ["Scale", "ControlHeight", "ControlHeightSmall", "RowHeight", "TouchTarget"];

    // ---- Font family slots. Empty means "the platform default". ----
    public static readonly string[] FontFamilyNames = ["FontFamily", "FontFamilyDisplay", "FontFamilyMono"];

    /// <summary>camelCase/PascalCase -> kebab-case (OnPrimaryContainer -> on-primary-container).</summary>
    public static string Kebab(string pascal)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < pascal.Length; i++)
        {
            var c = pascal[i];
            if (char.IsUpper(c) && i > 0)
                sb.Append('-');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
