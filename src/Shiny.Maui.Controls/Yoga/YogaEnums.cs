namespace Shiny.Maui.Controls;

/// <summary>The main axis of a <see cref="YogaLayout"/>. Yoga's default is <see cref="Column"/>, not CSS's row.</summary>
public enum YogaFlexDirection
{
    Column,
    ColumnReverse,
    Row,
    RowReverse
}

/// <summary>How a line's free space is shared out along the main axis (justify-content).</summary>
public enum YogaJustify
{
    FlexStart,
    Center,
    FlexEnd,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly
}

/// <summary>
/// Cross-axis alignment, used by align-items, align-self and align-content. <see cref="Auto"/> only means
/// something for align-self (inherit the container's align-items); the Space* values only for align-content.
/// </summary>
public enum YogaAlign
{
    Auto,
    FlexStart,
    Center,
    FlexEnd,
    Stretch,
    Baseline,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly
}

/// <summary>Whether children break onto more lines when the main axis runs out of room.</summary>
public enum YogaWrap
{
    NoWrap,
    Wrap,
    WrapReverse
}

/// <summary>
/// How a child is positioned. <see cref="Relative"/> (the default) flows and is then nudged by its insets,
/// <see cref="Absolute"/> leaves the flow and is placed by its insets against the container, and
/// <see cref="Static"/> flows and ignores insets.
/// </summary>
public enum YogaPositionType
{
    Relative,
    Absolute,
    Static
}

/// <summary><see cref="None"/> takes the child out of layout entirely, as if it were not there.</summary>
public enum YogaDisplay
{
    Flex,
    None
}

/// <summary>
/// Which of a child's margins are <c>auto</c>: an auto margin soaks up free space, which is how a child is
/// pushed to the far end of a line or centred on its own. Start/End follow the flow direction.
/// </summary>
[Flags]
public enum YogaEdges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
    Start = 16,
    End = 32,
    Horizontal = Left | Right,
    Vertical = Top | Bottom,
    All = Left | Top | Right | Bottom
}
