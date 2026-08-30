using System.Windows.Input;
using Shiny.Maui.Controls.FloatingPanel;
using Shiny.Maui.Controls.Images;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

/// <summary>
/// A tappable thumbnail that opens a full-screen, pinch-to-zoom overlay of the same image.
/// </summary>
/// <remarks>
/// Both the thumbnail and the overlay are <see cref="ShinyImage"/>, so a remote <see cref="Uri"/>
/// brings placeholder artwork, a loading ring, error artwork and <see cref="IImageService"/>
/// caching with it. The overlay is loaded from the same URI as the thumbnail, which means it comes
/// back off the memory cache rather than downloading the picture a second time.
/// </remarks>
public partial class ImageViewer : ContentView, IDisposable
{
    const double MinScale = 1.0;
    const double DefaultMaxZoom = 5.0;
    const uint AnimationDuration = 250;
    const string DefaultCloseButtonText = "✕";
    const int CloseButtonSize = 40;

    // Thumbnail — visible when IsOpen=false
    internal readonly ShinyImage thumbnailImage;

    // Overlay elements — injected into OverlayHost when IsOpen=true
    readonly Grid overlayGrid;
    readonly BoxView backdrop;
    internal readonly ShinyImage overlayImage;
    internal View closeView;
    View? headerView;
    View? footerView;
    readonly TapGestureRecognizer doubleTapGesture;
    readonly PinchGestureRecognizer pinchGesture;
    readonly PanGestureRecognizer panGesture;

    double currentScale = 1;
    double startScale = 1;
    double xOffset;
    double yOffset;
    double startX;
    double startY;
    bool isAnimating;
    bool isPinching;
    bool isDisposed;

    // Track where the overlay is hosted
    Layout? overlayParent;

    public ImageViewer()
    {
        // When no source is set, the viewer should not intercept touches
        InputTransparent = true;

        // Thumbnail: a ShinyImage with tap-to-open
        thumbnailImage = new ShinyImage
        {
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        var tapToOpen = new TapGestureRecognizer();
        tapToOpen.Tapped += (_, _) =>
        {
            if (!IsOpen && HasImage && OpenViewerOnTap)
                IsOpen = true;
        };
        thumbnailImage.GestureRecognizers.Add(tapToOpen);

        // The thumbnail is the copy that is always in the visual tree and always loads first, so it
        // is the one that drives the viewer's own State/Progress/IsLoading and its events.
        thumbnailImage.PropertyChanged += OnThumbnailPropertyChanged;
        thumbnailImage.ImageLoaded += OnThumbnailImageLoaded;
        thumbnailImage.ImageFailed += OnThumbnailImageFailed;

        Content = thumbnailImage;

        // Build overlay (not in the visual tree until opened)
        backdrop = new BoxView
        {
            Opacity = 0,
            InputTransparent = false
        };
        backdrop.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.Scrim);
        // Swallow touches on backdrop
        backdrop.GestureRecognizers.Add(new TapGestureRecognizer());

        overlayImage = new ShinyImage
        {
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            InputTransparent = false
        };

        doubleTapGesture = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        doubleTapGesture.Tapped += OnDoubleTapped;

        pinchGesture = new PinchGestureRecognizer();
        pinchGesture.PinchUpdated += OnPinchUpdated;

        panGesture = new PanGestureRecognizer();
        panGesture.PanUpdated += OnPanUpdated;

        overlayImage.GestureRecognizers.Add(pinchGesture);
        overlayImage.GestureRecognizers.Add(doubleTapGesture);

        closeView = CreateDefaultCloseButton();

        overlayGrid = new Grid
        {
            InputTransparent = false,
            CascadeInputTransparent = false,
            Children = { backdrop, overlayImage, closeView }
        };

        this.Loaded += (_, _) => this.InstallOverlayRoot();

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(ImageViewer));
    }

    #region Image Loading

    /// <summary>True when either an explicit <see cref="Source"/> or a <see cref="Uri"/> is set.</summary>
    bool HasImage => this.Source != null || !String.IsNullOrWhiteSpace(this.Uri);

    /// <summary>
    /// The service both images load through. Assign to override the resolved one for this instance -
    /// handy in tests and for a screen that needs its own cache policy.
    /// </summary>
    public IImageService ImageService
    {
        get => this.thumbnailImage.ImageService;
        set => this.ForEachImage(i => i.ImageService = value);
    }

    void ForEachImage(Action<ShinyImage> apply)
    {
        apply(this.thumbnailImage);
        apply(this.overlayImage);
    }

    /// <summary>
    /// Pushes the current source onto the thumbnail, and onto the overlay when it is on screen.
    /// </summary>
    /// <remarks>
    /// A closed overlay is deliberately left empty. Assigning it up front would decode a second
    /// full-size bitmap for every viewer in a list, and the overlay is populated on open anyway. It
    /// still has to track changes made <b>while</b> open, because paging between photos from a
    /// header template does exactly that.
    /// </remarks>
    void SyncSource()
    {
        this.thumbnailImage.Source = this.Source;
        this.thumbnailImage.Uri = this.Uri;

        if (this.IsOpen)
        {
            this.overlayImage.Source = this.Source;
            this.overlayImage.Uri = this.Uri;
        }

        // Only intercept touches when there's an image to show
        this.InputTransparent = !this.HasImage;
    }

    /// <summary>
    /// Re-fetches the image, skipping both cache tiers. The cache is refreshed with what comes back.
    /// </summary>
    public Task ReloadAsync() => Task.WhenAll(
        this.thumbnailImage.ReloadAsync(),
        this.IsOpen ? this.overlayImage.ReloadAsync() : Task.CompletedTask
    );

    void OnThumbnailPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ShinyImage.State):
                this.SetValue(StatePropertyKey, this.thumbnailImage.State);
                break;

            case nameof(ShinyImage.Progress):
                this.SetValue(ProgressPropertyKey, this.thumbnailImage.Progress);
                break;

            case nameof(ShinyImage.IsLoading):
                this.SetValue(IsLoadingPropertyKey, this.thumbnailImage.IsLoading);
                break;

            case nameof(ShinyImage.LoadError):
                this.SetValue(LoadErrorPropertyKey, this.thumbnailImage.LoadError);
                break;
        }
    }

    void OnThumbnailImageLoaded(object? sender, ImageLoadedEventArgs e)
    {
        this.ImageLoaded?.Invoke(this, e);

        var command = this.ImageLoadedCommand;
        if (command?.CanExecute(e) == true)
            command.Execute(e);
    }

    void OnThumbnailImageFailed(object? sender, ImageFailedEventArgs e)
    {
        this.ImageFailed?.Invoke(this, e);

        var command = this.ImageFailedCommand;
        if (command?.CanExecute(e.Error) == true)
            command.Execute(e.Error);
    }

    #endregion

    #region Template Application

    void ApplyCloseButtonTemplate()
    {
        overlayGrid.Children.Remove(closeView);

        if (CloseButtonTemplate != null)
        {
            var content = CloseButtonTemplate.CreateContent();
            if (content is View view)
            {
                view.HorizontalOptions = LayoutOptions.End;
                view.VerticalOptions = LayoutOptions.Start;
                var tap = new TapGestureRecognizer();
                tap.Tapped += (_, _) => IsOpen = false;
                view.GestureRecognizers.Add(tap);
                closeView = view;
            }
        }
        else
        {
            closeView = CreateDefaultCloseButton();
        }

        overlayGrid.Children.Add(closeView);
    }

    void ApplyHeaderTemplate()
    {
        if (headerView != null)
            overlayGrid.Children.Remove(headerView);

        if (HeaderTemplate != null)
        {
            var content = HeaderTemplate.CreateContent();
            if (content is View view)
            {
                view.VerticalOptions = LayoutOptions.Start;
                headerView = view;
                overlayGrid.Children.Add(headerView);
            }
        }
        else
        {
            headerView = null;
        }
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (footerView != null)
            footerView.BindingContext = BindingContext;
        if (headerView != null)
            headerView.BindingContext = BindingContext;
        overlayGrid.BindingContext = BindingContext;
    }

    void ApplyFooterTemplate()
    {
        if (footerView != null)
            overlayGrid.Children.Remove(footerView);

        if (FooterTemplate != null)
        {
            var content = FooterTemplate.CreateContent();
            if (content is View view)
            {
                view.VerticalOptions = LayoutOptions.End;
                view.BindingContext = BindingContext;
                footerView = view;
                overlayGrid.Children.Add(footerView);
            }
        }
        else
        {
            footerView = null;
        }
    }

    /// <summary>
    /// The close button used when there is no <see cref="CloseButtonTemplate"/>: an image button
    /// when <see cref="CloseButtonImage"/> is set, a text one otherwise.
    /// </summary>
    /// <remarks>
    /// Both come out the same size and in the same corner, so swapping the glyph for artwork does
    /// not move the target - the chip is what is being aimed at either way.
    /// </remarks>
    View CreateDefaultCloseButton()
    {
        // Translucent black chip on the dark Scrim backdrop, left as-is in both themes.
        var chip = Color.FromRgba(0, 0, 0, 0.5);
        View btn;

        if (CloseButtonImage != null)
        {
            var image = new ImageButton
            {
                Source = CloseButtonImage,
                Aspect = Aspect.AspectFit,
                // artwork would otherwise run into the chip's edge on every side
                Padding = 8,
                BackgroundColor = chip,
                CornerRadius = CloseButtonSize / 2
            };
            image.Clicked += (_, _) => IsOpen = false;
            btn = image;
        }
        else
        {
            var text = new Button
            {
                Text = CloseButtonText,
                FontSize = 20,
                // Close button sits on the dark Scrim backdrop — use inverse-on-surface.
                BackgroundColor = chip,
                CornerRadius = CloseButtonSize / 2,
                Padding = 0
            }.Neutralize();
            text.SetDynamicResource(Button.TextColorProperty, ShinyThemeKeys.Color.InverseOnSurface);
            text.Clicked += (_, _) => IsOpen = false;
            btn = text;
        }

        btn.WidthRequest = CloseButtonSize;
        btn.HeightRequest = CloseButtonSize;
        btn.HorizontalOptions = LayoutOptions.End;
        btn.VerticalOptions = LayoutOptions.Start;
        btn.Margin = new Thickness(0, 50, 16, 0);
        return btn;
    }

    #endregion

    #region Open / Close

    /// <summary>
    /// Where the lightbox is injected: an explicit <see cref="OverlayHost"/> if the viewer is inside
    /// one, otherwise the page's own overlay layer.
    /// </summary>
    /// <remarks>
    /// The page layer used to be "whatever <see cref="Grid"/> happens to be the page's root content",
    /// which failed twice over. A page whose content is not a Grid - a bare
    /// <c>&lt;ContentPage&gt;&lt;ChatView/&gt;&lt;/ContentPage&gt;</c>, say - had no host at all and
    /// the viewer threw into a task nobody awaits, so opening an image silently did nothing. And a
    /// page whose root Grid has more than one cell got a full-screen overlay dropped into cell (0,0),
    /// covering a corner of the window instead of the window. <see cref="PageOverlay"/> is a
    /// single-cell layer over the whole page with a z-index the other overlays are ordered against.
    /// </remarks>
    Layout? FindOverlayParent()
    {
        // An OverlayHost the viewer is already inside wins: the host was placed deliberately, and
        // some of them (a FloatingPanel's) are not the page.
        Element? current = this.Parent;
        while (current is not null)
        {
            if (current is OverlayHost host)
                return host;

            if (current is ShinyContentPage scp)
                return scp.OverlayHost;

            if (current is Page)
                break;

            current = current.Parent;
        }

        return PageOverlay.GetOrCreateLayer<PageOverlay.ImageViewerLayer>(this, PageOverlay.Layers.ImageViewer);
    }


    /// <summary>
    /// Installs the page's overlay root at load rather than on the tap that opens the viewer.
    /// </summary>
    /// <remarks>
    /// Creating the root re-parents the page's content, which tears down and rebuilds every native
    /// view under it. That is free while the page is still being set up and a visible hitch - a
    /// reset scroll position, a dropped focus - if it happens when a photo is tapped. Skipped
    /// entirely when the viewer already sits in an <see cref="OverlayHost"/>, which needs no wrapper.
    /// Dispatched so XAML inflation has finished assigning the page's Content first.
    /// </remarks>
    internal void InstallOverlayRoot()
    {
        Element? current = this.Parent;
        while (current is not null)
        {
            if (current is OverlayHost or ShinyContentPage)
                return;

            if (current is Page)
                break;

            current = current.Parent;
        }

        this.Dispatcher.Dispatch(() => PageOverlay.GetOrCreateRoot(this));
    }


    async Task OpenAsync()
    {
        if (isAnimating) return;
        isAnimating = true;

        ResetTransform();

        // Sync source to overlay image. Same URI as the thumbnail, so this is a memory cache hit
        // rather than a second download.
        overlayImage.Source = Source;
        overlayImage.Uri = Uri;

        // Find host and inject overlay
        // Null only when the viewer is not on a page at all - detached, or mid-navigation. There is
        // nowhere to draw, so the open is abandoned rather than throwing into a task nobody awaits.
        var host = FindOverlayParent();
        if (host is null)
        {
            isAnimating = false;
            SetValue(IsOpenProperty, false);
            return;
        }

        overlayParent = host;
        overlayGrid.BindingContext = BindingContext;
        overlayParent.Children.Add(overlayGrid);

        if (UseFeedback)
            FeedbackHelper.Execute(this, "Opened");

        var fadeTargets = new List<VisualElement> { backdrop, overlayImage, closeView };
        if (headerView != null) fadeTargets.Add(headerView);
        if (footerView != null) fadeTargets.Add(footerView);

        foreach (var v in fadeTargets) v.Opacity = 0;
        await Task.WhenAll(fadeTargets.Select(v => v.FadeToAsync(1, AnimationDuration)));

        isAnimating = false;
        Raise(Opened, OpenedCommand);
    }

    async Task CloseAsync()
    {
        if (isAnimating) return;
        isAnimating = true;

        var fadeTargets = new List<VisualElement> { backdrop, overlayImage, closeView };
        if (headerView != null) fadeTargets.Add(headerView);
        if (footerView != null) fadeTargets.Add(footerView);

        await Task.WhenAll(fadeTargets.Select(v => v.FadeToAsync(0, AnimationDuration)));

        if (UseFeedback)
            FeedbackHelper.Execute(this, "Closed");

        // Remove overlay from host
        overlayParent?.Children.Remove(overlayGrid);
        overlayParent = null;

        ResetTransform();
        isAnimating = false;
        Raise(Closed, ClosedCommand);
    }

    /// <summary>
    /// Fires the pair a state change carries. Both go off at the end of the animation rather than
    /// the start of it, so "opened" means the overlay is on screen and "closed" means it has left
    /// the tree - a handler that reads the visual state gets the one it was told about.
    /// </summary>
    void Raise(EventHandler? evt, ICommand? command)
    {
        evt?.Invoke(this, EventArgs.Empty);

        if (command?.CanExecute(null) == true)
            command.Execute(null);
    }

    void ResetTransform()
    {
        currentScale = 1;
        xOffset = 0;
        yOffset = 0;
        overlayImage.Scale = 1;
        overlayImage.TranslationX = 0;
        overlayImage.TranslationY = 0;
        overlayImage.GestureRecognizers.Remove(panGesture);
    }

    #endregion

    #region Gestures

    void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        switch (e.Status)
        {
            case GestureStatus.Started:
                isPinching = true;
                startScale = currentScale;
                break;

            case GestureStatus.Running:
                currentScale += (e.Scale - 1) * startScale;
                currentScale = Math.Clamp(currentScale, MinScale, MaxZoom);

                var pinchX = (e.ScaleOrigin.X - 0.5) * overlayImage.Width;
                var pinchY = (e.ScaleOrigin.Y - 0.5) * overlayImage.Height;
                var scaleDelta = currentScale - startScale;

                var targetX = xOffset - pinchX * scaleDelta;
                var targetY = yOffset - pinchY * scaleDelta;

                overlayImage.TranslationX = ClampX(targetX);
                overlayImage.TranslationY = ClampY(targetY);
                overlayImage.Scale = currentScale;
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                isPinching = false;
                xOffset = overlayImage.TranslationX;
                yOffset = overlayImage.TranslationY;

                if (currentScale <= MinScale)
                    _ = AnimateResetAsync();
                else if (!overlayImage.GestureRecognizers.Contains(panGesture))
                    overlayImage.GestureRecognizers.Add(panGesture);
                break;
        }
    }

    void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (isPinching || currentScale <= MinScale) return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                startX = xOffset;
                startY = yOffset;
                break;

            case GestureStatus.Running:
                overlayImage.TranslationX = ClampX(startX + e.TotalX);
                overlayImage.TranslationY = ClampY(startY + e.TotalY);
                break;

            case GestureStatus.Completed:
                xOffset = overlayImage.TranslationX;
                yOffset = overlayImage.TranslationY;
                break;
        }
    }

    void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (isAnimating) return;

        if (UseFeedback)
            FeedbackHelper.Execute(this, "DoubleTapped");

        if (currentScale > MinScale)
            _ = AnimateResetAsync();
        else
            _ = AnimateZoomInAsync(e);
    }

    async Task AnimateZoomInAsync(TappedEventArgs e)
    {
        isAnimating = true;

        var targetScale = Math.Min(2.5, MaxZoom);
        var point = e.GetPosition(overlayImage);

        double tx = 0, ty = 0;
        if (point.HasValue)
        {
            tx = -(point.Value.X - overlayImage.Width / 2) * (targetScale - 1);
            ty = -(point.Value.Y - overlayImage.Height / 2) * (targetScale - 1);
        }

        currentScale = targetScale;
        tx = ClampX(tx);
        ty = ClampY(ty);
        xOffset = tx;
        yOffset = ty;

        await Task.WhenAll(
            overlayImage.ScaleToAsync(targetScale, AnimationDuration, Easing.CubicOut),
            overlayImage.TranslateToAsync(tx, ty, AnimationDuration, Easing.CubicOut)
        );

        if (!overlayImage.GestureRecognizers.Contains(panGesture))
            overlayImage.GestureRecognizers.Add(panGesture);

        isAnimating = false;
    }

    async Task AnimateResetAsync()
    {
        isAnimating = true;
        overlayImage.GestureRecognizers.Remove(panGesture);

        await Task.WhenAll(
            overlayImage.ScaleToAsync(1, AnimationDuration, Easing.CubicOut),
            overlayImage.TranslateToAsync(0, 0, AnimationDuration, Easing.CubicOut)
        );

        currentScale = 1;
        xOffset = 0;
        yOffset = 0;
        isAnimating = false;
    }

    double ClampX(double x)
    {
        if (currentScale <= MinScale) return 0;
        var maxX = overlayImage.Width * (currentScale - 1) / 2;
        return Math.Clamp(x, -maxX, maxX);
    }

    double ClampY(double y)
    {
        if (currentScale <= MinScale) return 0;
        var maxY = overlayImage.Height * (currentScale - 1) / 2;
        return Math.Clamp(y, -maxY, maxY);
    }

    #endregion

    /// <summary>Cancels any in-flight load and releases the control's resources.</summary>
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Cancels any in-flight load and releases the control's resources.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (this.isDisposed || !disposing)
            return;

        this.isDisposed = true;
        this.thumbnailImage.PropertyChanged -= this.OnThumbnailPropertyChanged;
        this.thumbnailImage.ImageLoaded -= this.OnThumbnailImageLoaded;
        this.thumbnailImage.ImageFailed -= this.OnThumbnailImageFailed;
        this.thumbnailImage.Dispose();
        this.overlayImage.Dispose();
    }
}
