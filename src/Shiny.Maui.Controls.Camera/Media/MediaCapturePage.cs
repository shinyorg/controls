using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace Shiny.Maui.Controls.Camera.Media;

/// <summary>What the modal camera page was opened for.</summary>
enum MediaCaptureMode
{
    Photo,
    Video,
    Scan
}


/// <summary>
/// The modal camera page — the thing this whole service exists to give you instead of the system camera UI.
/// </summary>
/// <remarks>
/// <para>
/// Built entirely in code rather than XAML because it is presented from a service, has no view model, and
/// its three modes differ by which chrome is visible rather than by layout — a single tree with toggled
/// pieces is both smaller and easier to keep honest than three markup files.
/// </para>
/// <para>
/// Every child is constructed up front and shown or hidden with <see cref="VisualElement.IsVisible"/>,
/// never added later. On the AppKit head a child added after the page has laid out never gets a native view
/// and simply paints nothing, so "build it when you need it" is a blank screen on macOS.
/// </para>
/// </remarks>
class MediaCapturePage : ContentPage
{
    readonly TaskCompletionSource<object?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly MediaCaptureMode mode;
    readonly MediaCameraOptions options;
    readonly PhotoCaptureOptions? photoOptions;
    readonly VideoCaptureOptions? videoOptions;
    readonly MediaScanOptions? scanOptions;

    readonly Label titleLabel;
    readonly Label instructionsLabel;
    readonly Label statusLabel;
    readonly Label permissionLabel;
    readonly Border permissionPanel;
    readonly MediaIconButton closeButton;
    readonly MediaIconButton torchButton;
    readonly MediaIconButton flipButton;
    readonly MediaIconButton flashButton;
    readonly MediaShutterButton? shutter;
    readonly ScrollView effectStrip;
    readonly HorizontalStackLayout effectRow;
    readonly Grid confirmPanel;
    readonly Image confirmImage;
    readonly MediaIconButton doneButton;
    readonly ActivityIndicator busy;

    IDispatcherTimer? timer;
    readonly double requestedZoom;
    DateTimeOffset recordingStartedAt;
    CameraPhoto? pendingPhoto;
    int resultCount;
    bool isBusy;

    public MediaCapturePage(MediaCaptureMode mode, MediaCameraOptions options, IFrameAnalyzer? analyzer = null)
    {
        this.mode = mode;
        this.options = options;
        this.photoOptions = options as PhotoCaptureOptions;
        this.videoOptions = options as VideoCaptureOptions;
        this.scanOptions = options as MediaScanOptions;

        this.BackgroundColor = Colors.Black;
        this.Title = options.Title;
        // full-bleed preview: the chrome bars inset themselves, the camera runs edge to edge
        this.SafeAreaEdges = SafeAreaEdges.None;
        NavigationPage.SetHasNavigationBar(this, false);

        this.Camera = new CameraView
        {
            AutomationId = "shiny.media.camera",
            Facing = options.Facing,
            CameraId = options.CameraId,
            IsTorchOn = options.IsTorchOn,
            IsPinchToZoomEnabled = options.AllowZoom,
            ScaleMode = options.ScaleMode,
            Filter = options.Filter,
            Analyzer = analyzer
        };

        // Zoom is coerced against MaxZoom, which is 1 until the handler discovers what the lens can do — so
        // assigning the request here would clamp it to 1 and silently lose it, and a cap set here would be
        // overwritten the moment the handler reports the real range. Both are (re)applied every time the
        // range moves instead.
        this.requestedZoom = options.Zoom;
        this.Camera.PropertyChanged += this.OnCameraPropertyChanged;
        this.ApplyZoomRange();

        if (this.photoOptions is not null)
        {
            this.Camera.PhotoQuality = this.photoOptions.Quality;
            this.Camera.FlashMode = this.photoOptions.FlashMode;
        }

        if (this.videoOptions is not null)
        {
            this.Camera.VideoQuality = this.videoOptions.Quality;
            this.Camera.VideoBitrate = this.videoOptions.Bitrate;
            this.Camera.VideoFrameRate = this.videoOptions.FrameRate;
        }

        if (this.scanOptions is not null)
            this.Camera.ShowDetectionOverlay = this.scanOptions.ShowBoundingBox;

        foreach (var effect in options.Effects)
            this.Camera.Effects.Add(effect);

        var overlay = new CameraOverlayView { Camera = this.Camera, InputTransparent = true };

        // ---- top bar -------------------------------------------------------------------------------
        this.closeButton = new MediaIconButton(MediaIcon.Close, "shiny.media.close");
        this.closeButton.Clicked += (_, _) => this.Cancel();

        this.torchButton = new MediaIconButton(options.IsTorchOn ? MediaIcon.TorchOn : MediaIcon.TorchOff, "shiny.media.torch")
        {
            IsVisible = options.AllowTorch
        };
        this.torchButton.Clicked += (_, _) => this.ToggleTorch();

        this.flashButton = new MediaIconButton(IconFor(this.photoOptions?.FlashMode ?? CameraFlashMode.Auto), "shiny.media.flash")
        {
            IsVisible = mode == MediaCaptureMode.Photo && (this.photoOptions?.AllowFlashToggle ?? false)
        };
        this.flashButton.Clicked += (_, _) => this.CycleFlash();

        this.flipButton = new MediaIconButton(MediaIcon.FlipCamera, "shiny.media.flip")
        {
            IsVisible = options.AllowCameraSwitch
        };
        this.flipButton.Clicked += (_, _) => this.FlipCamera();

        this.titleLabel = new Label
        {
            Text = options.Title,
            IsVisible = !String.IsNullOrWhiteSpace(options.Title),
            TextColor = Colors.White,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center
        };

        var topBar = new Grid
        {
            ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)],
            ColumnSpacing = 8,
            Padding = new Thickness(14, 12),
            VerticalOptions = LayoutOptions.Start,
            SafeAreaEdges = ContainerSafeArea
        };
        topBar.Add(this.closeButton, 0);
        topBar.Add(this.titleLabel, 1);
        topBar.Add(this.flashButton, 2);
        topBar.Add(this.torchButton, 3);

        // ---- bottom bar ----------------------------------------------------------------------------
        this.instructionsLabel = new Label
        {
            Text = options.Instructions,
            IsVisible = !String.IsNullOrWhiteSpace(options.Instructions),
            TextColor = Colors.White,
            FontSize = 14,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(24, 0, 24, 10)
        };

        this.statusLabel = new Label
        {
            IsVisible = false,
            TextColor = Colors.White,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(24, 0, 24, 10),
            AutomationId = "shiny.media.status"
        };

        this.effectRow = new HorizontalStackLayout { Spacing = 8, Padding = new Thickness(14, 0) };
        this.effectStrip = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = this.effectRow,
            IsVisible = options.ShowEffectPicker,
            Margin = new Thickness(0, 0, 0, 12)
        };
        if (options.ShowEffectPicker)
            this.BuildEffectStrip();

        // every action on this page is a drawn icon: the modal ships no localizable strings of its own,
        // and the only text on it is the Title/Instructions the calling app supplies already localized
        this.doneButton = new MediaIconButton(MediaIcon.Check, "shiny.media.done", 64)
        {
            IsVisible = mode == MediaCaptureMode.Scan && (this.scanOptions?.ShowDoneButton ?? true),
            BackgroundColor = Color.FromArgb("#22C55E")
        };
        this.doneButton.Clicked += (_, _) => this.Complete(null);

        if (mode == MediaCaptureMode.Scan)
        {
            this.shutter = null;
        }
        else
        {
            this.shutter = new MediaShutterButton(mode == MediaCaptureMode.Video);
            this.shutter.Clicked += (_, _) => this.OnShutter();
        }

        var actionRow = new Grid { HorizontalOptions = LayoutOptions.Center };
        if (this.shutter is not null)
            actionRow.Add(this.shutter);
        actionRow.Add(this.doneButton);

        var bottomBar = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.End,
            Padding = new Thickness(0, 0, 0, 18),
            SafeAreaEdges = ContainerSafeArea,
            Children = { this.statusLabel, this.instructionsLabel, this.effectStrip, actionRow }
        };

        // ---- photo confirmation ---------------------------------------------------------------------
        this.confirmImage = new Image { Aspect = Aspect.AspectFit, AutomationId = "shiny.media.preview" };
        var retake = new MediaIconButton(MediaIcon.Close, "shiny.media.retake", 60);
        retake.Clicked += (_, _) => this.Retake();

        var use = new MediaIconButton(MediaIcon.Check, "shiny.media.confirm", 60)
        {
            BackgroundColor = Color.FromArgb("#22C55E")
        };
        use.Clicked += (_, _) => this.ConfirmPhoto();

        this.confirmPanel = new Grid
        {
            IsVisible = false,
            BackgroundColor = Colors.Black,
            RowDefinitions = [new(GridLength.Star), new(GridLength.Auto)],
            SafeAreaEdges = ContainerSafeArea
        };
        this.confirmPanel.Add(this.confirmImage);
        this.confirmPanel.Add(
            new HorizontalStackLayout
            {
                Spacing = 28,
                Padding = new Thickness(0, 18),
                HorizontalOptions = LayoutOptions.Center,
                Children = { retake, use }
            },
            0,
            1
        );

        // ---- permission / busy ----------------------------------------------------------------------
        this.permissionLabel = new Label
        {
            Text = options.PermissionDeniedText,
            TextColor = Colors.White,
            FontSize = 15,
            HorizontalTextAlignment = TextAlignment.Center
        };
        this.permissionPanel = new Border
        {
            IsVisible = false,
            AutomationId = "shiny.media.permission",
            BackgroundColor = Color.FromRgba(0, 0, 0, 200),
            StrokeThickness = 0,
            Padding = 22,
            Margin = 28,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = this.permissionLabel
        };

        this.busy = new ActivityIndicator
        {
            IsVisible = false,
            Color = Colors.White,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        var root = new Grid { BackgroundColor = Colors.Black };
        root.Add(this.Camera);
        root.Add(overlay);
        root.Add(topBar);
        root.Add(bottomBar);
        root.Add(this.confirmPanel);
        root.Add(this.permissionPanel);
        root.Add(this.busy);
        this.Content = root;

        options.ConfigureCamera?.Invoke(this.Camera);
        options.ConfigurePage?.Invoke(this);
    }

    /// <summary>
    /// <c>SafeAreaEdges.Container</c> — MAUI keeps the enum member internal, so the value is built from the
    /// region rather than named.
    /// </summary>
    static readonly SafeAreaEdges ContainerSafeArea = new(SafeAreaRegions.Container);

    /// <summary>The live camera, exposed so the service can arm scanning and read capture results.</summary>
    public CameraView Camera { get; }

    // Test seams. The chrome is built in the constructor and toggled with IsVisible, so what a mode does or
    // does not offer is assertable without a running app — which is the only way to pin "a scan modal has no
    // shutter" from a headless test.
    internal MediaShutterButton? ShutterButton => this.shutter;

    internal MediaIconButton TorchButton => this.torchButton;

    internal MediaIconButton FlipButton => this.flipButton;

    internal MediaIconButton FlashButton => this.flashButton;

    internal MediaIconButton CloseButton => this.closeButton;

    internal MediaIconButton DoneButton => this.doneButton;

    internal View ConfirmPanel => this.confirmPanel;

    internal Label StatusLabel => this.statusLabel;

    internal View EffectStrip => this.effectStrip;

    internal int ResultCount => this.resultCount;

    /// <summary>
    /// Completes when the page is done: with the result for a photo/video capture, or <c>null</c> when the
    /// user cancelled or a scan session was finished from the modal.
    /// </summary>
    public Task<object?> Completion => this.completion.Task;

    /// <summary>True when the page ended because the user dismissed it rather than producing a result.</summary>
    public bool WasCancelled { get; private set; }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        this.Dispatcher.Dispatch(async () => await this.StartCameraAsync());
    }

    void OnCameraPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == CameraView.MinZoomProperty.PropertyName ||
            e.PropertyName == CameraView.MaxZoomProperty.PropertyName)
            this.ApplyZoomRange();
    }

    /// <summary>
    /// Hold the camera's usable zoom range to what the options allow, and put the requested zoom inside it.
    /// </summary>
    /// <remarks>
    /// Runs again on every range change rather than once, because the handler publishes the lens's real
    /// range asynchronously and may publish it more than once (a lens switch republishes). Every write here
    /// is idempotent — the second pass a write triggers finds nothing left to do — so it settles rather than
    /// ping-ponging, which matters because MAUI defers a same-property <c>SetValue</c> made from inside that
    /// property's own change notification.
    /// </remarks>
    void ApplyZoomRange()
    {
        var min = this.Camera.MinZoom;
        var ceiling = this.options.AllowZoom
            ? this.options.MaxZoom ?? Double.MaxValue
            : min; // "no zoom at all" is an empty range, which no gesture or binding can escape

        ceiling = Math.Max(min, ceiling); // a cap under the lens minimum would be an inverted range
        if (this.Camera.MaxZoom > ceiling)
            this.Camera.MaxZoom = ceiling;

        var target = Math.Clamp(this.requestedZoom, min, this.Camera.MaxZoom);
        if (Math.Abs(this.Camera.Zoom - target) > 0.0001d)
            this.Camera.Zoom = target;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        this.Camera.PropertyChanged -= this.OnCameraPropertyChanged;
        this.StopTimer();
        _ = this.Camera.StopAsync();
        // a page that goes away without a result is a cancellation — never leave the caller awaiting forever
        this.completion.TrySetResult(null);
    }

    protected override bool OnBackButtonPressed()
    {
        this.Cancel();
        return true;
    }

    async Task StartCameraAsync()
    {
        try
        {
            await this.Camera.StartAsync().ConfigureAwait(true);
            if (this.mode == MediaCaptureMode.Scan)
                this.Camera.Scan();
        }
        catch (Exception ex)
        {
            this.ShowPermissionPanel(ex.Message);
        }
    }

    /// <summary>Show the refusal panel over the preview, with the reason when the platform gave one.</summary>
    public void ShowPermissionPanel(string? message = null)
    {
        this.permissionLabel.Text = String.IsNullOrWhiteSpace(message) ? this.options.PermissionDeniedText : message;
        this.permissionPanel.IsVisible = true;
    }

    /// <summary>
    /// Called by the service for each accepted scan result — updates the running count and ticks the
    /// haptic. Closes the page itself once <see cref="MediaScanOptions.MaxResults"/> is reached.
    /// </summary>
    /// <remarks>
    /// The count and the cap are handled synchronously so the session's arithmetic never depends on when a
    /// dispatch lands, while the chrome it drives is marshalled: results are consumed on whatever context
    /// the caller's <c>await foreach</c> is running on, which is the UI thread for a view model and is not
    /// for a background worker, and MAUI gives no error for a control mutated off the UI thread — it either
    /// does nothing or crashes some frames later, far from the cause.
    /// </remarks>
    public void ReportScanResult(string? description)
    {
        this.resultCount++;

        var caption = this.scanOptions?.ShowResultCount == true
            ? description is null
                ? $"{this.resultCount} found"
                : $"{this.resultCount} found — {description}"
            : null;

        if (caption is not null || this.scanOptions?.VibrateOnResult == true)
            this.OnMainThread(() =>
            {
                if (caption is not null)
                {
                    this.statusLabel.Text = caption;
                    this.statusLabel.IsVisible = true;
                }

                if (this.scanOptions?.VibrateOnResult == true)
                    TryVibrate();
            });

        if (this.scanOptions?.MaxResults is { } max && this.resultCount >= max)
            this.Complete(null);
    }

    /// <summary>Run <paramref name="action"/> on the UI thread, inline when already there (and in tests).</summary>
    void OnMainThread(Action action)
    {
        if (this.Dispatcher.IsDispatchRequired)
            this.Dispatcher.Dispatch(action);
        else
            action();
    }

    /// <summary>Finish with a result (or <c>null</c> for "the user is done"), which closes the modal.</summary>
    public void Complete(object? result) => this.completion.TrySetResult(result);

    void Cancel()
    {
        this.WasCancelled = true;
        this.completion.TrySetResult(null);
    }

    async void OnShutter()
    {
        if (this.isBusy)
            return;

        try
        {
            this.isBusy = true;
            if (this.mode == MediaCaptureMode.Photo)
                await this.CapturePhotoAsync();
            else
                await this.ToggleRecordingAsync();
        }
        catch (Exception ex)
        {
            this.ShowPermissionPanel(ex.Message);
        }
        finally
        {
            this.isBusy = false;
        }
    }

    async Task CapturePhotoAsync()
    {
        this.busy.IsVisible = this.busy.IsRunning = true;
        try
        {
            var photo = await this.Camera.CapturePhotoAsync().ConfigureAwait(true);
            if (this.photoOptions?.ShowConfirmation == false)
            {
                this.Complete(photo);
                return;
            }

            this.pendingPhoto = photo;
            this.confirmImage.Source = ImageSource.FromStream(() => new MemoryStream(photo.Data, false));
            this.confirmPanel.IsVisible = true;
        }
        finally
        {
            this.busy.IsVisible = this.busy.IsRunning = false;
        }
    }

    void Retake()
    {
        this.pendingPhoto = null;
        this.confirmImage.Source = null;
        this.confirmPanel.IsVisible = false;
    }

    void ConfirmPhoto()
    {
        if (this.pendingPhoto is not null)
            this.Complete(this.pendingPhoto);
    }

    async Task ToggleRecordingAsync()
    {
        if (this.Camera.IsRecording)
        {
            this.StopTimer();
            var video = await this.Camera.StopVideoRecordingAsync().ConfigureAwait(true);
            if (this.shutter is not null)
                this.shutter.IsRecording = false;
            this.Complete(video);
            return;
        }

        await this.Camera
            .StartVideoRecordingAsync(new VideoRecordingOptions
            {
                IncludeAudio = this.videoOptions?.IncludeAudio ?? true,
                FilePath = this.videoOptions?.FilePath,
                Overlay = this.videoOptions?.Overlay
            })
            .ConfigureAwait(true);

        if (this.shutter is not null)
            this.shutter.IsRecording = true;

        this.closeButton.IsVisible = false; // dismissing mid-record would strand a half-written file
        this.StartTimer();
    }

    void StartTimer()
    {
        if (this.videoOptions?.ShowElapsed != true && this.videoOptions?.MaxDuration is null)
            return;

        this.recordingStartedAt = DateTimeOffset.UtcNow;
        this.timer = this.Dispatcher.CreateTimer();
        this.timer.Interval = TimeSpan.FromMilliseconds(250);
        this.timer.Tick += (_, _) =>
        {
            var elapsed = DateTimeOffset.UtcNow - this.recordingStartedAt;
            if (this.videoOptions?.ShowElapsed == true)
            {
                this.statusLabel.Text = $"● {elapsed:mm\\:ss}";
                this.statusLabel.IsVisible = true;
            }

            if (this.videoOptions?.MaxDuration is { } max && elapsed >= max)
                this.OnShutter(); // hit the cap: stop exactly as a tap would
        };
        this.timer.Start();
    }

    void StopTimer()
    {
        this.timer?.Stop();
        this.timer = null;
    }

    void ToggleTorch()
    {
        this.Camera.IsTorchOn = !this.Camera.IsTorchOn;
        this.torchButton.Icon = this.Camera.IsTorchOn ? MediaIcon.TorchOn : MediaIcon.TorchOff;
    }

    void FlipCamera()
        => this.Camera.Facing = this.Camera.Facing == CameraFacing.Front ? CameraFacing.Back : CameraFacing.Front;

    void CycleFlash()
    {
        this.Camera.FlashMode = this.Camera.FlashMode switch
        {
            CameraFlashMode.Auto => CameraFlashMode.On,
            CameraFlashMode.On => CameraFlashMode.Off,
            _ => CameraFlashMode.Auto
        };
        this.flashButton.Icon = IconFor(this.Camera.FlashMode);
    }

    void BuildEffectStrip()
    {
        var choices = this.options.EffectChoices ?? MediaEffectChoices.Default;
        foreach (var choice in choices)
        {
            this.effectRow.Add(BuildEffectChip(choice.Name, new Command(() => this.ApplyLook(choice))));
        }
    }

    void ApplyLook(MediaEffectChoice choice)
    {
        this.Camera.Filter = choice.Filter;
        this.Camera.Effects.Clear();
        foreach (var effect in this.options.Effects)
            this.Camera.Effects.Add(effect);

        if (choice.Effect is not null)
            this.Camera.Effects.Add(choice.Effect);
    }

    static MediaIcon IconFor(CameraFlashMode flash) => flash switch
    {
        CameraFlashMode.On => MediaIcon.FlashOn,
        CameraFlashMode.Off => MediaIcon.FlashOff,
        _ => MediaIcon.FlashAuto
    };

    /// <summary>
    /// A look chip. The one piece of chrome that legitimately carries text, because the text <i>is</i> the
    /// data — the name of the effect the caller put in the strip.
    /// </summary>
    static Border BuildEffectChip(string text, ICommand command)
    {
        var border = new Border
        {
            AutomationId = $"shiny.media.effect.{text}",
            Padding = new Thickness(16, 9),
            StrokeThickness = 0,
            BackgroundColor = Color.FromRgba(255, 255, 255, 45),
            StrokeShape = new RoundRectangle { CornerRadius = 20 },
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = text,
                TextColor = Colors.White,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center
            }
        };
        border.GestureRecognizers.Add(new TapGestureRecognizer { Command = command });
        return border;
    }

    static void TryVibrate()
    {
        try
        {
            Microsoft.Maui.Devices.HapticFeedback.Default.Perform(Microsoft.Maui.Devices.HapticFeedbackType.Click);
        }
        catch
        {
            // haptics are decoration; a head without them must not fail a scan
        }
    }
}
