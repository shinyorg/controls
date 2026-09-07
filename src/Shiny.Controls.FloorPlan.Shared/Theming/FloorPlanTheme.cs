namespace Shiny.Controls.FloorPlan;

/// <summary>
/// The colours the plan's chrome and its unstyled elements are drawn with.
/// </summary>
/// <remarks>
/// <para>
/// The plan is painted rather than composed from themed views, so the theme has to arrive as a value.
/// Both hosts default it from the app: MAUI reads <c>Application.Current.Resources</c>, Blazor reads
/// the <c>--shiny-color-*</c> custom properties off the element, and both end at
/// <see cref="FloorPlanSurface"/> so the two cannot drift.
/// </para>
/// <para>
/// Only the neutrals follow the app. The selection blue means "this is what you have got hold of" and
/// the furniture colours mean "this is wood, that is upholstery"; restating either in an app's accent
/// is not theming, it is a different control. Same rule the Office surfaces follow.
/// </para>
/// </remarks>
public sealed record FloorPlanTheme
{
    public static readonly FloorPlanTheme Light = new();

    public static readonly FloorPlanTheme Dark = new()
    {
        Background = PlanColor.Rgb(0x1E, 0x1E, 0x1E),
        GridLine = PlanColor.Rgb(0x3A, 0x3A, 0x3A),
        PlanBorder = PlanColor.Rgb(0x5A, 0x5A, 0x5A),

        ElementFill = PlanColor.Rgb(0x2E, 0x2E, 0x2E),
        ElementStroke = PlanColor.Rgb(0x8A, 0x8A, 0x8A),
        LabelText = PlanColor.Rgb(0xE6, 0xE6, 0xE6),

        // The gap a door punches through a wall is drawn in the plan's own ground rather than in
        // white: on a dark plan a white slot reads as a lit strip across the wall.
        DoorOpening = PlanColor.Rgb(0x1E, 0x1E, 0x1E),

        OutletFill = PlanColor.Rgb(0x2A, 0x2A, 0x2A),
        OutletSymbol = PlanColor.Rgb(0xD8, 0xD8, 0xD8),

        // The ring separates a handle from whatever is under it, so it takes the plan's ground for
        // the same reason - a white handle on a dark plan is a brighter mark than the selection.
        HandleFill = PlanColor.Rgb(0x1E, 0x1E, 0x1E),

        Desk = PlanColor.Rgb(0x8A, 0x74, 0x5C),
        Chair = PlanColor.Rgb(0x7A, 0x7A, 0x7A),
        Table = PlanColor.Rgb(0x77, 0x62, 0x4E),
        Bookshelf = PlanColor.Rgb(0x6B, 0x46, 0x21),
        Sofa = PlanColor.Rgb(0x3A, 0x6A, 0x94),
        FileCabinet = PlanColor.Rgb(0x6E, 0x6E, 0x6E),
        CubicleDesk = PlanColor.Rgb(0x7A, 0x5F, 0x44)
    };


    // ---------------------------------------------------------------------------------------------
    // Ground
    // ---------------------------------------------------------------------------------------------

    /// <summary>What the canvas is cleared to.</summary>
    public PlanColor Background { get; init; } = PlanColor.Rgb(0xFF, 0xFF, 0xFF);

    /// <summary>The snap grid.</summary>
    public PlanColor GridLine { get; init; } = PlanColor.Rgb(0xD8, 0xD8, 0xD8);

    /// <summary>The rectangle bounding the document itself.</summary>
    public PlanColor PlanBorder { get; init; } = PlanColor.Rgb(0xA9, 0xA9, 0xA9);


    // ---------------------------------------------------------------------------------------------
    // Elements, where the document has not said otherwise
    // ---------------------------------------------------------------------------------------------

    public PlanColor ElementFill { get; init; } = PlanColor.Rgb(0xEC, 0xEC, 0xEC);
    public PlanColor ElementStroke { get; init; } = PlanColor.Rgb(0x33, 0x33, 0x33);
    public PlanColor LabelText { get; init; } = PlanColor.Rgb(0x1A, 0x1A, 0x1A);

    /// <summary>Painted over the wall where a door sits, to punch the opening.</summary>
    public PlanColor DoorOpening { get; init; } = PlanColor.Rgb(0xFF, 0xFF, 0xFF);

    public PlanColor OutletFill { get; init; } = PlanColor.Rgb(0xFF, 0xFF, 0xFF);
    public PlanColor OutletSymbol { get; init; } = PlanColor.Rgb(0x1A, 0x1A, 0x1A);


    // ---------------------------------------------------------------------------------------------
    // Interaction. Fixed across schemes on purpose - these say what is happening, not what the app
    // looks like.
    // ---------------------------------------------------------------------------------------------

    public PlanColor Selection { get; init; } = PlanColor.Rgb(0x1E, 0x90, 0xFF);

    /// <summary>Wash inside the rubber-band marquee.</summary>
    public PlanColor SelectionFill { get; init; } = new(30, 0x1E, 0x90, 0xFF);

    /// <summary>Inside of a resize handle.</summary>
    public PlanColor HandleFill { get; init; } = PlanColor.Rgb(0xFF, 0xFF, 0xFF);

    /// <summary>The dashed outline a draw or place tool shows before you commit.</summary>
    public PlanColor Preview { get; init; } = PlanColor.Rgb(0x1E, 0x90, 0xFF);

    /// <summary>How much of its alpha a hovered element keeps.</summary>
    public float HoverFade { get; init; } = 0.86f;


    // ---------------------------------------------------------------------------------------------
    // Furniture. Material, not theme.
    // ---------------------------------------------------------------------------------------------

    public PlanColor Desk { get; init; } = PlanColor.Rgb(0xD2, 0xB4, 0x8C);
    public PlanColor Chair { get; init; } = PlanColor.Rgb(0x64, 0x64, 0x64);
    public PlanColor Table { get; init; } = PlanColor.Rgb(0xB4, 0x96, 0x78);
    public PlanColor Bookshelf { get; init; } = PlanColor.Rgb(0x8B, 0x5A, 0x2B);
    public PlanColor Sofa { get; init; } = PlanColor.Rgb(0x46, 0x82, 0xB4);
    public PlanColor FileCabinet { get; init; } = PlanColor.Rgb(0xA0, 0xA0, 0xA0);

    /// <summary>The desk drawn inside a cubicle bay.</summary>
    public PlanColor CubicleDesk { get; init; } = PlanColor.Rgb(0xB4, 0x8C, 0x64);


    // ---------------------------------------------------------------------------------------------
    // Metrics
    // ---------------------------------------------------------------------------------------------

    /// <summary>Font size of a room label, in plan units.</summary>
    public float LabelFontSize { get; init; } = 14;

    /// <summary>Font size of an occupant name, in plan units.</summary>
    public float OccupantFontSize { get; init; } = 10;

    /// <summary>Font size of a furniture caption, in plan units.</summary>
    public float FurnitureFontSize { get; init; } = 9;

    /// <summary>Side of a selection handle, in screen pixels - it does not grow with zoom.</summary>
    public float HandleSize { get; init; } = 8;

    /// <summary>The colour to draw a piece of furniture of this kind.</summary>
    public PlanColor ColorFor(FurnitureKind kind) => kind switch
    {
        FurnitureKind.Desk => this.Desk,
        FurnitureKind.Chair => this.Chair,
        FurnitureKind.Table => this.Table,
        FurnitureKind.Bookshelf => this.Bookshelf,
        FurnitureKind.Sofa => this.Sofa,
        FurnitureKind.FileCabinet => this.FileCabinet,
        _ => this.ElementFill
    };
}
