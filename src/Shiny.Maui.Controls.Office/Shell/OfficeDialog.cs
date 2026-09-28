using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Controls.Office.Shell;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// An Office-style modal dialog: a scrim, a centred card with a title, any content, and OK / Cancel.
/// </summary>
/// <remarks>
/// <para>
/// Why not the core <c>IDialogService</c>: that one is alert / confirm / prompt, and an Office dialog
/// (Zoom, Paragraph, Page Setup) is arbitrary content with OK and Cancel under it.
/// </para>
/// <para>
/// Place it over whatever it should cover — <see cref="OfficeShell"/> hosts one for its zoom dialog — and
/// flip <see cref="IsOpen"/>. Everything is built in the constructor and only shown or hidden, because the
/// AppKit head never realises a child added after the page was laid out.
/// </para>
/// </remarks>
[ContentProperty(nameof(DialogContent))]
public class OfficeDialog : ContentView
{
    readonly Label title;
    readonly ContentView body;
    readonly Border cancel;
    readonly Label okLabel;
    readonly Label cancelLabel;

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(OfficeDialog), null,
        propertyChanged: (b, _, n) => { if (((OfficeDialog)b).title is { } t) t.Text = (string?)n; });

    public static readonly BindableProperty DialogContentProperty = BindableProperty.Create(
        nameof(DialogContent), typeof(View), typeof(OfficeDialog), null,
        propertyChanged: (b, _, n) => { if (((OfficeDialog)b).body is { } h) h.Content = (View?)n; });

    public static readonly BindableProperty OkTextProperty = BindableProperty.Create(
        nameof(OkText), typeof(string), typeof(OfficeDialog), "OK",
        propertyChanged: (b, _, n) => { if (((OfficeDialog)b).okLabel is { } l) l.Text = (string)n; });

    public static readonly BindableProperty CancelTextProperty = BindableProperty.Create(
        nameof(CancelText), typeof(string), typeof(OfficeDialog), "Cancel",
        propertyChanged: (b, _, n) => { if (((OfficeDialog)b).cancelLabel is { } l) l.Text = (string)n; });

    public static readonly BindableProperty ShowCancelProperty = BindableProperty.Create(
        nameof(ShowCancel), typeof(bool), typeof(OfficeDialog), true,
        propertyChanged: (b, _, n) => { if (((OfficeDialog)b).cancel is { } c) c.IsVisible = (bool)n; });

    public static readonly BindableProperty IsOpenProperty = BindableProperty.Create(
        nameof(IsOpen), typeof(bool), typeof(OfficeDialog), false, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => ((OfficeDialog)b).IsVisible = (bool)n);

    public static readonly BindableProperty DialogWidthProperty = BindableProperty.Create(
        nameof(DialogWidth), typeof(double), typeof(OfficeDialog), 360d);


    public OfficeDialog()
    {
        this.IsVisible = false;

        var scrim = new BoxView { Color = Color.FromRgba(0f, 0f, 0f, 0.4f) };
        ShellChrome.OnTap(scrim, this.Cancel);

        this.title = ShellChrome.Text(null, 17, attributes: FontAttributes.Bold);
        this.body = new ContentView();

        var ok = MakeButton("OK", this.Accept, primary: true, out this.okLabel);
        this.cancel = MakeButton("Cancel", this.Cancel, primary: false, out this.cancelLabel);

        var buttons = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.End };
        buttons.Children.Add(ok);
        buttons.Children.Add(this.cancel);

        var stack = new VerticalStackLayout { Spacing = 14 };
        stack.Children.Add(this.title);
        stack.Children.Add(this.body);
        stack.Children.Add(buttons);

        var card = new Border
        {
            Content = stack,
            Padding = 20,
            StrokeThickness = 1,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = 8 }
        };
        card.SetBinding(WidthRequestProperty, static (OfficeDialog d) => d.DialogWidth, source: this);
        card.SetDynamicResource(Border.StrokeProperty, ShinyThemeKeys.Brush.OutlineVariant);
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHigh);
        card.Shadow = new Shadow { Radius = 18, Opacity = 0.3f, Offset = new Point(0, 6), Brush = Colors.Black };

        var root = new Grid();
        root.Children.Add(scrim);
        root.Children.Add(card);
        this.Content = root;
    }


    public string? Title
    {
        get => (string?)this.GetValue(TitleProperty);
        set => this.SetValue(TitleProperty, value);
    }

    /// <summary>The dialog's body.</summary>
    public View? DialogContent
    {
        get => (View?)this.GetValue(DialogContentProperty);
        set => this.SetValue(DialogContentProperty, value);
    }

    public string OkText
    {
        get => (string)this.GetValue(OkTextProperty);
        set => this.SetValue(OkTextProperty, value);
    }

    public string CancelText
    {
        get => (string)this.GetValue(CancelTextProperty);
        set => this.SetValue(CancelTextProperty, value);
    }

    public bool ShowCancel
    {
        get => (bool)this.GetValue(ShowCancelProperty);
        set => this.SetValue(ShowCancelProperty, value);
    }

    /// <summary>Shown while true. Two-way.</summary>
    public bool IsOpen
    {
        get => (bool)this.GetValue(IsOpenProperty);
        set => this.SetValue(IsOpenProperty, value);
    }

    public double DialogWidth
    {
        get => (double)this.GetValue(DialogWidthProperty);
        set => this.SetValue(DialogWidthProperty, value);
    }

    /// <summary>OK was pressed. The dialog has already closed.</summary>
    public event EventHandler? Accepted;

    /// <summary>Cancel, the scrim, or <see cref="Cancel"/> closed it.</summary>
    public event EventHandler? Cancelled;


    /// <summary>Presses OK. Overridden by dialogs that validate or resolve before closing.</summary>
    public virtual void Accept()
    {
        this.IsOpen = false;
        this.Accepted?.Invoke(this, EventArgs.Empty);
    }


    public virtual void Cancel()
    {
        if (!this.IsOpen)
            return;

        this.IsOpen = false;
        this.Cancelled?.Invoke(this, EventArgs.Empty);
    }


    static Border MakeButton(string text, Action action, bool primary, out Label label)
    {
        label = new Label { Text = text, FontSize = 13, HorizontalTextAlignment = TextAlignment.Center };
        label.SetDynamicResource(Label.TextColorProperty, primary ? ShinyThemeKeys.Color.OnPrimary : ShinyThemeKeys.Color.OnSurface);

        var border = new Border
        {
            Content = label,
            Padding = new Thickness(18, 6),
            MinimumWidthRequest = 80,
            StrokeThickness = primary ? 0 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = 4 }
        };
        border.SetDynamicResource(Border.StrokeProperty, ShinyThemeKeys.Brush.Outline);
        border.SetDynamicResource(VisualElement.BackgroundColorProperty, primary ? ShinyThemeKeys.Color.Primary : ShinyThemeKeys.Color.Surface);
        ShellChrome.OnTap(border, action);
        return border;
    }
}


/// <summary>
/// Word's Zoom dialog: 200% / 100% / 75% / Page width / Text width / Whole page, or a typed percent.
/// </summary>
/// <remarks>
/// The fit presets need the page and the viewport; they are disabled while those are zero, which is
/// what a status bar that was never told them passes on.
/// </remarks>
public class OfficeZoomDialog : OfficeDialog
{
    readonly List<(OfficeZoomPreset Preset, RadioButton Radio)> radios = [];
    readonly RadioButton customRadio;
    readonly Entry percent;
    OfficeStatusBar? source;
    readonly string group = $"officezoom-{Guid.NewGuid():N}";

    public OfficeZoomDialog()
    {
        this.Title = "Zoom";
        this.DialogWidth = 320;

        var stack = new VerticalStackLayout { Spacing = 2 };

        foreach (var (preset, text) in OfficeZoomModel.Presets)
        {
            var radio = new RadioButton { Content = text, GroupName = this.group, FontSize = 13 };
            radio.SetDynamicResource(RadioButton.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
            this.radios.Add((preset, radio));
            stack.Children.Add(radio);
        }

        this.customRadio = new RadioButton { Content = "Percent:", GroupName = this.group, FontSize = 13 };
        this.customRadio.SetDynamicResource(RadioButton.TextColorProperty, ShinyThemeKeys.Color.OnSurface);

        this.percent = new Entry { WidthRequest = 90, FontSize = 13, Keyboard = Keyboard.Numeric };
        this.percent.SetDynamicResource(Entry.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
        this.percent.Focused += (_, _) => this.customRadio.IsChecked = true;

        var custom = new HorizontalStackLayout { Spacing = 8 };
        custom.Children.Add(this.customRadio);
        custom.Children.Add(this.percent);
        stack.Children.Add(custom);

        this.DialogContent = stack;
    }


    public OfficeZoomModel ZoomModel { get; set; } = OfficeZoomModel.Default;

    public double CurrentZoom { get; private set; } = 1;

    public double PageWidth { get; set; }
    public double PageHeight { get; set; }
    public double TextWidth { get; set; }
    public double ViewportWidth { get; set; }
    public double ViewportHeight { get; set; }

    /// <summary>The zoom OK resolved to. When opened from a status bar, its Zoom is set too.</summary>
    public event EventHandler<double>? ZoomSelected;


    /// <summary>Opens on <paramref name="currentZoom"/>, taking page and viewport sizes from the status bar when given.</summary>
    public void Open(double currentZoom, OfficeStatusBar? source = null)
    {
        this.source = source;
        this.CurrentZoom = currentZoom;

        if (source is not null)
        {
            this.ZoomModel = source.ZoomModel;
            this.PageWidth = source.PageWidth;
            this.PageHeight = source.PageHeight;
            this.TextWidth = source.TextWidth;
            this.ViewportWidth = source.ViewportWidth;
            this.ViewportHeight = source.ViewportHeight;
        }

        var canFit = this.PageWidth > 0 && this.ViewportWidth > 0;
        var canFitPage = canFit && this.PageHeight > 0 && this.ViewportHeight > 0;

        var matched = false;
        foreach (var (preset, radio) in this.radios)
        {
            radio.IsEnabled = preset switch
            {
                OfficeZoomPreset.PageWidth or OfficeZoomPreset.TextWidth => canFit,
                OfficeZoomPreset.WholePage => canFitPage,
                _ => true
            };

            var hit = preset switch
            {
                OfficeZoomPreset.Percent200 => Math.Abs(currentZoom - 2) < 0.001,
                OfficeZoomPreset.Percent100 => Math.Abs(currentZoom - 1) < 0.001,
                OfficeZoomPreset.Percent75 => Math.Abs(currentZoom - 0.75) < 0.001,
                _ => false
            };
            radio.IsChecked = hit;
            matched |= hit;
        }

        this.customRadio.IsChecked = !matched;
        this.percent.Text = Math.Round(currentZoom * 100).ToString("0", CultureInfo.CurrentCulture);
        this.IsOpen = true;
    }


    /// <summary>Picks a preset as clicking its radio would. Test seam.</summary>
    public void Choose(OfficeZoomPreset preset)
    {
        if (preset == OfficeZoomPreset.Custom)
        {
            this.customRadio.IsChecked = true;
            return;
        }

        foreach (var (p, radio) in this.radios)
            radio.IsChecked = p == preset;
    }


    /// <summary>Sets the typed percent. Test seam.</summary>
    public void SetPercent(string text)
    {
        this.percent.Text = text;
        this.customRadio.IsChecked = true;
    }


    /// <summary>The zoom the current choice resolves to.</summary>
    public double Resolve()
    {
        var preset = this.radios.FirstOrDefault(x => x.Radio.IsChecked).Preset;
        var chosen = this.radios.Any(x => x.Radio.IsChecked) ? preset : OfficeZoomPreset.Custom;

        var custom = OfficeZoomModel.TryParse(this.percent.Text, out var typed) ? typed : this.CurrentZoom;

        return this.ZoomModel.Resolve(
            chosen,
            this.PageWidth,
            this.PageHeight,
            this.ViewportWidth,
            this.ViewportHeight,
            this.TextWidth,
            custom);
    }


    public override void Accept()
    {
        var zoom = this.Resolve();
        base.Accept();

        if (this.source is not null)
            this.source.Zoom = zoom;

        this.ZoomSelected?.Invoke(this, zoom);
        this.source = null;
    }
}
