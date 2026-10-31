namespace Shiny.Controls.BarcodeReader.Linear;

/// <summary>A decode from one scanline. <see cref="Start"/>/<see cref="End"/> are element indices.</summary>
sealed record LinearRead(string Text, Symbology Symbology, bool IsChecksummed)
{
    public int Start { get; init; }
    public int End { get; init; }
}
