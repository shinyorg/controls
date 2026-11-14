using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Graphics;
using Shiny.Controls.Analysis;
using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Analysis;
using Shiny.Maui.Controls;
using Shiny.Maui.Controls.Camera;
using Shiny.Maui.Controls.Analysis.Ai;
using Shiny.Maui.Controls.Analysis.Barcode;
using Shiny.Maui.Controls.Analysis.Documents;
using Shiny.Maui.Controls.Analysis.Face;
using Shiny.Maui.Controls.Analysis.Motion;

namespace Sample.Features.Camera;

public partial class CameraPage : ShinyContentPage
{
    // sample-only: shared app-session list the document analyzers feed (see DocumentSessionPage)
    readonly DocumentSessionStore session;

    // one analyzer runs at a time; these are built once and swapped into Camera.Analyzer by the picker
    readonly BarcodeAnalyzer barcode = new();
    readonly Dictionary<string, IFrameAnalyzer> analyzers;

    // a center band the barcode analyzer scans when "Restrict scan area" is on
    static readonly RectF ScanBand = new(0.1f, 0.4f, 0.8f, 0.2f);

    // bound by the options-sheet TableView
    public CameraFilter[] Filters { get; } = Enum.GetValues<CameraFilter>();
    public ObservableCollection<CameraInfo> Cameras { get; } = [];

    /// <summary>
    /// The spatial effects offered by the "Effect" picker. Unlike <see cref="Filters"/> — which are colour
    /// grades and so map to a matrix — these need to see a pixel's neighbours, and stack on top of the filter.
    /// </summary>
    /// <remarks>
    /// Deliberately <b>not</b> called <c>Effects</c>: <see cref="Element"/> already has an <c>Effects</c>
    /// property (MAUI's routing effects), so a page-level one hides it and <c>{Binding Effects}</c> resolves to
    /// the empty base collection — the picker renders with no items and nothing appears broken.
    /// </remarks>
    public string[] SpatialEffects { get; } = ["None", "Comic", "Sketch", "Posterize", "Pixelate", "Blur"];

    /// <summary>
    /// One entry in the on-screen look strip. A look is either a <see cref="CameraFilter"/> (a colour grade,
    /// set on <see cref="CameraView.Filter"/>) or a spatial <see cref="BuiltInCameraEffect"/> (added to
    /// <see cref="CameraView.Effects"/>) — the strip presents them together because that is how a user thinks
    /// about them, while the Options sheet keeps the two pickers separate because they are different APIs.
    /// </summary>
    public record CameraLook(string Name, CameraFilter Filter, string Effect = "None");

    /// <summary>Everything reachable in one tap from the preview: no look, the 11 colour grades, the 5 spatial effects.</summary>
    public CameraLook[] Looks { get; } =
    [
        new("None", CameraFilter.None),
        .. Enum.GetValues<CameraFilter>()
            .Where(f => f != CameraFilter.None)
            .Select(f => new CameraLook(f.ToString(), f)),
        new("Comic", CameraFilter.None, "Comic"),
        new("Sketch", CameraFilter.None, "Sketch"),
        new("Poster", CameraFilter.None, "Posterize"),
        new("Pixel", CameraFilter.None, "Pixelate"),
        new("Blur", CameraFilter.None, "Blur")
    ];

    /// <summary>Applies a look from the strip — sets the colour filter and the spatial effect together.</summary>
    public ICommand SelectLookCommand { get; }

    string selectedEffect = "None";

    /// <summary>Two-way bound to the "Effect" picker; rewrites <see cref="CameraView.Effects"/> live.</summary>
    public string SelectedEffect
    {
        get => this.selectedEffect;
        set
        {
            this.selectedEffect = value;
            this.OnPropertyChanged();
            this.ApplyEffects();
        }
    }

    // The two toggled effects are built once and added/removed from Camera.Effects, so their state (the mask
    // smoother, the stylizer's prompt) survives being switched off and back on.
    FaceMaskEffect? faceMask;
    AiPhotoStylizer? stylizer;

    /// <summary>Target capture resolutions for the "Video quality" picker.</summary>
    public VideoQuality[] VideoQualities { get; } = Enum.GetValues<VideoQuality>();

    /// <summary>Choices for the "Capture orientation" picker.</summary>
    public CameraOrientation[] Orientations { get; } = Enum.GetValues<CameraOrientation>();

    /// <summary>
    /// Frame-rate choices, with 0 standing in for "platform default" — <see cref="CameraView.VideoFrameRate"/>
    /// is <c>int?</c> and a picker cannot bind a null entry, so <see cref="SelectedFrameRate"/> maps between them.
    /// </summary>
    public int[] FrameRates { get; } = [0, 15, 24, 30, 60];

    int selectedFrameRate;

    /// <summary>Two-way bound to the sheet's frame-rate picker; 0 clears the request back to the platform default.</summary>
    public int SelectedFrameRate
    {
        get => this.selectedFrameRate;
        set
        {
            this.selectedFrameRate = value;
            this.Camera.VideoFrameRate = value > 0 ? value : null;
            this.OnPropertyChanged();
        }
    }

    /// <summary>The detector names shown in the "Detector" picker.</summary>
    public string[] Detectors { get; }

    string selectedDetector = "None";

    /// <summary>Two-way bound to the sheet's "Detector" picker; swaps <see cref="CameraView.Analyzer"/> live.</summary>
    public string SelectedDetector
    {
        get => this.selectedDetector;
        set
        {
            this.selectedDetector = value;
            this.OnPropertyChanged(nameof(this.SelectedDetector));
            this.ApplyDetector();
        }
    }

    CameraInfo? selectedCamera;

    /// <summary>Two-way bound to the sheet's "Lens" picker; shows the active lens and switches to a chosen one.</summary>
    public CameraInfo? SelectedCamera
    {
        get => this.selectedCamera;
        set => this.SetCamera(value, fromUser: true);
    }

    /// <summary>Opens the scanned-documents session page (from the options sheet).</summary>
    public ICommand ViewSessionCommand { get; }

    /// <summary>Right-aligned count shown on the "Scanned documents" row; updates as captures arrive.</summary>
    public string ScannedSummary => this.session.Documents.Count switch
    {
        0 => "None yet",
        1 => "1 captured",
        var n => $"{n} captured"
    };

    // Each analyzer's OnDetected handler runs (on the UI thread) with its typed args — but only while the
    // analyzer is armed (tap "Scan" -> Camera.ScanCommand). Each returns whether to keep scanning: barcode /
    // motion / face honor the Continuous switch; documents are single-shot (return false), optionally
    // capturing + stopping the preview.
    Func<BarcodesDetectedEventArgs, Task<bool>> OnBarcode { get; }
    Func<MotionEventArgs, Task<bool>> OnMotion { get; }
    Func<FacesDetectedEventArgs, Task<bool>> OnFace { get; }
    Func<DocumentDetectedEventArgs<Invoice>, Task<bool>> OnInvoice { get; }
    Func<DocumentDetectedEventArgs<Receipt>, Task<bool>> OnReceipt { get; }
    Func<DocumentDetectedEventArgs<BusinessCard>, Task<bool>> OnBusinessCard { get; }
    Func<DocumentDetectedEventArgs<HealthCard>, Task<bool>> OnHealthCard { get; }
    Func<DocumentDetectedEventArgs<DriversLicense>, Task<bool>> OnLicense { get; }
    Func<DocumentDetectedEventArgs<CreditCard>, Task<bool>> OnCreditCard { get; }
    Func<DocumentDetectedEventArgs<Passport>, Task<bool>> OnPassport { get; }

    public CameraPage()
    {
        this.session = IPlatformApplication.Current!.Services.GetRequiredService<DocumentSessionStore>();

        this.ViewSessionCommand = new Command(async () =>
        {
            this.OptionsPanel.IsOpen = false;
            await Shell.Current.GoToAsync("//documentsession");
        });

        this.SelectLookCommand = new Command<CameraLook>(look =>
        {
            if (look is null)
                return;

            this.Camera.Filter = look.Filter;
            this.SelectedEffect = look.Effect;   // setter rebuilds Camera.Effects
            this.ShowStatus(look.Name == "None" ? "Look cleared" : $"Look: {look.Name}");
        });

        this.OnBarcode = e => { this.ShowStatus($"{e.Barcodes.Count} code(s): {e.First.Format} {e.First.Value}"); return Task.FromResult(this.ContinuousSwitch.On); };
        this.OnMotion = e => { this.ShowStatus(e.InMotion ? "Motion detected" : "Motion stopped"); return Task.FromResult(this.ContinuousSwitch.On); };
        this.OnFace = e => { this.ShowStatus($"{e.Faces.Count} face(s)"); return Task.FromResult(this.ContinuousSwitch.On); };

        this.OnInvoice = e =>
        {
            var d = e.Document;
            var summary = $"Invoice {d.Number ?? "?"} — total {d.Total?.ToString("0.00") ?? "?"}, {d.Lines.Count} line(s)";
            return this.OnDocument("Invoice", summary, Detail(
                ("Number", d.Number),
                ("Date", d.Date?.ToString("yyyy-MM-dd")),
                ("Total", d.Total?.ToString("0.00")),
                ("Lines", d.Lines.Count.ToString())));
        };
        this.OnReceipt = e =>
        {
            var d = e.Document;
            var summary = $"{d.Merchant ?? "Receipt"} — total {d.Total?.ToString("0.00") ?? "?"}, {d.Lines.Count} item(s)";
            return this.OnDocument("Receipt", summary, Detail(
                ("Merchant", d.Merchant),
                ("Receipt #", d.ReceiptNumber),
                ("Date", d.Date?.ToString("yyyy-MM-dd")),
                ("Time", d.Time?.ToString("HH:mm")),
                ("Items", d.Lines.Count.ToString()),
                ("Subtotal", d.Subtotal?.ToString("0.00")),
                ("Tax", d.Tax?.ToString("0.00")),
                ("Tip", d.Tip?.ToString("0.00")),
                ("Discount", d.Discount?.ToString("0.00")),
                ("Total", d.Total?.ToString("0.00")),
                ("Payment", d.PaymentMethod),
                ("Card", d.CardLast4 is null ? null : $"•••• {d.CardLast4}")));
        };
        this.OnBusinessCard = e =>
        {
            var d = e.Document;
            var summary = $"{d.Name ?? "Business Card"}{(d.Company is null ? "" : $" — {d.Company}")}";
            return this.OnDocument("Business Card", summary, Detail(
                ("Name", d.Name),
                ("Title", d.JobTitle),
                ("Company", d.Company),
                ("Email", d.Email),
                ("Phone", d.Phone),
                ("Website", d.Website),
                ("Address", d.Address)));
        };
        this.OnHealthCard = e =>
        {
            var d = e.Document;
            var summary = $"Health Card {d.Number}{(d.Province is null ? "" : $" ({d.Province})")} — {d.Name}";
            return this.OnDocument("Health Card", summary, Detail(
                ("Number", d.Number),
                ("Name", d.Name),
                ("Province", d.Province),
                ("Issuer", d.Issuer),
                ("Expiry", d.Expiry?.ToString("yyyy-MM-dd"))));
        };
        this.OnLicense = e =>
        {
            var d = e.Document;
            var summary = $"License {d.Number} — {d.FirstName} {d.LastName}";
            return this.OnDocument("Driver's License", summary, Detail(
                ("Number", d.Number),
                ("Name", $"{d.FirstName} {d.LastName}".Trim()),
                ("Date of birth", d.DateOfBirth?.ToString("yyyy-MM-dd")),
                ("Expiry", d.Expiry?.ToString("yyyy-MM-dd")),
                ("Province / State", d.Jurisdiction),
                ("Address", d.Address)));
        };
        this.OnCreditCard = e =>
        {
            var d = e.Document;
            var last4 = d.Number is { Length: >= 4 } n ? n[^4..] : d.Number;
            var summary = $"{d.Type} •••• {last4} exp {d.Expiry?.ToString("MM/yy") ?? "?"}";
            return this.OnDocument("Credit Card", summary, Detail(
                ("Type", d.Type.ToString()),
                ("Number", last4 is null ? null : $"•••• {last4}"),
                ("Expiry", d.Expiry?.ToString("MM/yy")),
                ("Name", $"{d.FirstName} {d.LastName}".Trim()),
                ("Company", d.CompanyName)));
        };
        this.OnPassport = e =>
        {
            var d = e.Document;
            var summary = $"Passport {d.Number} — {d.GivenNames} {d.Surname} ({d.Nationality})";
            return this.OnDocument("Passport", summary, Detail(
                ("Number", d.Number),
                ("Name", $"{d.GivenNames} {d.Surname}".Trim()),
                ("Nationality", d.Nationality),
                ("Issuing country", d.IssuingCountry),
                ("Date of birth", d.DateOfBirth?.ToString("yyyy-MM-dd")),
                ("Expiry", d.Expiry?.ToString("yyyy-MM-dd")),
                ("Sex", d.Sex.ToString())));
        };

        // build the analyzers once and wire their OnDetected handlers; the picker swaps which one is active
        this.barcode.OnDetected = this.OnBarcode;
        this.analyzers = new()
        {
            ["Barcode / QR"] = this.barcode,
            ["Motion"] = new MotionAnalyzer { OnDetected = this.OnMotion },
            // DetectLandmarks is what FaceMaskEffect follows — off by default because it costs more per frame
            ["Face"] = new FaceAnalyzer { OnDetected = this.OnFace, DetectLandmarks = true },
            ["Invoice"] = new InvoiceAnalyzer { OnDetected = this.OnInvoice },
            ["Receipt"] = new ReceiptAnalyzer { OnDetected = this.OnReceipt },
            ["Business Card"] = new BusinessCardAnalyzer { OnDetected = this.OnBusinessCard },
            ["Health Card"] = new HealthCardAnalyzer { OnDetected = this.OnHealthCard },
            ["Driver's License"] = new DriversLicenseAnalyzer { OnDetected = this.OnLicense },
            ["Credit Card"] = new CreditCardAnalyzer { OnDetected = this.OnCreditCard },
            ["Passport"] = new PassportAnalyzer { OnDetected = this.OnPassport },
            // detects a document is present (cheap), then ships that one frame to an IChatClient (MEAI) to parse
            ["AI Document"] = new AiDocumentAnalyzer(
                IPlatformApplication.Current!.Services.GetRequiredService<Microsoft.Extensions.AI.IChatClient>())
                { OnDetected = this.OnAiDocument },
        };
        this.Detectors = ["None", .. this.analyzers.Keys];

        InitializeComponent();
        SampleSourceCode.Attach(this);
        this.BindingContext = this;

        // re-apply ShowBoundingBox / ScanWindow on the active analyzer when their switches toggle
        this.BoxesSwitch.PropertyChanged += (_, _) => this.ApplyBoxes();
        this.ScanAreaSwitch.PropertyChanged += (_, _) => this.ApplyScanArea();
        this.FaceMaskSwitch.PropertyChanged += (_, e) => this.OnSwitch(e, this.FaceMaskSwitch.On, this.SetFaceMask);
        this.AiStylizeSwitch.PropertyChanged += (_, e) => this.OnSwitch(e, this.AiStylizeSwitch.On, this.SetAiStylize);

        // open the options sheet at full screen
        this.OptionsPanel.Detents = new ObservableCollection<DetentValue> { DetentValue.Full };

        // keep the options-sheet count in sync as documents are captured
        this.session.Documents.CollectionChanged += (_, _) => this.OnPropertyChanged(nameof(this.ScannedSummary));

        this.Camera.CameraError += (_, e) => this.ShowStatus(e.Message);
        this.Camera.VideoCaptured += (_, v) => this.ShowStatus($"Saved video: {v.FilePath}");
    }

    // Swap the active analyzer (null = "None") and re-apply the box / scan-area options to it.
    void ApplyDetector()
    {
        if (this.Camera is null)
            return;
        this.Camera.Analyzer = this.analyzers.GetValueOrDefault(this.selectedDetector);
        this.ApplyBoxes();
        this.ApplyScanArea();
    }

    void ApplyBoxes()
    {
        if (this.Camera?.Analyzer is FrameAnalyzer fa)
            fa.ShowBoundingBox = this.BoxesSwitch.On;
    }


    // Rebuild Camera.Effects from the sheet. Order is meaningful — the spatial look goes first so the face
    // mask draws on top of it rather than being comic-ified with the scene — and CameraView.Filter is always
    // applied ahead of everything here.
    void ApplyEffects()
    {
        if (this.Camera is null)
            return;

        this.Camera.Effects.Clear();

        if (SpatialEffect(this.selectedEffect) is { } spatial)
        {
            this.Camera.Effects.Add(spatial);

            // Coverage is genuinely uneven (no live preview filter on Windows, Android needs API 33 for
            // shaders), so say what will actually happen rather than letting it look broken.
            var support = CameraView.GetEffectSupport(spatial);
            if (support != EffectSupport.Full)
                this.ShowStatus($"{this.selectedEffect}: {Describe(support)}");
        }

        if (this.faceMask is { } mask)
            this.Camera.Effects.Add(mask);

        if (this.stylizer is { } ai)
            this.Camera.Effects.Add(ai);
    }

    static BuiltInCameraEffect? SpatialEffect(string name) => name switch
    {
        "Comic" => CameraEffects.Comic,
        "Sketch" => CameraEffects.Sketch,
        "Posterize" => CameraEffects.Posterize,
        "Pixelate" => CameraEffects.Pixelate,
        "Blur" => CameraEffects.Blur,
        _ => null
    };

    static string Describe(EffectSupport support) => support switch
    {
        EffectSupport.StillOnly => "photos only on this device — the live preview can't run it",
        EffectSupport.ColorOnly => "approximated with a colour matrix on this device",
        EffectSupport.Unsupported => "not supported on this device",
        _ => "supported"
    };


    // Pins shades + a moustache to every tracked face. Drawn rather than image-backed so the sample needs no
    // asset: OnDraw gets the canvas already centred on the anchor and rotated to the head's roll, so
    // everything below is drawn around (0,0) in mask-sized units.
    // SwitchCell has no Toggled event, so react to its bindable changing (the pattern the other switches use).
    void OnSwitch(System.ComponentModel.PropertyChangedEventArgs e, bool value, Action<bool> apply)
    {
        if (e.PropertyName == SwitchCell.OnProperty.PropertyName)
            apply(value);
    }

    void SetFaceMask(bool on)
    {
        if (on)
        {
            this.faceMask ??= new FaceMaskEffect
            {
                Anchor = FaceAnchor.EyeCenter,
                Scale = 2.6f,
                AspectRatio = 2.4f,
                OnDraw = DrawShades
            };
            this.faceMask.ResetTracking();

            if (this.Camera?.Analyzer is not FaceAnalyzer)
                this.ShowStatus("Face mask needs the Face detector — pick it in the Detector list above");
        }
        else
        {
            this.faceMask = null;
        }

        this.ApplyEffects();
    }

    static void DrawShades(ICanvas canvas, FaceMaskPlacement placement)
    {
        var w = placement.Width;
        var h = placement.Height;
        var lensW = w * 0.4f;
        var lensH = h * 0.62f;

        canvas.FillColor = Color.FromRgba(10, 10, 12, 235);
        canvas.FillRoundedRectangle(-w / 2f, -lensH / 2f, lensW, lensH, lensH * 0.35f);
        canvas.FillRoundedRectangle((w / 2f) - lensW, -lensH / 2f, lensW, lensH, lensH * 0.35f);

        // bridge + arms
        canvas.StrokeColor = Color.FromRgba(10, 10, 12, 235);
        canvas.StrokeSize = Math.Max(2f, h * 0.12f);
        canvas.DrawLine(-w / 2f + lensW, 0, (w / 2f) - lensW, 0);

        // moustache, a little below the eye line
        var mw = w * 0.5f;
        var my = h * 1.15f;
        canvas.FillColor = Color.FromRgba(40, 26, 16, 235);
        canvas.FillEllipse(-mw / 2f, my, mw / 2f, h * 0.34f);
        canvas.FillEllipse(0, my, mw / 2f, h * 0.34f);
    }


    // Adds an ICaptureEffect: CapturePhotoAsync then runs the still through the image model before returning,
    // which is why the shutter takes visibly longer with this on.
    void SetAiStylize(bool on)
    {
        if (on)
        {
            if (this.stylizer is null)
            {
                var generator = IPlatformApplication.Current!.Services
                    .GetRequiredService<Microsoft.Extensions.AI.IImageGenerator>();

                this.stylizer = new AiPhotoStylizer(generator);
                this.stylizer.Error += (_, ex) => this.ShowStatus($"Stylize failed: {ex.Message}");
            }
            this.ShowStatus("AI stylize on — captures will take a few seconds");
        }
        else
        {
            this.stylizer = null;
        }

        this.ApplyEffects();
    }

    // ScanWindow demo: restrict the barcode analyzer to a center band (the overlay frames it as a reticle).
    void ApplyScanArea()
        => this.barcode.ScanWindow = this.ScanAreaSwitch.On ? ScanBand : null;

    // Show the headline on-screen and add the document to the shared session list.
    void Capture(string kind, string summary, string detail)
    {
        this.ShowStatus(summary);
        this.session.Add(kind, summary, detail);
    }

    // AI document detector: the model returns a free-form AiDocument (type + summary + label/value fields).
    Task<bool> OnAiDocument(DocumentDetectedEventArgs<AiDocument> e)
    {
        var d = e.Document;
        var summary = d.Summary ?? d.DocumentType ?? "AI document";
        var detail = Detail([.. (d.Fields ?? []).Select(f => (f.Label, f.Value))]);
        return this.OnDocument(d.DocumentType ?? "AI Document", summary, detail);
    }

    // OnDetected handler shared by every document analyzer: record the document, and when "Capture & stop" is
    // on grab a still and freeze the preview (tap the thumbnail to resume). Always returns false — documents are
    // single-shot, so the analyzer disarms until the next "Scan" tap.
    async Task<bool> OnDocument(string kind, string summary, string detail)
    {
        this.Capture(kind, summary, detail);
        if (this.CaptureStopSwitch.On)
        {
            var photo = await this.Camera.CaptureAndStopAsync();
            this.ShowThumbnail(photo);
            this.ShowStatus("Captured & stopped — tap the photo to resume");
        }
        return false;
    }

    void ShowThumbnail(CameraPhoto photo)
    {
        var bytes = photo.Data;
        this.PhotoThumbnail.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
        this.PhotoThumbnail.IsVisible = true;
    }

    // Join the non-empty "Label: value" lines into a detail block.
    static string Detail(params (string Label, string? Value)[] fields)
        => string.Join("\n", fields
            .Where(f => !string.IsNullOrWhiteSpace(f.Value))
            .Select(f => $"{f.Label}: {f.Value}"));

    void OnOptionsClicked(object? sender, EventArgs e) => this.OptionsPanel.IsOpen = true;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // The CameraView starts its own preview and requests permission itself (reporting any denial via
        // CameraError) — so we don't gate on it here. We only need the handler to be connected before asking
        // it for the lens list; on first appearance it may not be yet, so wait for it.
        this.Camera.IsActive = true;
        if (this.Camera.Handler is not null)
            _ = this.LoadCamerasAsync();
        else
            this.Camera.HandlerChanged += this.OnCameraHandlerReady;
    }

    void OnCameraHandlerReady(object? sender, EventArgs e)
    {
        if (this.Camera.Handler is null)
            return;
        this.Camera.HandlerChanged -= this.OnCameraHandlerReady;
        _ = this.LoadCamerasAsync();
    }

    async Task LoadCamerasAsync()
    {
        var available = await this.Camera.GetAvailableCamerasAsync();
        this.Cameras.Clear();
        foreach (var c in available)
            this.Cameras.Add(c);

        // show the active lens in the picker (display only — don't reassign CameraId and force a reconfigure)
        this.SetCamera(
            this.Cameras.FirstOrDefault(c => c.Facing == this.Camera.Facing) ?? this.Cameras.FirstOrDefault(),
            fromUser: false);
    }

    void SetCamera(CameraInfo? value, bool fromUser)
    {
        if (ReferenceEquals(this.selectedCamera, value))
            return;
        this.selectedCamera = value;
        this.OnPropertyChanged(nameof(this.SelectedCamera));
        if (fromUser && value is not null)
            this.Camera.CameraId = value.Id;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        this.Camera.IsActive = false;
    }

    void OnFlipClicked(object? sender, EventArgs e)
        => this.Camera.Facing = this.Camera.Facing == Shiny.Controls.Camera.CameraFacing.Back
            ? Shiny.Controls.Camera.CameraFacing.Front
            : Shiny.Controls.Camera.CameraFacing.Back;

    void OnTorchClicked(object? sender, EventArgs e)
        => this.Camera.IsTorchOn = !this.Camera.IsTorchOn;

    async void OnRecordClicked(object? sender, EventArgs e)
    {
        try
        {
            if (!this.Camera.IsRecording)
            {
                // Burn-in overlay demo: composite a running timestamp + watermark into every recorded frame.
                // DrawOverlay runs off the UI thread once per encoded frame — draw in the frame's pixel space.
                var options = new VideoRecordingOptions();
                if (this.OverlayVideoSwitch.On)
                    options.Overlay = new DelegateVideoOverlay((canvas, frame, ctx) =>
                    {
                        var pad = frame.Width * 0.03f;
                        canvas.FontColor = Colors.White;
                        canvas.FontSize = Math.Max(24, frame.Height * 0.04f);

                        // top-left running timer
                        canvas.DrawString(
                            ctx.Elapsed.ToString(@"mm\:ss\.f"),
                            pad, pad, frame.Width, 60,
                            HorizontalAlignment.Left, VerticalAlignment.Top);

                        // bottom-right watermark
                        canvas.FontColor = Color.FromRgba(255, 255, 255, 180);
                        canvas.DrawString(
                            "SHINY CAMERA",
                            0, frame.Height - pad - 40, frame.Width - pad, 40,
                            HorizontalAlignment.Right, VerticalAlignment.Top);
                    });

                await this.Camera.StartVideoRecordingAsync(options);
                this.RecordButton.Text = "Stop";
                this.ShowStatus(this.OverlayVideoSwitch.On ? "Recording with overlay…" : "Recording…");
            }
            else
            {
                this.RecordButton.Text = "Record";
                var video = await this.Camera.StopVideoRecordingAsync();
                this.ShowStatus($"Saved: {Path.GetFileName(video.FilePath)}");
            }
        }
        catch (Exception ex)
        {
            this.RecordButton.Text = "Record";
            this.ShowStatus("Record failed: " + ex.Message);
        }
    }

    async void OnThumbnailTapped(object? sender, TappedEventArgs e)
    {
        // restart the preview after a document "capture & stop" (tap Scan again to re-arm)
        await this.Camera.StartAsync();
        this.PhotoThumbnail.IsVisible = false;
        this.ShowStatus("Resumed");
    }

    void OnScanClicked(object? sender, EventArgs e)
    {
        this.Camera.Scan(); // arm the active analyzer for one scan
        this.ShowStatus("Scanning…");
    }

    async void OnCaptureClicked(object? sender, EventArgs e)
    {
        try
        {
            var photo = await this.Camera.CapturePhotoAsync();
            // show the captured still so the applied filter is visible
            var bytes = photo.Data;
            this.PhotoThumbnail.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
            this.PhotoThumbnail.IsVisible = true;
            this.ShowStatus($"Captured {photo.Width}x{photo.Height} ({photo.Data.Length / 1024} KB)");
        }
        catch (Exception ex)
        {
            this.ShowStatus("Capture failed: " + ex.Message);
        }
    }

    void ShowStatus(string message)
    {
        this.StatusLabel.Text = message;
        this.StatusLabel.IsVisible = true;
    }
}
