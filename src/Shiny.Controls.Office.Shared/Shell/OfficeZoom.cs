using System.Globalization;

namespace Shiny.Controls.Office.Shell;

/// <summary>The presets in the zoom dialog.</summary>
public enum OfficeZoomPreset
{
    Percent200,
    Percent100,
    Percent75,

    /// <summary>The page's width fills the viewport.</summary>
    PageWidth,

    /// <summary>The text column (page less its margins) fills the viewport.</summary>
    TextWidth,

    /// <summary>A whole page fits in the viewport.</summary>
    WholePage,

    /// <summary>Whatever percentage the user types.</summary>
    Custom
}


/// <summary>
/// The status bar's zoom: its range, its snap points, the slider's mapping and the fit presets.
/// </summary>
/// <remarks>
/// <para>
/// Zoom is a factor throughout (1.0 is 100%), the same unit every editor's <c>Zoom</c> property takes,
/// so the shell can hand it straight through.
/// </para>
/// <para>
/// The slider is not linear, and that is the one thing about it worth copying from Office. Its left half
/// covers <see cref="Minimum"/> to 100% and its right half 100% to <see cref="Maximum"/>, so 100% sits
/// dead centre and the zooms people actually use get most of the travel. A linear 10–500% track would
/// put 100% a fifth of the way along and leave everything below it in a few pixels.
/// </para>
/// </remarks>
public sealed class OfficeZoomModel
{
    /// <summary>The default: 10% to 500%, snapping to 100%.</summary>
    public static OfficeZoomModel Default { get; } = new();

    public OfficeZoomModel() { }

    public OfficeZoomModel(double minimum, double maximum, params double[] snapPoints)
    {
        if (minimum <= 0 || maximum <= minimum)
            throw new ArgumentOutOfRangeException(nameof(minimum), "The zoom range must be positive and increasing.");

        this.Minimum = minimum;
        this.Maximum = maximum;
        this.SnapPoints = snapPoints.Length == 0 ? [1.0] : snapPoints;
    }

    /// <summary>The smallest zoom. Default 0.1 (10%).</summary>
    public double Minimum { get; } = 0.1;

    /// <summary>The largest zoom. Default 5 (500%).</summary>
    public double Maximum { get; } = 5.0;

    /// <summary>Zooms the slider sticks to as it passes them. Default just 100%.</summary>
    public IReadOnlyList<double> SnapPoints { get; } = [1.0];

    /// <summary>How close on the slider (0–1) counts as "on" a snap point. Default 0.025 — a few pixels.</summary>
    public double SnapTolerance { get; init; } = 0.025;

    /// <summary>What the − and + buttons move by. Default 0.1 (10 points), which is Word's.</summary>
    public double Step { get; init; } = 0.1;

    /// <summary>Where the slider puts 100%: the middle, unless 100% is outside the range.</summary>
    double Pivot => Math.Clamp(1.0, this.Minimum, this.Maximum);


    /// <summary>Holds a zoom inside the range.</summary>
    public double Clamp(double zoom)
        => double.IsNaN(zoom) ? this.Pivot : Math.Clamp(zoom, this.Minimum, this.Maximum);


    /// <summary>The slider position (0–1) for a zoom.</summary>
    public double ToSlider(double zoom)
    {
        zoom = this.Clamp(zoom);
        var pivot = this.Pivot;

        if (pivot <= this.Minimum)
            return (zoom - this.Minimum) / (this.Maximum - this.Minimum);

        if (pivot >= this.Maximum)
            return (zoom - this.Minimum) / (this.Maximum - this.Minimum);

        return zoom <= pivot
            ? 0.5 * (zoom - this.Minimum) / (pivot - this.Minimum)
            : 0.5 + (0.5 * (zoom - pivot) / (this.Maximum - pivot));
    }


    /// <summary>The zoom for a slider position, snapped to a snap point when close and rounded to a whole percent.</summary>
    public double FromSlider(double position, bool snap = true)
    {
        position = Math.Clamp(double.IsNaN(position) ? 0.5 : position, 0, 1);

        if (snap)
        {
            foreach (var point in this.SnapPoints)
            {
                if (point < this.Minimum || point > this.Maximum)
                    continue;

                if (Math.Abs(this.ToSlider(point) - position) <= this.SnapTolerance)
                    return point;
            }
        }

        var pivot = this.Pivot;
        double zoom;

        if (pivot <= this.Minimum || pivot >= this.Maximum)
            zoom = this.Minimum + (position * (this.Maximum - this.Minimum));
        else if (position <= 0.5)
            zoom = this.Minimum + ((position / 0.5) * (pivot - this.Minimum));
        else
            zoom = pivot + (((position - 0.5) / 0.5) * (this.Maximum - pivot));

        return this.Clamp(Math.Round(zoom * 100) / 100);
    }


    /// <summary>
    /// One press of + (<paramref name="direction"/> 1) or − (-1). Lands on the next multiple of
    /// <see cref="Step"/> rather than adding to an odd value, so 87% goes to 90% and then 100%.
    /// </summary>
    public double StepZoom(double zoom, int direction)
    {
        if (direction == 0)
            return this.Clamp(zoom);

        var units = zoom / this.Step;
        var next = direction > 0
            ? Math.Floor(units + 1e-9) + 1
            : Math.Ceiling(units - 1e-9) - 1;

        return this.Clamp(Math.Round(next * this.Step, 4));
    }


    /// <summary>The zoom a preset asks for.</summary>
    /// <param name="preset">The preset.</param>
    /// <param name="pageWidth">The page's width at 100%, in the same unit as the viewport.</param>
    /// <param name="pageHeight">The page's height at 100%.</param>
    /// <param name="viewportWidth">The visible width of the editor.</param>
    /// <param name="viewportHeight">The visible height of the editor.</param>
    /// <param name="textWidth">The text column's width at 100% — the page less its margins. Needed only for TextWidth.</param>
    /// <param name="custom">The typed factor for <see cref="OfficeZoomPreset.Custom"/>.</param>
    /// <param name="gutter">Room left either side of the page, so it does not touch the viewport edge.</param>
    public double Resolve(
        OfficeZoomPreset preset,
        double pageWidth = 0,
        double pageHeight = 0,
        double viewportWidth = 0,
        double viewportHeight = 0,
        double textWidth = 0,
        double custom = 1,
        double gutter = 24)
        => preset switch
        {
            OfficeZoomPreset.Percent200 => this.Clamp(2),
            OfficeZoomPreset.Percent100 => this.Clamp(1),
            OfficeZoomPreset.Percent75 => this.Clamp(0.75),
            OfficeZoomPreset.PageWidth => this.Fit(pageWidth, viewportWidth, gutter),
            OfficeZoomPreset.TextWidth => this.Fit(textWidth > 0 ? textWidth : pageWidth, viewportWidth, gutter),
            OfficeZoomPreset.WholePage => Math.Min(
                this.Fit(pageWidth, viewportWidth, gutter),
                this.Fit(pageHeight, viewportHeight, gutter)),
            _ => this.Clamp(custom)
        };


    /// <summary>The zoom at which <paramref name="content"/> fills <paramref name="viewport"/> less a gutter each side.</summary>
    public double Fit(double content, double viewport, double gutter = 24)
    {
        if (content <= 0 || viewport <= 0)
            return this.Clamp(1);

        var room = Math.Max(1, viewport - (gutter * 2));
        return this.Clamp(Math.Floor(room / content * 100) / 100);
    }


    /// <summary>"100%" — whole percents, the way the status bar shows it.</summary>
    public static string Format(double zoom, CultureInfo? culture = null)
        => Math.Round(zoom * 100).ToString("0", culture ?? CultureInfo.CurrentCulture) + "%";


    /// <summary>Reads "150", "150%" or "150 %" as 1.5. False for anything else.</summary>
    public static bool TryParse(string? text, out double zoom)
    {
        zoom = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var cleaned = text.Trim().TrimEnd('%').Trim();
        if (!double.TryParse(cleaned, NumberStyles.Float, CultureInfo.CurrentCulture, out var percent) &&
            !double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
            return false;

        if (percent <= 0 || double.IsInfinity(percent))
            return false;

        zoom = percent / 100;
        return true;
    }


    /// <summary>The zoom dialog's presets, in the order it lists them, with their labels.</summary>
    public static IReadOnlyList<(OfficeZoomPreset Preset, string Text)> Presets { get; } =
    [
        (OfficeZoomPreset.Percent200, "200%"),
        (OfficeZoomPreset.Percent100, "100%"),
        (OfficeZoomPreset.Percent75, "75%"),
        (OfficeZoomPreset.PageWidth, "Page width"),
        (OfficeZoomPreset.TextWidth, "Text width"),
        (OfficeZoomPreset.WholePage, "Whole page")
    ];
}
