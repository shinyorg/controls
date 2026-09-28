using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Controls.Office.Icons;
using Shiny.Controls.Office.Shell;
using Shiny.Maui.Controls.Ribbons;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// The buttons at the far end of an Office ribbon's tab strip: Comments, the Editing / Reviewing /
/// Viewing mode dropdown, and Share.
/// </summary>
/// <remarks>
/// Made for <see cref="Ribbon.HeaderEndContent"/>. The mode dropdown opens through the ribbon's own
/// panel presenter, so it shares the ribbon's backdrop and closes when any other ribbon menu opens;
/// outside a ribbon it cycles to the next mode instead.
/// </remarks>
public class OfficeRibbonActions : ContentView
{
    readonly Border comments;
    readonly Border mode;
    readonly OfficeShellIconView modeIcon;
    readonly Label modeLabel;
    readonly Border share;
    readonly Label commentsLabel;
    readonly Label shareLabel;
    bool shellCompact;

    public static readonly BindableProperty ShowCommentsProperty = BindableProperty.Create(
        nameof(ShowComments), typeof(bool), typeof(OfficeRibbonActions), true, propertyChanged: (b, _, _) => ((OfficeRibbonActions)b).Apply());

    public static readonly BindableProperty IsCommentsOpenProperty = BindableProperty.Create(
        nameof(IsCommentsOpen), typeof(bool), typeof(OfficeRibbonActions), false, BindingMode.TwoWay, propertyChanged: (b, _, _) => ((OfficeRibbonActions)b).Apply());

    public static readonly BindableProperty ShowEditModeProperty = BindableProperty.Create(
        nameof(ShowEditMode), typeof(bool), typeof(OfficeRibbonActions), true, propertyChanged: (b, _, _) => ((OfficeRibbonActions)b).Apply());

    public static readonly BindableProperty EditModeProperty = BindableProperty.Create(
        nameof(EditMode), typeof(OfficeEditMode), typeof(OfficeRibbonActions), OfficeEditMode.Editing, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => ((OfficeRibbonActions)b).OnEditModeChanged((OfficeEditMode)n));

    public static readonly BindableProperty ShowShareProperty = BindableProperty.Create(
        nameof(ShowShare), typeof(bool), typeof(OfficeRibbonActions), true, propertyChanged: (b, _, _) => ((OfficeRibbonActions)b).Apply());

    public static readonly BindableProperty ShareCommandProperty = BindableProperty.Create(
        nameof(ShareCommand), typeof(ICommand), typeof(OfficeRibbonActions));

    public static readonly BindableProperty CompactProperty = BindableProperty.Create(
        nameof(Compact), typeof(bool?), typeof(OfficeRibbonActions), null, propertyChanged: (b, _, _) => ((OfficeRibbonActions)b).Apply());

    public static readonly BindableProperty ShareAccentProperty = BindableProperty.Create(
        nameof(ShareAccent), typeof(Color), typeof(OfficeRibbonActions), null, propertyChanged: (b, _, _) => ((OfficeRibbonActions)b).Apply());


    public OfficeRibbonActions()
    {
        this.comments = ShellChrome.TextButton("Comments", this.ToggleComments, OfficeShellIcon.Comments, null, out this.commentsLabel);

        this.modeIcon = new OfficeShellIconView { Icon = OfficeShellIcon.Editing, VerticalOptions = LayoutOptions.Center };
        this.modeLabel = ShellChrome.Text("Editing");
        var chevron = new OfficeShellIconView { Icon = OfficeShellIcon.ChevronDown, WidthRequest = 10, HeightRequest = 10, VerticalOptions = LayoutOptions.Center };
        var modeRow = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
        modeRow.Children.Add(this.modeIcon);
        modeRow.Children.Add(this.modeLabel);
        modeRow.Children.Add(chevron);
        this.mode = new Border
        {
            Content = modeRow,
            Padding = new Thickness(10, 5),
            StrokeThickness = 1,
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = 4 }
        };
        this.mode.SetDynamicResource(Border.StrokeProperty, ShinyThemeKeys.Brush.OutlineVariant);
        ShellChrome.Hint(this.mode, "Editing mode");
        ShellChrome.OnTap(this.mode, this.OpenModeMenu);

        this.share = ShellChrome.TextButton("Share", this.Share, OfficeShellIcon.Share, Colors.White, out this.shareLabel);

        var row = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
        row.Children.Add(this.comments);
        row.Children.Add(this.mode);
        row.Children.Add(this.share);
        this.Content = row;

        this.Apply();
    }


    public bool ShowComments { get => (bool)this.GetValue(ShowCommentsProperty); set => this.SetValue(ShowCommentsProperty, value); }

    /// <summary>Whether the comments pane is open — the button draws pressed. Two-way.</summary>
    public bool IsCommentsOpen { get => (bool)this.GetValue(IsCommentsOpenProperty); set => this.SetValue(IsCommentsOpenProperty, value); }

    public bool ShowEditMode { get => (bool)this.GetValue(ShowEditModeProperty); set => this.SetValue(ShowEditModeProperty, value); }

    /// <summary>Editing, Reviewing or Viewing. Two-way.</summary>
    public OfficeEditMode EditMode { get => (OfficeEditMode)this.GetValue(EditModeProperty); set => this.SetValue(EditModeProperty, value); }

    public bool ShowShare { get => (bool)this.GetValue(ShowShareProperty); set => this.SetValue(ShowShareProperty, value); }

    /// <summary>
    /// Draws the three as icons only. Null follows the enclosing shell's compact layout - on a phone
    /// the labelled buttons are wider than the tab strip they share a row with, and the tabs get none.
    /// </summary>
    public bool? Compact { get => (bool?)this.GetValue(CompactProperty); set => this.SetValue(CompactProperty, value); }

    /// <summary>The shell's compact layout, pushed by <see cref="OfficeShell"/>; <see cref="Compact"/> overrides it.</summary>
    internal void InheritCompact(bool compact)
    {
        if (this.shellCompact == compact)
            return;

        this.shellCompact = compact;
        this.Apply();
    }

    bool IconsOnly => this.Compact ?? this.shellCompact;

    public ICommand? ShareCommand { get => (ICommand?)this.GetValue(ShareCommandProperty); set => this.SetValue(ShareCommandProperty, value); }

    /// <summary>The Share button's fill. Null follows the enclosing shell's app accent (Word blue outside one).</summary>
    public Color? ShareAccent { get => (Color?)this.GetValue(ShareAccentProperty); set => this.SetValue(ShareAccentProperty, value); }

    public event EventHandler? CommentsClicked;
    public event EventHandler<OfficeEditMode>? EditModeChanged;
    public event EventHandler? ShareClicked;


    /// <summary>Toggles <see cref="IsCommentsOpen"/> and raises <see cref="CommentsClicked"/>.</summary>
    public void ToggleComments()
    {
        this.IsCommentsOpen = !this.IsCommentsOpen;
        this.CommentsClicked?.Invoke(this, EventArgs.Empty);
    }


    public void Share()
    {
        if (this.ShareCommand?.CanExecute(null) == true)
            this.ShareCommand.Execute(null);

        this.ShareClicked?.Invoke(this, EventArgs.Empty);
    }


    /// <summary>Picks a mode as the dropdown would.</summary>
    public void SelectMode(OfficeEditMode mode)
    {
        ShellChrome.Ancestor<Ribbon>(this)?.CloseMenu();
        this.EditMode = mode;
    }


    void OpenModeMenu()
    {
        if (ShellChrome.Ancestor<Ribbon>(this) is not { } ribbon)
        {
            var all = OfficeEditModes.All;
            this.SelectMode(all[(all.ToList().IndexOf(this.EditMode) + 1) % all.Count]);
            return;
        }

        var list = new VerticalStackLayout { Spacing = 0, WidthRequest = 260 };
        foreach (var m in OfficeEditModes.All)
        {
            var mode = m;
            var icon = new OfficeShellIconView { Icon = OfficeEditModes.Icon(mode), VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 2, 0, 0) };
            var texts = new VerticalStackLayout { Spacing = 0 };
            texts.Children.Add(ShellChrome.Text(OfficeEditModes.Title(mode), 13, attributes: FontAttributes.Bold));
            texts.Children.Add(ShellChrome.Text(OfficeEditModes.Description(mode), 12, ShinyThemeKeys.Color.OnSurfaceVariant));
            var line = new HorizontalStackLayout { Spacing = 10, Padding = new Thickness(10, 6) };
            line.Children.Add(icon);
            line.Children.Add(texts);
            var entry = new Border { Content = line, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 4 } };
            if (mode == this.EditMode)
                entry.SetDynamicResource(BackgroundColorProperty, ShinyThemeKeys.Color.SecondaryContainer);
            else
                entry.BackgroundColor = Colors.Transparent;

            ShellChrome.OnTap(entry, () => this.SelectMode(mode));
            list.Children.Add(entry);
        }

        ribbon.Present(this.mode, null, list);
    }


    void OnEditModeChanged(OfficeEditMode mode)
    {
        this.Apply();
        this.EditModeChanged?.Invoke(this, mode);
    }


    protected override void OnParentSet()
    {
        base.OnParentSet();
        this.Apply();
    }


    void Apply()
    {
        if (this.share is null)
            return;

        var iconsOnly = this.IconsOnly;
        this.commentsLabel.IsVisible = !iconsOnly;
        this.modeLabel.IsVisible = !iconsOnly;
        this.shareLabel.IsVisible = !iconsOnly;

        this.comments.IsVisible = this.ShowComments;
        if (this.IsCommentsOpen)
            this.comments.SetDynamicResource(BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHighest);
        else
        {
            this.comments.RemoveDynamicResource(BackgroundColorProperty);
            this.comments.BackgroundColor = Colors.Transparent;
        }

        this.mode.IsVisible = this.ShowEditMode;
        this.modeIcon.Icon = OfficeEditModes.Icon(this.EditMode);
        this.modeLabel.Text = OfficeEditModes.Title(this.EditMode);

        this.share.IsVisible = this.ShowShare;
        var app = ShellChrome.Ancestor<OfficeShell>(this)?.App ?? OfficeApp.Word;
        this.share.BackgroundColor = this.ShareAccent ?? OfficeAppInfo.For(app).Accent.Color.ToColor();
    }
}
