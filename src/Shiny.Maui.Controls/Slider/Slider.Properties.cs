using System.Windows.Input;
using Shiny.Maui.Controls.Themes;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

public partial class Slider
{
    // Value
    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value), typeof(double), typeof(Slider), 0.0,
        BindingMode.TwoWay,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                ((Slider)b).UpdateVisuals();
            }));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    // Minimum
    public static readonly BindableProperty MinimumProperty = BindableProperty.Create(
        nameof(Minimum), typeof(double), typeof(Slider), 0.0,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                ((Slider)b).UpdateVisuals();
            }));
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }

    // Maximum
    public static readonly BindableProperty MaximumProperty = BindableProperty.Create(
        nameof(Maximum), typeof(double), typeof(Slider), 100.0,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                ((Slider)b).UpdateVisuals();
            }));
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    // Step
    public static readonly BindableProperty StepProperty = BindableProperty.Create(
        nameof(Step), typeof(double), typeof(Slider), 1.0);
    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }

    // ColdColor
    public static readonly BindableProperty ColdColorProperty = BindableProperty.Create(
        nameof(ColdColor), typeof(Color), typeof(Slider), Color.FromArgb("#3B82F6"),
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                ((Slider)b).UpdateVisuals();
            }));
    public Color ColdColor { get => (Color)GetValue(ColdColorProperty); set => SetValue(ColdColorProperty, value); }

    // HotColor
    public static readonly BindableProperty HotColorProperty = BindableProperty.Create(
        nameof(HotColor), typeof(Color), typeof(Slider), Color.FromArgb("#EF4444"),
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                ((Slider)b).UpdateVisuals();
            }));
    public Color HotColor { get => (Color)GetValue(HotColorProperty); set => SetValue(HotColorProperty, value); }

    // TrackHeight
    public static readonly BindableProperty TrackHeightProperty = BindableProperty.Create(
        nameof(TrackHeight), typeof(double), typeof(Slider), 8.0,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                ((Slider)b).Refresh();
            }));
    public double TrackHeight { get => (double)GetValue(TrackHeightProperty); set => SetValue(TrackHeightProperty, value); }

    // ThumbSize
    public static readonly BindableProperty ThumbSizeProperty = BindableProperty.Create(
        nameof(ThumbSize), typeof(double), typeof(Slider), 24.0,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                ((Slider)b).Refresh();
            }));
    public double ThumbSize { get => (double)GetValue(ThumbSizeProperty); set => SetValue(ThumbSizeProperty, value); }

    // ThumbColor
    public static readonly BindableProperty ThumbColorProperty = BindableProperty.Create(
        nameof(ThumbColor), typeof(Color), typeof(Slider), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
            var slider = (Slider)b;
            if (n is Color c)
                slider.thumb.BackgroundColor = c;
            else
                slider.thumb.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.OnPrimary);
            slider.UpdateVisuals();
        }));
    /// <summary>Thumb fill color. When null, the theme OnPrimary token is used.</summary>
    public Color? ThumbColor { get => (Color?)GetValue(ThumbColorProperty); set => SetValue(ThumbColorProperty, value); }

    // ThumbWidth
    public static readonly BindableProperty ThumbWidthProperty = BindableProperty.Create(
        nameof(ThumbWidth), typeof(double), typeof(Slider), -1.0,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                ((Slider)b).Refresh();
            }));
    /// <summary>
    /// Thumb width. The default, <c>-1</c>, keeps the thumb square at <see cref="ThumbSize"/>. Set it
    /// when <see cref="ThumbTemplate"/> puts content in the thumb that is not square.
    /// </summary>
    public double ThumbWidth { get => (double)GetValue(ThumbWidthProperty); set => SetValue(ThumbWidthProperty, value); }

    // ThumbHeight
    public static readonly BindableProperty ThumbHeightProperty = BindableProperty.Create(
        nameof(ThumbHeight), typeof(double), typeof(Slider), -1.0,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                ((Slider)b).Refresh();
            }));
    /// <summary>Thumb height. The default, <c>-1</c>, keeps the thumb square at <see cref="ThumbSize"/>.</summary>
    public double ThumbHeight { get => (double)GetValue(ThumbHeightProperty); set => SetValue(ThumbHeightProperty, value); }

    // ThumbCornerRadius
    public static readonly BindableProperty ThumbCornerRadiusProperty = BindableProperty.Create(
        nameof(ThumbCornerRadius), typeof(double), typeof(Slider), -1.0,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                ((Slider)b).UpdateVisuals();
            }));
    /// <summary>
    /// Thumb corner radius. The default, <c>-1</c>, keeps the thumb fully rounded — a circle while it is
    /// square, a pill once it is not.
    /// </summary>
    public double ThumbCornerRadius { get => (double)GetValue(ThumbCornerRadiusProperty); set => SetValue(ThumbCornerRadiusProperty, value); }

    // ThumbPadding
    public static readonly BindableProperty ThumbPaddingProperty = BindableProperty.Create(
        nameof(ThumbPadding), typeof(Thickness), typeof(Slider), default(Thickness),
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                ((Slider)b).UpdateVisuals();
            }));
    /// <summary>Inset between the thumb's border and the content <see cref="ThumbTemplate"/> puts inside it.</summary>
    public Thickness ThumbPadding { get => (Thickness)GetValue(ThumbPaddingProperty); set => SetValue(ThumbPaddingProperty, value); }

    // ThumbTemplate
    public static readonly BindableProperty ThumbTemplateProperty = BindableProperty.Create(
        nameof(ThumbTemplate), typeof(DataTemplate), typeof(Slider), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                ((Slider)b).RebuildThumbContent();
            }));
    /// <summary>
    /// Content drawn inside the thumb — an icon, a glyph, the value itself. The template is realized once
    /// and its <c>BindingContext</c> is the current <see cref="Value"/>, rebound as the thumb moves, so a
    /// template that animates keeps its state across a drag. The thumb does not grow to fit: size it with
    /// <see cref="ThumbSize"/>, or <see cref="ThumbWidth"/>/<see cref="ThumbHeight"/>.
    /// </summary>
    public DataTemplate? ThumbTemplate { get => (DataTemplate?)GetValue(ThumbTemplateProperty); set => SetValue(ThumbTemplateProperty, value); }

    // ThumbBorderWidth
    public static readonly BindableProperty ThumbBorderWidthProperty = BindableProperty.Create(
        nameof(ThumbBorderWidth), typeof(double), typeof(Slider), 2.0,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                ((Slider)b).UpdateVisuals();
            }));
    public double ThumbBorderWidth { get => (double)GetValue(ThumbBorderWidthProperty); set => SetValue(ThumbBorderWidthProperty, value); }

    // ShowTooltip
    public static readonly BindableProperty ShowTooltipProperty = BindableProperty.Create(
        nameof(ShowTooltip), typeof(bool), typeof(Slider), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                ((Slider)b).Refresh();
            }));
    public bool ShowTooltip { get => (bool)GetValue(ShowTooltipProperty); set => SetValue(ShowTooltipProperty, value); }

    // TooltipBackgroundColor
    public static readonly BindableProperty TooltipBackgroundColorProperty = BindableProperty.Create(
        nameof(TooltipBackgroundColor), typeof(Color), typeof(Slider), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                ((Slider)b).UpdateVisuals();
            }));
    /// <summary>Tooltip badge background color. When null, the theme SurfaceVariant token is used.</summary>
    public Color? TooltipBackgroundColor { get => (Color?)GetValue(TooltipBackgroundColorProperty); set => SetValue(TooltipBackgroundColorProperty, value); }

    // TooltipTextColor
    public static readonly BindableProperty TooltipTextColorProperty = BindableProperty.Create(
        nameof(TooltipTextColor), typeof(Color), typeof(Slider), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                ((Slider)b).UpdateVisuals();
            }));
    /// <summary>Tooltip text color. When null, the theme OnSurfaceVariant token is used.</summary>
    public Color? TooltipTextColor { get => (Color?)GetValue(TooltipTextColorProperty); set => SetValue(TooltipTextColorProperty, value); }

    // TooltipFontSize
    public static readonly BindableProperty TooltipFontSizeProperty = BindableProperty.Create(
        nameof(TooltipFontSize), typeof(double), typeof(Slider), 12.0,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                ((Slider)b).Refresh();
            }));
    public double TooltipFontSize { get => (double)GetValue(TooltipFontSizeProperty); set => SetValue(TooltipFontSizeProperty, value); }

    // ValueFormat
    public static readonly BindableProperty ValueFormatProperty = BindableProperty.Create(
        nameof(ValueFormat), typeof(string), typeof(Slider), null);
    public string? ValueFormat { get => (string?)GetValue(ValueFormatProperty); set => SetValue(ValueFormatProperty, value); }

    // TooltipTemplate
    public static readonly BindableProperty TooltipTemplateProperty = BindableProperty.Create(
        nameof(TooltipTemplate), typeof(DataTemplate), typeof(Slider), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                ((Slider)b).UpdateVisuals();
            }));
    public DataTemplate? TooltipTemplate { get => (DataTemplate?)GetValue(TooltipTemplateProperty); set => SetValue(TooltipTemplateProperty, value); }

    // ---------------------------------------------------------------------------------------------
    // Orientation
    // ---------------------------------------------------------------------------------------------

    // Orientation
    public static readonly BindableProperty OrientationProperty = BindableProperty.Create(
        nameof(Orientation), typeof(SliderOrientation), typeof(Slider), SliderOrientation.Horizontal,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                // A tick is drawn across the track, so it swaps its axes with the slider.
                ((Slider)b).RebuildMarks();
            }));
    /// <summary>Which way the slider runs. Vertical puts <see cref="Minimum"/> at the bottom.</summary>
    public SliderOrientation Orientation { get => (SliderOrientation)GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }

    // VerticalLength
    public static readonly BindableProperty VerticalLengthProperty = BindableProperty.Create(
        nameof(VerticalLength), typeof(double), typeof(Slider), 220.0,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                ((Slider)b).Refresh();
            }));
    /// <summary>
    /// How long the track is when <see cref="Orientation"/> is vertical. A vertical slider has no width
    /// to stretch into, so it has to be told how tall to be.
    /// </summary>
    public double VerticalLength { get => (double)GetValue(VerticalLengthProperty); set => SetValue(VerticalLengthProperty, value); }


    // ---------------------------------------------------------------------------------------------
    // Marks
    // ---------------------------------------------------------------------------------------------

    // SnapToMarks
    public static readonly BindableProperty SnapToMarksProperty = BindableProperty.Create(
        nameof(SnapToMarks), typeof(bool), typeof(Slider), true);
    /// <summary>
    /// Whether the thumb comes to rest on the nearest <see cref="Marks">mark</see> — what makes a mark a
    /// stop point rather than a label. Set it false to keep the marks purely as reference points and let
    /// <see cref="Step"/> govern the value. Has no effect while there are no marks.
    /// </summary>
    public bool SnapToMarks { get => (bool)GetValue(SnapToMarksProperty); set => SetValue(SnapToMarksProperty, value); }

    // MarkShape
    public static readonly BindableProperty MarkShapeProperty = BindableProperty.Create(
        nameof(MarkShape), typeof(SliderMarkShape), typeof(Slider), SliderMarkShape.Dot,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                ((Slider)b).RebuildMarks();
            }));
    /// <summary>The shape every mark uses unless it sets <see cref="SliderMark.Shape"/> itself.</summary>
    public SliderMarkShape MarkShape { get => (SliderMarkShape)GetValue(MarkShapeProperty); set => SetValue(MarkShapeProperty, value); }

    // MarkSize
    public static readonly BindableProperty MarkSizeProperty = BindableProperty.Create(
        nameof(MarkSize), typeof(double), typeof(Slider), 10.0,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                ((Slider)b).RebuildMarks();
            }));
    /// <summary>Dot diameter, or tick thickness, for marks that do not set <see cref="SliderMark.Size"/>.</summary>
    public double MarkSize { get => (double)GetValue(MarkSizeProperty); set => SetValue(MarkSizeProperty, value); }

    // MarkColor
    public static readonly BindableProperty MarkColorProperty = BindableProperty.Create(
        nameof(MarkColor), typeof(Color), typeof(Slider), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                ((Slider)b).RebuildMarks();
            }));
    /// <summary>Fill for marks that do not set <see cref="SliderMark.Color"/>. Null uses the theme surface tokens.</summary>
    public Color? MarkColor { get => (Color?)GetValue(MarkColorProperty); set => SetValue(MarkColorProperty, value); }

    // MarkTextColor
    public static readonly BindableProperty MarkTextColorProperty = BindableProperty.Create(
        nameof(MarkTextColor), typeof(Color), typeof(Slider), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                ((Slider)b).RebuildMarks();
            }));
    /// <summary>Text colour for marks that do not set <see cref="SliderMark.TextColor"/>. Null uses the theme OnSurfaceVariant token.</summary>
    public Color? MarkTextColor { get => (Color?)GetValue(MarkTextColorProperty); set => SetValue(MarkTextColorProperty, value); }

    // MarkFontSize
    public static readonly BindableProperty MarkFontSizeProperty = BindableProperty.Create(
        nameof(MarkFontSize), typeof(double), typeof(Slider), 11.0,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                ((Slider)b).RebuildMarks();
            }));
    public double MarkFontSize { get => (double)GetValue(MarkFontSizeProperty); set => SetValue(MarkFontSizeProperty, value); }

    // ShowMarkLabels
    public static readonly BindableProperty ShowMarkLabelsProperty = BindableProperty.Create(
        nameof(ShowMarkLabels), typeof(bool), typeof(Slider), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(Slider), () =>
            {
                ((Slider)b).RebuildMarks();
            }));
    /// <summary>Whether dot and tick marks show their <see cref="SliderMark.Text"/> as a caption. A bubble always shows its text.</summary>
    public bool ShowMarkLabels { get => (bool)GetValue(ShowMarkLabelsProperty); set => SetValue(ShowMarkLabelsProperty, value); }


    // ValueChangedCommand
    public static readonly BindableProperty ValueChangedCommandProperty = BindableProperty.Create(
        nameof(ValueChangedCommand), typeof(ICommand), typeof(Slider));
    public ICommand? ValueChangedCommand { get => (ICommand?)GetValue(ValueChangedCommandProperty); set => SetValue(ValueChangedCommandProperty, value); }

    // Event
    public event EventHandler<double>? ValueChangedEvent;
}
