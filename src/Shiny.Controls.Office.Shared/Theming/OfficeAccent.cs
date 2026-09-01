using Shiny.Controls.Office.Spreadsheet;

namespace Shiny.Controls.Office.Theming;

/// <summary>
/// The colour an Office control wears: its ribbon's header band, its tab underline, and the ink that
/// stays readable on both.
/// </summary>
/// <remarks>
/// <para>
/// This is the one part of an Office control's appearance that is deliberately <b>not</b> taken from
/// the app's theme. Everything else — the grid, the page, the surround — follows the host's neutrals
/// so the control sits on the same ground as the chrome around it. The accent is the opposite: it says
/// which of the three you are looking at, and a workbook and a deck open side by side in one app want
/// to be told apart, not matched.
/// </para>
/// <para>
/// Which is why the presets are the ones Microsoft uses. The colours are not a house style to be
/// improved on — they are what a user already reads as "spreadsheet" and "slides" before any label
/// has been looked at.
/// </para>
/// </remarks>
public sealed record OfficeAccent(ArgbColor Color, ArgbColor Ink)
{
    static readonly ArgbColor White = new(255, 0xFF, 0xFF, 0xFF);

    /// <summary>Word blue.</summary>
    public static readonly OfficeAccent Document = new(new ArgbColor(255, 0x18, 0x5A, 0xBD), White);

    /// <summary>Excel green.</summary>
    public static readonly OfficeAccent Spreadsheet = new(new ArgbColor(255, 0x10, 0x7C, 0x41), White);

    /// <summary>PowerPoint orange-red.</summary>
    public static readonly OfficeAccent Presentation = new(new ArgbColor(255, 0xC4, 0x3E, 0x1C), White);

    /// <summary>OneNote purple.</summary>
    public static readonly OfficeAccent Notebook = new(new ArgbColor(255, 0x7A, 0x33, 0x8C), White);

    /// <summary>
    /// An accent from a colour, with the ink chosen for legibility on it.
    /// </summary>
    /// <remarks>
    /// The ink is computed rather than asked for because getting it wrong is the whole failure mode:
    /// a caller who picks a brand colour is not thinking about whether their tab labels have gone
    /// invisible on it, and a pale accent with white text is unreadable in exactly the way that is
    /// hard to notice on the machine it was authored on.
    /// </remarks>
    public static OfficeAccent From(ArgbColor color)
        => new(color, InkFor(color));

    /// <summary>Black or white, whichever stands out on <paramref name="background"/>.</summary>
    /// <remarks>
    /// sRGB relative luminance, the same measure the WCAG contrast ratio is built on — and the cut is
    /// <b>0.179</b>, not the midpoint. Contrast is a ratio of <c>(L + 0.05)</c>, so white and black are
    /// equally readable well below halfway: at 0.5 a mid-grey scores 2.7:1 against white and 7.8:1
    /// against black, and picking the midpoint hands it the unreadable one.
    /// </remarks>
    public static ArgbColor InkFor(ArgbColor background)
    {
        var luminance =
            (0.2126 * Channel(background.R)) +
            (0.7152 * Channel(background.G)) +
            (0.0722 * Channel(background.B));

        return luminance > 0.179
            ? new ArgbColor(255, 0x1A, 0x1A, 0x1A)
            : White;

        static double Channel(byte value)
        {
            var v = value / 255d;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
    }
}
