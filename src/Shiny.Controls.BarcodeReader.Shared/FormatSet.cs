namespace Shiny.Controls.BarcodeReader;

/// <summary>A set of symbologies as a bitmask, so the per-line checks cost nothing.</summary>
public readonly struct FormatSet
{
    readonly ulong bits;

    FormatSet(ulong bits) => this.bits = bits;

    /// <summary>Every symbology.</summary>
    public static FormatSet All { get; } = new(ulong.MaxValue);

    /// <summary>The given symbologies; null or empty means all of them.</summary>
    public static FormatSet Of(IEnumerable<Symbology>? symbologies)
    {
        if (symbologies == null)
            return All;

        ulong bits = 0;
        foreach (var s in symbologies)
            bits |= 1UL << (int)s;
        return bits == 0 ? All : new FormatSet(bits);
    }

    /// <summary>Whether <paramref name="symbology"/> is in the set.</summary>
    public bool Has(Symbology symbology) => (this.bits & (1UL << (int)symbology)) != 0;

    /// <summary>True when any symbology the engine decodes itself is in the set.</summary>
    public bool HasAnyLinear =>
        this.Has(Symbology.Ean13) || this.Has(Symbology.UpcA) || this.Has(Symbology.Ean8) || this.Has(Symbology.UpcE) ||
        this.Has(Symbology.Code128) || this.Has(Symbology.Code39) || this.Has(Symbology.Itf);

    /// <summary>True when any symbology only a native decoder reads is in the set.</summary>
    public bool HasAnyNativeOnly =>
        this.Has(Symbology.QrCode) || this.Has(Symbology.DataMatrix) || this.Has(Symbology.Aztec) ||
        this.Has(Symbology.Pdf417) || this.Has(Symbology.Code93) || this.Has(Symbology.Codabar);
}
