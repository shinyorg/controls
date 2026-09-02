using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Camera;
using Shiny.Maui.Controls.Camera.Barcode;
using Shiny.Maui.Controls.Camera.Documents;
using Shiny.Maui.Controls.Camera.Face;
using Shiny.Maui.Controls.Camera.Media;
using Shiny.Maui.Controls.Camera.Ocr;

namespace Sample.Features.Camera;

/// <summary>
/// Every verb on <see cref="IMediaService"/> in one place — permissions, capture, gallery and the whole
/// family of <c>Scan…</c> extensions the analyzer packages contribute.
/// </summary>
public partial class MediaServicePage : ContentPage
{
    readonly IMediaService media;

    ImageSource? preview;

    // resolved from the container rather than injected: a Shell ContentTemplate activates the page itself,
    // and a page it cannot construct renders blank *and* wedges navigation for the rest of the session
    public MediaServicePage()
    {
        this.media = IPlatformApplication.Current!.Services.GetRequiredService<IMediaService>();
        this.BindingContext = this;
        this.InitializeComponent();
        SampleSourceCode.Attach(this);
    }

    public ObservableCollection<string> Log { get; } = [];

    public ImageSource? Preview
    {
        get => this.preview;
        private set
        {
            this.preview = value;
            this.OnPropertyChanged();
            this.OnPropertyChanged(nameof(this.HasPreview));
        }
    }

    public bool HasPreview => this.Preview is not null;

    // ---- permissions ---------------------------------------------------------------------------

    public ICommand RequestCameraCommand => this.Run(async () =>
        this.Write($"camera permission: {await this.media.RequestCameraPermissionAsync()}"));

    public ICommand RequestCameraMicCommand => this.Run(async () =>
        this.Write($"camera + microphone: {await this.media.RequestCameraPermissionAsync(true)}"));

    public ICommand RequestGalleryReadCommand => this.Run(async () =>
        this.Write($"gallery read: {await this.media.RequestGalleryPermissionAsync()}"));

    public ICommand RequestGalleryWriteCommand => this.Run(async () =>
        this.Write($"gallery write: {await this.media.RequestGalleryPermissionAsync(true)}"));

    public ICommand OpenSettingsCommand => this.Run(async () => await this.media.OpenSettingsAsync());

    public ICommand ListCamerasCommand => this.Run(async () =>
    {
        var cameras = await this.media.GetAvailableCamerasAsync();
        if (cameras.Count == 0)
            this.Write("no cameras reported");

        foreach (var camera in cameras)
            this.Write($"camera: {camera.Name} ({camera.Facing}{(camera.IsDefault ? ", default" : "")}) — {camera.Id}");
    });

    // ---- capture -------------------------------------------------------------------------------

    public ICommand TakePhotoCommand => this.Run(async () => this.ShowPhoto(
        await this.media.TakePhotoAsync(new PhotoCaptureOptions
        {
            Title = "Take a photo",
            Instructions = "Tap the shutter, then keep it or shoot again"
        })
    ));

    public ICommand TakePhotoWithEffectsCommand => this.Run(async () => this.ShowPhoto(
        await this.media.TakePhotoAsync(new PhotoCaptureOptions
        {
            Title = "Pick a look",
            Instructions = "Swipe the strip — the look is baked into the capture",
            ShowEffectPicker = true
        })
    ));

    public ICommand TakeZoomCappedPhotoCommand => this.Run(async () => this.ShowPhoto(
        await this.media.TakePhotoAsync(new PhotoCaptureOptions
        {
            Title = "Zoom capped at 4x",
            Instructions = "Pinch to zoom — it stops at 4x",
            Zoom = 2d,
            MaxZoom = 4d
        })
    ));

    public ICommand TakeCompressedPhotoCommand => this.Run(async () => this.ShowPhoto(
        await this.media.TakePhotoAsync(new PhotoCaptureOptions
        {
            Title = "Compressed",
            CompressionQuality = 60,
            MaxDimension = 1024
        })
    ));

    public ICommand TakePhotoNoConfirmCommand => this.Run(async () => this.ShowPhoto(
        await this.media.TakePhotoAsync(new PhotoCaptureOptions
        {
            Title = "One tap",
            ShowConfirmation = false
        })
    ));

    public ICommand RecordVideoCommand => this.Run(async () => this.ShowVideo(
        await this.media.RecordVideoAsync(new VideoCaptureOptions { Title = "Record" })
    ));

    public ICommand RecordShortVideoCommand => this.Run(async () => this.ShowVideo(
        await this.media.RecordVideoAsync(new VideoCaptureOptions
        {
            Title = "10 seconds",
            Instructions = "Stops itself at ten seconds",
            MaxDuration = TimeSpan.FromSeconds(10),
            Quality = VideoQuality.Medium
        })
    ));

    // ---- gallery -------------------------------------------------------------------------------

    public ICommand PickPhotoCommand => this.Run(async () => this.ShowPhoto(await this.media.PickPhotoAsync()));

    public ICommand PickPhotosCommand => this.Run(async () =>
    {
        var photos = await this.media.PickPhotosAsync(5, new MediaPickOptions { MaxDimension = 1600 });
        this.Write($"picked {photos.Count} photo(s)");
        if (photos.Count > 0)
            this.ShowPhoto(photos[0]);
    });

    public ICommand PickVideoCommand => this.Run(async () => this.ShowVideo(await this.media.PickVideoAsync()));

    // ---- scanning ------------------------------------------------------------------------------

    public ICommand ScanBarcodeCommand => this.Run(async () =>
    {
        var code = await this.media.ScanBarcodeAsync();
        this.Write(code is null ? "barcode: cancelled" : $"barcode: {code.Format} — {code.Value}");
    });

    public ICommand ScanBarcodesCommand => this.Run(async () =>
    {
        // the modal stays up and streams; tap ✓ to finish
        await foreach (var code in this.media.ScanBarcodesAsync())
            this.Write($"barcode: {code.Format} — {code.Value}");
    });

    public ICommand ScanBarcodesWithDupesCommand => this.Run(async () =>
    {
        // duplicate filtering off: the same code re-reports every time it re-enters view
        await foreach (var code in this.media.ScanBarcodesAsync(filterDuplicates: false))
            this.Write($"barcode (dupes on): {code.Value}");
    });

    public ICommand ScanQrBandCommand => this.Run(async () =>
    {
        var code = await this.media.ScanBarcodeAsync(
            [BarcodeFormat.QrCode],
            new MediaScanOptions
            {
                Title = "QR only",
                Instructions = "Line the code up in the band",
                ScanWindow = new RectF(0.1f, 0.38f, 0.8f, 0.24f)
            }
        );
        this.Write(code is null ? "qr: cancelled" : $"qr: {code.Value}");
    });

    public ICommand ScanTextCommand => this.Run(async () =>
    {
        var text = await this.media.ScanTextStringAsync(options: new MediaScanOptions { Title = "Read text" });
        this.Write(text is null ? "ocr: cancelled" : $"ocr: {Summarize(text)}");
    });

    public ICommand ScanCreditCardCommand => this.Run(async () =>
    {
        var card = await this.media.ScanCreditCardAsync(new MediaScanOptions
        {
            Title = "Credit card",
            Instructions = "Hold the front of the card in frame"
        });
        this.Write(card is null
            ? "card: cancelled"
            : $"card: {card.Type} ****{card.Number?[^4..]} exp {card.Expiry:yyyy-MM}");
    });

    public ICommand ScanDriversLicenseCommand => this.Run(async () =>
    {
        var licence = await this.media.ScanDriversLicenseAsync(new MediaScanOptions
        {
            Title = "Driver's license",
            Instructions = "Point at the barcode on the BACK of the card"
        });
        this.Write(licence is null
            ? "licence: cancelled"
            : $"licence: {licence.FirstName} {licence.LastName} #{licence.Number} ({licence.Jurisdiction})");
    });

    public ICommand ScanPassportCommand => this.Run(async () =>
    {
        var passport = await this.media.ScanPassportAsync(new MediaScanOptions
        {
            Title = "Passport",
            Instructions = "Frame the two lines at the foot of the photo page"
        });
        this.Write(passport is null
            ? "passport: cancelled"
            : $"passport: {passport.GivenNames} {passport.Surname} #{passport.Number} ({passport.Nationality})");
    });

    public ICommand ScanBusinessCardCommand => this.Run(async () =>
    {
        var card = await this.media.ScanBusinessCardAsync(new MediaScanOptions { Title = "Business card" });
        this.Write(card is null ? "business card: cancelled" : $"business card: {card.Name} — {card.Company} — {card.Email}");
    });

    public ICommand ScanReceiptsCommand => this.Run(async () =>
    {
        await foreach (var receipt in this.media.ScanReceiptsAsync(options: new MediaScanOptions
        {
            Title = "Receipts",
            Instructions = "Scan as many as you like, then tap ✓",
            AllowZoom = false // a zoomed frame is a cropped one, and OCR wants the edges
        }))
            this.Write($"receipt: {receipt.Merchant} {receipt.Total} on {receipt.Date}");
    });

    public ICommand DetectFacesCommand => this.Run(async () =>
    {
        var faces = await this.media.DetectFaceAsync();
        this.Write(faces is null ? "faces: cancelled" : $"faces: {faces.Count} in frame");
    });

    public ICommand ClearLogCommand => new Command(() =>
    {
        this.Log.Clear();
        this.Preview = null;
    });

    // ---- plumbing ------------------------------------------------------------------------------

    void ShowPhoto(MediaPhoto? photo)
    {
        if (photo is null)
        {
            this.Write("photo: cancelled");
            return;
        }

        this.Write($"photo: {photo.Width}x{photo.Height} {photo.ContentType} — {photo.Data.Length / 1024}KB");
        this.Preview = photo.AsImageSource();
    }

    void ShowVideo(MediaVideo? video)
    {
        if (video is null)
        {
            this.Write("video: cancelled");
            return;
        }

        this.Write($"video: {Path.GetFileName(video.FilePath)} — {video.Length / 1024}KB, {video.Duration}");
    }

    /// <summary>
    /// A command whose async body reports its own failures. A bare <c>new Command(async () =&gt; …)</c> is
    /// async void, so anything the camera throws — a refused permission on a head that throws rather than
    /// returning a status, a decode failure — takes the app down instead of landing in the log.
    /// </summary>
    ICommand Run(Func<Task> work) => new Command(async () =>
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            this.Write($"error: {ex.Message}");
        }
    });

    void Write(string message) => this.Log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");

    static string Summarize(string text)
    {
        var single = text.ReplaceLineEndings(" / ");
        return single.Length > 120 ? single[..120] + "…" : single;
    }

}
