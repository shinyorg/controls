using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Controls.Office.Icons;
using Shiny.Controls.Office.Shell;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// The File backstage: a full-window page with an accent rail down the left — Back, Home, New, Open,
/// Info, Save, Save As, Print, Export, History, Options — and the chosen page beside it.
/// </summary>
/// <remarks>
/// <para>
/// Every action is an event; the backstage opens and writes nothing. <see cref="TemplateSelected"/>,
/// <see cref="RecentFileSelected"/>, <see cref="OpenRequested"/>, <see cref="SaveRequested"/>,
/// <see cref="SaveAsRequested"/>, <see cref="ExportRequested"/> and <see cref="PrintRequested"/> hand the
/// host the choice and close the backstage, which is what Office does.
/// </para>
/// <para>
/// All pages are built in the constructor and switched with <c>IsVisible</c>. The lists inside them
/// (templates, recent files, properties, formats) are rebuilt when their source property changes —
/// normally before the backstage is first shown; the AppKit head may not paint rows added to a page
/// that has already been laid out.
/// </para>
/// </remarks>
public class OfficeBackstage : ContentView
{
    const int HomeTemplateCount = 6;

    readonly Grid root;
    readonly VerticalStackLayout railTop;
    readonly VerticalStackLayout railBottom;
    readonly List<(OfficeBackstageEntry Entry, Border View, Label Label, OfficeShellIconView Icon)> railEntries = [];
    readonly Dictionary<OfficeBackstagePage, View> pages = new();
    readonly List<Label> railLabels = [];
    readonly List<OfficeShellIconView> railIcons = [];
    readonly Border rail;

    // Page contents that change with the host's data.
    readonly Label greeting;
    readonly Label headline;
    readonly FlexLayout homeTemplates;
    readonly VerticalStackLayout homeRecents;
    readonly FlexLayout newTemplates;
    readonly VerticalStackLayout openRecents;
    readonly Label infoTitle;
    readonly VerticalStackLayout infoProperties;
    readonly VerticalStackLayout saveAsFormats;
    readonly VerticalStackLayout exportFormats;
    readonly ContentView printPreviewHost;
    readonly Label printPlaceholder;
    readonly ContentView historyHost;
    readonly Entry optionsName;
    readonly Entry optionsInitials;
    readonly List<(OfficeThemeChoice Choice, RadioButton Radio)> themeRadios = [];
    readonly Switch optionsAutoSave;
    OfficeApp? inheritedApp;
    OfficeShellOptions? observedOptions;
    bool syncingOptions;

    static BindableProperty Prop(string name, Type type, object? value, Action<OfficeBackstage>? changed, BindingMode mode = BindingMode.OneWay)
        => BindableProperty.Create(name, type, typeof(OfficeBackstage), value, mode,
            propertyChanged: changed is null ? null : (b, _, _) => { if (((OfficeBackstage)b).root is not null) changed((OfficeBackstage)b); });

    public static readonly BindableProperty AppProperty = Prop(nameof(App), typeof(OfficeApp?), null, b => b.ApplyApp());
    public static readonly BindableProperty IsOpenProperty = Prop(nameof(IsOpen), typeof(bool), false, b => b.OnIsOpenChanged(), BindingMode.TwoWay);
    public static readonly BindableProperty SelectedPageProperty = Prop(nameof(SelectedPage), typeof(OfficeBackstagePage), OfficeBackstagePage.Home, b => b.ApplyPage(), BindingMode.TwoWay);
    public static readonly BindableProperty DocumentNameProperty = Prop(nameof(DocumentName), typeof(string), null, b => b.BuildInfo());
    public static readonly BindableProperty TemplatesProperty = Prop(nameof(Templates), typeof(IEnumerable<OfficeTemplate>), null, b => b.OnTemplatesChanged());
    public static readonly BindableProperty RecentFilesProperty = Prop(nameof(RecentFiles), typeof(IEnumerable<OfficeRecentFile>), null, b => b.OnRecentsChanged());
    public static readonly BindableProperty DocumentInfoProperty = Prop(nameof(DocumentInfo), typeof(OfficeDocumentInfo), null, b => b.BuildInfo());
    public static readonly BindableProperty ShowHistoryProperty = Prop(nameof(ShowHistory), typeof(bool), false, b => b.BuildRail());
    public static readonly BindableProperty SaveAsFormatsProperty = Prop(nameof(SaveAsFormats), typeof(IReadOnlyList<OfficeFileFormat>), null, b => b.BuildFormats());
    public static readonly BindableProperty ExportFormatsProperty = Prop(nameof(ExportFormats), typeof(IReadOnlyList<OfficeFileFormat>), null, b => b.BuildFormats());
    public static readonly BindableProperty PrintPreviewProperty = Prop(nameof(PrintPreview), typeof(View), null, b => b.ApplyPrintPreview());
    public static readonly BindableProperty HistoryContentProperty = Prop(nameof(HistoryContent), typeof(View), null, b => b.historyHost.Content = b.HistoryContent ?? ShellChrome.Text("No version history.", 14, ShinyThemeKeys.Color.OnSurfaceVariant));
    public static readonly BindableProperty OptionsProperty = Prop(nameof(Options), typeof(OfficeShellOptions), null, b => b.OnOptionsChanged());


    public OfficeBackstage()
    {
        this.IsVisible = false;
        this.SetDynamicResource(BackgroundColorProperty, ShinyThemeKeys.Color.Surface);

        // -- rail --------------------------------------------------------------------------------------
        this.railTop = new VerticalStackLayout { Spacing = 2, Padding = new Thickness(0, 12, 0, 0) };
        this.railBottom = new VerticalStackLayout { Spacing = 2, Padding = new Thickness(0, 0, 0, 12) };
        var railGrid = new Grid { RowDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        railGrid.Add(new ScrollView { Content = this.railTop }, 0, 0);
        railGrid.Add(this.railBottom, 0, 1);
        this.rail = new Border { Content = railGrid, StrokeThickness = 0, WidthRequest = 200 };

        // -- pages ---------------------------------------------------------------------------------------
        this.greeting = ShellChrome.Text(null, 14, ShinyThemeKeys.Color.OnSurfaceVariant);
        this.headline = ShellChrome.Text(null, 26, attributes: FontAttributes.Bold);
        this.homeTemplates = Tiles();
        this.homeRecents = new VerticalStackLayout { Spacing = 2 };
        var moreTemplates = ShellChrome.TextButton("More templates", () => this.SelectPage(OfficeBackstagePage.New), OfficeShellIcon.ChevronRight, null, out _);
        moreTemplates.HorizontalOptions = LayoutOptions.End;
        this.AddPage(OfficeBackstagePage.Home, this.greeting, this.headline, this.homeTemplates, moreTemplates, Heading("Recent"), this.homeRecents);

        this.newTemplates = Tiles();
        this.AddPage(OfficeBackstagePage.New, Heading("New", 26), this.newTemplates);

        var browse = ShellChrome.Card(Row(OfficeShellIcon.Open, "Browse", "Open a file from this device"), 14);
        browse.HorizontalOptions = LayoutOptions.Start;
        browse.WidthRequest = 320;
        ShellChrome.OnTap(browse, this.Open);
        this.openRecents = new VerticalStackLayout { Spacing = 2 };
        this.AddPage(OfficeBackstagePage.Open, Heading("Open", 26), browse, Heading("Recent"), this.openRecents);

        this.infoTitle = ShellChrome.Text(null, 16, ShinyThemeKeys.Color.OnSurfaceVariant);
        var protect = ShellChrome.Card(Row(OfficeShellIcon.Protect, "Protect Document", "Control what types of changes people can make to this document."), 14);
        ShellChrome.OnTap(protect, () => this.ProtectRequested?.Invoke(this, EventArgs.Empty));
        var inspect = ShellChrome.Card(Row(OfficeShellIcon.Inspect, "Inspect Document", "Check the document for hidden properties or personal information."), 14);
        ShellChrome.OnTap(inspect, () => this.InspectRequested?.Invoke(this, EventArgs.Empty));
        var actions = new VerticalStackLayout { Spacing = 10 };
        actions.Children.Add(protect);
        actions.Children.Add(inspect);
        this.infoProperties = new VerticalStackLayout { Spacing = 6 };
        var props = new VerticalStackLayout { Spacing = 8 };
        props.Children.Add(Heading("Properties"));
        props.Children.Add(this.infoProperties);
        var infoGrid = new Grid { ColumnDefinitions = { new(GridLength.Star), new(new GridLength(280)) }, ColumnSpacing = 24 };
        infoGrid.Add(actions, 0);
        infoGrid.Add(props, 1);
        this.AddPage(OfficeBackstagePage.Info, Heading("Info", 26), this.infoTitle, infoGrid);

        this.saveAsFormats = new VerticalStackLayout { Spacing = 8 };
        this.AddPage(OfficeBackstagePage.SaveAs, Heading("Save As", 26), this.saveAsFormats);

        this.exportFormats = new VerticalStackLayout { Spacing = 8 };
        this.AddPage(OfficeBackstagePage.Export, Heading("Export", 26), this.exportFormats);

        var printButton = ShellChrome.Card(Row(OfficeShellIcon.Print, "Print", "Send the document to a printer"), 14);
        printButton.WidthRequest = 240;
        printButton.VerticalOptions = LayoutOptions.Start;
        ShellChrome.OnTap(printButton, this.Print);
        this.printPlaceholder = ShellChrome.Text("No preview available", 14, ShinyThemeKeys.Color.OnSurfaceVariant);
        this.printPlaceholder.HorizontalOptions = LayoutOptions.Center;
        this.printPreviewHost = new ContentView { MinimumHeightRequest = 400 };
        var previewFrame = new Grid();
        previewFrame.SetDynamicResource(BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainer);
        previewFrame.Children.Add(this.printPlaceholder);
        previewFrame.Children.Add(this.printPreviewHost);
        var printGrid = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, ColumnSpacing = 24 };
        printGrid.Add(printButton, 0);
        printGrid.Add(previewFrame, 1);
        this.AddPage(OfficeBackstagePage.Print, Heading("Print", 26), printGrid);

        this.historyHost = new ContentView { Content = ShellChrome.Text("No version history.", 14, ShinyThemeKeys.Color.OnSurfaceVariant) };
        this.AddPage(OfficeBackstagePage.History, Heading("History", 26), this.historyHost);

        this.optionsName = new Entry { Placeholder = "Your name", WidthRequest = 280, HorizontalOptions = LayoutOptions.Start };
        this.optionsName.SetDynamicResource(Entry.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
        this.optionsName.TextChanged += (_, e) => this.EditOptions(o => o.UserName = e.NewTextValue);
        this.optionsInitials = new Entry { Placeholder = "Initials", WidthRequest = 100, MaxLength = 4, HorizontalOptions = LayoutOptions.Start };
        this.optionsInitials.SetDynamicResource(Entry.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
        this.optionsInitials.TextChanged += (_, e) => this.EditOptions(o => o.Initials = string.IsNullOrWhiteSpace(e.NewTextValue) ? null : e.NewTextValue);
        var themeRow = new HorizontalStackLayout { Spacing = 12 };
        var group = $"officetheme-{Guid.NewGuid():N}";
        foreach (var choice in new[] { OfficeThemeChoice.System, OfficeThemeChoice.Light, OfficeThemeChoice.Dark })
        {
            var c = choice;
            var radio = new RadioButton { Content = choice == OfficeThemeChoice.System ? "Use system setting" : choice.ToString(), GroupName = group };
            radio.SetDynamicResource(RadioButton.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
            radio.CheckedChanged += (_, e) => { if (e.Value) this.EditOptions(o => o.Theme = c); };
            this.themeRadios.Add((choice, radio));
            themeRow.Children.Add(radio);
        }
        this.optionsAutoSave = new Switch();
        this.optionsAutoSave.Toggled += (_, e) => this.EditOptions(o => o.AutoSave = e.Value);
        var autoSaveRow = new HorizontalStackLayout { Spacing = 8 };
        autoSaveRow.Children.Add(this.optionsAutoSave);
        autoSaveRow.Children.Add(ShellChrome.Text("AutoSave files by default"));
        this.AddPage(OfficeBackstagePage.Options, Heading("Options", 26),
            Heading("Personalize your copy of Office"),
            ShellChrome.Text("User name", 12, ShinyThemeKeys.Color.OnSurfaceVariant), this.optionsName,
            ShellChrome.Text("Initials", 12, ShinyThemeKeys.Color.OnSurfaceVariant), this.optionsInitials,
            Heading("Office Theme"), themeRow,
            Heading("Save"), autoSaveRow);

        var pageStack = new Grid();
        foreach (var page in this.pages.Values)
            pageStack.Children.Add(page);

        this.root = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        this.root.Add(this.rail, 0);
        this.root.Add(pageStack, 1);
        this.Content = this.root;

        this.BuildRail();
        this.ApplyApp();
        this.ApplyPage();
        this.BuildFormats();
        this.BuildInfo();
        this.ApplyPrintPreview();
        this.OnTemplatesChanged();
        this.OnRecentsChanged();
    }


    // ---------------------------------------------------------------------------------------------
    // Properties
    // ---------------------------------------------------------------------------------------------

    /// <summary>Which app it belongs to — the rail's accent, the headline, the blank template, the formats. Null inherits from an <see cref="OfficeShell"/>.</summary>
    public OfficeApp? App { get => (OfficeApp?)this.GetValue(AppProperty); set => this.SetValue(AppProperty, value); }

    /// <summary>Showing while true. Two-way; Back and every completed action set it false.</summary>
    public bool IsOpen { get => (bool)this.GetValue(IsOpenProperty); set => this.SetValue(IsOpenProperty, value); }

    /// <summary>The page showing. Two-way.</summary>
    public OfficeBackstagePage SelectedPage { get => (OfficeBackstagePage)this.GetValue(SelectedPageProperty); set => this.SetValue(SelectedPageProperty, value); }

    public string? DocumentName { get => (string?)this.GetValue(DocumentNameProperty); set => this.SetValue(DocumentNameProperty, value); }

    /// <summary>Templates for Home and New. A blank one for the app is added first when none is supplied.</summary>
    public IEnumerable<OfficeTemplate>? Templates { get => (IEnumerable<OfficeTemplate>?)this.GetValue(TemplatesProperty); set => this.SetValue(TemplatesProperty, value); }

    public IEnumerable<OfficeRecentFile>? RecentFiles { get => (IEnumerable<OfficeRecentFile>?)this.GetValue(RecentFilesProperty); set => this.SetValue(RecentFilesProperty, value); }

    /// <summary>What the Info page lists.</summary>
    public OfficeDocumentInfo? DocumentInfo { get => (OfficeDocumentInfo?)this.GetValue(DocumentInfoProperty); set => this.SetValue(DocumentInfoProperty, value); }

    public bool ShowHistory { get => (bool)this.GetValue(ShowHistoryProperty); set => this.SetValue(ShowHistoryProperty, value); }

    /// <summary>Save As choices. Null uses the app's.</summary>
    public IReadOnlyList<OfficeFileFormat>? SaveAsFormats { get => (IReadOnlyList<OfficeFileFormat>?)this.GetValue(SaveAsFormatsProperty); set => this.SetValue(SaveAsFormatsProperty, value); }

    /// <summary>Export choices. Null uses the app's.</summary>
    public IReadOnlyList<OfficeFileFormat>? ExportFormats { get => (IReadOnlyList<OfficeFileFormat>?)this.GetValue(ExportFormatsProperty); set => this.SetValue(ExportFormatsProperty, value); }

    /// <summary>The Print page's preview — whatever the editor renders a page into.</summary>
    public View? PrintPreview { get => (View?)this.GetValue(PrintPreviewProperty); set => this.SetValue(PrintPreviewProperty, value); }

    /// <summary>The History page's body, for a host that keeps versions.</summary>
    public View? HistoryContent { get => (View?)this.GetValue(HistoryContentProperty); set => this.SetValue(HistoryContentProperty, value); }

    /// <summary>What the Options page edits, in place.</summary>
    public OfficeShellOptions? Options { get => (OfficeShellOptions?)this.GetValue(OptionsProperty); set => this.SetValue(OptionsProperty, value); }


    public event EventHandler? Closed;
    public event EventHandler<OfficeTemplate>? TemplateSelected;
    public event EventHandler<OfficeRecentFile>? RecentFileSelected;
    public event EventHandler? OpenRequested;
    public event EventHandler? SaveRequested;
    public event EventHandler<OfficeFileFormat>? SaveAsRequested;
    public event EventHandler<OfficeFileFormat>? ExportRequested;
    public event EventHandler? PrintRequested;
    public event EventHandler? ProtectRequested;
    public event EventHandler? InspectRequested;
    public event EventHandler<OfficeShellOptions>? OptionsChanged;


    // ---------------------------------------------------------------------------------------------
    // Actions (public seams)
    // ---------------------------------------------------------------------------------------------

    /// <summary>Moves to a page. Save is a command, so it saves and closes instead.</summary>
    public void SelectPage(OfficeBackstagePage page)
    {
        if (page == OfficeBackstagePage.Save)
        {
            this.Save();
            return;
        }

        this.SelectedPage = page;
    }

    public void Close() => this.IsOpen = false;

    public void Save() => this.Finish(() => this.SaveRequested?.Invoke(this, EventArgs.Empty));

    public void Open() => this.Finish(() => this.OpenRequested?.Invoke(this, EventArgs.Empty));

    public void Print() => this.PrintRequested?.Invoke(this, EventArgs.Empty);

    public void ChooseTemplate(OfficeTemplate template) => this.Finish(() => this.TemplateSelected?.Invoke(this, template));

    public void ChooseRecent(OfficeRecentFile file) => this.Finish(() => this.RecentFileSelected?.Invoke(this, file));

    public void ChooseSaveAs(OfficeFileFormat format) => this.Finish(() => this.SaveAsRequested?.Invoke(this, format));

    public void ChooseExport(OfficeFileFormat format) => this.Finish(() => this.ExportRequested?.Invoke(this, format));


    void Finish(Action raise)
    {
        this.Close();
        raise();
    }


    internal OfficeApp EffectiveApp => this.App ?? this.inheritedApp ?? OfficeApp.Word;

    internal void InheritApp(OfficeApp app)
    {
        this.inheritedApp = app;
        this.ApplyApp();
    }


    // ---------------------------------------------------------------------------------------------
    // Build
    // ---------------------------------------------------------------------------------------------

    void AddPage(OfficeBackstagePage page, params View[] children)
    {
        var stack = new VerticalStackLayout { Spacing = 14, Padding = new Thickness(40, 28, 40, 40), MaximumWidthRequest = 1100, HorizontalOptions = LayoutOptions.Start };
        foreach (var child in children)
            stack.Children.Add(child);

        this.pages[page] = new ScrollView { Content = stack, IsVisible = false };
    }


    void BuildRail()
    {
        if (this.railTop is null)
            return;

        // The rail changes only with ShowHistory, which a host sets up front.
        this.railTop.Children.Clear();
        this.railBottom.Children.Clear();
        this.railEntries.Clear();
        this.railLabels.Clear();
        this.railIcons.Clear();

        this.railTop.Children.Add(this.RailButton(OfficeShellIcon.Back, "Back", this.Close, out _, out _));

        foreach (var entry in OfficeBackstageText.Entries(this.ShowHistory))
        {
            var e = entry;
            var view = this.RailButton(entry.Icon, entry.Text, () => this.SelectPage(e.Page), out var label, out var icon);
            this.railEntries.Add((entry, view, label, icon));
            (entry.StartsFooter ? this.railBottom : this.railTop).Children.Add(view);
        }

        this.ApplyApp();
    }


    Border RailButton(OfficeShellIcon icon, string text, Action action, out Label label, out OfficeShellIconView iconView)
    {
        iconView = new OfficeShellIconView { Icon = icon, WidthRequest = 18, HeightRequest = 18, VerticalOptions = LayoutOptions.Center };
        label = new Label { Text = text, FontSize = 14, VerticalTextAlignment = TextAlignment.Center };
        var row = new HorizontalStackLayout { Spacing = 12, Padding = new Thickness(20, 9) };
        row.Children.Add(iconView);
        row.Children.Add(label);
        this.railIcons.Add(iconView);
        this.railLabels.Add(label);

        var border = new Border { Content = row, StrokeThickness = 0, BackgroundColor = Colors.Transparent };
        ShellChrome.Hint(border, text);
        ShellChrome.OnTap(border, action);
        return border;
    }


    void ApplyApp()
    {
        if (this.rail is null)
            return;

        var info = OfficeAppInfo.For(this.EffectiveApp);
        var ink = info.Accent.Ink.ToColor();
        this.rail.BackgroundColor = info.Accent.Color.ToColor();

        foreach (var label in this.railLabels)
            label.TextColor = ink;

        foreach (var icon in this.railIcons)
            icon.Color = ink;

        this.headline.Text = OfficeBackstageText.Headline(this.EffectiveApp);
        this.greeting.Text = OfficeBackstageText.Greeting(DateTime.Now);
        this.ApplyPage();
        this.BuildFormats();
        this.OnTemplatesChanged();
    }


    void ApplyPage()
    {
        if (this.rail is null)
            return;

        foreach (var (page, view) in this.pages)
            view.IsVisible = page == this.SelectedPage;

        var dark = OfficeAppInfo.For(this.EffectiveApp).AccentDark.ToColor();
        foreach (var (entry, view, _, _) in this.railEntries)
            view.BackgroundColor = entry.Page == this.SelectedPage ? dark : Colors.Transparent;
    }


    void OnIsOpenChanged()
    {
        this.IsVisible = this.IsOpen;

        if (this.IsOpen)
            this.greeting.Text = OfficeBackstageText.Greeting(DateTime.Now);
        else
            this.Closed?.Invoke(this, EventArgs.Empty);
    }


    static FlexLayout Tiles() => new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Direction = Microsoft.Maui.Layouts.FlexDirection.Row };


    void OnTemplatesChanged()
    {
        if (this.homeTemplates is null)
            return;

        var all = OfficeBackstageText.WithBlank(this.EffectiveApp, this.Templates);
        this.homeTemplates.Children.Clear();
        this.newTemplates.Children.Clear();

        foreach (var template in all.Take(HomeTemplateCount))
            this.homeTemplates.Children.Add(this.Tile(template));

        foreach (var template in all)
            this.newTemplates.Children.Add(this.Tile(template));
    }


    View Tile(OfficeTemplate template)
    {
        var app = this.EffectiveApp;
        var (w, h) = app == OfficeApp.PowerPoint ? (150d, 84d) : (110d, 142d);

        View picture;
        if (!template.IsBlank && !string.IsNullOrWhiteSpace(template.Thumbnail))
        {
            picture = new Image { Source = ImageSource.FromUri(Uri.TryCreate(template.Thumbnail, UriKind.Absolute, out var uri) ? uri : new Uri("file://" + template.Thumbnail)), Aspect = Aspect.AspectFill, WidthRequest = w, HeightRequest = h };
        }
        else
        {
            // A plain page in the app's shape; a workbook gets its grid lines.
            var page = new Grid { WidthRequest = w, HeightRequest = h };
            page.SetDynamicResource(BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerLowest);
            if (app == OfficeApp.Excel)
            {
                for (var i = 1; i < 6; i++)
                {
                    var line = ShellChrome.Rule();
                    line.VerticalOptions = LayoutOptions.Start;
                    line.Margin = new Thickness(0, i * h / 6, 0, 0);
                    page.Children.Add(line);
                }
            }
            picture = page;
        }

        var frame = new Border
        {
            Content = picture,
            StrokeThickness = 1,
            Padding = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 4 }
        };
        frame.SetDynamicResource(Border.StrokeProperty, ShinyThemeKeys.Brush.OutlineVariant);

        var name = ShellChrome.Text(template.Name, 12);
        name.HorizontalTextAlignment = TextAlignment.Center;
        name.LineBreakMode = LineBreakMode.TailTruncation;
        name.WidthRequest = w;

        var stack = new VerticalStackLayout { Spacing = 6, Margin = new Thickness(0, 0, 16, 16) };
        stack.Children.Add(frame);
        stack.Children.Add(name);

        ShellChrome.Hint(stack, template.Description ?? template.Name);
        ShellChrome.OnTap(stack, () => this.ChooseTemplate(template));
        return stack;
    }


    void OnRecentsChanged()
    {
        if (this.homeRecents is null)
            return;

        this.homeRecents.Children.Clear();
        this.openRecents.Children.Clear();

        var files = OfficeBackstageText.Order(this.RecentFiles);
        if (files.Count == 0)
        {
            this.homeRecents.Children.Add(ShellChrome.Text("No recent files.", 13, ShinyThemeKeys.Color.OnSurfaceVariant));
            this.openRecents.Children.Add(ShellChrome.Text("No recent files.", 13, ShinyThemeKeys.Color.OnSurfaceVariant));
            return;
        }

        foreach (var file in files)
        {
            this.homeRecents.Children.Add(this.RecentRow(file));
            this.openRecents.Children.Add(this.RecentRow(file));
        }
    }


    View RecentRow(OfficeRecentFile file)
    {
        var icon = new OfficeShellIconView { Icon = OfficeShellIcon.Document, WidthRequest = 20, HeightRequest = 20, VerticalOptions = LayoutOptions.Center };
        icon.Color = OfficeAppInfo.For(file.App ?? this.EffectiveApp).Accent.Color.ToColor();

        var texts = new VerticalStackLayout { Spacing = 0 };
        texts.Children.Add(ShellChrome.Text(file.Name, 13));
        if (!string.IsNullOrWhiteSpace(file.Location))
            texts.Children.Add(ShellChrome.Text(file.Location, 11, ShinyThemeKeys.Color.OnSurfaceVariant));

        var when = ShellChrome.Text(OfficeBackstageText.Relative(file.LastOpened, DateTimeOffset.Now), 12, ShinyThemeKeys.Color.OnSurfaceVariant);

        var grid = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 12, Padding = new Thickness(10, 6) };
        grid.Add(icon, 0);
        grid.Add(texts, 1);
        grid.Add(when, 2);

        var row = new Border { Content = grid, StrokeThickness = 0, BackgroundColor = Colors.Transparent, StrokeShape = new RoundRectangle { CornerRadius = 4 } };
        ShellChrome.OnTap(row, () => this.ChooseRecent(file));
        return row;
    }


    void BuildInfo()
    {
        if (this.infoProperties is null)
            return;

        this.infoTitle.Text = this.DocumentName ?? this.DocumentInfo?.Title ?? OfficeAppInfo.For(this.EffectiveApp).DefaultDocumentName;
        this.infoProperties.Children.Clear();

        var properties = this.DocumentInfo?.Properties() ?? [];
        if (properties.Count == 0)
        {
            this.infoProperties.Children.Add(ShellChrome.Text("No properties.", 13, ShinyThemeKeys.Color.OnSurfaceVariant));
            return;
        }

        foreach (var property in properties)
        {
            var grid = new Grid { ColumnDefinitions = { new(new GridLength(110)), new(GridLength.Star) } };
            grid.Add(ShellChrome.Text(property.Name, 12, ShinyThemeKeys.Color.OnSurfaceVariant), 0);
            var value = ShellChrome.Text(property.Value, 12);
            value.LineBreakMode = LineBreakMode.WordWrap;
            grid.Add(value, 1);
            this.infoProperties.Children.Add(grid);
        }
    }


    void BuildFormats()
    {
        if (this.saveAsFormats is null)
            return;

        var info = OfficeAppInfo.For(this.EffectiveApp);
        Fill(this.saveAsFormats, this.SaveAsFormats ?? info.SaveAsFormats, this.ChooseSaveAs);
        Fill(this.exportFormats, this.ExportFormats ?? info.ExportFormats, this.ChooseExport);

        static void Fill(VerticalStackLayout host, IReadOnlyList<OfficeFileFormat> formats, Action<OfficeFileFormat> choose)
        {
            host.Children.Clear();
            foreach (var format in formats)
            {
                var f = format;
                var card = ShellChrome.Card(Row(format.IsNative ? OfficeShellIcon.Save : OfficeShellIcon.Export, $"{format.Name} ({format.Extension})", format.Description), 12);
                card.WidthRequest = 460;
                card.HorizontalOptions = LayoutOptions.Start;
                ShellChrome.OnTap(card, () => choose(f));
                host.Children.Add(card);
            }
        }
    }


    void ApplyPrintPreview()
    {
        if (this.printPreviewHost is null)
            return;

        this.printPreviewHost.Content = this.PrintPreview;
        this.printPlaceholder.IsVisible = this.PrintPreview is null;
    }


    void OnOptionsChanged()
    {
        if (this.observedOptions is not null)
            this.observedOptions.PropertyChanged -= this.OnOptionsPropertyChanged;

        this.observedOptions = this.Options;
        if (this.observedOptions is not null)
            this.observedOptions.PropertyChanged += this.OnOptionsPropertyChanged;

        this.ShowOptions();
    }


    void OnOptionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!this.syncingOptions)
            this.ShowOptions();
    }


    void ShowOptions()
    {
        var options = this.Options;
        this.syncingOptions = true;
        try
        {
            this.optionsName.Text = options?.UserName;
            this.optionsInitials.Text = options?.Initials;
            this.optionsAutoSave.IsToggled = options?.AutoSave ?? true;
            foreach (var (choice, radio) in this.themeRadios)
                radio.IsChecked = (options?.Theme ?? OfficeThemeChoice.System) == choice;
        }
        finally
        {
            this.syncingOptions = false;
        }
    }


    void EditOptions(Action<OfficeShellOptions> edit)
    {
        if (this.syncingOptions || this.Options is not { } options)
            return;

        this.syncingOptions = true;
        try
        {
            edit(options);
        }
        finally
        {
            this.syncingOptions = false;
        }

        this.OptionsChanged?.Invoke(this, options);
    }


    static Label Heading(string text, double size = 16) => ShellChrome.Text(text, size, attributes: FontAttributes.Bold);


    static View Row(OfficeShellIcon icon, string title, string? description)
    {
        var glyph = new OfficeShellIconView { Icon = icon, WidthRequest = 28, HeightRequest = 28, VerticalOptions = LayoutOptions.Start };
        var texts = new VerticalStackLayout { Spacing = 2 };
        texts.Children.Add(ShellChrome.Text(title, 14, attributes: FontAttributes.Bold));
        if (!string.IsNullOrWhiteSpace(description))
        {
            var d = ShellChrome.Text(description, 12, ShinyThemeKeys.Color.OnSurfaceVariant);
            d.LineBreakMode = LineBreakMode.WordWrap;
            texts.Children.Add(d);
        }

        var grid = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, ColumnSpacing = 12 };
        grid.Add(glyph, 0);
        grid.Add(texts, 1);
        return grid;
    }
}
