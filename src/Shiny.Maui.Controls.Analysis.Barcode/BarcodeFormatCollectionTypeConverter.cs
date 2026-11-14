using System.ComponentModel;
using System.Globalization;

namespace Shiny.Maui.Controls.Analysis.Barcode;

/// <summary>
/// Converts a comma/space/semicolon-separated string of <see cref="BarcodeFormat"/> names into an
/// <c>IList&lt;BarcodeFormat&gt;</c> so <see cref="BarcodeAnalyzer.Formats"/> can be set inline in XAML —
/// e.g. <c>Formats="QrCode,Ean13,Code128"</c> (case-insensitive). An empty/whitespace value yields
/// <c>null</c> (all formats).
/// </summary>
public class BarcodeFormatCollectionTypeConverter : TypeConverter
{
    /// <inheritdoc/>
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string);

    /// <inheritdoc/>
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object? value)
    {
        if (value is not string s || String.IsNullOrWhiteSpace(s))
            return null;

        var list = new List<BarcodeFormat>();
        foreach (var part in s.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<BarcodeFormat>(part, ignoreCase: true, out var format))
                throw new FormatException($"'{part}' is not a valid {nameof(BarcodeFormat)}.");
            list.Add(format);
        }
        return list.Count == 0 ? null : list;
    }
}
