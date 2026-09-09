using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Shiny.Maui.Controls;

/// <summary>
/// One action, submenu or separator on a <see cref="FloatingToolbar"/>.
/// </summary>
/// <remarks>
/// Named <c>ShinyToolbarItem</c> rather than <c>ToolbarItem</c> because MAUI already has a
/// <see cref="Microsoft.Maui.Controls.ToolbarItem"/>, and a second type of that name in a namespace
/// XAML imports wholesale resolves to whichever the compiler saw first. The Blazor half calls the
/// same shape <c>ToolbarItem</c>, where nothing collides.
/// <para>
/// A <see cref="BindableObject"/> rather than a plain class: XAML's <c>{Binding}</c> only attaches to
/// a <see cref="BindableProperty"/>, so a POCO here would fail to compile with MAUIX2002 the moment
/// anyone bound <see cref="IsEnabled"/> to a view model.
/// </para>
/// </remarks>
public class ShinyToolbarItem : BindableObject
{
    public ShinyToolbarItem()
    {
        this.Children = new ObservableCollection<ShinyToolbarItem>();
    }


    /// <summary>The glyph or picture. A <c>FontImageSource</c> covers the icon-font case.</summary>
    public static readonly BindableProperty IconProperty = BindableProperty.Create(
        nameof(Icon), typeof(ImageSource), typeof(ShinyToolbarItem));

    /// <inheritdoc cref="IconProperty" />
    public ImageSource? Icon
    {
        get => (ImageSource?)this.GetValue(IconProperty);
        set => this.SetValue(IconProperty, value);
    }


    /// <summary>Label beside or under the icon, and the accessible name when there is no icon.</summary>
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(ShinyToolbarItem));

    /// <inheritdoc cref="TextProperty" />
    public string? Text
    {
        get => (string?)this.GetValue(TextProperty);
        set => this.SetValue(TextProperty, value);
    }


    /// <summary>Hover text. Falls back to <see cref="Text"/>.</summary>
    public static readonly BindableProperty TooltipProperty = BindableProperty.Create(
        nameof(Tooltip), typeof(string), typeof(ShinyToolbarItem));

    /// <inheritdoc cref="TooltipProperty" />
    public string? Tooltip
    {
        get => (string?)this.GetValue(TooltipProperty);
        set => this.SetValue(TooltipProperty, value);
    }


    /// <summary>Small count or flag drawn on the item's corner.</summary>
    public static readonly BindableProperty BadgeProperty = BindableProperty.Create(
        nameof(Badge), typeof(string), typeof(ShinyToolbarItem));

    /// <inheritdoc cref="BadgeProperty" />
    public string? Badge
    {
        get => (string?)this.GetValue(BadgeProperty);
        set => this.SetValue(BadgeProperty, value);
    }


    /// <summary>Overrides the toolbar's foreground for this item alone.</summary>
    public static readonly BindableProperty IconColorProperty = BindableProperty.Create(
        nameof(IconColor), typeof(Color), typeof(ShinyToolbarItem));

    /// <inheritdoc cref="IconColorProperty" />
    public Color? IconColor
    {
        get => (Color?)this.GetValue(IconColorProperty);
        set => this.SetValue(IconColorProperty, value);
    }


    /// <summary>Dimmed and unclickable when false.</summary>
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.Create(
        nameof(IsEnabled), typeof(bool), typeof(ShinyToolbarItem), true);

    /// <inheritdoc cref="IsEnabledProperty" />
    public bool IsEnabled
    {
        get => (bool)this.GetValue(IsEnabledProperty);
        set => this.SetValue(IsEnabledProperty, value);
    }


    /// <summary>Hides the item entirely, without taking it out of the collection.</summary>
    public static readonly BindableProperty IsVisibleProperty = BindableProperty.Create(
        nameof(IsVisible), typeof(bool), typeof(ShinyToolbarItem), true);

    /// <inheritdoc cref="IsVisibleProperty" />
    public bool IsVisible
    {
        get => (bool)this.GetValue(IsVisibleProperty);
        set => this.SetValue(IsVisibleProperty, value);
    }


    /// <summary>
    /// A divider line inside a menu. Icon, text and children are ignored; one that lands on the bar
    /// itself is drawn as a thin rule across the bar's cross axis.
    /// </summary>
    public static readonly BindableProperty IsSeparatorProperty = BindableProperty.Create(
        nameof(IsSeparator), typeof(bool), typeof(ShinyToolbarItem), false);

    /// <inheritdoc cref="IsSeparatorProperty" />
    public bool IsSeparator
    {
        get => (bool)this.GetValue(IsSeparatorProperty);
        set => this.SetValue(IsSeparatorProperty, value);
    }


    /// <summary>Raised in place of <see cref="FloatingToolbar.ItemClicked"/> when set.</summary>
    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command), typeof(ICommand), typeof(ShinyToolbarItem));

    /// <inheritdoc cref="CommandProperty" />
    public ICommand? Command
    {
        get => (ICommand?)this.GetValue(CommandProperty);
        set => this.SetValue(CommandProperty, value);
    }


    /// <inheritdoc cref="CommandProperty" />
    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(
        nameof(CommandParameter), typeof(object), typeof(ShinyToolbarItem));

    /// <inheritdoc cref="CommandParameterProperty" />
    public object? CommandParameter
    {
        get => this.GetValue(CommandParameterProperty);
        set => this.SetValue(CommandParameterProperty, value);
    }


    /// <summary>Anything of your own, handed back on <see cref="FloatingToolbar.ItemClicked"/>.</summary>
    public object? Tag { get; set; }


    /// <summary>
    /// Turns the item into a menu button: it opens a dropdown instead of raising a click, and those
    /// children may have children of their own, which fly out as submenus.
    /// </summary>
    public IList<ShinyToolbarItem> Children { get; }


    /// <summary>True when the item opens a dropdown rather than acting.</summary>
    public bool HasChildren => !this.IsSeparator && this.Children.Count > 0;
}
