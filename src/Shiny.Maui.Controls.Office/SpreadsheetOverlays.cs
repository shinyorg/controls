using Shiny.Controls.Office.Spreadsheet.Calc;

namespace Shiny.Maui.Controls.Office;

/// <summary>The colours an overlay card is drawn in, taken from the grid's theme.</summary>
readonly record struct OverlayPalette(Color Surface, Color Ink, Color Muted, Color Rule, Color Header, Color Accent, Color AccentInk, Color Selected)
{
    public static OverlayPalette From(SpreadsheetTheme theme)
    {
        var accent = OfficeAccent.Spreadsheet;
        return new OverlayPalette(
            OfficeScheme.ToMauiColor(theme.Background),
            OfficeScheme.ToMauiColor(theme.CellText),
            OfficeScheme.ToMauiColor(theme.HeaderText),
            OfficeScheme.ToMauiColor(theme.HeaderBorder),
            OfficeScheme.ToMauiColor(theme.HeaderBackground),
            OfficeScheme.ToMauiColor(accent.Color),
            OfficeScheme.ToMauiColor(accent.Ink),
            OfficeScheme.ToMauiColor(theme.HeaderSelectedBackground));
    }
}

/// <summary>
/// Renders a <see cref="SheetDialog"/> as a modal card over the spreadsheet.
/// </summary>
/// <remarks>
/// <para>
/// In the view's own tree rather than pushed as a page. A modal page needs a navigation stack the host
/// may not have — a spreadsheet embedded in a Shell tab, a flyout, a desktop window's content — and it
/// would take the card away from the grid it is about, which a filter dropdown in particular should sit
/// over.
/// </para>
/// <para>
/// The dialog is data: every field kind is rendered here once, and what a field means lives in the
/// kernel's <see cref="SpreadsheetDialogs"/>, shared with the Blazor host. A change the dialog makes to
/// itself — a search narrowing a list, a type choice hiding fields — is applied by refreshing the views
/// in place rather than rebuilding them, so the entry being typed into keeps its focus.
/// </para>
/// </remarks>
sealed class SheetDialogHost : ContentView
{
    readonly Grid scrim;
    readonly Border card;
    readonly List<FieldView> views = [];

    SheetDialog? dialog;
    OverlayPalette palette;
    int tab;
    readonly Shiny.Maui.Controls.Infrastructure.AndroidBackButton back;
    bool suppress;
    Label? error;

    public SheetDialogHost()
    {
        this.scrim = new Grid { BackgroundColor = Color.FromRgba(0, 0, 0, 0.35) };

        // Taps on the backdrop are swallowed: a dialog closes through its buttons, as in Excel.
        this.scrim.GestureRecognizers.Add(new TapGestureRecognizer());

        this.card = new Border
        {
            StrokeThickness = 1,
            Padding = 0,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 }
        };

        this.scrim.Add(this.card);
        this.Content = this.scrim;
        this.IsVisible = false;

        // Android's back button is the dialog's Cancel, not a way off the page with it still open.
        this.back = new Shiny.Maui.Controls.Infrastructure.AndroidBackButton(this.PressCancel);
        this.Unloaded += (_, _) => this.back.SetActive(false);
    }

    void PressCancel()
    {
        if (this.dialog?.Buttons.FirstOrDefault(x => x.IsCancel) is { } cancel)
            this.Press(cancel);
        else
            this.Close();
    }

    /// <summary>Raised when the last dialog in a chain closes.</summary>
    public event EventHandler? Closed;

    public bool IsOpen => this.dialog is not null;

    public void Show(SheetDialog next, SpreadsheetTheme theme)
    {
        if (this.dialog is not null)
            this.dialog.Changed -= this.OnDialogChanged;

        this.dialog = next;
        this.CurrentTheme = theme;
        this.palette = OverlayPalette.From(theme);
        this.tab = 0;
        next.Changed += this.OnDialogChanged;

        this.Build();
        this.IsVisible = true;
        this.back.SetActive(true);
    }

    public void Close()
    {
        if (this.dialog is not null)
            this.dialog.Changed -= this.OnDialogChanged;

        this.dialog = null;
        this.views.Clear();
        this.card.Content = null;
        this.IsVisible = false;
        this.back.SetActive(false);
        this.Closed?.Invoke(this, EventArgs.Empty);
    }

    void OnDialogChanged(object? sender, EventArgs e) => this.Refresh();

    void Build()
    {
        if (this.dialog is not { } dialog)
            return;

        var p = this.palette;
        this.card.BackgroundColor = p.Surface;
        this.card.Stroke = p.Rule;
        this.card.WidthRequest = Math.Min(dialog.Width, Math.Max(260, (this.Width > 0 ? this.Width : 800) - 24));

        var stack = new VerticalStackLayout { Spacing = 10, Padding = new Thickness(16, 14) };

        stack.Add(new Label { Text = dialog.Title, FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = p.Ink });

        if (dialog.Tabs.Count > 1)
        {
            var strip = new HorizontalStackLayout { Spacing = 2 };
            for (var i = 0; i < dialog.Tabs.Count; i++)
            {
                var index = i;
                var selected = i == this.tab;
                var button = new Button
                {
                    Text = dialog.Tabs[i].Title,
                    FontSize = 12,
                    Padding = new Thickness(10, 4),
                    CornerRadius = 4,
                    MinimumHeightRequest = 28,
                    BackgroundColor = selected ? p.Selected : p.Header,
                    TextColor = p.Ink
                };

                button.Clicked += (_, _) =>
                {
                    this.tab = index;
                    this.Build();
                };

                strip.Add(button);
            }

            stack.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = strip });
        }

        this.views.Clear();
        var body = new VerticalStackLayout { Spacing = 8 };
        foreach (var field in dialog.Tabs[Math.Clamp(this.tab, 0, dialog.Tabs.Count - 1)].Fields)
        {
            var view = this.CreateField(field);
            this.views.Add(view);
            body.Add(view.Row);
        }

        // The fields scroll, the title and buttons do not - so the scroller gets what the host has left
        // after them. A fixed 420 overflowed any editor shorter than the dialog (a tablet's sample page,
        // every phone in landscape) and cut OK / Cancel off below the card with no way to reach them.
        var reserve = (dialog.Tabs.Count > 1 ? 60 : 20) + 170;
        // The host is hidden until Show makes it visible - after this runs - and a hidden view has no
        // size, so its parent (which it fills) is the measure on the first open.
        var available = this.Height > 0 ? this.Height : (this.Parent as VisualElement)?.Height ?? 0;
        if (available <= 0)
            available = 800;

        stack.Add(new ScrollView { Content = body, MaximumHeightRequest = Math.Clamp(available - reserve, 100, 420) });

        this.error = new Label { TextColor = Colors.Firebrick, FontSize = 12, IsVisible = false };
        stack.Add(this.error);

        var buttons = new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            JustifyContent = Microsoft.Maui.Layouts.FlexJustify.End
        };

        foreach (var model in dialog.Buttons)
        {
            var button = new Button
            {
                Text = model.Text,
                FontSize = 13,
                Margin = new Thickness(4, 2),
                Padding = new Thickness(12, 4),
                CornerRadius = 4,
                MinimumHeightRequest = 32,
                BackgroundColor = model.IsPrimary ? p.Accent : p.Header,
                TextColor = model.IsPrimary ? p.AccentInk : p.Ink,
                BorderColor = p.Rule,
                BorderWidth = model.IsPrimary ? 0 : 1
            };

            button.Clicked += (_, _) => this.Press(model);
            buttons.Add(button);
        }

        stack.Add(buttons);
        this.card.Content = stack;
        this.Refresh();
    }

    void Press(SheetDialogButton button)
    {
        if (this.dialog is not { } current)
            return;

        if (!current.Press(button))
            return;

        if (current.Next is { } next)
        {
            this.Show(next, this.CurrentTheme);
            return;
        }

        this.Close();
    }

    /// <summary>The theme the card was opened with, kept so a chained dialog matches it.</summary>
    SpreadsheetTheme CurrentTheme { get; set; } = OfficeScheme.Default;

    void Refresh()
    {
        if (this.dialog is not { } dialog)
            return;

        this.suppress = true;
        foreach (var view in this.views)
        {
            view.Row.IsVisible = view.Field.IsVisible;
            view.Row.IsEnabled = view.Field.IsEnabled;

            if (view.Caption is not null)
                view.Caption.Text = view.Field.Label;

            view.Refresh();
        }

        this.suppress = false;

        if (this.error is not null)
        {
            this.error.Text = dialog.Error ?? string.Empty;
            this.error.IsVisible = !string.IsNullOrEmpty(dialog.Error);
        }
    }

    void Changed(SheetField field)
    {
        if (!this.suppress)
            this.dialog?.OnFieldChanged(field);
    }

    sealed record FieldView(SheetField Field, View Row, Label? Caption, Action Refresh);

    FieldView CreateField(SheetField field)
    {
        var p = this.palette;

        Label Caption() => new() { Text = field.Label, FontSize = 12, TextColor = p.Muted };

        VerticalStackLayout Row(Label? caption, View control)
        {
            var row = new VerticalStackLayout { Spacing = 3 };
            if (caption is not null)
                row.Add(caption);

            row.Add(control);
            return row;
        }

        switch (field.Kind)
        {
            case SheetFieldKind.Label:
            {
                var label = new Label { FontSize = 13, TextColor = p.Ink, LineBreakMode = LineBreakMode.WordWrap };
                return new FieldView(field, label, null, () => label.Text = field.Label);
            }

            case SheetFieldKind.Text or SheetFieldKind.Number:
            {
                var caption = Caption();
                var entry = new Entry
                {
                    Text = field.Text,
                    Placeholder = field.Placeholder,
                    FontSize = 13,
                    TextColor = p.Ink,
                    PlaceholderColor = p.Muted,
                    Keyboard = field.Kind == SheetFieldKind.Number ? Keyboard.Numeric : Keyboard.Default
                };

                entry.TextChanged += (_, e) =>
                {
                    if (this.suppress)
                        return;

                    field.Text = e.NewTextValue ?? string.Empty;
                    this.Changed(field);
                };

                entry.Completed += (_, _) =>
                {
                    if (this.dialog?.Primary is { } primary)
                        this.Press(primary);
                };

                return new FieldView(field, Row(caption, entry), caption, () =>
                {
                    if (!entry.IsFocused && entry.Text != field.Text)
                        entry.Text = field.Text;
                });
            }

            case SheetFieldKind.MultilineText:
            {
                var caption = Caption();
                var editor = new Editor { Text = field.Text, FontSize = 13, TextColor = p.Ink, HeightRequest = 96 };
                editor.TextChanged += (_, e) =>
                {
                    if (this.suppress)
                        return;

                    field.Text = e.NewTextValue ?? string.Empty;
                    this.Changed(field);
                };

                return new FieldView(field, Row(caption, editor), caption, () =>
                {
                    if (!editor.IsFocused && editor.Text != field.Text)
                        editor.Text = field.Text;
                });
            }

            case SheetFieldKind.Choice:
            {
                var caption = Caption();
                var picker = new Picker { FontSize = 13, TextColor = p.Ink, ItemsSource = field.Options.ToList() };
                IReadOnlyList<string> shown = field.Options;

                picker.SelectedIndexChanged += (_, _) =>
                {
                    if (this.suppress)
                        return;

                    field.SelectedIndex = picker.SelectedIndex;
                    this.Changed(field);
                };

                return new FieldView(field, Row(caption, picker), caption, () =>
                {
                    if (!ReferenceEquals(shown, field.Options))
                    {
                        shown = field.Options;
                        picker.ItemsSource = field.Options.ToList();
                    }

                    if (picker.SelectedIndex != field.SelectedIndex)
                        picker.SelectedIndex = field.SelectedIndex;
                });
            }

            case SheetFieldKind.Check:
            {
                var box = new CheckBox { IsChecked = field.IsChecked, Color = p.Accent };
                var label = new Label { Text = field.Label, FontSize = 13, TextColor = p.Ink, VerticalOptions = LayoutOptions.Center };
                var row = new HorizontalStackLayout { Spacing = 4, Children = { box, label } };

                box.CheckedChanged += (_, e) =>
                {
                    if (this.suppress)
                        return;

                    field.IsChecked = e.Value;
                    this.Changed(field);
                };

                label.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => box.IsChecked = !box.IsChecked) });

                return new FieldView(field, row, label, () =>
                {
                    if (box.IsChecked != field.IsChecked)
                        box.IsChecked = field.IsChecked;
                });
            }

            case SheetFieldKind.Color:
                return this.ColorField(field, Caption());

            case SheetFieldKind.Checklist:
                return this.ChecklistField(field, Caption());

            default:
                return this.ListField(field, Caption());
        }
    }

    FieldView ColorField(SheetField field, Label caption)
    {
        var p = this.palette;
        var swatches = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        var borders = new List<(Border View, ArgbColor? Color)>();

        void Add(ArgbColor? color, string name)
        {
            var swatch = new Border
            {
                WidthRequest = color is null ? 76 : 24,
                HeightRequest = 24,
                Margin = new Thickness(2),
                StrokeThickness = 1,
                Stroke = p.Rule,
                BackgroundColor = color is { } c ? OfficeScheme.ToMauiColor(c) : p.Surface,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 3 },
                Content = color is null ? new Label { Text = name, FontSize = 11, TextColor = p.Ink, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center } : null
            };

            SemanticProperties.SetDescription(swatch, name);
            swatch.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() =>
                {
                    field.Color = color;
                    this.Changed(field);
                    Sync();
                })
            });

            borders.Add((swatch, color));
            swatches.Add(swatch);
        }

        Add(null, field.Key is "fontColor" or "lineColor" ? "Automatic" : "No Color");
        foreach (var (name, color) in SheetField.Palette)
            Add(color, name);

        void Sync()
        {
            foreach (var (view, color) in borders)
            {
                var selected = color == field.Color;
                view.StrokeThickness = selected ? 3 : 1;
                view.Stroke = selected ? p.Accent : p.Rule;
            }
        }

        var row = new VerticalStackLayout { Spacing = 3, Children = { caption, swatches } };
        return new FieldView(field, row, caption, Sync);
    }

    FieldView ChecklistField(SheetField field, Label caption)
    {
        var p = this.palette;
        var rows = new VerticalStackLayout { Spacing = 0 };
        var boxes = new List<(CheckBox Box, SheetChecklistItem Item)>();
        List<SheetChecklistItem>? shown = null;

        void Rebuild()
        {
            rows.Clear();
            boxes.Clear();

            foreach (var item in field.Items)
            {
                var box = new CheckBox { IsChecked = item.IsChecked, Color = p.Accent };
                var label = new Label { Text = item.Text, FontSize = 13, TextColor = p.Ink, VerticalOptions = LayoutOptions.Center };

                box.CheckedChanged += (_, e) =>
                {
                    if (this.suppress)
                        return;

                    item.IsChecked = e.Value;
                    this.Changed(field);
                };

                label.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => box.IsChecked = !box.IsChecked) });
                rows.Add(new HorizontalStackLayout { Spacing = 2, Children = { box, label } });
                boxes.Add((box, item));
            }
        }

        var scroll = new ScrollView
        {
            Content = rows,
            MaximumHeightRequest = 200
        };

        var frame = new Border { Stroke = p.Rule, StrokeThickness = 1, Padding = new Thickness(4, 0), Content = scroll };
        var row = new VerticalStackLayout { Spacing = 3, Children = { caption, frame } };

        return new FieldView(field, row, caption, () =>
        {
            if (!ReferenceEquals(shown, field.Items))
            {
                shown = field.Items;
                Rebuild();
                return;
            }

            foreach (var (box, item) in boxes)
            {
                if (box.IsChecked != item.IsChecked)
                    box.IsChecked = item.IsChecked;
            }
        });
    }

    FieldView ListField(SheetField field, Label caption)
    {
        var p = this.palette;
        var rows = new VerticalStackLayout { Spacing = 0 };
        var cells = new List<Grid>();
        IReadOnlyList<string>? shown = null;

        void Highlight()
        {
            for (var i = 0; i < cells.Count; i++)
                cells[i].BackgroundColor = i == field.SelectedIndex ? p.Selected : Colors.Transparent;
        }

        void Rebuild()
        {
            rows.Clear();
            cells.Clear();

            for (var i = 0; i < field.Options.Count; i++)
            {
                var index = i;
                var line = new Grid { Padding = new Thickness(6, 4), RowSpacing = 1 };
                line.AddRowDefinition(new RowDefinition(GridLength.Auto));
                line.AddRowDefinition(new RowDefinition(GridLength.Auto));
                line.Add(new Label { Text = field.Options[i], FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = p.Ink });

                if (field.Details is { } details && i < details.Count && !string.IsNullOrEmpty(details[i]))
                    line.Add(new Label { Text = details[i], FontSize = 11, TextColor = p.Muted, LineBreakMode = LineBreakMode.TailTruncation }, 0, 1);

                line.GestureRecognizers.Add(new TapGestureRecognizer
                {
                    Command = new Command(() =>
                    {
                        field.SelectedIndex = index;
                        Highlight();
                        this.Changed(field);
                    })
                });

                // A double tap is the list's "OK", as double-clicking a function is in Excel.
                line.GestureRecognizers.Add(new TapGestureRecognizer
                {
                    NumberOfTapsRequired = 2,
                    Command = new Command(() =>
                    {
                        field.SelectedIndex = index;
                        if (this.dialog?.Primary is { } primary)
                            this.Press(primary);
                    })
                });

                cells.Add(line);
                rows.Add(line);
            }

            Highlight();
        }

        var frame = new Border
        {
            Stroke = p.Rule,
            StrokeThickness = 1,
            Content = new ScrollView { Content = rows, HeightRequest = 220 }
        };

        var row = new VerticalStackLayout { Spacing = 3, Children = { caption, frame } };
        return new FieldView(field, row, caption, () =>
        {
            if (!ReferenceEquals(shown, field.Options))
            {
                shown = field.Options;
                Rebuild();
                return;
            }

            Highlight();
        });
    }
}

/// <summary>
/// Renders a <see cref="SheetMenuRequest"/> — the right-click menu, a validated cell's list — as a popup
/// card at a point on the view.
/// </summary>
/// <remarks>
/// Submenus drill in rather than fly out: a flyout needs room to the right that a phone does not have,
/// and one level of "Back" is how a touch menu reads anyway.
/// </remarks>
sealed class SheetMenuHost : ContentView
{
    readonly AbsoluteLayout surface = new();
    readonly Border card;
    readonly VerticalStackLayout lines = new() { Spacing = 0 };
    readonly Stack<IReadOnlyList<SheetMenuItem>> path = new();

    OverlayPalette palette;
    Point origin;
    readonly Shiny.Maui.Controls.Infrastructure.AndroidBackButton back;

    public SheetMenuHost()
    {
        var backdrop = new BoxView { Color = Colors.Transparent };
        backdrop.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(this.Close) });

        this.surface.Add(backdrop);
        AbsoluteLayout.SetLayoutFlags(backdrop, AbsoluteLayoutFlags.All);
        AbsoluteLayout.SetLayoutBounds(backdrop, new Rect(0, 0, 1, 1));

        this.card = new Border
        {
            StrokeThickness = 1,
            Padding = new Thickness(0, 4),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            Content = new ScrollView { Content = this.lines, MaximumHeightRequest = 420 },
            Shadow = new Shadow { Opacity = 0.25f, Radius = 8, Offset = new Point(0, 2) }
        };

        this.surface.Add(this.card);
        this.Content = this.surface;
        this.IsVisible = false;

        this.back = new Shiny.Maui.Controls.Infrastructure.AndroidBackButton(this.Close);
        this.Unloaded += (_, _) => this.back.SetActive(false);

        // The first show happens before the host has a size, so the clamp to its edges is redone once it
        // has one.
        this.SizeChanged += (_, _) =>
        {
            if (this.IsVisible && this.path.Count > 0)
                this.Render();
        };
    }

    public bool IsOpen => this.IsVisible;

    public void Show(SheetMenuRequest request, Point at, SpreadsheetTheme theme)
    {
        this.palette = OverlayPalette.From(theme);
        this.origin = at;
        this.path.Clear();
        this.path.Push(request.Items);
        this.IsVisible = true;
        this.back.SetActive(true);
        this.Render();
    }

    public void Close()
    {
        this.back.SetActive(false);
        this.IsVisible = false;
        this.lines.Clear();
        this.path.Clear();
    }

    void Render()
    {
        var p = this.palette;
        this.card.BackgroundColor = p.Surface;
        this.card.Stroke = p.Rule;
        this.lines.Clear();

        if (this.path.Count > 1)
        {
            this.lines.Add(this.Line("< Back", null, false, true, () =>
            {
                this.path.Pop();
                this.Render();
            }));

            this.lines.Add(new BoxView { HeightRequest = 1, Color = p.Rule, Margin = new Thickness(0, 3) });
        }

        foreach (var item in this.path.Peek())
        {
            if (item.IsSeparator)
            {
                this.lines.Add(new BoxView { HeightRequest = 1, Color = p.Rule, Margin = new Thickness(0, 3) });
                continue;
            }

            this.lines.Add(this.Line(
                item.Text + (item.HasChildren ? "  >" : string.Empty),
                item.Shortcut,
                item.IsChecked,
                item.IsEnabled,
                () =>
                {
                    if (item.HasChildren)
                    {
                        this.path.Push(item.Children!);
                        this.Render();
                        return;
                    }

                    this.Close();
                    item.Action?.Invoke();
                }));
        }

        // Where it was asked for, pulled back inside the view when it would run off an edge.
        const double width = 240;
        var height = Math.Min(430, this.path.Peek().Count * 30 + 16);
        var bounds = this.Width > 0 ? this.Width : 800;
        var tall = this.Height > 0 ? this.Height : 600;
        var x = Math.Clamp(this.origin.X, 4, Math.Max(4, bounds - width - 4));
        var y = Math.Clamp(this.origin.Y, 4, Math.Max(4, tall - height - 4));

        AbsoluteLayout.SetLayoutBounds(this.card, new Rect(x, y, width, AbsoluteLayout.AutoSize));
    }

    View Line(string text, string? shortcut, bool isChecked, bool enabled, Action action)
    {
        var p = this.palette;
        var grid = new Grid
        {
            Padding = new Thickness(8, 5),
            ColumnDefinitions = [new ColumnDefinition(18), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Opacity = enabled ? 1 : 0.45
        };

        if (isChecked)
            grid.Add(new Label { Text = "✓", FontSize = 13, TextColor = p.Ink });

        grid.Add(new Label { Text = text, FontSize = 13, TextColor = p.Ink, LineBreakMode = LineBreakMode.TailTruncation }, 1);

        if (!string.IsNullOrEmpty(shortcut))
            grid.Add(new Label { Text = shortcut, FontSize = 11, TextColor = p.Muted, Margin = new Thickness(12, 0, 0, 0) }, 2);

        if (enabled)
            grid.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(action) });

        return grid;
    }
}

/// <summary>
/// Formula autocomplete: the list of function names that drops under an editor while a formula is
/// typed, and the signature tip once a parenthesis is open.
/// </summary>
/// <remarks>
/// Shared by the in-cell editor and the formula bar. The analysis is the kernel's
/// <see cref="FormulaAssist"/>, the same the Blazor host uses; this only draws it and hands a picked line
/// back to whichever entry asked.
/// </remarks>
sealed class FormulaAssistView : ContentView
{
    readonly VerticalStackLayout lines = new() { Spacing = 0 };
    readonly Label hint = new() { FontSize = 11, Padding = new Thickness(6, 3) };
    readonly Border list;

    Entry? target;
    FormulaAssistState state = FormulaAssistState.None;
    OverlayPalette palette;

    public FormulaAssistView()
    {
        this.list = new Border
        {
            StrokeThickness = 1,
            Padding = new Thickness(0, 2),
            Content = new ScrollView { Content = this.lines, MaximumHeightRequest = 220 }
        };

        this.Content = new VerticalStackLayout { Spacing = 2, Children = { this.hint, this.list } };
        this.IsVisible = false;
        this.InputTransparent = false;
    }

    /// <summary>Re-reads the entry and shows what fits. Hides itself outside a formula.</summary>
    public void Update(Entry entry, SpreadsheetController controller, SpreadsheetTheme theme)
    {
        this.target = entry;
        this.palette = OverlayPalette.From(theme);

        var text = entry.Text ?? string.Empty;
        var caret = Math.Clamp(entry.CursorPosition, 0, text.Length);
        this.state = FormulaAssist.Analyze(text, caret, controller.Workbook.VisibleNames.Select(x => x.Name));

        var p = this.palette;
        this.hint.Text = this.state.ActiveFunction is { } function
            ? $"{function.Signature}   (argument {this.state.ArgumentIndex + 1})"
            : string.Empty;

        this.hint.IsVisible = this.hint.Text.Length > 0;
        this.hint.BackgroundColor = p.Header;
        this.hint.TextColor = p.Ink;

        this.lines.Clear();
        foreach (var suggestion in this.state.Suggestions)
        {
            var picked = suggestion;
            var line = new VerticalStackLayout { Padding = new Thickness(6, 3), Spacing = 0 };
            line.Add(new Label { Text = suggestion.Name, FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = p.Ink });
            line.Add(new Label { Text = suggestion.Description, FontSize = 10, TextColor = p.Muted, LineBreakMode = LineBreakMode.TailTruncation });
            line.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => this.Accept(picked)) });
            this.lines.Add(line);
        }

        this.list.IsVisible = this.state.HasSuggestions;
        this.list.BackgroundColor = p.Surface;
        this.list.Stroke = p.Rule;
        this.IsVisible = this.hint.IsVisible || this.list.IsVisible;
    }

    /// <summary>Takes the first suggestion — what Tab does in Excel's list.</summary>
    public bool AcceptFirst()
    {
        if (!this.IsVisible || !this.state.HasSuggestions)
            return false;

        this.Accept(this.state.Suggestions[0]);
        return true;
    }

    void Accept(FormulaSuggestion suggestion)
    {
        if (this.target is not { } entry)
            return;

        var (text, caret) = FormulaAssist.Accept(entry.Text ?? string.Empty, this.state, suggestion);
        entry.Text = text;
        entry.CursorPosition = Math.Clamp(caret, 0, text.Length);
        entry.Focus();
        this.list.IsVisible = false;
    }

    public void Hide()
    {
        this.IsVisible = false;
        this.target = null;
    }
}
