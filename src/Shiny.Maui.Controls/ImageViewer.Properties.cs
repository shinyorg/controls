using System.Windows.Input;
using Shiny.Maui.Controls.Images;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

public partial class ImageViewer
{
    // Source

    /// <summary>
    /// An explicit <see cref="ImageSource"/>. Takes precedence over <see cref="Uri"/> and bypasses
    /// <see cref="IImageService"/> entirely - use it for streams, embedded resources and font images.
    /// </summary>
    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source),
        typeof(ImageSource),
        typeof(ImageViewer),
        null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageViewer), () => ((ImageViewer)b).SyncSource())
    );

    /// <inheritdoc cref="SourceProperty" />
    public ImageSource? Source
    {
        get => (ImageSource?)this.GetValue(SourceProperty);
        set => this.SetValue(SourceProperty, value);
    }


    /// <summary>
    /// The image to load. An absolute <c>http</c>/<c>https</c> URI goes through
    /// <see cref="IImageService"/> - memory and disk cached, queued, and de-duplicated against any
    /// other control loading the same URI. Anything else is treated as a local file or bundled
    /// resource and loaded directly.
    /// </summary>
    /// <remarks>
    /// The thumbnail loads as soon as this is set, which warms the cache for the overlay: opening the
    /// viewer then resolves from memory instead of downloading the image a second time.
    /// </remarks>
    public static readonly BindableProperty UriProperty = BindableProperty.Create(
        nameof(Uri),
        typeof(string),
        typeof(ImageViewer),
        null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageViewer), () => ((ImageViewer)b).SyncSource())
    );

    /// <inheritdoc cref="UriProperty" />
    public string? Uri
    {
        get => (string?)this.GetValue(UriProperty);
        set => this.SetValue(UriProperty, value);
    }


    // Loading / error artwork - applied to both the thumbnail and the overlay

    /// <summary>
    /// Artwork shown before and during the load, behind the loading ring, in both the thumbnail and
    /// the full-screen overlay.
    /// </summary>
    public static readonly BindableProperty PlaceholderImageProperty = BindableProperty.Create(
        nameof(PlaceholderImage),
        typeof(ImageSource),
        typeof(ImageViewer),
        null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ImageViewer), () =>
            ((ImageViewer)b).ForEachImage(i => i.PlaceholderImage = (ImageSource?)n))
    );

    /// <inheritdoc cref="PlaceholderImageProperty" />
    public ImageSource? PlaceholderImage
    {
        get => (ImageSource?)this.GetValue(PlaceholderImageProperty);
        set => this.SetValue(PlaceholderImageProperty, value);
    }


    /// <summary>Artwork shown when the load fails. Ignored when <see cref="ErrorTemplate"/> is set.</summary>
    public static readonly BindableProperty ErrorImageProperty = BindableProperty.Create(
        nameof(ErrorImage),
        typeof(ImageSource),
        typeof(ImageViewer),
        null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ImageViewer), () =>
            ((ImageViewer)b).ForEachImage(i => i.ErrorImage = (ImageSource?)n))
    );

    /// <inheritdoc cref="ErrorImageProperty" />
    public ImageSource? ErrorImage
    {
        get => (ImageSource?)this.GetValue(ErrorImageProperty);
        set => this.SetValue(ErrorImageProperty, value);
    }


    /// <summary>
    /// Replaces the built-in loading ring. The template's binding context is the live
    /// <see cref="ImageLoadProgress"/>. Instantiated once per image, so the thumbnail and the overlay
    /// each get their own copy.
    /// </summary>
    public static readonly BindableProperty LoadingTemplateProperty = BindableProperty.Create(
        nameof(LoadingTemplate),
        typeof(DataTemplate),
        typeof(ImageViewer),
        null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ImageViewer), () =>
            ((ImageViewer)b).ForEachImage(i => i.LoadingTemplate = (DataTemplate?)n))
    );

    /// <inheritdoc cref="LoadingTemplateProperty" />
    public DataTemplate? LoadingTemplate
    {
        get => (DataTemplate?)this.GetValue(LoadingTemplateProperty);
        set => this.SetValue(LoadingTemplateProperty, value);
    }


    /// <summary>Replaces the built-in error artwork. The binding context is the failing progress record.</summary>
    public static readonly BindableProperty ErrorTemplateProperty = BindableProperty.Create(
        nameof(ErrorTemplate),
        typeof(DataTemplate),
        typeof(ImageViewer),
        null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ImageViewer), () =>
            ((ImageViewer)b).ForEachImage(i => i.ErrorTemplate = (DataTemplate?)n))
    );

    /// <inheritdoc cref="ErrorTemplateProperty" />
    public DataTemplate? ErrorTemplate
    {
        get => (DataTemplate?)this.GetValue(ErrorTemplateProperty);
        set => this.SetValue(ErrorTemplateProperty, value);
    }


    // Appearance

    /// <summary>How the thumbnail scales. Applies to its placeholder too.</summary>
    public static readonly BindableProperty AspectProperty = BindableProperty.Create(
        nameof(Aspect),
        typeof(Aspect),
        typeof(ImageViewer),
        Aspect.AspectFit,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ImageViewer), () =>
            ((ImageViewer)b).thumbnailImage.Aspect = (Aspect)n)
    );

    /// <inheritdoc cref="AspectProperty" />
    public Aspect Aspect
    {
        get => (Aspect)this.GetValue(AspectProperty);
        set => this.SetValue(AspectProperty, value);
    }


    /// <summary>How the image scales inside the full-screen overlay.</summary>
    public static readonly BindableProperty OverlayAspectProperty = BindableProperty.Create(
        nameof(OverlayAspect),
        typeof(Aspect),
        typeof(ImageViewer),
        Aspect.AspectFit,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ImageViewer), () =>
            ((ImageViewer)b).overlayImage.Aspect = (Aspect)n)
    );

    /// <inheritdoc cref="OverlayAspectProperty" />
    public Aspect OverlayAspect
    {
        get => (Aspect)this.GetValue(OverlayAspectProperty);
        set => this.SetValue(OverlayAspectProperty, value);
    }


    /// <summary>Milliseconds each image fades in over once loaded. Zero shows it instantly.</summary>
    public static readonly BindableProperty FadeInDurationProperty = BindableProperty.Create(
        nameof(FadeInDuration),
        typeof(uint),
        typeof(ImageViewer),
        (uint)150,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ImageViewer), () =>
            ((ImageViewer)b).ForEachImage(i => i.FadeInDuration = (uint)n))
    );

    /// <inheritdoc cref="FadeInDurationProperty" />
    public uint FadeInDuration
    {
        get => (uint)this.GetValue(FadeInDurationProperty);
        set => this.SetValue(FadeInDurationProperty, value);
    }


    /// <summary>Whether the percentage is drawn inside the ring. Never shown when indeterminate.</summary>
    public static readonly BindableProperty ShowProgressTextProperty = BindableProperty.Create(
        nameof(ShowProgressText),
        typeof(bool),
        typeof(ImageViewer),
        true,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ImageViewer), () =>
            ((ImageViewer)b).ForEachImage(i => i.ShowProgressText = (bool)n))
    );

    /// <inheritdoc cref="ShowProgressTextProperty" />
    public bool ShowProgressText
    {
        get => (bool)this.GetValue(ShowProgressTextProperty);
        set => this.SetValue(ShowProgressTextProperty, value);
    }


    /// <summary>Diameter of the loading ring.</summary>
    public static readonly BindableProperty RingSizeProperty = BindableProperty.Create(
        nameof(RingSize),
        typeof(double),
        typeof(ImageViewer),
        48.0,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ImageViewer), () =>
            ((ImageViewer)b).ForEachImage(i => i.RingSize = (double)n))
    );

    /// <inheritdoc cref="RingSizeProperty" />
    public double RingSize
    {
        get => (double)this.GetValue(RingSizeProperty);
        set => this.SetValue(RingSizeProperty, value);
    }


    /// <summary>Colour of the ring's progress arc. Null uses the theme Primary token.</summary>
    public static readonly BindableProperty RingColorProperty = BindableProperty.Create(
        nameof(RingColor),
        typeof(Color),
        typeof(ImageViewer),
        null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ImageViewer), () =>
            ((ImageViewer)b).ForEachImage(i => i.RingColor = (Color?)n))
    );

    /// <inheritdoc cref="RingColorProperty" />
    public Color? RingColor
    {
        get => (Color?)this.GetValue(RingColorProperty);
        set => this.SetValue(RingColorProperty, value);
    }


    /// <summary>Colour of the ring's unfilled track. Null uses the theme SurfaceContainerHighest token.</summary>
    public static readonly BindableProperty RingTrackColorProperty = BindableProperty.Create(
        nameof(RingTrackColor),
        typeof(Color),
        typeof(ImageViewer),
        null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ImageViewer), () =>
            ((ImageViewer)b).ForEachImage(i => i.RingTrackColor = (Color?)n))
    );

    /// <inheritdoc cref="RingTrackColorProperty" />
    public Color? RingTrackColor
    {
        get => (Color?)this.GetValue(RingTrackColorProperty);
        set => this.SetValue(RingTrackColorProperty, value);
    }


    /// <summary>Colour of the percentage label. Null uses the theme OnSurface token.</summary>
    public static readonly BindableProperty ProgressTextColorProperty = BindableProperty.Create(
        nameof(ProgressTextColor),
        typeof(Color),
        typeof(ImageViewer),
        null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ImageViewer), () =>
            ((ImageViewer)b).ForEachImage(i => i.ProgressTextColor = (Color?)n))
    );

    /// <inheritdoc cref="ProgressTextColorProperty" />
    public Color? ProgressTextColor
    {
        get => (Color?)this.GetValue(ProgressTextColorProperty);
        set => this.SetValue(ProgressTextColorProperty, value);
    }


    // Caching

    /// <summary>Whether this image participates in the memory and disk caches.</summary>
    public static readonly BindableProperty CacheEnabledProperty = BindableProperty.Create(
        nameof(CacheEnabled),
        typeof(bool),
        typeof(ImageViewer),
        true,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ImageViewer), () =>
            ((ImageViewer)b).ForEachImage(i => i.CacheEnabled = (bool)n))
    );

    /// <inheritdoc cref="CacheEnabledProperty" />
    public bool CacheEnabled
    {
        get => (bool)this.GetValue(CacheEnabledProperty);
        set => this.SetValue(CacheEnabledProperty, value);
    }


    /// <summary>Overrides <see cref="ImageOptions.DiskCacheDuration"/> for this image.</summary>
    public static readonly BindableProperty CacheDurationProperty = BindableProperty.Create(
        nameof(CacheDuration),
        typeof(TimeSpan?),
        typeof(ImageViewer),
        null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ImageViewer), () =>
            ((ImageViewer)b).ForEachImage(i => i.CacheDuration = (TimeSpan?)n))
    );

    /// <inheritdoc cref="CacheDurationProperty" />
    public TimeSpan? CacheDuration
    {
        get => (TimeSpan?)this.GetValue(CacheDurationProperty);
        set => this.SetValue(CacheDurationProperty, value);
    }


    // Read-only load state
    //
    // Mirrored from the thumbnail. It is the copy that is always in the visual tree and always the
    // first to load, so it is the one that reports something meaningful while the viewer is closed.

    static readonly BindablePropertyKey StatePropertyKey = BindableProperty.CreateReadOnly(
        nameof(State), typeof(ImageLoadState), typeof(ImageViewer), ImageLoadState.None
    );

    /// <summary>Where the current load is.</summary>
    public static readonly BindableProperty StateProperty = StatePropertyKey.BindableProperty;

    /// <inheritdoc cref="StateProperty" />
    public ImageLoadState State => (ImageLoadState)this.GetValue(StateProperty);


    static readonly BindablePropertyKey ProgressPropertyKey = BindableProperty.CreateReadOnly(
        nameof(Progress), typeof(ImageLoadProgress), typeof(ImageViewer), null,
        defaultValueCreator: _ => ImageLoadProgress.None
    );

    /// <summary>The live progress snapshot.</summary>
    public static readonly BindableProperty ProgressProperty = ProgressPropertyKey.BindableProperty;

    /// <inheritdoc cref="ProgressProperty" />
    public ImageLoadProgress Progress => (ImageLoadProgress)this.GetValue(ProgressProperty);


    static readonly BindablePropertyKey IsLoadingPropertyKey = BindableProperty.CreateReadOnly(
        nameof(IsLoading), typeof(bool), typeof(ImageViewer), false
    );

    /// <summary>True while queued or downloading.</summary>
    public static readonly BindableProperty IsLoadingProperty = IsLoadingPropertyKey.BindableProperty;

    /// <inheritdoc cref="IsLoadingProperty" />
    public bool IsLoading => (bool)this.GetValue(IsLoadingProperty);


    static readonly BindablePropertyKey LoadErrorPropertyKey = BindableProperty.CreateReadOnly(
        nameof(LoadError), typeof(Exception), typeof(ImageViewer), null
    );

    /// <summary>Why the last load failed, or null.</summary>
    public static readonly BindableProperty LoadErrorProperty = LoadErrorPropertyKey.BindableProperty;

    /// <inheritdoc cref="LoadErrorProperty" />
    public Exception? LoadError => (Exception?)this.GetValue(LoadErrorProperty);


    // Zoom / overlay behaviour

    /// <summary>How far the overlay image can be pinched in.</summary>
    public static readonly BindableProperty MaxZoomProperty = BindableProperty.Create(
        nameof(MaxZoom),
        typeof(double),
        typeof(ImageViewer),
        DefaultMaxZoom,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ImageViewer>(b, ctrl => ctrl.zoomPan.MaxZoom = (double)n));

    /// <inheritdoc cref="MaxZoomProperty" />
    public double MaxZoom
    {
        get => (double)this.GetValue(MaxZoomProperty);
        set => this.SetValue(MaxZoomProperty, value);
    }


    /// <summary>Whether the full-screen overlay is showing.</summary>
    public static readonly BindableProperty IsOpenProperty = BindableProperty.Create(
        nameof(IsOpen),
        typeof(bool),
        typeof(ImageViewer),
        false,
        BindingMode.TwoWay,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ImageViewer), () =>
        {
            var viewer = (ImageViewer)b;
            if ((bool)n)
                _ = viewer.OpenAsync();
            else
                _ = viewer.CloseAsync();
        }));

    /// <inheritdoc cref="IsOpenProperty" />
    public bool IsOpen
    {
        get => (bool)this.GetValue(IsOpenProperty);
        set => this.SetValue(IsOpenProperty, value);
    }


    /// <summary>Replaces the default close button in the overlay.</summary>
    public static readonly BindableProperty CloseButtonTemplateProperty = BindableProperty.Create(
        nameof(CloseButtonTemplate),
        typeof(DataTemplate),
        typeof(ImageViewer),
        null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageViewer), () =>
            ((ImageViewer)b).ApplyCloseButtonTemplate()));

    /// <inheritdoc cref="CloseButtonTemplateProperty" />
    public DataTemplate? CloseButtonTemplate
    {
        get => (DataTemplate?)this.GetValue(CloseButtonTemplateProperty);
        set => this.SetValue(CloseButtonTemplateProperty, value);
    }


    /// <summary>
    /// The glyph or word on the built-in close button. Ignored once
    /// <see cref="CloseButtonImage"/> or <see cref="CloseButtonTemplate"/> is set.
    /// </summary>
    public static readonly BindableProperty CloseButtonTextProperty = BindableProperty.Create(
        nameof(CloseButtonText),
        typeof(string),
        typeof(ImageViewer),
        DefaultCloseButtonText,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageViewer), () =>
            ((ImageViewer)b).ApplyCloseButtonTemplate()));

    /// <inheritdoc cref="CloseButtonTextProperty" />
    public string CloseButtonText
    {
        get => (string)this.GetValue(CloseButtonTextProperty);
        set => this.SetValue(CloseButtonTextProperty, value);
    }


    /// <summary>
    /// Artwork for the built-in close button, in place of <see cref="CloseButtonText"/>. Ignored
    /// once <see cref="CloseButtonTemplate"/> is set.
    /// </summary>
    /// <remarks>
    /// The chip, its size and its corner are the same either way - this swaps what is drawn inside
    /// it, which is the whole of what most apps want and is a good deal less than a template.
    /// </remarks>
    public static readonly BindableProperty CloseButtonImageProperty = BindableProperty.Create(
        nameof(CloseButtonImage),
        typeof(ImageSource),
        typeof(ImageViewer),
        null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageViewer), () =>
            ((ImageViewer)b).ApplyCloseButtonTemplate()));

    /// <inheritdoc cref="CloseButtonImageProperty" />
    public ImageSource? CloseButtonImage
    {
        get => (ImageSource?)this.GetValue(CloseButtonImageProperty);
        set => this.SetValue(CloseButtonImageProperty, value);
    }


    /// <summary>Content pinned to the top of the overlay.</summary>
    public static readonly BindableProperty HeaderTemplateProperty = BindableProperty.Create(
        nameof(HeaderTemplate),
        typeof(DataTemplate),
        typeof(ImageViewer),
        null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageViewer), () =>
            ((ImageViewer)b).ApplyHeaderTemplate()));

    /// <inheritdoc cref="HeaderTemplateProperty" />
    public DataTemplate? HeaderTemplate
    {
        get => (DataTemplate?)this.GetValue(HeaderTemplateProperty);
        set => this.SetValue(HeaderTemplateProperty, value);
    }


    /// <summary>Content pinned to the bottom of the overlay.</summary>
    public static readonly BindableProperty FooterTemplateProperty = BindableProperty.Create(
        nameof(FooterTemplate),
        typeof(DataTemplate),
        typeof(ImageViewer),
        null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageViewer), () =>
            ((ImageViewer)b).ApplyFooterTemplate()));

    /// <inheritdoc cref="FooterTemplateProperty" />
    public DataTemplate? FooterTemplate
    {
        get => (DataTemplate?)this.GetValue(FooterTemplateProperty);
        set => this.SetValue(FooterTemplateProperty, value);
    }


    /// <summary>Whether open, close and double-tap raise feedback through <see cref="IFeedbackService"/>.</summary>
    public static readonly BindableProperty UseFeedbackProperty = BindableProperty.Create(
        nameof(UseFeedback),
        typeof(bool),
        typeof(ImageViewer),
        true);

    /// <inheritdoc cref="UseFeedbackProperty" />
    public bool UseFeedback
    {
        get => (bool)this.GetValue(UseFeedbackProperty);
        set => this.SetValue(UseFeedbackProperty, value);
    }


    /// <summary>Whether tapping the thumbnail opens the overlay. Turn it off to drive <see cref="IsOpen"/> yourself.</summary>
    public static readonly BindableProperty OpenViewerOnTapProperty = BindableProperty.Create(
        nameof(OpenViewerOnTap),
        typeof(bool),
        typeof(ImageViewer),
        true);

    /// <inheritdoc cref="OpenViewerOnTapProperty" />
    public bool OpenViewerOnTap
    {
        get => (bool)this.GetValue(OpenViewerOnTapProperty);
        set => this.SetValue(OpenViewerOnTapProperty, value);
    }


    // Commands and events
    //
    // Raised by the thumbnail only. The overlay loads the same URI a second time when it opens -
    // firing again from there would report one image twice.

    /// <summary>Invoked with <see cref="ImageLoadedEventArgs"/> once the image is on screen.</summary>
    public static readonly BindableProperty ImageLoadedCommandProperty = BindableProperty.Create(
        nameof(ImageLoadedCommand), typeof(ICommand), typeof(ImageViewer)
    );

    /// <inheritdoc cref="ImageLoadedCommandProperty" />
    public ICommand? ImageLoadedCommand
    {
        get => (ICommand?)this.GetValue(ImageLoadedCommandProperty);
        set => this.SetValue(ImageLoadedCommandProperty, value);
    }


    /// <summary>Invoked with the exception when a load fails.</summary>
    public static readonly BindableProperty ImageFailedCommandProperty = BindableProperty.Create(
        nameof(ImageFailedCommand), typeof(ICommand), typeof(ImageViewer)
    );

    /// <inheritdoc cref="ImageFailedCommandProperty" />
    public ICommand? ImageFailedCommand
    {
        get => (ICommand?)this.GetValue(ImageFailedCommandProperty);
        set => this.SetValue(ImageFailedCommandProperty, value);
    }


    /// <summary>Invoked once the full-screen overlay has faded in.</summary>
    public static readonly BindableProperty OpenedCommandProperty = BindableProperty.Create(
        nameof(OpenedCommand), typeof(ICommand), typeof(ImageViewer)
    );

    /// <inheritdoc cref="OpenedCommandProperty" />
    public ICommand? OpenedCommand
    {
        get => (ICommand?)this.GetValue(OpenedCommandProperty);
        set => this.SetValue(OpenedCommandProperty, value);
    }


    /// <summary>Invoked once the full-screen overlay has faded out.</summary>
    public static readonly BindableProperty ClosedCommandProperty = BindableProperty.Create(
        nameof(ClosedCommand), typeof(ICommand), typeof(ImageViewer)
    );

    /// <inheritdoc cref="ClosedCommandProperty" />
    public ICommand? ClosedCommand
    {
        get => (ICommand?)this.GetValue(ClosedCommandProperty);
        set => this.SetValue(ClosedCommandProperty, value);
    }


    /// <summary>Raised once the image is on screen.</summary>
    public event EventHandler<ImageLoadedEventArgs>? ImageLoaded;

    /// <summary>Raised when a load fails.</summary>
    public event EventHandler<ImageFailedEventArgs>? ImageFailed;

    /// <summary>
    /// Raised once the full-screen overlay has faded in, however it was opened - a tap on the
    /// thumbnail and a <see cref="IsOpen"/> set from a view model both come through here.
    /// </summary>
    public event EventHandler? Opened;

    /// <summary>Raised once the full-screen overlay has faded out and left the visual tree.</summary>
    public event EventHandler? Closed;
}
