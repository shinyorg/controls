using Shiny.Controls.Office.Shell;
using Shiny.Maui.Controls.Ribbons;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// Word's Styles gallery: "AaBbCcDd" previews drawn in each style's own font, size and colour, the
/// style under the caret highlighted, stepping a row at a time in the ribbon and expanding to a grid.
/// </summary>
/// <example>
/// <code>
/// var styles = new OfficeStyleGallery();
/// styles.StyleSelected += (_, s) => editor.Controller?.ApplyStyle(s.Id);
/// group.Items.Add(styles);
/// // as the caret moves:
/// styles.SelectedStyleId = controller.CurrentStyleId;
/// </code>
/// </example>
/// <remarks>A <see cref="RibbonGallery"/> over <see cref="OfficeStyleDescriptor"/>s; applying the style is the editor's job.</remarks>
public class OfficeStyleGallery : RibbonGallery
{
    bool syncing;

    public static readonly BindableProperty StylesProperty = BindableProperty.Create(
        nameof(Styles), typeof(IReadOnlyList<OfficeStyleDescriptor>), typeof(OfficeStyleGallery), null,
        propertyChanged: (b, _, n) => ((OfficeStyleGallery)b).ItemsSource = (IReadOnlyList<OfficeStyleDescriptor>?)n ?? OfficeStyleDescriptors.Word);

    public static readonly BindableProperty SelectedStyleIdProperty = BindableProperty.Create(
        nameof(SelectedStyleId), typeof(string), typeof(OfficeStyleGallery), null, BindingMode.TwoWay,
        propertyChanged: (b, _, _) => ((OfficeStyleGallery)b).SyncFromId());


    public OfficeStyleGallery()
    {
        this.Text = "Styles";
        this.Tooltip = "Styles";
        this.ItemWidth = 78;
        this.ItemHeight = 60;
        this.ItemText = x => (x as OfficeStyleDescriptor)?.Name;
        this.ItemTemplate = new DataTemplate(() => new StylePreview());
        this.ItemsSource = OfficeStyleDescriptors.Word;

        this.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(this.SelectedItem) && !this.syncing)
            {
                this.syncing = true;
                this.SelectedStyleId = (this.SelectedItem as OfficeStyleDescriptor)?.Id;
                this.syncing = false;
            }
        };

        this.ItemSelected += (_, e) =>
        {
            if (e.Item is OfficeStyleDescriptor style)
                this.StyleSelected?.Invoke(this, style);
        };
    }


    /// <summary>The styles offered. Null (the default) is Word's built-in quick styles.</summary>
    public IReadOnlyList<OfficeStyleDescriptor>? Styles
    {
        get => (IReadOnlyList<OfficeStyleDescriptor>?)this.GetValue(StylesProperty);
        set => this.SetValue(StylesProperty, value);
    }

    /// <summary>The highlighted style's id — the style under the caret. Two-way.</summary>
    public string? SelectedStyleId
    {
        get => (string?)this.GetValue(SelectedStyleIdProperty);
        set => this.SetValue(SelectedStyleIdProperty, value);
    }

    /// <summary>A style was picked. Apply it by <see cref="OfficeStyleDescriptor.Id"/>.</summary>
    public event EventHandler<OfficeStyleDescriptor>? StyleSelected;


    /// <summary>Picks the style with this id as a tap would. Test seam.</summary>
    public void PickStyle(string id)
    {
        var list = this.Styles ?? OfficeStyleDescriptors.Word;
        var index = OfficeStyleDescriptors.IndexOf(list, id);
        if (index >= 0)
            this.Pick(list[index]);
    }


    void SyncFromId()
    {
        if (this.syncing)
            return;

        var list = this.Styles ?? OfficeStyleDescriptors.Word;
        var index = OfficeStyleDescriptors.IndexOf(list, this.SelectedStyleId);

        this.syncing = true;
        this.SelectedItem = index >= 0 ? list[index] : null;
        this.syncing = false;
    }


    /// <summary>One swatch: the sample in the style's own look, its name underneath.</summary>
    sealed class StylePreview : VerticalStackLayout
    {
        readonly Label sample = new()
        {
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.NoWrap,
            HeightRequest = 32
        };

        readonly Label caption = new()
        {
            FontSize = 10,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        public StylePreview()
        {
            this.Spacing = 2;
            this.VerticalOptions = LayoutOptions.Center;
            this.caption.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
            this.Children.Add(this.sample);
            this.Children.Add(this.caption);
        }

        protected override void OnBindingContextChanged()
        {
            base.OnBindingContextChanged();
            if (this.BindingContext is not OfficeStyleDescriptor style)
                return;

            this.sample.Text = style.Sample;
            this.sample.FontFamily = style.FontFamily;
            this.sample.FontSize = style.PreviewSize;
            this.sample.FontAttributes = (style.Bold ? FontAttributes.Bold : FontAttributes.None) | (style.Italic ? FontAttributes.Italic : FontAttributes.None);
            this.sample.TextDecorations = style.Underline ? TextDecorations.Underline : TextDecorations.None;

            if (style.Color is { } color)
                this.sample.TextColor = color.ToColor();
            else
                this.sample.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurface);

            this.caption.Text = style.Name;
        }
    }
}
