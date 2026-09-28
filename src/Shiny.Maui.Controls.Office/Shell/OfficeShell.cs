using System.ComponentModel;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Controls.Office.Icons;
using Shiny.Controls.Office.Shell;
using Shiny.Maui.Controls.Ribbons;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// The Office application window around an editor: title bar, ribbon, rulers, left and right panes,
/// the editor itself, status bar, and the File backstage over all of it.
/// </summary>
/// <example>
/// <code>
/// var shell = new OfficeShell
/// {
///     App = OfficeApp.Word,
///     TitleBar = new OfficeTitleBar { DocumentName = "Report" },
///     Ribbon = editorView.Ribbon,
///     StatusBar = new OfficeStatusBar(),
///     Backstage = new OfficeBackstage(),
///     ShellContent = editor
/// };
/// </code>
/// </example>
/// <remarks>
/// <para>
/// Slots, not a template: each region is a view the host hands over, and the shell only lays them out
/// and wires the obvious connections — the ribbon's File button opens the backstage, the status bar's
/// Focus button toggles focus mode, the status bar's percentage opens the shell's zoom dialog, and the
/// shell's <see cref="App"/> reaches a title bar, status bar and backstage that did not set their own.
/// </para>
/// <para>
/// Every slot host, the overlay layer, the focus-exit pill and the zoom dialog are built in the
/// constructor; setting a slot only sets a <c>ContentView.Content</c>. The AppKit head never realises a
/// child added after the page was laid out, so a shell that grew rows on demand would be blank there.
/// </para>
/// <para>
/// Responsive by <see cref="CompactWidth"/> (600): below it rulers and side panes hide (their open state
/// is kept), the title bar's search becomes an icon, the status bar drops its slider, and a ribbon in
/// the Ribbon slot goes simplified.
/// </para>
/// </remarks>
[ContentProperty(nameof(ShellContent))]
public class OfficeShell : ContentView
{
    readonly Grid root;
    readonly ContentView titleHost = new();
    readonly ContentView ribbonHost = new();
    readonly ContentView rulerHost = new();
    readonly ContentView verticalRulerHost = new();
    readonly ContentView leftHost = new();
    readonly ContentView contentHost = new();
    readonly ContentView rightHost = new();
    readonly ContentView statusHost = new();
    readonly ContentView backstageHost = new();
    readonly Grid overlay;
    readonly Border focusExit;
    readonly OfficeZoomDialog zoomDialog = new();
    bool shellSimplifiedRibbon;
    bool syncingBackstage;

    static BindableProperty Slot(string name)
        => BindableProperty.Create(name, typeof(View), typeof(OfficeShell), null,
            propertyChanged: (b, o, n) => { if (((OfficeShell)b).root is not null) ((OfficeShell)b).OnSlotChanged(name, (View?)o, (View?)n); });

    static BindableProperty Flag(string name, bool value, BindingMode mode = BindingMode.OneWay)
        => BindableProperty.Create(name, typeof(bool), typeof(OfficeShell), value, mode,
            propertyChanged: (b, _, _) => { if (((OfficeShell)b).root is not null) ((OfficeShell)b).ApplyLayout(); });

    public static readonly BindableProperty AppProperty = BindableProperty.Create(
        nameof(App), typeof(OfficeApp), typeof(OfficeShell), OfficeApp.Word,
        propertyChanged: (b, _, _) => { if (((OfficeShell)b).root is not null) ((OfficeShell)b).PushApp(); });

    public static readonly BindableProperty TitleBarProperty = Slot(nameof(TitleBar));
    public static readonly BindableProperty RibbonProperty = Slot(nameof(Ribbon));
    public static readonly BindableProperty RulerProperty = Slot(nameof(Ruler));
    public static readonly BindableProperty VerticalRulerProperty = Slot(nameof(VerticalRuler));
    public static readonly BindableProperty LeftPaneProperty = Slot(nameof(LeftPane));
    public static readonly BindableProperty ShellContentProperty = Slot(nameof(ShellContent));
    public static readonly BindableProperty RightPaneProperty = Slot(nameof(RightPane));
    public static readonly BindableProperty StatusBarProperty = Slot(nameof(StatusBar));
    public static readonly BindableProperty BackstageProperty = Slot(nameof(Backstage));

    public static readonly BindableProperty IsLeftPaneOpenProperty = Flag(nameof(IsLeftPaneOpen), false, BindingMode.TwoWay);
    public static readonly BindableProperty IsRightPaneOpenProperty = Flag(nameof(IsRightPaneOpen), false, BindingMode.TwoWay);
    public static readonly BindableProperty IsFocusModeProperty = Flag(nameof(IsFocusMode), false, BindingMode.TwoWay);

    public static readonly BindableProperty IsBackstageOpenProperty = BindableProperty.Create(
        nameof(IsBackstageOpen), typeof(bool), typeof(OfficeShell), false, BindingMode.TwoWay,
        propertyChanged: (b, _, _) => { if (((OfficeShell)b).root is not null) ((OfficeShell)b).OnBackstageOpenChanged(); });

    public static readonly BindableProperty LeftPaneWidthProperty = BindableProperty.Create(
        nameof(LeftPaneWidth), typeof(double), typeof(OfficeShell), 280d,
        propertyChanged: (b, _, n) => ((OfficeShell)b).leftHost.WidthRequest = (double)n);

    public static readonly BindableProperty RightPaneWidthProperty = BindableProperty.Create(
        nameof(RightPaneWidth), typeof(double), typeof(OfficeShell), 320d,
        propertyChanged: (b, _, n) => ((OfficeShell)b).rightHost.WidthRequest = (double)n);

    public static readonly BindableProperty CompactWidthProperty = BindableProperty.Create(
        nameof(CompactWidth), typeof(double), typeof(OfficeShell), OfficeShellLayout.CompactWidth,
        propertyChanged: (b, _, _) => { if (((OfficeShell)b).root is not null) ((OfficeShell)b).Relayout(); });


    public OfficeShell()
    {
        this.SetDynamicResource(BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerLow);

        this.leftHost.WidthRequest = this.LeftPaneWidth;
        this.rightHost.WidthRequest = this.RightPaneWidth;

        var body = new Grid
        {
            ColumnDefinitions =
            {
                new(GridLength.Auto),   // left pane
                new(GridLength.Auto),   // vertical ruler
                new(GridLength.Star),   // content
                new(GridLength.Auto)    // right pane
            },
            RowDefinitions =
            {
                new(GridLength.Auto),   // horizontal ruler
                new(GridLength.Star)    // content
            }
        };
        body.Add(this.leftHost, 0, 0);
        Grid.SetRowSpan(this.leftHost, 2);
        body.Add(this.rulerHost, 2, 0);
        body.Add(this.verticalRulerHost, 1, 1);
        body.Add(this.contentHost, 2, 1);
        body.Add(this.rightHost, 3, 0);
        Grid.SetRowSpan(this.rightHost, 2);

        this.focusExit = ShellChrome.TextButton("Exit Focus", () => this.IsFocusMode = false, OfficeShellIcon.ExitFocus, null, out _);
        this.focusExit.HorizontalOptions = LayoutOptions.End;
        this.focusExit.VerticalOptions = LayoutOptions.Start;
        this.focusExit.Margin = new Thickness(0, 12, 16, 0);
        this.focusExit.StrokeThickness = 1;
        this.focusExit.StrokeShape = new RoundRectangle { CornerRadius = 16 };
        this.focusExit.SetDynamicResource(Border.StrokeProperty, ShinyThemeKeys.Brush.OutlineVariant);
        this.focusExit.SetDynamicResource(BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHigh);
        this.focusExit.Shadow = new Shadow { Radius = 10, Opacity = 0.2f, Offset = new Point(0, 3), Brush = Colors.Black };
        this.focusExit.IsVisible = false;

        // The overlay passes touches through everywhere it has nothing showing: drop-downs adopted from
        // the title bar live here, and must hang over the ribbon without a full-window hit-test sheet.
        this.overlay = new Grid { CascadeInputTransparent = false, InputTransparent = true };

        this.root = new Grid
        {
            RowDefinitions =
            {
                new(GridLength.Auto),   // title bar
                new(GridLength.Auto),   // ribbon
                new(GridLength.Star),   // body
                new(GridLength.Auto)    // status bar
            }
        };
        this.root.Add(this.titleHost, 0, 0);
        this.root.Add(this.ribbonHost, 0, 1);
        this.root.Add(body, 0, 2);
        this.root.Add(this.statusHost, 0, 3);

        foreach (var layer in new View[] { this.overlay, this.backstageHost, this.focusExit, this.zoomDialog })
        {
            this.root.Add(layer, 0, 0);
            Grid.SetRowSpan(layer, 4);
        }

        this.backstageHost.IsVisible = false;

        this.Content = this.root;
        this.SizeChanged += (_, _) => this.Relayout();
        this.ApplyLayout();
    }


    // ---------------------------------------------------------------------------------------------
    // Properties
    // ---------------------------------------------------------------------------------------------

    /// <summary>Which app the shell dresses as. Reaches the title bar, status bar and backstage unless they set their own.</summary>
    public OfficeApp App { get => (OfficeApp)this.GetValue(AppProperty); set => this.SetValue(AppProperty, value); }

    public View? TitleBar { get => (View?)this.GetValue(TitleBarProperty); set => this.SetValue(TitleBarProperty, value); }
    public View? Ribbon { get => (View?)this.GetValue(RibbonProperty); set => this.SetValue(RibbonProperty, value); }

    /// <summary>The horizontal ruler, above the content. Hidden in compact and focus modes.</summary>
    public View? Ruler { get => (View?)this.GetValue(RulerProperty); set => this.SetValue(RulerProperty, value); }

    /// <summary>An optional vertical ruler, left of the content.</summary>
    public View? VerticalRuler { get => (View?)this.GetValue(VerticalRulerProperty); set => this.SetValue(VerticalRulerProperty, value); }

    /// <summary>The navigation pane or anything else docked on the left. Shown while <see cref="IsLeftPaneOpen"/>.</summary>
    public View? LeftPane { get => (View?)this.GetValue(LeftPaneProperty); set => this.SetValue(LeftPaneProperty, value); }

    /// <summary>The editor. The content property.</summary>
    public View? ShellContent { get => (View?)this.GetValue(ShellContentProperty); set => this.SetValue(ShellContentProperty, value); }

    /// <summary>The comments pane or anything else docked on the right. Shown while <see cref="IsRightPaneOpen"/>.</summary>
    public View? RightPane { get => (View?)this.GetValue(RightPaneProperty); set => this.SetValue(RightPaneProperty, value); }

    public View? StatusBar { get => (View?)this.GetValue(StatusBarProperty); set => this.SetValue(StatusBarProperty, value); }

    /// <summary>The File backstage — normally an <see cref="OfficeBackstage"/>. Covers the whole shell while open.</summary>
    public View? Backstage { get => (View?)this.GetValue(BackstageProperty); set => this.SetValue(BackstageProperty, value); }

    public bool IsLeftPaneOpen { get => (bool)this.GetValue(IsLeftPaneOpenProperty); set => this.SetValue(IsLeftPaneOpenProperty, value); }
    public bool IsRightPaneOpen { get => (bool)this.GetValue(IsRightPaneOpenProperty); set => this.SetValue(IsRightPaneOpenProperty, value); }
    public double LeftPaneWidth { get => (double)this.GetValue(LeftPaneWidthProperty); set => this.SetValue(LeftPaneWidthProperty, value); }
    public double RightPaneWidth { get => (double)this.GetValue(RightPaneWidthProperty); set => this.SetValue(RightPaneWidthProperty, value); }

    /// <summary>Whether the backstage is showing. Two-way; the ribbon's File button sets it.</summary>
    public bool IsBackstageOpen { get => (bool)this.GetValue(IsBackstageOpenProperty); set => this.SetValue(IsBackstageOpenProperty, value); }

    /// <summary>
    /// Hides the title bar, ribbon, rulers, panes and status bar, leaving the page and a floating
    /// "Exit Focus" button. Two-way.
    /// </summary>
    public bool IsFocusMode { get => (bool)this.GetValue(IsFocusModeProperty); set => this.SetValue(IsFocusModeProperty, value); }

    /// <summary>The width below which the shell goes compact. Default 600.</summary>
    public double CompactWidth { get => (double)this.GetValue(CompactWidthProperty); set => this.SetValue(CompactWidthProperty, value); }

    /// <summary>What fits at the current width.</summary>
    public OfficeShellLayout ShellLayout { get; private set; } = OfficeShellLayout.For(0);

    /// <summary>The shell's own zoom dialog, which a status bar inside it opens.</summary>
    public OfficeZoomDialog ZoomDialog => this.zoomDialog;

    /// <summary>Raised when the width crosses a breakpoint.</summary>
    public event EventHandler<OfficeShellLayout>? ShellLayoutChanged;


    // ---------------------------------------------------------------------------------------------
    // Public actions
    // ---------------------------------------------------------------------------------------------

    /// <summary>Opens the zoom dialog for a status bar, writing the chosen zoom back to it.</summary>
    public void ShowZoomDialog(OfficeStatusBar source) => this.zoomDialog.Open(source.Zoom, source);

    public void ToggleFocusMode() => this.IsFocusMode = !this.IsFocusMode;

    public void OpenBackstage(OfficeBackstagePage? page = null)
    {
        if (page is { } p && this.Backstage is OfficeBackstage backstage)
            backstage.SelectedPage = p;

        this.IsBackstageOpen = true;
    }


    /// <summary>Closes whichever pane holds <paramref name="pane"/>. Called by <see cref="OfficeSidePane"/> and the navigation pane.</summary>
    public void ClosePane(View pane)
    {
        if (ReferenceEquals(this.LeftPane, pane))
            this.IsLeftPaneOpen = false;
        else if (ReferenceEquals(this.RightPane, pane))
            this.IsRightPaneOpen = false;
    }


    // ---------------------------------------------------------------------------------------------
    // Slots
    // ---------------------------------------------------------------------------------------------

    void OnSlotChanged(string name, View? old, View? value)
    {
        switch (name)
        {
            case nameof(this.TitleBar):
                this.titleHost.Content = value;
                if (value is OfficeTitleBar title)
                    title.AdoptPanels(this.overlay);
                break;

            case nameof(this.Ribbon):
                if (old is Ribbon oldRibbon)
                    oldRibbon.ApplicationButtonClicked -= this.OnApplicationButton;

                this.shellSimplifiedRibbon = false;
                this.ribbonHost.Content = value;

                if (value is Ribbon ribbon)
                {
                    ribbon.ApplicationButtonClicked += this.OnApplicationButton;
                    if (string.IsNullOrWhiteSpace(ribbon.ApplicationButtonText))
                        ribbon.ApplicationButtonText = "File";
                }
                break;

            case nameof(this.Ruler): this.rulerHost.Content = value; break;
            case nameof(this.VerticalRuler): this.verticalRulerHost.Content = value; break;
            case nameof(this.LeftPane): this.leftHost.Content = value; break;
            case nameof(this.ShellContent): this.contentHost.Content = value; break;
            case nameof(this.RightPane): this.rightHost.Content = value; break;

            case nameof(this.StatusBar):
                if (old is OfficeStatusBar oldStatus)
                    oldStatus.FocusRequested -= this.OnFocusRequested;

                this.statusHost.Content = value;
                if (value is OfficeStatusBar status)
                    status.FocusRequested += this.OnFocusRequested;
                break;

            case nameof(this.Backstage):
                if (old is OfficeBackstage oldBackstage)
                    oldBackstage.PropertyChanged -= this.OnBackstagePropertyChanged;

                this.backstageHost.Content = value;
                if (value is OfficeBackstage backstage)
                {
                    backstage.PropertyChanged += this.OnBackstagePropertyChanged;
                    backstage.IsOpen = this.IsBackstageOpen;
                }
                break;
        }

        this.PushApp();
        this.ApplyLayout();
    }


    void OnApplicationButton(object? sender, EventArgs e) => this.IsBackstageOpen = true;

    void OnFocusRequested(object? sender, EventArgs e) => this.ToggleFocusMode();


    void OnBackstagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OfficeBackstage.IsOpen) && sender is OfficeBackstage backstage && !this.syncingBackstage)
            this.IsBackstageOpen = backstage.IsOpen;
    }


    void OnBackstageOpenChanged()
    {
        if (this.Backstage is OfficeBackstage backstage && backstage.IsOpen != this.IsBackstageOpen)
        {
            this.syncingBackstage = true;
            backstage.IsOpen = this.IsBackstageOpen;
            this.syncingBackstage = false;
        }

        this.ApplyLayout();
    }


    void PushApp()
    {
        if (this.TitleBar is OfficeTitleBar title)
            title.InheritApp(this.App);

        if (this.StatusBar is OfficeStatusBar status)
            status.InheritApp(this.App);

        if (this.Backstage is OfficeBackstage backstage)
            backstage.InheritApp(this.App);
    }


    // ---------------------------------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------------------------------

    void Relayout()
    {
        var width = this.Width;
        var layout = OfficeShellLayout.For(width);

        // A custom breakpoint moves only the compact line; the rest of the table stays as it is.
        if (width > 0 && this.CompactWidth != OfficeShellLayout.CompactWidth)
        {
            var compact = width < this.CompactWidth;
            layout = layout with
            {
                IsCompact = compact,
                ShowSearchBox = !compact,
                ShowRulers = !compact,
                ShowSidePanes = !compact,
                SimplifiedRibbon = compact
            };
        }

        if (layout == this.ShellLayout)
            return;

        this.ShellLayout = layout;
        this.ApplyLayout();
        this.ShellLayoutChanged?.Invoke(this, layout);
    }


    void ApplyLayout()
    {
        var layout = this.ShellLayout;
        var focus = this.IsFocusMode;

        this.titleHost.IsVisible = !focus && this.TitleBar is not null;
        this.ribbonHost.IsVisible = !focus && this.Ribbon is not null;
        this.statusHost.IsVisible = !focus && this.StatusBar is not null;
        this.rulerHost.IsVisible = !focus && layout.ShowRulers && this.Ruler is not null;
        this.verticalRulerHost.IsVisible = !focus && layout.ShowRulers && this.VerticalRuler is not null;
        this.leftHost.IsVisible = !focus && layout.ShowSidePanes && this.IsLeftPaneOpen && this.LeftPane is not null;
        this.rightHost.IsVisible = !focus && layout.ShowSidePanes && this.IsRightPaneOpen && this.RightPane is not null;
        this.backstageHost.IsVisible = this.IsBackstageOpen && this.Backstage is not null;
        this.focusExit.IsVisible = focus;

        if (this.TitleBar is OfficeTitleBar title)
            title.IsCompact = layout.IsCompact;

        if (this.StatusBar is OfficeStatusBar status)
            status.IsCompact = layout.IsCompact;

        if (this.Ribbon is Ribbon ribbon)
        {
            if (layout.SimplifiedRibbon && ribbon.DisplayMode == RibbonDisplayMode.Expanded)
            {
                ribbon.DisplayMode = RibbonDisplayMode.Simplified;
                this.shellSimplifiedRibbon = true;
            }
            else if (!layout.SimplifiedRibbon && this.shellSimplifiedRibbon && ribbon.DisplayMode == RibbonDisplayMode.Simplified)
            {
                ribbon.DisplayMode = RibbonDisplayMode.Expanded;
                this.shellSimplifiedRibbon = false;
            }
        }
    }
}
