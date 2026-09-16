using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.ColorPicker;
using Shiny.Maui.Controls.FontPicker;
using Shiny.Maui.Controls.ImageEditor.EditActions;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.ImageEditor;

public partial class ImageEditor : ContentView
{
    readonly Grid rootGrid;
    readonly GraphicsView graphicsView;
    readonly ImageEditorDrawable drawable;
    readonly ImageEditorState state;
    View? toolbarView;
    ColorPickerButton? drawColorButton;
    ColorPickerButton? shapeFillButton;
    Border? shapeFillToggle;
    ThemedIconView? shapeFillIcon;
    FontPickerButton? fontPickerButton;
    FontSizePickerButton? fontSizePickerButton;
    Label? zoomReadout;
    Border? undoButton;
    Border? redoButton;
    Border? resetButton;

    /// <summary>
    /// The colour the fill toggle switches back on. Turning fill off nulls
    /// <see cref="ShapeFillColor"/>, and the user should get their colour back — not white — when
    /// they turn it on again.
    /// </summary>
    Color lastShapeFill = Color.FromRgba(255, 255, 255, 0.35f);

    public ImageEditor()
    {
        state = new ImageEditorState();
        drawable = new ImageEditorDrawable { State = state };

        state.StateChanged += OnStateChanged;

        graphicsView = new GraphicsView
        {
            Drawable = drawable,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };

        SetupGestures();
        SetupCommands();

        rootGrid = new Grid
        {
            RowDefinitions =
            [
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            ],
            Children = { graphicsView }
        };

        Grid.SetRow(graphicsView, 0);

        BuildDefaultToolbar();

        Content = rootGrid;


        // Invalidate once layout is ready so images set during binding actually render. A resize
        // (rotation, split view) also changes the viewport the pan is clamped against, so the
        // offsets are re-clamped on the next frame — once the drawable knows the new viewport.
        graphicsView.SizeChanged += (_, _) =>
        {
            Invalidate();
            if (zoomScale > 1.001f)
                Dispatcher.Dispatch(() =>
                {
                    ClampOffsets();
                    PushTransformToDrawable();
                });
        };

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(ImageEditor));
    }

    #region Bindable Properties

    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source),
        typeof(ImageSource),
        typeof(ImageEditor),
        null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                _ = ((ImageEditor)b).OnSourceChangedAsync();
            }));

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public static readonly BindableProperty CurrentToolModeProperty = BindableProperty.Create(
        nameof(CurrentToolMode),
        typeof(ImageEditorToolMode),
        typeof(ImageEditor),
        ImageEditorToolMode.Move,
        BindingMode.TwoWay,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                ((ImageEditor)b).OnToolModeChanged((ImageEditorToolMode)n);
            }));

    public ImageEditorToolMode CurrentToolMode
    {
        get => (ImageEditorToolMode)GetValue(CurrentToolModeProperty);
        set => SetValue(CurrentToolModeProperty, value);
    }

    public static readonly BindableProperty AllowCropProperty = BindableProperty.Create(
        nameof(AllowCrop), typeof(bool), typeof(ImageEditor), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                ((ImageEditor)b).BuildDefaultToolbar();
            }));

    public bool AllowCrop
    {
        get => (bool)GetValue(AllowCropProperty);
        set => SetValue(AllowCropProperty, value);
    }

    public static readonly BindableProperty AllowRotateProperty = BindableProperty.Create(
        nameof(AllowRotate), typeof(bool), typeof(ImageEditor), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                ((ImageEditor)b).BuildDefaultToolbar();
            }));

    public bool AllowRotate
    {
        get => (bool)GetValue(AllowRotateProperty);
        set => SetValue(AllowRotateProperty, value);
    }

    public static readonly BindableProperty AllowDrawProperty = BindableProperty.Create(
        nameof(AllowDraw), typeof(bool), typeof(ImageEditor), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                ((ImageEditor)b).BuildDefaultToolbar();
            }));

    public bool AllowDraw
    {
        get => (bool)GetValue(AllowDrawProperty);
        set => SetValue(AllowDrawProperty, value);
    }

    public static readonly BindableProperty AllowTextAnnotationProperty = BindableProperty.Create(
        nameof(AllowTextAnnotation), typeof(bool), typeof(ImageEditor), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                ((ImageEditor)b).BuildDefaultToolbar();
            }));

    public bool AllowTextAnnotation
    {
        get => (bool)GetValue(AllowTextAnnotationProperty);
        set => SetValue(AllowTextAnnotationProperty, value);
    }

    public static readonly BindableProperty AllowLineProperty = BindableProperty.Create(
        nameof(AllowLine), typeof(bool), typeof(ImageEditor), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                ((ImageEditor)b).BuildDefaultToolbar();
            }));

    public bool AllowLine
    {
        get => (bool)GetValue(AllowLineProperty);
        set => SetValue(AllowLineProperty, value);
    }

    public static readonly BindableProperty AllowArrowProperty = BindableProperty.Create(
        nameof(AllowArrow), typeof(bool), typeof(ImageEditor), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                ((ImageEditor)b).BuildDefaultToolbar();
            }));

    public bool AllowArrow
    {
        get => (bool)GetValue(AllowArrowProperty);
        set => SetValue(AllowArrowProperty, value);
    }

    public static readonly BindableProperty AllowRectangleProperty = BindableProperty.Create(
        nameof(AllowRectangle), typeof(bool), typeof(ImageEditor), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                ((ImageEditor)b).BuildDefaultToolbar();
            }));

    public bool AllowRectangle
    {
        get => (bool)GetValue(AllowRectangleProperty);
        set => SetValue(AllowRectangleProperty, value);
    }

    public static readonly BindableProperty AllowEllipseProperty = BindableProperty.Create(
        nameof(AllowEllipse), typeof(bool), typeof(ImageEditor), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                ((ImageEditor)b).BuildDefaultToolbar();
            }));

    public bool AllowEllipse
    {
        get => (bool)GetValue(AllowEllipseProperty);
        set => SetValue(AllowEllipseProperty, value);
    }

    public static readonly BindableProperty AllowCircleProperty = BindableProperty.Create(
        nameof(AllowCircle), typeof(bool), typeof(ImageEditor), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                ((ImageEditor)b).BuildDefaultToolbar();
            }));

    public bool AllowCircle
    {
        get => (bool)GetValue(AllowCircleProperty);
        set => SetValue(AllowCircleProperty, value);
    }

    public static readonly BindableProperty ShapeFillColorProperty = BindableProperty.Create(
        nameof(ShapeFillColor), typeof(Color), typeof(ImageEditor), null,
        BindingMode.TwoWay,
        // Updated in place rather than by rebuilding the bar: this fires while the user is dragging
        // inside the fill picker's popup, and a rebuild would discard the very button that popup
        // belongs to
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                var editor = (ImageEditor)b;
                var color = (Color?)n;
                editor.drawable.ActiveFillColor = color;

                if (color != null)
                {
                    editor.lastShapeFill = color;
                    if (editor.shapeFillButton != null && !editor.shapeFillButton.SelectedColor.Equals(color))
                        editor.shapeFillButton.SelectedColor = color;
                }

                editor.UpdateShapeFillToggle();
                editor.Invalidate();
            }));

    /// <summary>
    /// Interior colour for the shape tools. Null — the default — leaves shapes unfilled, which is
    /// what you want for a highlight box drawn over a photo; a solid colour turns the same tool into
    /// a redaction block. Alpha is honoured, so a translucent fill tints without hiding.
    /// </summary>
    public Color? ShapeFillColor
    {
        get => (Color?)GetValue(ShapeFillColorProperty);
        set => SetValue(ShapeFillColorProperty, value);
    }

    public static readonly BindableProperty ShowShapeFillPickerProperty = BindableProperty.Create(
        nameof(ShowShapeFillPicker), typeof(bool), typeof(ImageEditor), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                ((ImageEditor)b).BuildDefaultToolbar();
            }));

    /// <summary>Shows the fill swatch and the fill on/off toggle while a shape tool is active.</summary>
    public bool ShowShapeFillPicker
    {
        get => (bool)GetValue(ShowShapeFillPickerProperty);
        set => SetValue(ShowShapeFillPickerProperty, value);
    }

    public static readonly BindableProperty AllowFontSelectionProperty = BindableProperty.Create(
        nameof(AllowFontSelection), typeof(bool), typeof(ImageEditor), false,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                ((ImageEditor)b).BuildDefaultToolbar();
            }));

    public bool AllowFontSelection
    {
        get => (bool)GetValue(AllowFontSelectionProperty);
        set => SetValue(AllowFontSelectionProperty, value);
    }

    public static readonly BindableProperty AllowFontSizeSelectionProperty = BindableProperty.Create(
        nameof(AllowFontSizeSelection), typeof(bool), typeof(ImageEditor), false,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                ((ImageEditor)b).BuildDefaultToolbar();
            }));

    public bool AllowFontSizeSelection
    {
        get => (bool)GetValue(AllowFontSizeSelectionProperty);
        set => SetValue(AllowFontSizeSelectionProperty, value);
    }

    public static readonly BindableProperty AllowZoomProperty = BindableProperty.Create(
        nameof(AllowZoom), typeof(bool), typeof(ImageEditor), true,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                var editor = (ImageEditor)b;
                if (!(bool)n)
                    editor.ZoomToFit();
                editor.BuildDefaultToolbar();
            }));

    public bool AllowZoom
    {
        get => (bool)GetValue(AllowZoomProperty);
        set => SetValue(AllowZoomProperty, value);
    }

    public static readonly BindableProperty CanUndoProperty = BindableProperty.Create(
        nameof(CanUndo), typeof(bool), typeof(ImageEditor), false, BindingMode.OneWayToSource);

    public bool CanUndo
    {
        get => (bool)GetValue(CanUndoProperty);
        private set => SetValue(CanUndoProperty, value);
    }

    public static readonly BindableProperty CanRedoProperty = BindableProperty.Create(
        nameof(CanRedo), typeof(bool), typeof(ImageEditor), false, BindingMode.OneWayToSource);

    public bool CanRedo
    {
        get => (bool)GetValue(CanRedoProperty);
        private set => SetValue(CanRedoProperty, value);
    }

    public static readonly BindableProperty DrawStrokeColorProperty = BindableProperty.Create(
        nameof(DrawStrokeColor), typeof(Color), typeof(ImageEditor), Colors.White,
        BindingMode.TwoWay,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
            var editor = (ImageEditor)b;
            editor.drawable.ActiveStrokeColor = (Color)n;
            if (editor.drawColorButton != null)
                editor.drawColorButton.SelectedColor = (Color)n;
        }));

    public Color DrawStrokeColor
    {
        get => (Color)GetValue(DrawStrokeColorProperty);
        set => SetValue(DrawStrokeColorProperty, value);
    }

    public static readonly BindableProperty DrawStrokeWidthProperty = BindableProperty.Create(
        nameof(DrawStrokeWidth), typeof(double), typeof(ImageEditor), 3.0,
        BindingMode.TwoWay,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                var editor = (ImageEditor)b;
                editor.drawable.ActiveStrokeWidth = (float)(double)n;
                editor.BuildDefaultToolbar();
            }));

    public double DrawStrokeWidth
    {
        get => (double)GetValue(DrawStrokeWidthProperty);
        set => SetValue(DrawStrokeWidthProperty, value);
    }

    public static readonly BindableProperty TextFontSizeProperty = BindableProperty.Create(
        nameof(TextFontSize), typeof(double), typeof(ImageEditor), 16.0,
        BindingMode.TwoWay,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
            var editor = (ImageEditor)b;
            if (editor.activeTextEntry != null)
                editor.activeTextEntry.FontSize = (double)n;
            if (editor.fontSizePickerButton != null)
                editor.fontSizePickerButton.SelectedFontSize = (double)n;
        }));

    public double TextFontSize
    {
        get => (double)GetValue(TextFontSizeProperty);
        set => SetValue(TextFontSizeProperty, value);
    }

    public static readonly BindableProperty AvailableFontSizesProperty = BindableProperty.Create(
        nameof(AvailableFontSizes), typeof(IList<double>), typeof(ImageEditor), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                ((ImageEditor)b).BuildDefaultToolbar();
            }));

    public IList<double>? AvailableFontSizes
    {
        get => (IList<double>?)GetValue(AvailableFontSizesProperty);
        set => SetValue(AvailableFontSizesProperty, value);
    }

    public static readonly BindableProperty AnnotationTextColorProperty = BindableProperty.Create(
        nameof(AnnotationTextColor), typeof(Color), typeof(ImageEditor), Colors.White);

    public Color AnnotationTextColor
    {
        get => (Color)GetValue(AnnotationTextColorProperty);
        set => SetValue(AnnotationTextColorProperty, value);
    }

    public static readonly BindableProperty TextFontFamilyProperty = BindableProperty.Create(
        nameof(TextFontFamily), typeof(string), typeof(ImageEditor), null,
        BindingMode.TwoWay,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
            var editor = (ImageEditor)b;
            if (editor.activeTextEntry != null)
                editor.activeTextEntry.FontFamily = n as string;
            if (editor.fontPickerButton != null)
                editor.fontPickerButton.SelectedFont = n as string;
        }));

    public string? TextFontFamily
    {
        get => (string?)GetValue(TextFontFamilyProperty);
        set => SetValue(TextFontFamilyProperty, value);
    }

    public static readonly BindableProperty AvailableFontsProperty = BindableProperty.Create(
        nameof(AvailableFonts), typeof(IList<string>), typeof(ImageEditor), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                ((ImageEditor)b).BuildDefaultToolbar();
            }));

    public IList<string>? AvailableFonts
    {
        get => (IList<string>?)GetValue(AvailableFontsProperty);
        set => SetValue(AvailableFontsProperty, value);
    }

    public static readonly BindableProperty ToolbarTemplateProperty = BindableProperty.Create(
        nameof(ToolbarTemplate), typeof(DataTemplate), typeof(ImageEditor), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                ((ImageEditor)b).ApplyToolbarTemplate();
            }));

    public DataTemplate? ToolbarTemplate
    {
        get => (DataTemplate?)GetValue(ToolbarTemplateProperty);
        set => SetValue(ToolbarTemplateProperty, value);
    }

    public static readonly BindableProperty ShowToolLabelsProperty = BindableProperty.Create(
        nameof(ShowToolLabels), typeof(bool), typeof(ImageEditor), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                ((ImageEditor)b).BuildDefaultToolbar();
            }));

    /// <summary>
    /// Shows a caption under each tool icon. Turn this off for a compact icon-only bar.
    /// </summary>
    public bool ShowToolLabels
    {
        get => (bool)GetValue(ShowToolLabelsProperty);
        set => SetValue(ShowToolLabelsProperty, value);
    }

    public static readonly BindableProperty ShowStrokeWidthPickerProperty = BindableProperty.Create(
        nameof(ShowStrokeWidthPicker), typeof(bool), typeof(ImageEditor), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                ((ImageEditor)b).BuildDefaultToolbar();
            }));

    /// <summary>Shows the pen-weight presets alongside the colour swatch for the ink tools.</summary>
    public bool ShowStrokeWidthPicker
    {
        get => (bool)GetValue(ShowStrokeWidthPickerProperty);
        set => SetValue(ShowStrokeWidthPickerProperty, value);
    }

    public static readonly BindableProperty StrokeWidthPresetsProperty = BindableProperty.Create(
        nameof(StrokeWidthPresets), typeof(IList<double>), typeof(ImageEditor), null,
        defaultValueCreator: _ => new List<double> { 2, 4, 8 },
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                ((ImageEditor)b).BuildDefaultToolbar();
            }));

    /// <summary>Pen weights offered by the stroke-width picker.</summary>
    public IList<double> StrokeWidthPresets
    {
        get => (IList<double>)GetValue(StrokeWidthPresetsProperty);
        set => SetValue(StrokeWidthPresetsProperty, value);
    }

    public static readonly BindableProperty ToolbarBackgroundColorProperty = BindableProperty.Create(
        // 20/255f, not 20: FromRgba(20, 20, 22, 0.86f) binds to the all-float overload (the ints widen),
        // where the channels are 0-1 - so 20 clamped to 1 and the "dark scrim" was painted white. It
        // rendered a near-white bar under the white icons and labels, which read as an empty strip.
        nameof(ToolbarBackgroundColor), typeof(Color), typeof(ImageEditor), Color.FromRgba(20 / 255f, 20 / 255f, 22 / 255f, 0.86f),
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                ((ImageEditor)b).BuildDefaultToolbar();
            }));

    /// <summary>
    /// Background of the default toolbar. It defaults to a dark scrim because the bar floats over
    /// arbitrary photos and has to stay legible on all of them.
    /// </summary>
    public Color ToolbarBackgroundColor
    {
        get => (Color)GetValue(ToolbarBackgroundColorProperty);
        set => SetValue(ToolbarBackgroundColorProperty, value);
    }

    public static readonly BindableProperty ToolbarPositionProperty = BindableProperty.Create(
        // Top, since the toolbar became a ribbon: a ribbon is top-of-window chrome, and read upside
        // down - tab strip above a body of groups, pinned to the floor - it stops looking like one.
        nameof(ToolbarPosition), typeof(ToolbarPosition), typeof(ImageEditor), ToolbarPosition.Top,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                ((ImageEditor)b).UpdateToolbarPosition();
            }));

    public ToolbarPosition ToolbarPosition
    {
        get => (ToolbarPosition)GetValue(ToolbarPositionProperty);
        set => SetValue(ToolbarPositionProperty, value);
    }

    public static readonly BindableProperty UseFeedbackProperty = BindableProperty.Create(
        nameof(UseFeedback), typeof(bool), typeof(ImageEditor), true);

    public bool UseFeedback
    {
        get => (bool)GetValue(UseFeedbackProperty);
        set => SetValue(UseFeedbackProperty, value);
    }

    public static readonly BindableProperty CropApplyTextProperty = BindableProperty.Create(
        nameof(CropApplyText), typeof(string), typeof(ImageEditor), "Apply",
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                ((ImageEditor)b).BuildDefaultToolbar();
            }));

    public string CropApplyText
    {
        get => (string)GetValue(CropApplyTextProperty);
        set => SetValue(CropApplyTextProperty, value);
    }

    public static readonly BindableProperty CropCancelTextProperty = BindableProperty.Create(
        nameof(CropCancelText), typeof(string), typeof(ImageEditor), "Cancel",
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                ((ImageEditor)b).BuildDefaultToolbar();
            }));

    public string CropCancelText
    {
        get => (string)GetValue(CropCancelTextProperty);
        set => SetValue(CropCancelTextProperty, value);
    }

    public static readonly BindableProperty SaveCommandProperty = BindableProperty.Create(
        nameof(SaveCommand), typeof(System.Windows.Input.ICommand), typeof(ImageEditor), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                ((ImageEditor)b).BuildDefaultToolbar();
            }));

    public System.Windows.Input.ICommand? SaveCommand
    {
        get => (System.Windows.Input.ICommand?)GetValue(SaveCommandProperty);
        set => SetValue(SaveCommandProperty, value);
    }

    public static readonly BindableProperty SaveTextProperty = BindableProperty.Create(
        nameof(SaveText), typeof(string), typeof(ImageEditor), "Save",
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ImageEditor), () =>
            {
                ((ImageEditor)b).BuildDefaultToolbar();
            }));

    public string SaveText
    {
        get => (string)GetValue(SaveTextProperty);
        set => SetValue(SaveTextProperty, value);
    }

    #endregion

    async Task OnSourceChangedAsync()
    {
        if (Source == null)
        {
            drawable.Image = null;
        }
        else
        {
            try
            {
                var stream = await ResolveImageSourceStreamAsync(Source);
                if (stream != null)
                {
                    await using (stream)
                        drawable.Image = Microsoft.Maui.Graphics.Platform.PlatformImage.FromStream(stream);

                }
                else
                {
                    drawable.Image = null;
                }
            }
            catch
            {
                drawable.Image = null;
            }
        }

        state.Reset();
        ResetViewTransform();
        Invalidate();
    }

    static async Task<Stream?> ResolveImageSourceStreamAsync(ImageSource source)
    {
        switch (source)
        {
            case FileImageSource fileSource:
                // Try regular file path first
                if (File.Exists(fileSource.File))
                    return File.OpenRead(fileSource.File);

                // Try app package file (raw assets/bundles)
                try { return await FileSystem.OpenAppPackageFileAsync(fileSource.File); }
                catch { /* fall through to platform-specific loading */ }

#if IOS || MACCATALYST
                // On iOS/Catalyst, MAUI images are in the app bundle
                var uiImage = UIKit.UIImage.FromBundle(System.IO.Path.GetFileNameWithoutExtension(fileSource.File));
                if (uiImage != null)
                {
                    var pngData = uiImage.AsPNG();
                    if (pngData != null)
                        return pngData.AsStream();
                }
#elif ANDROID
                // On Android, try loading via the MAUI resource system
                var context = Android.App.Application.Context;
                var resName = System.IO.Path.GetFileNameWithoutExtension(fileSource.File)?.ToLowerInvariant();
                if (resName != null && context.Resources != null)
                {
                    var resId = context.Resources.GetIdentifier(resName, "drawable", context.PackageName);
                    if (resId != 0)
                    {
                        var drawable = context.Resources.GetDrawable(resId, context.Theme);
                        if (drawable is Android.Graphics.Drawables.BitmapDrawable bd && bd.Bitmap != null)
                        {
                            var ms = new MemoryStream();
                            await bd.Bitmap.CompressAsync(Android.Graphics.Bitmap.CompressFormat.Png!, 100, ms);
                            ms.Position = 0;
                            return ms;
                        }
                    }
                }
#endif
                return null;

            case StreamImageSource streamSource:
                return await streamSource.Stream(CancellationToken.None);

            case UriImageSource uriSource:
                using (var client = new HttpClient())
                    return new MemoryStream(await client.GetByteArrayAsync(uriSource.Uri));

            default:
                return null;
        }
    }

    void OnToolModeChanged(ImageEditorToolMode mode)
    {
        // The rebuild that follows adds or drops the contextual tab; this tells it to reveal one that
        // has just appeared. Only on a tool change - every other rebuild leaves the tab where it was.
        toolTabIsNew = true;

        // Finalize any in-progress operations
        FinalizeCurrentOperation();

        if (UseFeedback)
            FeedbackHelper.Execute(this, "ToolModeChanged", mode.ToString());

        drawable.ToolMode = mode;

        // The crop rect is normalised against the whole image, so entering crop returns to
        // fit-to-view; every other tool keeps whatever zoom the user set up
        if (mode == ImageEditorToolMode.Crop)
        {
            drawable.ActiveCropRect = new RectF(0.1f, 0.1f, 0.8f, 0.8f);
            ZoomToFit();
        }
        else
        {
            drawable.ActiveCropRect = null;
        }

        BuildDefaultToolbar();
        Invalidate();
    }

    void OnStateChanged()
    {
        CanUndo = state.CanUndo;
        CanRedo = state.CanRedo;

        // Toggle in place rather than rebuilding — the toolbar rebuilds on tool changes only,
        // so drawing a stroke doesn't flicker the whole bar
        if (undoButton != null) SetButtonEnabled(undoButton, CanUndo);
        if (redoButton != null) SetButtonEnabled(redoButton, CanRedo);
        if (resetButton != null) SetButtonEnabled(resetButton, CanUndo);

        Invalidate();
    }

    void FinalizeCurrentOperation()
    {
        // Finalize in-progress text entry
        CommitActiveTextEntry();

        // Finalize in-progress draw stroke
        if (drawable.ActiveStrokePoints is { Count: >= 2 })
        {
            var imageRect = drawable.GetImageRect();
            if (imageRect is { Width: > 0, Height: > 0 })
            {
                var normalized = drawable.ActiveStrokePoints
                    .Select(p => new PointF(
                        (p.X - imageRect.X) / imageRect.Width,
                        (p.Y - imageRect.Y) / imageRect.Height))
                    .ToArray();

                state.Push(new DrawStrokeAction
                {
                    Points = normalized,
                    StrokeColor = DrawStrokeColor,
                    StrokeWidth = (float)DrawStrokeWidth,
                    ReferenceWidth = imageRect.Width
                });
            }
            drawable.ActiveStrokePoints = null;
        }

        // Finalize in-progress line / arrow
        if (drawable.ActiveLineStart.HasValue && drawable.ActiveLineEnd.HasValue)
        {
            CommitCurrentLine();
        }

        // Finalize in-progress shape
        if (drawable.ActiveShapeStart.HasValue && drawable.ActiveShapeEnd.HasValue)
        {
            CommitCurrentShape();
        }
    }

    void Invalidate() => graphicsView.Invalidate();

    #region Toolbar

    void ApplyToolbarTemplate()
    {
        if (ToolbarTemplate != null)
        {
            RemoveToolbar();
            var content = ToolbarTemplate.CreateContent();
            if (content is View view)
            {
                toolbarView = view;
                AddToolbarToGrid();
            }
        }
        else
        {
            BuildDefaultToolbar();
        }
    }

    void BuildDefaultToolbar()
    {
        // Don't build if custom template is set
        if (ToolbarTemplate != null)
            return;

        RemoveToolbar();
        zoomReadout = null;
        drawColorButton = null;
        shapeFillButton = null;
        shapeFillToggle = null;
        shapeFillIcon = null;
        fontPickerButton = null;
        fontSizePickerButton = null;
        undoButton = redoButton = resetButton = null;

        // Crop is modal — it gets a focused confirm/cancel bar instead of the full tool set
        toolbarView = CurrentToolMode == ImageEditorToolMode.Crop
            ? BuildCropToolbar()
            : BuildStandardToolbar();

        AddToolbarToGrid();
    }

    // The ribbon is the standard toolbar now - see ImageEditor.Ribbon.cs. It groups and captions what
    // the old three-row bar left unlabelled, and on a narrow editor it runs in Simplified mode rather
    // than eating a quarter of the screen. The crop bar below stays hand-rolled: it is modal, two
    // commands wide, and a ribbon would be the wrong shape for it entirely.
    View BuildStandardToolbar() => BuildRibbonToolbar();

    // BuildToolRow / BuildOptionsRow / BuildActionRow / BuildZoomCluster used to build the
    // three-row floating bar. The ribbon replaced all four - see ImageEditor.Ribbon.cs. The crop
    // bar below is still hand-rolled: it is modal and two commands wide, and a ribbon would be
    // the wrong shape for it.

    View BuildCropToolbar()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            ],
            ColumnSpacing = 8
        };

        var cancelBtn = new Button
        {
            Text = CropCancelText,
            TextColor = ChromeForeground,
            BackgroundColor = Color.FromRgba(255, 255, 255, 0.14f),
            CornerRadius = 14,
            HeightRequest = 42,
            MinimumWidthRequest = 64,
            Padding = new Thickness(16, 0),
            VerticalOptions = LayoutOptions.Center
        }.Neutralize().WithFontSize(ShinyThemeKeys.Type.BodyMediumSize);
        cancelBtn.Clicked += (_, _) => CurrentToolMode = ImageEditorToolMode.Move;
        grid.Add(cancelBtn, 0);

        grid.Add(new Label
        {
            Text = "Drag the edges to crop",
            TextColor = ChromeForeground,
            FontSize = 13,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        }, 1);

        var applyBtn = new Button
        {
            Text = CropApplyText,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 14,
            HeightRequest = 42,
            MinimumWidthRequest = 64,
            Padding = new Thickness(16, 0),
            VerticalOptions = LayoutOptions.Center
        }.Neutralize().WithFontSize(ShinyThemeKeys.Type.BodyMediumSize);
        applyBtn.SetDynamicResource(Button.TextColorProperty, ShinyThemeKeys.Color.OnPrimary);
        applyBtn.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Primary);
        applyBtn.Clicked += (_, _) => ApplyCrop();
        grid.Add(applyBtn, 2);

        return WrapInChrome(grid);
    }

    /// <summary>The shared rounded scrim every default toolbar sits in.</summary>
    Border WrapInChrome(View content) => new()
    {
        Content = content,
        StrokeThickness = 0,
        BackgroundColor = ToolbarBackgroundColor,
        StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerExtraLargeRadius),
        Padding = new Thickness(8, 8),
        Margin = new Thickness(10, 8)
    };

    View CreateToolButton(ImageEditorIcon icon, string label, ImageEditorToolMode mode)
    {
        var selected = CurrentToolMode == mode;
        return CreateChromeButton(icon, label, selected, true, () =>
        {
            CurrentToolMode = selected ? ImageEditorToolMode.Move : mode;
        });
    }

    Border CreateChromeButton(ImageEditorIcon icon, string? label, bool selected, bool enabled, Action action, double? width = null)
    {
        var showLabel = ShowToolLabels && !string.IsNullOrEmpty(label);

        var content = new VerticalStackLayout
        {
            Spacing = 2,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        var drawable = new ImageEditorIconDrawable { Icon = icon };
        content.Children.Add(new ThemedIconView(drawable, c => drawable.Color = c, 22));
        TintOnAccent((ThemedIconView)content.Children[0], ThemedIconView.IconColorProperty, selected);

        if (showLabel)
        {
            var text = new Label
            {
                Text = label,
                FontSize = 9.5,
                LineBreakMode = LineBreakMode.NoWrap,
                HorizontalTextAlignment = TextAlignment.Center
            };
            TintOnAccent(text, Label.TextColorProperty, selected);
            content.Children.Add(text);
        }

        var button = new Border
        {
            Content = content,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerLargeRadius),
            Padding = new Thickness(6, 4),
            MinimumWidthRequest = width ?? (showLabel ? 54 : 44),
            HeightRequest = showLabel ? 48 : 40,
            VerticalOptions = LayoutOptions.Center
        };
        TintAccent(button, selected);

        SetButtonEnabled(button, enabled);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => action();
        button.GestureRecognizers.Add(tap);

        return button;
    }

    static void SetButtonEnabled(View button, bool enabled)
    {
        button.IsEnabled = enabled;
        button.Opacity = enabled ? 1 : 0.3;
    }

    internal View CreateStrokeWidthButton(double width)
    {
        var selected = Math.Abs(DrawStrokeWidth - width) < 0.01;
        var diameter = 6 + width * 1.6;

        var dot = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = (float)(diameter / 2) },
            WidthRequest = diameter,
            HeightRequest = diameter,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };
        TintOnAccent(dot, VisualElement.BackgroundColorProperty, selected);

        var button = new Border
        {
            Content = dot,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerLargeRadius),
            WidthRequest = 36,
            HeightRequest = 36,
            VerticalOptions = LayoutOptions.Center
        };
        TintAccent(button, selected);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => DrawStrokeWidth = width;
        button.GestureRecognizers.Add(tap);

        return button;
    }

    ColorPickerButton CreateDrawColorButton()
    {
        drawColorButton = new ColorPickerButton
        {
            SelectedColor = DrawStrokeColor,
            CornerRadius = 18,
            HeightRequest = 36,
            WidthRequest = 36,
            VerticalOptions = LayoutOptions.Center
        };
        drawColorButton.ColorChanged += (_, color) => DrawStrokeColor = color;
        return drawColorButton;
    }

    /// <summary>
    /// The shape interior. Opacity is on because a translucent fill is the common case — an outline
    /// you can still see the photo through.
    /// </summary>
    ColorPickerButton CreateShapeFillButton()
    {
        shapeFillButton = new ColorPickerButton
        {
            SelectedColor = ShapeFillColor ?? lastShapeFill,
            ShowOpacity = true,
            CornerRadius = 8,
            HeightRequest = 36,
            WidthRequest = 36,
            VerticalOptions = LayoutOptions.Center
        };
        shapeFillButton.ColorChanged += (_, color) => ShapeFillColor = color;
        return shapeFillButton;
    }

    /// <summary>
    /// Fill on/off. Its own builder rather than <see cref="CreateChromeButton"/> because the icon
    /// and the selected tint are re-applied in place every time the fill colour changes.
    /// </summary>
    internal Border CreateShapeFillToggle()
    {
        // Its ink is a bindable property, so the "on" colour can follow the on-primary token; the
        // drawable is only fed from it while the fill is on.
        var fillDrawable = new ImageEditorIconDrawable();
        shapeFillIcon = new ThemedIconView(fillDrawable, c => fillDrawable.Color = c, 22);

        shapeFillToggle = new Border
        {
            Content = shapeFillIcon,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerLargeRadius),
            Padding = new Thickness(6, 4),
            WidthRequest = 36,
            HeightRequest = 36,
            VerticalOptions = LayoutOptions.Center
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => ShapeFillColor = ShapeFillColor == null ? lastShapeFill : null;
        shapeFillToggle.GestureRecognizers.Add(tap);

        UpdateShapeFillToggle();
        return shapeFillToggle;
    }

    void UpdateShapeFillToggle()
    {
        if (shapeFillToggle == null || shapeFillIcon?.Drawable is not ImageEditorIconDrawable icon)
            return;

        var filled = ShapeFillColor != null;

        TintAccent(shapeFillToggle, filled);
        icon.Icon = filled ? ImageEditorIcon.Fill : ImageEditorIcon.NoFill;

        // Re-pointing the property pushes the colour into the drawable and invalidates.
        TintOnAccent(shapeFillIcon, ThemedIconView.IconColorProperty, filled);
        icon.Color = shapeFillIcon.IconColor;
        shapeFillIcon.Invalidate();
        SemanticProperties.SetDescription(shapeFillToggle, filled ? "Fill on" : "Fill off");
    }

    FontPickerButton CreateFontPickerButton()
    {
        fontPickerButton = new FontPickerButton
        {
            AvailableFonts = AvailableFonts,
            SelectedFont = TextFontFamily,
            CornerRadius = 12,
            HeightRequest = 36,
            VerticalOptions = LayoutOptions.Center
        };
        fontPickerButton.FontChanged += (_, font) => TextFontFamily = font;
        return fontPickerButton;
    }

    FontSizePickerButton CreateFontSizePickerButton()
    {
        fontSizePickerButton = new FontSizePickerButton
        {
            AvailableFontSizes = AvailableFontSizes,
            SelectedFontSize = TextFontSize,
            CornerRadius = 12,
            HeightRequest = 36,
            VerticalOptions = LayoutOptions.Center
        };
        fontSizePickerButton.FontSizeChanged += (_, size) => TextFontSize = size;
        return fontSizePickerButton;
    }

    void UpdateZoomReadout()
    {
        if (zoomReadout != null)
            zoomReadout.Text = FormatZoom(zoomScale);
    }

    static string FormatZoom(float scale) => $"{Math.Round(scale * 100)}%";

    Color ChromeForeground => Color.FromRgba(255, 255, 255, 0.88f);

    /// <summary>
    /// A selected control's fill: the primary token while selected, transparent otherwise.
    /// </summary>
    /// <remarks>
    /// Resolved through the element tree rather than copied out of the application's resources, so a
    /// light/dark flip reaches a toolbar that is already built and a palette scoped over the editor is
    /// honoured. <see cref="ThemeProbe.Tint"/> clears the transparent local value on the way back to
    /// the token - without that a control that was once unselected would never pick the accent up again.
    /// </remarks>
    static void TintAccent(VisualElement target, bool selected)
        => ThemeProbe.Tint(target, VisualElement.BackgroundColorProperty, selected ? null : Colors.Transparent, ShinyThemeKeys.Color.Primary);

    /// <summary>Ink that sits on the accent: on-primary while selected, the fixed chrome foreground otherwise.</summary>
    void TintOnAccent(Element target, BindableProperty property, bool selected)
        => ThemeProbe.Tint(target, property, selected ? null : ChromeForeground, ShinyThemeKeys.Color.OnPrimary);

    void AddToolbarToGrid()
    {
        if (toolbarView == null)
            return;

        if (ToolbarPosition == ToolbarPosition.Top)
        {
            // Swap row definitions so toolbar is on top
            rootGrid.RowDefinitions.Clear();
            rootGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            rootGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            Grid.SetRow(graphicsView, 1);
            Grid.SetRow(toolbarView, 0);
        }
        else
        {
            rootGrid.RowDefinitions.Clear();
            rootGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            rootGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetRow(graphicsView, 0);
            Grid.SetRow(toolbarView, 1);
        }

        rootGrid.Children.Add(toolbarView);
    }

    void RemoveToolbar()
    {
        if (toolbarView != null)
        {
            rootGrid.Children.Remove(toolbarView);
            toolbarView = null;
        }
    }

    void UpdateToolbarPosition()
    {
        if (toolbarView == null)
            return;

        RemoveToolbar();
        if (ToolbarTemplate != null)
            ApplyToolbarTemplate();
        else
            BuildDefaultToolbar();
    }


    #endregion
}

public enum ToolbarPosition
{
    Top,
    Bottom
}
