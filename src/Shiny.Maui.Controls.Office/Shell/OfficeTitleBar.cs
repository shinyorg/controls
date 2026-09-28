using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Controls.Office.Icons;
using Shiny.Controls.Office.Shell;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// The Office window's title bar: AutoSave, Save / Undo / Redo, the document name with its
/// rename flyout, the save state, the "Search for tools, help, and more" box, help and the user's avatar.
/// </summary>
/// <remarks>
/// <para>
/// Painted in the app's accent (<see cref="App"/>), with every glyph and label in the accent's ink.
/// </para>
/// <para>
/// The two drop-downs (the rename flyout and the search results) are built in the constructor and only
/// shown or hidden. Inside an <see cref="OfficeShell"/> the shell adopts them into its overlay layer so
/// they can hang over the ribbon and be hit-tested there; standalone they sit in the bar's own grid,
/// translated below it — which draws, but on some heads cannot be tapped outside the bar's bounds.
/// </para>
/// </remarks>
public class OfficeTitleBar : ContentView
{
    public const int MaxResults = 8;

    readonly ObservableCollection<OfficeQuickAccessItem> quickAccessItems = new();
    readonly Grid root;
    readonly HorizontalStackLayout autoSaveGroup;
    readonly Switch autoSaveSwitch;
    readonly Label autoSaveLabel;
    readonly Border save;
    readonly Border undo;
    readonly Border redo;
    readonly HorizontalStackLayout extraQuickAccess;
    readonly Border nameButton;
    readonly Label nameLabel;
    readonly OfficeShellIconView nameChevron;
    readonly Label statusLabel;
    readonly Border searchBox;
    readonly Entry searchEntry;
    readonly OfficeShellIconView searchGlyph;
    readonly Border searchIcon;
    readonly Border help;
    readonly Border avatar;
    readonly Label avatarLabel;
    readonly List<OfficeShellIconView> inkIcons = [];

    // drop-downs
    readonly Border nameFlyout;
    readonly Entry renameEntry;
    readonly Label locationLabel;
    readonly Border searchPanel;
    readonly List<(Border Row, Label Title, Label Category, Label Shortcut)> resultRows = [];
    readonly Label noResults;
    IReadOnlyList<OfficeCommandMatch> results = [];
    bool adopted;
    bool searchExpanded;
    OfficeApp? inheritedApp;
    OfficeShellOptions? observedOptions;


    public static readonly BindableProperty AppProperty = Create(nameof(App), typeof(OfficeApp?), null, (t, _) => t.ApplyAccent());
    public static readonly BindableProperty AccentColorProperty = Create(nameof(AccentColor), typeof(Color), null, (t, _) => t.ApplyAccent());
    public static readonly BindableProperty DocumentNameProperty = Create(nameof(DocumentName), typeof(string), null, (t, _) => t.ApplyText(), BindingMode.TwoWay);
    public static readonly BindableProperty DocumentLocationProperty = Create(nameof(DocumentLocation), typeof(string), null, (t, _) => t.ApplyText());
    public static readonly BindableProperty SaveStateProperty = Create(nameof(SaveState), typeof(OfficeSaveState), OfficeSaveState.None, (t, _) => t.ApplyText());
    public static readonly BindableProperty SaveStatusTextProperty = Create(nameof(SaveStatusText), typeof(string), null, (t, _) => t.ApplyText());
    public static readonly BindableProperty AutoSaveProperty = Create(nameof(AutoSave), typeof(bool), false, (t, n) => t.OnAutoSaveChanged((bool)n!), BindingMode.TwoWay);
    public static readonly BindableProperty ShowAutoSaveProperty = Create(nameof(ShowAutoSave), typeof(bool), true, (t, _) => t.ApplyVisibility());
    public static readonly BindableProperty ShowSaveProperty = Create(nameof(ShowSave), typeof(bool), true, (t, _) => t.ApplyVisibility());
    public static readonly BindableProperty ShowUndoProperty = Create(nameof(ShowUndo), typeof(bool), true, (t, _) => t.ApplyVisibility());
    public static readonly BindableProperty ShowRedoProperty = Create(nameof(ShowRedo), typeof(bool), true, (t, _) => t.ApplyVisibility());
    public static readonly BindableProperty CanUndoProperty = Create(nameof(CanUndo), typeof(bool), true, (t, _) => t.ApplyVisibility());
    public static readonly BindableProperty CanRedoProperty = Create(nameof(CanRedo), typeof(bool), true, (t, _) => t.ApplyVisibility());
    public static readonly BindableProperty ShowSearchProperty = Create(nameof(ShowSearch), typeof(bool), true, (t, _) => t.ApplyVisibility());
    public static readonly BindableProperty ShowHelpProperty = Create(nameof(ShowHelp), typeof(bool), true, (t, _) => t.ApplyVisibility());
    public static readonly BindableProperty ShowAvatarProperty = Create(nameof(ShowAvatar), typeof(bool), true, (t, _) => t.ApplyVisibility());
    public static readonly BindableProperty SearchPlaceholderProperty = Create(nameof(SearchPlaceholder), typeof(string), "Search for tools, help, and more (Alt + Q)", (t, _) => t.ApplyText());
    public static readonly BindableProperty CommandIndexProperty = Create(nameof(CommandIndex), typeof(OfficeCommandIndex), null, (t, _) => t.RefreshResults());
    public static readonly BindableProperty UserNameProperty = Create(nameof(UserName), typeof(string), null, (t, _) => t.ApplyText());
    public static readonly BindableProperty OptionsProperty = Create(nameof(Options), typeof(OfficeShellOptions), null, (t, _) => t.OnOptionsChanged());
    public static readonly BindableProperty IsCompactProperty = Create(nameof(IsCompact), typeof(bool), false, (t, _) => t.ApplyVisibility());
    public static readonly BindableProperty SaveCommandProperty = Create(nameof(SaveCommand), typeof(ICommand), null, null);
    public static readonly BindableProperty UndoCommandProperty = Create(nameof(UndoCommand), typeof(ICommand), null, null);
    public static readonly BindableProperty RedoCommandProperty = Create(nameof(RedoCommand), typeof(ICommand), null, null);

    static BindableProperty Create(string name, Type type, object? defaultValue, Action<OfficeTitleBar, object?>? changed, BindingMode mode = BindingMode.OneWay)
        => BindableProperty.Create(name, type, typeof(OfficeTitleBar), defaultValue, mode,
            propertyChanged: changed is null ? null : (b, _, n) => { if (((OfficeTitleBar)b).root is not null) changed((OfficeTitleBar)b, n); });


    public OfficeTitleBar()
    {
        this.HeightRequest = 44;

        // -- left: AutoSave, quick access ----------------------------------------------------
        this.autoSaveLabel = new Label { Text = "AutoSave", FontSize = 12, VerticalTextAlignment = TextAlignment.Center };
        this.autoSaveSwitch = new Switch { VerticalOptions = LayoutOptions.Center, Scale = 0.75 };
        this.autoSaveSwitch.Toggled += (_, e) => this.AutoSave = e.Value;
        this.autoSaveGroup = new HorizontalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        this.autoSaveGroup.Children.Add(this.autoSaveLabel);
        this.autoSaveGroup.Children.Add(this.autoSaveSwitch);

        this.save = this.InkButton(OfficeShellIcon.Save, "Save (Ctrl+S)", this.RequestSave);
        this.undo = this.InkButton(OfficeShellIcon.Undo, "Undo (Ctrl+Z)", this.RequestUndo);
        this.redo = this.InkButton(OfficeShellIcon.Redo, "Redo (Ctrl+Y)", this.RequestRedo);
        this.extraQuickAccess = new HorizontalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center };

        var leftStack = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center, Padding = new Thickness(8, 0, 0, 0) };
        leftStack.Children.Add(this.autoSaveGroup);
        leftStack.Children.Add(this.save);
        leftStack.Children.Add(this.undo);
        leftStack.Children.Add(this.redo);
        leftStack.Children.Add(this.extraQuickAccess);

        // -- document name + status ------------------------------------------------------------------
        this.nameLabel = new Label { FontSize = 13, FontAttributes = FontAttributes.Bold, LineBreakMode = LineBreakMode.TailTruncation, VerticalTextAlignment = TextAlignment.Center, MaximumWidthRequest = 240 };
        this.nameChevron = new OfficeShellIconView { Icon = OfficeShellIcon.ChevronDown, WidthRequest = 12, HeightRequest = 12, VerticalOptions = LayoutOptions.Center };
        this.inkIcons.Add(this.nameChevron);
        this.statusLabel = new Label { FontSize = 12, Opacity = 0.85, VerticalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.TailTruncation };

        var nameRow = new HorizontalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
        nameRow.Children.Add(this.nameLabel);
        nameRow.Children.Add(this.nameChevron);
        this.nameButton = new Border
        {
            Content = nameRow,
            Padding = new Thickness(8, 4),
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = 4 }
        };
        ShellChrome.Hint(this.nameButton, "Document name and location");
        ShellChrome.OnTap(this.nameButton, this.ToggleDocumentFlyout);

        var nameGroup = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center, Padding = new Thickness(8, 0) };
        nameGroup.Children.Add(this.nameButton);
        nameGroup.Children.Add(this.statusLabel);

        // -- search ----------------------------------------------------------------------------------
        this.searchGlyph = new OfficeShellIconView { Icon = OfficeShellIcon.Search, WidthRequest = 14, HeightRequest = 14, VerticalOptions = LayoutOptions.Center };
        this.searchGlyph.SetDynamicResource(OfficeShellIconView.ColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        this.searchEntry = ShellChrome.FlatEntry();
        this.searchEntry.FontSize = 13;
        this.searchEntry.VerticalOptions = LayoutOptions.Center;
        this.searchEntry.BackgroundColor = Colors.Transparent;
        this.searchEntry.SetDynamicResource(Entry.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
        this.searchEntry.SetDynamicResource(Entry.PlaceholderColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        this.searchEntry.TextChanged += (_, _) => this.RefreshResults();
        this.searchEntry.Completed += (_, _) => _ = this.SubmitSearchAsync();
        this.searchEntry.Unfocused += (_, _) => this.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(200), () =>
        {
            // Delayed so a tap on a result lands before the panel it is on disappears.
            this.searchPanel!.IsVisible = false;
            if (this.IsCompact && string.IsNullOrEmpty(this.searchEntry.Text))
                this.SetSearchExpanded(false);
        });

        var searchRow = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, ColumnSpacing = 6, Padding = new Thickness(10, 0) };
        searchRow.Add(this.searchGlyph, 0);
        searchRow.Add(this.searchEntry, 1);
        this.searchBox = new Border
        {
            Content = searchRow,
            HeightRequest = 30,
            MaximumWidthRequest = 480,
            StrokeThickness = 0,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            StrokeShape = new RoundRectangle { CornerRadius = 6 }
        };
        this.searchBox.SetDynamicResource(BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerLowest);

        this.searchIcon = this.InkButton(OfficeShellIcon.Search, "Search", () => this.SetSearchExpanded(true));

        // -- right: help, avatar ---------------------------------------------------------------------
        this.help = this.InkButton(OfficeShellIcon.Help, "Help", () => this.HelpRequested?.Invoke(this, EventArgs.Empty));

        this.avatarLabel = new Label { FontSize = 12, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
        this.avatar = new Border
        {
            Content = this.avatarLabel,
            WidthRequest = 30,
            HeightRequest = 30,
            StrokeThickness = 1.5,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new Ellipse()
        };
        ShellChrome.Hint(this.avatar, "Account");
        ShellChrome.OnTap(this.avatar, () => this.AccountRequested?.Invoke(this, EventArgs.Empty));

        var rightStack = new HorizontalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center, Padding = new Thickness(0, 0, 10, 0) };
        rightStack.Children.Add(this.searchIcon);
        rightStack.Children.Add(this.help);
        rightStack.Children.Add(this.avatar);

        var bar = new Grid
        {
            ColumnDefinitions =
            {
                new(GridLength.Auto),   // AutoSave, quick access
                new(GridLength.Auto),   // document name
                new(GridLength.Star),   // search
                new(GridLength.Auto)    // help, avatar
            },
            ColumnSpacing = 4
        };
        bar.Add(leftStack, 0);
        bar.Add(nameGroup, 1);
        bar.Add(this.searchBox, 2);
        bar.Add(rightStack, 3);

        // -- drop-downs, built now -------------------------------------------------------------------
        this.renameEntry = new Entry { FontSize = 13 };
        this.renameEntry.SetDynamicResource(Entry.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
        this.renameEntry.Completed += (_, _) => this.Rename(this.renameEntry.Text);
        this.locationLabel = ShellChrome.Text(null, 12, ShinyThemeKeys.Color.OnSurfaceVariant);
        var openLocation = ShellChrome.TextButton("Open file location", () =>
        {
            this.nameFlyout!.IsVisible = false;
            this.LocationRequested?.Invoke(this, EventArgs.Empty);
        }, OfficeShellIcon.Open, null, out _);

        var flyoutStack = new VerticalStackLayout { Spacing = 8 };
        flyoutStack.Children.Add(ShellChrome.Text("File name", 12, ShinyThemeKeys.Color.OnSurfaceVariant));
        flyoutStack.Children.Add(this.renameEntry);
        flyoutStack.Children.Add(ShellChrome.Text("Location", 12, ShinyThemeKeys.Color.OnSurfaceVariant));
        flyoutStack.Children.Add(this.locationLabel);
        flyoutStack.Children.Add(openLocation);

        this.nameFlyout = Panel(flyoutStack, 300);

        var resultStack = new VerticalStackLayout { Spacing = 0 };
        for (var i = 0; i < MaxResults; i++)
        {
            var index = i;
            var title = ShellChrome.Text(null, 13);
            var category = ShellChrome.Text(null, 11, ShinyThemeKeys.Color.OnSurfaceVariant);
            var shortcut = ShellChrome.Text(null, 11, ShinyThemeKeys.Color.OnSurfaceVariant);
            var titles = new VerticalStackLayout { Spacing = 0 };
            titles.Children.Add(title);
            titles.Children.Add(category);
            var line = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, Padding = new Thickness(10, 5) };
            line.Add(titles, 0);
            line.Add(shortcut, 1);
            var row = new Border { Content = line, StrokeThickness = 0, BackgroundColor = Colors.Transparent, IsVisible = false, StrokeShape = new RoundRectangle { CornerRadius = 4 } };
            ShellChrome.OnTap(row, () => _ = this.ExecuteResultAsync(index));
            this.resultRows.Add((row, title, category, shortcut));
            resultStack.Children.Add(row);
        }

        this.noResults = ShellChrome.Text("No commands match. Press Enter to search.", 12, ShinyThemeKeys.Color.OnSurfaceVariant);
        this.noResults.Padding = new Thickness(10, 6);
        resultStack.Children.Add(this.noResults);
        this.searchPanel = Panel(resultStack, 420);

        this.root = new Grid();
        this.root.Children.Add(bar);
        this.root.Children.Add(this.nameFlyout);
        this.root.Children.Add(this.searchPanel);
        this.Content = this.root;

        this.quickAccessItems.CollectionChanged += this.OnQuickAccessChanged;

        this.ApplyAccent();
        this.ApplyText();
        this.ApplyVisibility();
    }


    // ---------------------------------------------------------------------------------------------
    // Properties
    // ---------------------------------------------------------------------------------------------

    /// <summary>The app whose accent and letter the bar wears. Null inherits from an <see cref="OfficeShell"/>, else Word.</summary>
    public OfficeApp? App { get => (OfficeApp?)this.GetValue(AppProperty); set => this.SetValue(AppProperty, value); }

    /// <summary>Overrides the app's accent.</summary>
    public Color? AccentColor { get => (Color?)this.GetValue(AccentColorProperty); set => this.SetValue(AccentColorProperty, value); }

    /// <summary>The document's name. Two-way; the rename flyout writes it.</summary>
    public string? DocumentName { get => (string?)this.GetValue(DocumentNameProperty); set => this.SetValue(DocumentNameProperty, value); }

    public string? DocumentLocation { get => (string?)this.GetValue(DocumentLocationProperty); set => this.SetValue(DocumentLocationProperty, value); }

    public OfficeSaveState SaveState { get => (OfficeSaveState)this.GetValue(SaveStateProperty); set => this.SetValue(SaveStateProperty, value); }

    /// <summary>Replaces the words <see cref="SaveState"/> would show.</summary>
    public string? SaveStatusText { get => (string?)this.GetValue(SaveStatusTextProperty); set => this.SetValue(SaveStatusTextProperty, value); }

    /// <summary>The AutoSave switch. Two-way.</summary>
    public bool AutoSave { get => (bool)this.GetValue(AutoSaveProperty); set => this.SetValue(AutoSaveProperty, value); }

    public bool ShowAutoSave { get => (bool)this.GetValue(ShowAutoSaveProperty); set => this.SetValue(ShowAutoSaveProperty, value); }
    public bool ShowSave { get => (bool)this.GetValue(ShowSaveProperty); set => this.SetValue(ShowSaveProperty, value); }
    public bool ShowUndo { get => (bool)this.GetValue(ShowUndoProperty); set => this.SetValue(ShowUndoProperty, value); }
    public bool ShowRedo { get => (bool)this.GetValue(ShowRedoProperty); set => this.SetValue(ShowRedoProperty, value); }
    public bool CanUndo { get => (bool)this.GetValue(CanUndoProperty); set => this.SetValue(CanUndoProperty, value); }
    public bool CanRedo { get => (bool)this.GetValue(CanRedoProperty); set => this.SetValue(CanRedoProperty, value); }
    public bool ShowSearch { get => (bool)this.GetValue(ShowSearchProperty); set => this.SetValue(ShowSearchProperty, value); }
    public bool ShowHelp { get => (bool)this.GetValue(ShowHelpProperty); set => this.SetValue(ShowHelpProperty, value); }
    public bool ShowAvatar { get => (bool)this.GetValue(ShowAvatarProperty); set => this.SetValue(ShowAvatarProperty, value); }

    public string SearchPlaceholder { get => (string)this.GetValue(SearchPlaceholderProperty); set => this.SetValue(SearchPlaceholderProperty, value); }

    /// <summary>What the search box searches and runs. Fill it with <c>index.AddRibbon(ribbon)</c> plus the editor's own commands.</summary>
    public OfficeCommandIndex? CommandIndex { get => (OfficeCommandIndex?)this.GetValue(CommandIndexProperty); set => this.SetValue(CommandIndexProperty, value); }

    /// <summary>The user's name, for the avatar's initials. <see cref="Options"/> wins when set.</summary>
    public string? UserName { get => (string?)this.GetValue(UserNameProperty); set => this.SetValue(UserNameProperty, value); }

    /// <summary>The shell's options; the avatar follows its initials and AutoSave follows its switch.</summary>
    public OfficeShellOptions? Options { get => (OfficeShellOptions?)this.GetValue(OptionsProperty); set => this.SetValue(OptionsProperty, value); }

    /// <summary>Phone width: the search box collapses to an icon. Set by <see cref="OfficeShell"/>.</summary>
    public bool IsCompact { get => (bool)this.GetValue(IsCompactProperty); set => this.SetValue(IsCompactProperty, value); }

    public ICommand? SaveCommand { get => (ICommand?)this.GetValue(SaveCommandProperty); set => this.SetValue(SaveCommandProperty, value); }
    public ICommand? UndoCommand { get => (ICommand?)this.GetValue(UndoCommandProperty); set => this.SetValue(UndoCommandProperty, value); }
    public ICommand? RedoCommand { get => (ICommand?)this.GetValue(RedoCommandProperty); set => this.SetValue(RedoCommandProperty, value); }

    /// <summary>Quick access buttons after Save / Undo / Redo.</summary>
    public IList<OfficeQuickAccessItem> QuickAccessItems => this.quickAccessItems;

    /// <summary>The current search results, best first.</summary>
    public IReadOnlyList<OfficeCommandMatch> SearchResults => this.results;

    public bool IsDocumentFlyoutOpen => this.nameFlyout.IsVisible;


    public event EventHandler? SaveRequested;
    public event EventHandler? UndoRequested;
    public event EventHandler? RedoRequested;
    public event EventHandler<string>? DocumentRenamed;
    public event EventHandler? LocationRequested;
    public event EventHandler? HelpRequested;
    public event EventHandler? AccountRequested;

    /// <summary>Enter was pressed with no matching command — the host may search the document instead.</summary>
    public event EventHandler<string>? SearchSubmitted;


    // ---------------------------------------------------------------------------------------------
    // Actions (public seams)
    // ---------------------------------------------------------------------------------------------

    public void RequestSave()
    {
        if (this.SaveCommand?.CanExecute(null) == true)
            this.SaveCommand.Execute(null);
        this.SaveRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestUndo()
    {
        if (!this.CanUndo) return;
        if (this.UndoCommand?.CanExecute(null) == true)
            this.UndoCommand.Execute(null);
        this.UndoRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestRedo()
    {
        if (!this.CanRedo) return;
        if (this.RedoCommand?.CanExecute(null) == true)
            this.RedoCommand.Execute(null);
        this.RedoRequested?.Invoke(this, EventArgs.Empty);
    }


    /// <summary>Renames the document as the flyout's field would. Blank names are ignored.</summary>
    public void Rename(string? name)
    {
        this.nameFlyout.IsVisible = false;
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == this.DocumentName)
            return;

        this.DocumentName = name.Trim();
        this.DocumentRenamed?.Invoke(this, this.DocumentName);
    }


    public void ToggleDocumentFlyout()
    {
        var open = !this.nameFlyout.IsVisible;
        this.searchPanel.IsVisible = false;

        if (open)
        {
            this.renameEntry.Text = this.DocumentName;
            this.locationLabel.Text = string.IsNullOrWhiteSpace(this.DocumentLocation) ? "Not saved yet" : this.DocumentLocation;
            this.Place(this.nameFlyout, this.nameButton);
        }

        this.nameFlyout.IsVisible = open;
    }


    /// <summary>Types into the search box. Test seam.</summary>
    public void Search(string? text)
    {
        this.searchEntry.Text = text;
        this.RefreshResults();
    }


    /// <summary>Runs the best match for the box's text, or raises <see cref="SearchSubmitted"/>.</summary>
    public async Task SubmitSearchAsync()
    {
        var text = this.searchEntry.Text ?? string.Empty;
        if (this.results.Count > 0)
        {
            await this.ExecuteResultAsync(0);
            return;
        }

        if (!string.IsNullOrWhiteSpace(text))
            this.SearchSubmitted?.Invoke(this, text);
    }


    /// <summary>Runs the result at <paramref name="index"/> and clears the box.</summary>
    public async Task ExecuteResultAsync(int index)
    {
        if (index < 0 || index >= this.results.Count)
            return;

        var command = this.results[index].Command;
        if (!command.IsEnabled)
            return;

        this.searchEntry.Text = string.Empty;
        this.searchPanel.IsVisible = false;
        if (this.IsCompact)
            this.SetSearchExpanded(false);

        await command.Execute();
    }


    // ---------------------------------------------------------------------------------------------
    // Shell hand-off
    // ---------------------------------------------------------------------------------------------

    internal OfficeApp EffectiveApp => this.App ?? this.inheritedApp ?? OfficeApp.Word;

    internal void InheritApp(OfficeApp app)
    {
        this.inheritedApp = app;
        this.ApplyAccent();
    }


    /// <summary>Moves the two drop-downs into the shell's overlay layer so they can hang over the ribbon.</summary>
    internal void AdoptPanels(Layout overlay)
    {
        if (this.adopted)
            return;

        this.adopted = true;
        this.root.Children.Remove(this.nameFlyout);
        this.root.Children.Remove(this.searchPanel);
        this.nameFlyout.TranslationY = this.searchPanel.TranslationY = 0;
        overlay.Children.Add(this.nameFlyout);
        overlay.Children.Add(this.searchPanel);
    }


    void Place(View panel, VisualElement anchor)
    {
        var at = ShellChrome.OffsetWithin(anchor, this);
        var y = this.Height > 0 ? this.Height : this.HeightRequest;

        if (this.adopted)
        {
            // The shell's overlay starts at the shell's top-left, and the title bar is its first row.
            var origin = ShellChrome.Ancestor<OfficeShell>(this) is { } shell ? ShellChrome.OffsetWithin(this, shell) : Point.Zero;
            panel.Margin = new Thickness(origin.X + at.X, origin.Y + y, 0, 0);
        }
        else
        {
            panel.TranslationX = at.X;
            panel.TranslationY = y;
        }
    }


    // ---------------------------------------------------------------------------------------------
    // Rendering
    // ---------------------------------------------------------------------------------------------

    Border InkButton(OfficeShellIcon icon, string hint, Action action)
    {
        var button = ShellChrome.IconButton(icon, hint, action);
        this.inkIcons.Add((OfficeShellIconView)button.Content!);
        return button;
    }


    static Border Panel(View content, double width)
    {
        var panel = new Border
        {
            Content = content,
            Padding = 10,
            WidthRequest = width,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            StrokeThickness = 1,
            IsVisible = false,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Shadow = new Shadow { Radius = 12, Opacity = 0.25f, Offset = new Point(0, 4), Brush = Colors.Black }
        };
        panel.SetDynamicResource(Border.StrokeProperty, ShinyThemeKeys.Brush.OutlineVariant);
        panel.SetDynamicResource(BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHigh);
        return panel;
    }


    void ApplyAccent()
    {
        var info = OfficeAppInfo.For(this.EffectiveApp);
        var accent = this.AccentColor ?? info.Accent.Color.ToColor();
        var ink = this.AccentColor is { } custom
            ? OfficeAccent.InkFor(new ArgbColor((byte)(custom.Alpha * 255), (byte)(custom.Red * 255), (byte)(custom.Green * 255), (byte)(custom.Blue * 255))).ToColor()
            : info.Accent.Ink.ToColor();

        this.BackgroundColor = accent;

        foreach (var icon in this.inkIcons)
            icon.Color = ink;

        this.nameLabel.TextColor = ink;
        this.statusLabel.TextColor = ink;
        this.autoSaveLabel.TextColor = ink;
        this.avatarLabel.TextColor = ink;
        this.avatar.Stroke = ink;
        this.avatar.BackgroundColor = Colors.Transparent;
        this.RebuildQuickAccess(ink);
    }


    void ApplyText()
    {
        this.nameLabel.Text = string.IsNullOrWhiteSpace(this.DocumentName)
            ? OfficeAppInfo.For(this.EffectiveApp).DefaultDocumentName
            : this.DocumentName;

        var status = this.SaveStatusText ?? OfficeSaveStateText.For(this.SaveState);
        this.statusLabel.Text = status is null ? null : "· " + status;
        this.statusLabel.IsVisible = status is not null && !this.IsCompact;

        this.searchEntry.Placeholder = this.SearchPlaceholder;
        this.avatarLabel.Text = this.Options?.EffectiveInitials ?? OfficeColorText.Initials(this.UserName);
        ShellChrome.Hint(this.avatar, this.Options?.UserName ?? this.UserName ?? "Account");
    }


    void ApplyVisibility()
    {
        this.autoSaveGroup.IsVisible = this.ShowAutoSave && !this.IsCompact;
        this.save.IsVisible = this.ShowSave;
        this.undo.IsVisible = this.ShowUndo;
        this.redo.IsVisible = this.ShowRedo && !this.IsCompact;
        ShellChrome.SetEnabled(this.undo, this.CanUndo);
        ShellChrome.SetEnabled(this.redo, this.CanRedo);
        this.help.IsVisible = this.ShowHelp && !this.IsCompact;
        this.avatar.IsVisible = this.ShowAvatar;
        this.SetSearchExpanded(this.searchExpanded);
        this.ApplyText();
    }


    void SetSearchExpanded(bool expanded)
    {
        this.searchExpanded = expanded && this.IsCompact;
        var showBox = this.ShowSearch && (!this.IsCompact || this.searchExpanded);
        this.searchBox.IsVisible = showBox;
        this.searchIcon.IsVisible = this.ShowSearch && this.IsCompact && !this.searchExpanded;
        this.nameButton.IsVisible = !this.searchExpanded;

        if (this.searchExpanded)
            this.searchEntry.Focus();
    }


    void RefreshResults()
    {
        var text = this.searchEntry.Text;
        this.results = this.CommandIndex?.Search(text, MaxResults) ?? [];

        for (var i = 0; i < this.resultRows.Count; i++)
        {
            var (row, title, category, shortcut) = this.resultRows[i];
            if (i < this.results.Count)
            {
                var command = this.results[i].Command;
                title.Text = command.Label;
                category.Text = command.Category;
                category.IsVisible = !string.IsNullOrWhiteSpace(command.Category);
                shortcut.Text = command.Shortcut;
                row.Opacity = command.IsEnabled ? 1 : 0.45;
                row.IsVisible = true;
            }
            else
                row.IsVisible = false;
        }

        this.noResults.IsVisible = this.results.Count == 0;

        var show = !string.IsNullOrWhiteSpace(text) && this.ShowSearch;
        if (show)
        {
            this.nameFlyout.IsVisible = false;
            this.Place(this.searchPanel, this.searchBox);
        }

        this.searchPanel.IsVisible = show;
    }


    void OnAutoSaveChanged(bool value)
    {
        if (this.autoSaveSwitch.IsToggled != value)
            this.autoSaveSwitch.IsToggled = value;

        if (this.Options is { } options && options.AutoSave != value)
            options.AutoSave = value;
    }


    void OnOptionsChanged()
    {
        if (this.observedOptions is not null)
            this.observedOptions.PropertyChanged -= this.OnOptionsPropertyChanged;

        this.observedOptions = this.Options;
        if (this.observedOptions is not null)
        {
            this.observedOptions.PropertyChanged += this.OnOptionsPropertyChanged;
            this.AutoSave = this.observedOptions.AutoSave;
        }

        this.ApplyText();
    }


    void OnOptionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (this.Options is { } options)
            this.AutoSave = options.AutoSave;

        this.ApplyText();
    }


    void OnQuickAccessChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (OfficeQuickAccessItem item in e.OldItems)
                item.PropertyChanged -= this.OnQuickAccessItemChanged;

        if (e.NewItems is not null)
            foreach (OfficeQuickAccessItem item in e.NewItems)
                item.PropertyChanged += this.OnQuickAccessItemChanged;

        this.ApplyAccent();
    }


    void OnQuickAccessItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        var index = this.quickAccessItems.IndexOf((OfficeQuickAccessItem)sender!);
        if (index >= 0 && index < this.extraQuickAccess.Children.Count && this.extraQuickAccess.Children[index] is View view)
        {
            view.IsVisible = this.quickAccessItems[index].IsVisible;
            ShellChrome.SetEnabled(view, this.quickAccessItems[index].IsEnabled);
        }
    }


    void RebuildQuickAccess(Color ink)
    {
        // Quick access items are declared with the bar, before layout. Adding one later works on every
        // head but AppKit, which does not realise children added after the page was laid out.
        if (this.extraQuickAccess.Children.Count == this.quickAccessItems.Count)
        {
            foreach (var child in this.extraQuickAccess.Children)
                if (child is Border { Content: OfficeShellIconView icon })
                    icon.Color = ink;

            return;
        }

        this.extraQuickAccess.Children.Clear();
        foreach (var item in this.quickAccessItems)
        {
            var captured = item;
            var button = ShellChrome.IconButton(item.Icon, item.Tooltip, () =>
            {
                if (captured.IsEnabled)
                    _ = captured.Execute();
            }, ink);
            button.IsVisible = item.IsVisible;
            ShellChrome.SetEnabled(button, item.IsEnabled);
            this.extraQuickAccess.Children.Add(button);
        }
    }
}
