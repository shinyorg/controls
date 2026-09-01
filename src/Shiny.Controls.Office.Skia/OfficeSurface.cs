using Shiny.Controls.Office.Spreadsheet;

namespace Shiny.Controls.Office.Skia;

/// <summary>
/// The neutral part of an app's theme, so a drawn Office surface can sit on the same ground as the
/// composed chrome around it.
/// </summary>
/// <remarks>
/// <para>
/// The grid, the page and the deck are painted rather than composed from themed views, so their
/// colours have to arrive as values. Those values were a fixed pair of palettes — a neutral grey for
/// dark, white for light — while the toolbar above them followed the app's theme tokens. In any theme
/// whose neutrals carry a tint (the packs here run blue) that put a blue-grey bar directly on top of a
/// flat grey grid, close enough to look like a mistake rather than a choice.
/// </para>
/// <para>
/// Only the neutrals are taken. The selection green, the clipboard marquee's blue and a document's
/// link colour carry meaning rather than surface, and an app's accent is no substitute for any of
/// them — a spreadsheet with a purple selection is not theming, it is a different control.
/// </para>
/// </remarks>
public readonly record struct OfficeSurface(
    ArgbColor Surface,
    ArgbColor OnSurface,
    ArgbColor SurfaceContainer,
    ArgbColor SurfaceContainerLow,
    ArgbColor OnSurfaceVariant,
    ArgbColor Outline,
    ArgbColor OutlineVariant)
{
    /// <summary>
    /// Restates a spreadsheet theme's neutrals in the app's own, leaving everything that means
    /// something alone.
    /// </summary>
    /// <remarks>
    /// The touch handles' ring takes the grid's ground, which is the whole reason it exists: it
    /// separates the handle from whatever cell is under it, so on a themed sheet it has to be that
    /// sheet's colour rather than a fixed white.
    /// </remarks>
    public SpreadsheetTheme Apply(SpreadsheetTheme baseline)
        => baseline with
        {
            Background = this.Surface,
            CellText = this.OnSurface,
            GridLine = this.OutlineVariant,
            HeaderBackground = this.SurfaceContainer,
            HeaderText = this.OnSurfaceVariant,
            HeaderBorder = this.Outline,
            FrozenDivider = this.Outline,
            TouchHandleRing = this.Surface
        };

    /// <summary>
    /// Restates a document theme's surround in the app's own — the page itself stays paper.
    /// </summary>
    /// <remarks>
    /// Deliberately not the page. A document is a picture of a printed sheet, and the whole point of
    /// the surround is to make that sheet read as paper lying on a desk; tinting the paper with the
    /// app's surface would misrepresent what the document actually looks like, which is the same
    /// reason the deck's slides are left alone. The surround, the desk it lies on, is chrome.
    /// </remarks>
    public DocumentTheme Apply(DocumentTheme baseline)
        => baseline with
        {
            SurroundBackground = this.SurfaceContainerLow
        };

    /// <summary>Restates a deck theme's surround in the app's own. The slides are left alone.</summary>
    public SlideTheme Apply(SlideTheme baseline)
        => baseline with
        {
            Surround = this.SurfaceContainerLow
        };

    /// <summary>
    /// Restates a notebook's paper, its rule and its default ink in the app's own neutrals.
    /// </summary>
    /// <remarks>
    /// The one surface here whose <i>page</i> follows the theme, and the reason is what the page is.
    /// A document and a deck are pictures of something printed, so tinting the paper would
    /// misrepresent what the file looks like. A notebook page was never printed and has no canonical
    /// appearance — it is the app's own writing surface — so a dark app with a white page reads as a
    /// control that missed the memo rather than as fidelity.
    /// </remarks>
    public NotebookTheme Apply(NotebookTheme baseline)
        => baseline with
        {
            Paper = this.Surface,
            Rule = Mix(this.OutlineVariant, this.Surface, 0.55),
            DefaultInk = this.OnSurface
        };

    /// <summary>
    /// Blends <paramref name="color"/> towards <paramref name="towards"/> by <paramref name="amount"/>.
    /// </summary>
    /// <remarks>
    /// The rule is the one token here that cannot be taken from the theme as-is. Every neutral in the
    /// palette is sized for something meant to be <i>seen</i> — <c>outline-variant</c> is a divider —
    /// and a writing guide is not: ruled paper works because the lines sit under the words rather than
    /// competing with them. Taken raw it came out around twice the weight of this painter's own
    /// default, which is a page that reads as a table. Blending it back towards the paper keeps the
    /// app's hue while restoring the weight the default was chosen at.
    /// </remarks>
    static ArgbColor Mix(ArgbColor color, ArgbColor towards, double amount)
    {
        var t = Math.Clamp(amount, 0, 1);

        return new ArgbColor(
            color.A,
            (byte)Math.Round(color.R + (towards.R - color.R) * t),
            (byte)Math.Round(color.G + (towards.G - color.G) * t),
            (byte)Math.Round(color.B + (towards.B - color.B) * t));
    }
}
