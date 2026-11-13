using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.SignaturePad;

/// <summary>
/// An inline signature surface: draw with a finger, pen or mouse anywhere on a page - a form, a card, a
/// table row. <see cref="SignaturePad"/> is this view inside a floating panel with Sign and Cancel buttons.
/// </summary>
/// <example>
/// <code>
/// &lt;shiny:SignatureView x:Name="Signature" HeightRequest="180" PlaceholderText="Sign here" /&gt;
///
/// using var png = Signature.ExportToPng();
/// </code>
/// </example>
/// <remarks>
/// Inside a <see cref="ScrollView"/> the scroller is held off for the length of a stroke, so signing never
/// scrolls the page; a touch that misses the surface still scrolls. On Android 10+ the surface is excluded
/// from the system Back edge-swipe, so a stroke that starts near the screen edge stays a stroke.
/// </remarks>
public partial class SignatureView : ContentView
{
    /// <summary>Names the drawing surface.</summary>
    public const string CanvasAutomationIdSuffix = "_Canvas";

    /// <summary>Names the clear button.</summary>
    public const string ClearAutomationIdSuffix = "_Clear";

    /// <summary>The height the view asks for when nothing else sizes it.</summary>
    public const double DefaultHeight = 160;

    readonly SignaturePadDrawable drawable = new();
    readonly GraphicsView surface;
    readonly Button clearButton;
    readonly Border border;
#if IOS
    readonly DragTouchHook touchHook;
#endif
    bool stroking;

    public SignatureView()
    {
        this.surface = new GraphicsView
        {
            Drawable = this.drawable,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        SemanticProperties.SetDescription(this.surface, "Signature");
        this.surface.StartInteraction += this.OnStartInteraction;
        this.surface.DragInteraction += this.OnDragInteraction;
        this.surface.EndInteraction += this.OnEndInteraction;
        this.surface.CancelInteraction += (_, _) => this.CancelStroke();

#if IOS
        // UIScrollView's pan starts as soon as the finger moves, so the scroller is locked from the raw
        // touch-down, before the first drag sample. Android locks from StartInteraction instead (see
        // ScratchCard for why the hook cannot be used there).
        this.touchHook = new DragTouchHook(this.surface)
        {
            Pressed = () =>
            {
                if (this.CanDraw)
                    this.touchHook!.LockScroller(true);
            },
            Released = () => this.touchHook!.LockScroller(false)
        };
#endif

        this.clearButton = new Button
        {
            Text = "Clear",
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Padding = new Thickness(8, 2),
            // Not themed: it sits on the paper, which stays white in dark mode.
            TextColor = Color.FromArgb("#6B7280"),
            IsVisible = false
        }.Neutralize().WithFontSize(ShinyThemeKeys.Type.BodySmallSize);
        this.clearButton.Clicked += (_, _) => this.Clear();

        this.border = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = 0,
            Content = new Grid
            {
                Children = { this.surface, this.clearButton }
            }
        };
        this.ApplyBorderColor(null);

        this.surface.SizeChanged += (_, _) => this.UpdateEdgeGestures();
        this.surface.HandlerChanged += (_, _) => this.UpdateEdgeGestures();

        AutomationIds.Link(this, this.surface, CanvasAutomationIdSuffix);
        AutomationIds.Link(this, this.clearButton, ClearAutomationIdSuffix);

        this.Content = this.border;
        StyleGuard.MarkReady(this, typeof(SignatureView));
    }


    // ---------------------------------------------------------------------------------------------
    // Events and methods
    // ---------------------------------------------------------------------------------------------

    /// <summary>A stroke was finished and kept.</summary>
    public event EventHandler? StrokeCompleted;

    /// <summary>The surface was emptied - by the clear button, <see cref="Clear"/> or undoing the last stroke.</summary>
    public event EventHandler? Cleared;

    /// <summary>Removes every stroke.</summary>
    public void Clear()
    {
        var had = this.drawable.HasSignature;
        this.drawable.Clear();
        this.stroking = false;
        this.surface.Invalidate();
        this.SyncState();

        if (had)
            this.RaiseCleared();
    }

    /// <summary>Removes the last stroke.</summary>
    /// <returns>False when there was nothing to undo.</returns>
    public bool Undo()
    {
        if (!this.drawable.Undo())
            return false;

        this.surface.Invalidate();
        this.SyncState();
        if (!this.drawable.HasSignature)
            this.RaiseCleared();
        return true;
    }

    /// <summary>
    /// Renders the signature to a PNG of <paramref name="width"/> x <paramref name="height"/> (defaults
    /// <see cref="ExportWidth"/> x <see cref="ExportHeight"/>), scaled to fit and centred on
    /// <see cref="SignatureBackgroundColor"/>. The placeholder and signature line are never exported.
    /// </summary>
    /// <remarks>
    /// <see cref="Stream.Null"/> on a plain <c>net10.0</c> backend that registers no
    /// <see cref="IBitmapExportService"/>.
    /// </remarks>
    public Stream ExportToPng(int? width = null, int? height = null)
    {
        var exportService = this.Handler?.MauiContext?.Services.GetService(typeof(IBitmapExportService)) as IBitmapExportService;
        return this.drawable.ExportToPng(width ?? this.ExportWidth, height ?? this.ExportHeight, exportService);
    }


    // ---------------------------------------------------------------------------------------------
    // Internals
    // ---------------------------------------------------------------------------------------------

    /// <summary>The drawing surface; <see cref="SignaturePad"/> reaches the native view through it.</summary>
    internal GraphicsView Surface => this.surface;

    internal Button ClearButton => this.clearButton;

    bool CanDraw => this.IsEnabled && !this.IsReadOnly;

    protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
    {
        var size = base.MeasureOverride(widthConstraint, heightConstraint);

        // A GraphicsView wants no size of its own; left to a stack layout it would collapse to nothing.
        if (this.HeightRequest < 0 && size.Height < DefaultHeight)
            size = new Size(size.Width, Math.Min(DefaultHeight, heightConstraint));

        return size;
    }

    void OnStartInteraction(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0 || !this.CanDraw)
            return;

        this.LockScroller(true);
        this.stroking = true;
        this.drawable.BeginStroke(e.Touches[0]);
        this.surface.Invalidate();
    }

    void OnDragInteraction(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0 || !this.stroking)
            return;

        this.drawable.AddPoint(e.Touches[0]);
        this.surface.Invalidate();
    }

    void OnEndInteraction(object? sender, TouchEventArgs e)
    {
        if (!this.stroking)
            return;

        this.stroking = false;
        this.LockScroller(false);
        var kept = this.drawable.EndStroke();
        this.surface.Invalidate();
        this.SyncState();

        if (kept)
        {
            this.StrokeCompleted?.Invoke(this, EventArgs.Empty);
            if (this.StrokeCompletedCommand?.CanExecute(null) == true)
                this.StrokeCompletedCommand.Execute(null);
        }
    }

    void CancelStroke()
    {
        this.stroking = false;
        this.LockScroller(false);
        this.drawable.CancelStroke();
        this.surface.Invalidate();
    }

    void RaiseCleared()
    {
        this.Cleared?.Invoke(this, EventArgs.Empty);
        if (this.ClearedCommand?.CanExecute(null) == true)
            this.ClearedCommand.Execute(null);
    }

    void SyncState()
    {
        var has = this.drawable.HasSignature;
        this.SetValue(HasSignaturePropertyKey, has);
        this.SetValue(StrokeCountPropertyKey, this.drawable.StrokeCount);
        this.clearButton.IsVisible = has && this.ShowClearButton && !this.IsReadOnly;
    }

    /// <summary>
    /// Keeps an enclosing scroller off the stroke in flight. Android's request walks every ancestor and
    /// the framework clears it on the next touch-down, so it only ever covers this one gesture.
    /// </summary>
    void LockScroller(bool locked)
    {
#if IOS
        this.touchHook.LockScroller(locked);
#elif ANDROID
        (this.surface.Handler?.PlatformView as Android.Views.View)?.Parent?.RequestDisallowInterceptTouchEvent(locked);
#endif
    }

    /// <summary>
    /// Android 10+ takes a swipe that starts near the left or right screen edge as Back, which for a
    /// surface laid out with the usual page margin is a stroke starting on its own edge. Excluding the
    /// surface from the system gestures while it can draw keeps the stroke. Android caps exclusions at
    /// 200dp of height per edge, which covers any sensible signature box.
    /// </summary>
    void UpdateEdgeGestures()
    {
#if ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(29) || this.surface.Handler?.PlatformView is not Android.Views.View native)
            return;

        // From the MAUI size: SizeChanged arrives before the native view's own layout pass.
        var width = (int)Math.Ceiling(Microsoft.Maui.Platform.ContextExtensions.ToPixels(native.Context!, this.surface.Width));
        var height = (int)Math.Ceiling(Microsoft.Maui.Platform.ContextExtensions.ToPixels(native.Context!, this.surface.Height));
        native.SystemGestureExclusionRects = this.CanDraw && width > 0 && height > 0
            ? [new Android.Graphics.Rect(0, 0, width, height)]
            : [];
#endif
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == IsEnabledProperty.PropertyName)
        {
            if (!this.IsEnabled)
                this.CancelStroke();
            this.UpdateEdgeGestures();
        }
    }

    void ApplyBorderColor(Color? explicitColor)
        => ThemeBrush.Apply(this.border, Border.StrokeProperty, explicitColor, ShinyThemeKeys.Brush.OutlineVariant);
}
