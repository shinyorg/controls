
namespace Shiny.Maui.Controls.Analysis.Documents;

/// <summary>
/// Document scanning off <c>IMediaService</c> — credit cards, driver's licenses, passports, health
/// cards, receipts, invoices and business cards, each opening the modal camera with the right analyzer
/// already wired up. Install <c>Shiny.Maui.Controls.Analysis.Documents</c> and these appear.
/// </summary>
/// <remarks>
/// <para>
/// Every document type gets the same pair: a singular <c>Scan…Async</c> returning <c>Task&lt;T?&gt;</c> that
/// closes the modal on the first complete read, and a plural one returning
/// <c>IAsyncEnumerable&lt;T&gt;</c> that keeps the modal up and streams — scanning a stack of receipts is
/// then a <c>foreach</c> rather than a loop of modal opens.
/// </para>
/// <para>
/// The analyzers accumulate across frames before they emit (see <c>DocumentAnalyzer.AccumulationFrames</c>),
/// so one result here is already a document assembled from several reads, not a single noisy frame.
/// </para>
/// </remarks>
public static class MediaServiceDocumentExtensions
{
    // ---------------------------------------------------------------------------------------------
    // credit cards
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Scan one payment card. The brand and number validity are derived deterministically (IIN prefix +
    /// Luhn); the name and expiry are best-effort OCR. Returns <c>null</c> if the user backs out.
    /// </summary>
    /// <remarks>
    /// <c>Cvv</c> is on the back panel and is PCI-sensitive — a front scan almost never populates it, and if
    /// you do capture it, do not persist it.
    /// </remarks>
    public static Task<CreditCard?> ScanCreditCardAsync<TOptions>(this IFrameScanService<TOptions> media, TOptions? options = null, CancellationToken ct = default) where TOptions : class, IScanOptions, new()
        => media.ScanCreditCardsAsync(false, options, ct).FirstOrDefaultAsync(ct).AsTask();

    /// <summary>Scan payment cards continuously, keyed on the card number for duplicate filtering.</summary>
    /// <param name="media">The media service.</param>
    /// <param name="filterDuplicates">Skip a card whose number was already returned. Default <c>true</c>.</param>
    /// <param name="options">Modal appearance and scan behaviour.</param>
    /// <param name="ct">Cancels the scan and closes the modal.</param>
    public static IAsyncEnumerable<CreditCard> ScanCreditCardsAsync<TOptions>(
        this IFrameScanService<TOptions> media,
        bool filterDuplicates = true,
        TOptions? options = null,
        CancellationToken ct = default
    ) where TOptions : class, IScanOptions, new() => media.ScanDocumentsAsync(
        new CreditCardAnalyzer(),
        filterDuplicates,
        c => c.Number ?? $"{c.Type}|{c.FirstName}|{c.LastName}",
        c => c.Number is { Length: > 4 } n ? $"•••• {n[^4..]}" : c.Type.ToString(),
        options,
        ct
    );

    // ---------------------------------------------------------------------------------------------
    // driver's licenses
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Scan a North American driver's license from the AAMVA PDF417 barcode on the <b>back</b> of the card —
    /// which is why the instruction text should say "back", not "front". Returns <c>null</c> if the user
    /// backs out.
    /// </summary>
    public static Task<DriversLicense?> ScanDriversLicenseAsync<TOptions>(this IFrameScanService<TOptions> media, TOptions? options = null, CancellationToken ct = default) where TOptions : class, IScanOptions, new()
        => media.ScanDriversLicensesAsync(false, options, ct).FirstOrDefaultAsync(ct).AsTask();

    /// <summary>Scan driver's licenses continuously, keyed on the license number for duplicate filtering.</summary>
    public static IAsyncEnumerable<DriversLicense> ScanDriversLicensesAsync<TOptions>(
        this IFrameScanService<TOptions> media,
        bool filterDuplicates = true,
        TOptions? options = null,
        CancellationToken ct = default
    )
        where TOptions : class, IScanOptions, new()
    {
        // DriversLicenseAnalyzer reads a barcode rather than OCR text, so it derives from FrameAnalyzer
        // directly and is the one document type ScanDocumentsAsync can't take
        var analyzer = new DriversLicenseAnalyzer();
        return media.ScanAsync(
            new FrameScanRequest<DriversLicense>
            {
                Analyzer = analyzer,
                Subscribe = emit => analyzer.OnDetected = args =>
                {
                    emit(args.Document);
                    return Task.FromResult(true);
                },
                DuplicateKey = d => d.Number ?? $"{d.LastName}|{d.FirstName}|{d.DateOfBirth}",
                Describe = d => String.Join(" ", new[] { d.FirstName, d.LastName }.Where(x => !String.IsNullOrWhiteSpace(x)))
            },
            options.WithDuplicateFilter(filterDuplicates),
            ct
        );
    }

    // ---------------------------------------------------------------------------------------------
    // passports
    // ---------------------------------------------------------------------------------------------

    /// <summary>Scan a passport's machine-readable zone (the two lines at the foot of the photo page).</summary>
    public static Task<Passport?> ScanPassportAsync<TOptions>(this IFrameScanService<TOptions> media, TOptions? options = null, CancellationToken ct = default) where TOptions : class, IScanOptions, new()
        => media.ScanPassportsAsync(false, options, ct).FirstOrDefaultAsync(ct).AsTask();

    /// <summary>Scan passports continuously, keyed on the passport number for duplicate filtering.</summary>
    public static IAsyncEnumerable<Passport> ScanPassportsAsync<TOptions>(
        this IFrameScanService<TOptions> media,
        bool filterDuplicates = true,
        TOptions? options = null,
        CancellationToken ct = default
    ) where TOptions : class, IScanOptions, new() => media.ScanDocumentsAsync(
        new PassportAnalyzer(),
        filterDuplicates,
        p => p.Number ?? $"{p.Surname}|{p.GivenNames}|{p.DateOfBirth}",
        p => String.Join(" ", new[] { p.GivenNames, p.Surname }.Where(x => !String.IsNullOrWhiteSpace(x))),
        options,
        ct
    );

    // ---------------------------------------------------------------------------------------------
    // health cards
    // ---------------------------------------------------------------------------------------------

    /// <summary>Scan a health/insurance card.</summary>
    public static Task<HealthCard?> ScanHealthCardAsync<TOptions>(this IFrameScanService<TOptions> media, TOptions? options = null, CancellationToken ct = default) where TOptions : class, IScanOptions, new()
        => media.ScanHealthCardsAsync(false, options, ct).FirstOrDefaultAsync(ct).AsTask();

    /// <summary>Scan health cards continuously, keyed on the card number for duplicate filtering.</summary>
    public static IAsyncEnumerable<HealthCard> ScanHealthCardsAsync<TOptions>(
        this IFrameScanService<TOptions> media,
        bool filterDuplicates = true,
        TOptions? options = null,
        CancellationToken ct = default
    ) where TOptions : class, IScanOptions, new() => media.ScanDocumentsAsync(
        new HealthCardAnalyzer(),
        filterDuplicates,
        h => h.Number ?? $"{h.Name}|{h.Issuer}",
        h => h.Name ?? h.Number ?? String.Empty,
        options,
        ct
    );

    // ---------------------------------------------------------------------------------------------
    // receipts / invoices / business cards
    // ---------------------------------------------------------------------------------------------

    /// <summary>Scan a point-of-sale receipt — merchant, date, line items, taxes and total.</summary>
    public static Task<Receipt?> ScanReceiptAsync<TOptions>(this IFrameScanService<TOptions> media, TOptions? options = null, CancellationToken ct = default) where TOptions : class, IScanOptions, new()
        => media.ScanReceiptsAsync(false, options, ct).FirstOrDefaultAsync(ct).AsTask();

    /// <summary>
    /// Scan receipts continuously — the "expense a stack of them" flow. Keyed on merchant + date + total,
    /// because a receipt has no identifier that is reliably present.
    /// </summary>
    public static IAsyncEnumerable<Receipt> ScanReceiptsAsync<TOptions>(
        this IFrameScanService<TOptions> media,
        bool filterDuplicates = true,
        TOptions? options = null,
        CancellationToken ct = default
    ) where TOptions : class, IScanOptions, new() => media.ScanDocumentsAsync(
        new ReceiptAnalyzer(),
        filterDuplicates,
        r => r.ReceiptNumber ?? $"{r.Merchant}|{r.Date}|{r.Total}",
        r => r.Merchant ?? r.Total?.ToString() ?? String.Empty,
        options,
        ct
    );

    /// <summary>Scan an invoice — number, date, line items and total.</summary>
    public static Task<Invoice?> ScanInvoiceAsync<TOptions>(this IFrameScanService<TOptions> media, TOptions? options = null, CancellationToken ct = default) where TOptions : class, IScanOptions, new()
        => media.ScanInvoicesAsync(false, options, ct).FirstOrDefaultAsync(ct).AsTask();

    /// <summary>Scan invoices continuously, keyed on the invoice number for duplicate filtering.</summary>
    public static IAsyncEnumerable<Invoice> ScanInvoicesAsync<TOptions>(
        this IFrameScanService<TOptions> media,
        bool filterDuplicates = true,
        TOptions? options = null,
        CancellationToken ct = default
    ) where TOptions : class, IScanOptions, new() => media.ScanDocumentsAsync(
        new InvoiceAnalyzer(),
        filterDuplicates,
        i => i.Number ?? $"{i.Date}|{i.Total}",
        i => i.Number ?? i.Total?.ToString() ?? String.Empty,
        options,
        ct
    );

    /// <summary>Scan a business card — name, title, company, emails, phones, website, address.</summary>
    public static Task<BusinessCard?> ScanBusinessCardAsync<TOptions>(this IFrameScanService<TOptions> media, TOptions? options = null, CancellationToken ct = default) where TOptions : class, IScanOptions, new()
        => media.ScanBusinessCardsAsync(false, options, ct).FirstOrDefaultAsync(ct).AsTask();

    /// <summary>
    /// Scan business cards continuously — the conference-badge flow. Keyed on the first email, falling back
    /// to name + company.
    /// </summary>
    public static IAsyncEnumerable<BusinessCard> ScanBusinessCardsAsync<TOptions>(
        this IFrameScanService<TOptions> media,
        bool filterDuplicates = true,
        TOptions? options = null,
        CancellationToken ct = default
    ) where TOptions : class, IScanOptions, new() => media.ScanDocumentsAsync(
        new BusinessCardAnalyzer(),
        filterDuplicates,
        c => c.Email ?? $"{c.Name}|{c.Company}",
        c => c.Name ?? c.Company ?? String.Empty,
        options,
        ct
    );

    // ---------------------------------------------------------------------------------------------
    // the shared primitive
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Run any <see cref="DocumentAnalyzer{TDocument}"/> in the modal camera and stream its documents. Every
    /// OCR-backed document verb above is one call to this; use it directly with a configured analyzer (a
    /// custom <see cref="IDocumentParser{TDocument}"/>, a different <c>AccumulationFrames</c>) or with a
    /// document type of your own.
    /// </summary>
    /// <param name="media">The media service.</param>
    /// <param name="analyzer">The analyzer to run. Configure it fully before passing it in.</param>
    /// <param name="filterDuplicates">Whether <paramref name="duplicateKey"/> suppresses repeats. Overrides <c>MediaScanOptions.FilterDuplicates</c>.</param>
    /// <param name="duplicateKey">The document's identity for duplicate filtering.</param>
    /// <param name="describe">A short caption shown in the modal's running count.</param>
    /// <param name="options">Modal appearance and scan behaviour.</param>
    /// <param name="ct">Cancels the scan and closes the modal.</param>
    public static IAsyncEnumerable<TDocument> ScanDocumentsAsync<TDocument, TOptions>(
        this IFrameScanService<TOptions> media,
        DocumentAnalyzer<TDocument> analyzer,
        bool filterDuplicates = true,
        Func<TDocument, string>? duplicateKey = null,
        Func<TDocument, string>? describe = null,
        TOptions? options = null,
        CancellationToken ct = default
    ) where TOptions : class, IScanOptions, new() => media.ScanAsync(
        new FrameScanRequest<TDocument>
        {
            Analyzer = analyzer,
            Subscribe = emit => analyzer.OnDetected = args =>
            {
                emit(args.Document);
                return Task.FromResult(true); // stay armed — the service decides when to stop
            },
            DuplicateKey = duplicateKey,
            Describe = describe
        },
        options.WithDuplicateFilter(filterDuplicates),
        ct
    );
}
