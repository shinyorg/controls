using System.Windows.Input;
using Shiny.Controls.Office.Icons;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// A titled side pane with a close button — the frame around the Comments pane, the Reviewing pane,
/// or anything else an editor docks beside the page.
/// </summary>
/// <remarks>The frame only: what goes in it is the editor's. Put it in <see cref="OfficeShell.RightPane"/>.</remarks>
[ContentProperty(nameof(PaneContent))]
public class OfficeSidePane : ContentView
{
    readonly Label title;
    readonly ContentView body;
    readonly ContentView header;
    readonly Border close;

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(OfficeSidePane), null,
        propertyChanged: (b, _, n) => { if (((OfficeSidePane)b).title is { } l) l.Text = (string?)n; });

    public static readonly BindableProperty PaneContentProperty = BindableProperty.Create(
        nameof(PaneContent), typeof(View), typeof(OfficeSidePane), null,
        propertyChanged: (b, _, n) => { if (((OfficeSidePane)b).body is { } h) h.Content = (View?)n; });

    public static readonly BindableProperty HeaderContentProperty = BindableProperty.Create(
        nameof(HeaderContent), typeof(View), typeof(OfficeSidePane), null,
        propertyChanged: (b, _, n) => { if (((OfficeSidePane)b).header is { } h) h.Content = (View?)n; });

    public static readonly BindableProperty ShowCloseProperty = BindableProperty.Create(
        nameof(ShowClose), typeof(bool), typeof(OfficeSidePane), true,
        propertyChanged: (b, _, n) => { if (((OfficeSidePane)b).close is { } c) c.IsVisible = (bool)n; });

    public static readonly BindableProperty CloseCommandProperty = BindableProperty.Create(
        nameof(CloseCommand), typeof(ICommand), typeof(OfficeSidePane));


    public OfficeSidePane()
    {
        this.SetDynamicResource(BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerLow);

        this.title = ShellChrome.Text(null, 15, attributes: FontAttributes.Bold);
        this.header = new ContentView { VerticalOptions = LayoutOptions.Center };
        this.close = ShellChrome.IconButton(OfficeShellIcon.Close, "Close", this.Close, size: 14);
        this.body = new ContentView();

        var top = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) },
            Padding = new Thickness(12, 8, 6, 8)
        };
        top.Add(this.title, 0);
        top.Add(this.header, 1);
        top.Add(this.close, 2);

        var stack = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        stack.Add(top, 0, 0);
        stack.Add(this.body, 0, 1);

        var root = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        root.Add(ShellChrome.Rule(vertical: true), 0);
        root.Add(stack, 1);
        this.Content = root;
    }


    public string? Title { get => (string?)this.GetValue(TitleProperty); set => this.SetValue(TitleProperty, value); }

    /// <summary>The pane's body.</summary>
    public View? PaneContent { get => (View?)this.GetValue(PaneContentProperty); set => this.SetValue(PaneContentProperty, value); }

    /// <summary>Extra buttons beside the title — "New comment", a filter.</summary>
    public View? HeaderContent { get => (View?)this.GetValue(HeaderContentProperty); set => this.SetValue(HeaderContentProperty, value); }

    public bool ShowClose { get => (bool)this.GetValue(ShowCloseProperty); set => this.SetValue(ShowCloseProperty, value); }

    public ICommand? CloseCommand { get => (ICommand?)this.GetValue(CloseCommandProperty); set => this.SetValue(CloseCommandProperty, value); }

    /// <summary>The close button was pressed. Inside an <see cref="OfficeShell"/> the pane is also closed for you.</summary>
    public event EventHandler? CloseRequested;


    /// <summary>Presses the close button.</summary>
    public void Close()
    {
        if (this.CloseCommand?.CanExecute(null) == true)
            this.CloseCommand.Execute(null);

        this.CloseRequested?.Invoke(this, EventArgs.Empty);
        ShellChrome.Ancestor<OfficeShell>(this)?.ClosePane(this);
    }
}
