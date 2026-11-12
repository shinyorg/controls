using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using Shiny.Maui.Controls.DataGrid;
using Shiny.Maui.Controls.Markdown;
using Shiny.Maui.Controls.Themes;
using Grid = Microsoft.Maui.Controls.Grid;
using ShinyDataGrid = Shiny.Maui.Controls.DataGrid.DataGrid;

namespace Shiny.Maui.Controls.Agents;

/// <summary>
/// The Shiny catalog (<see cref="A2UICatalog.ShinyCatalogId"/>): each component is the real Shiny
/// control - DataGrid, Gauge, ChipGroup, DateRangePicker and the rest - not a look-alike.
/// </summary>
static class A2UIShinyRenderers
{
    // above this many rows a grid in a surface shows a preview and opens the rest in a modal, so it
    // never needs its own vertical scroller inside the surface's (or the conversation's)
    const int MaxInlineRows = 12;
    const double GridRowHeight = 40;

    public static A2UIRendererRegistry AddTo(A2UIRendererRegistry registry) => registry
        .Register("DataGrid", DataGrid)
        .Register("Gauge", Gauge)
        .Register("ProgressBar", ProgressBar)
        .Register("Pill", Pill)
        .Register("Avatar", Avatar)
        .Register("ChipGroup", ChipGroup)
        .Register("TagEntry", TagEntry)
        .Register("DateRangePicker", DateRangePicker)
        .Register("RangeSlider", RangeSlider)
        .Register("Timeline", Timeline)
        .Register("Expander", Expander)
        .Register("Markdown", Markdown);

    // ── DataGrid ─────────────────────────────────────────────────────────────────

    static View DataGrid(A2UIRenderContext ctx)
    {
        var culture = ctx.Surface.Host.Culture;
        var sortable = ctx.Component["sortable"] is not JsonValue sv || !sv.TryGetValue<bool>(out var so) || so;
        var hasRowAction = ctx.Component["rowAction"] is JsonObject;
        var specs = (ctx.Component["columns"] as JsonArray ?? []).OfType<JsonObject>().ToList();

        ShinyDataGrid Create()
        {
            var grid = new ShinyDataGrid
            {
                RowHeight = GridRowHeight,
                // columns keep their measured width and the grid scrolls sideways (the catalog's promise),
                // instead of squeezing every column into a phone's width and ellipsizing them all
                HorizontalScroll = true,
                SelectionMode = hasRowAction ? DataGridSelectionMode.Single : DataGridSelectionMode.None,
                SortMode = sortable ? DataGridSortMode.Single : DataGridSortMode.None
            };
            foreach (var spec in specs)
            {
                var key = A2UIValues.AsString(spec["key"]) ?? "";
                var format = A2UIValues.AsString(spec["format"]) ?? "text";
                var currency = A2UIValues.AsString(spec["currency"]) ?? "USD";
                var align = (A2UIValues.AsString(spec["align"]) ?? (format is "number" or "currency" or "percent" ? "end" : "start")) switch
                {
                    "center" => DataGridCellAlignment.Center,
                    "end" => DataGridCellAlignment.End,
                    _ => DataGridCellAlignment.Start
                };
                var column = new DataGridColumn
                {
                    // plain cells bind a CLR member path; JSON rows draw through a template instead
                    CellTemplate = new DataTemplate(() => new A2UIGridCell(key, format, row => FormatCell(row.Get(key, format), format, currency, culture), align)),
                    PropertyName = key,
                    Sortable = sortable,
                    ValueGetter = row => ((A2UIGridRow)row).Get(key, format),
                    TextFormatter = value => FormatCell(value, format, currency, culture),
                    Alignment = align
                };
                ctx.Watch(s => column.Title = ctx.Surface.EvaluateString(spec["header"], s));
                grid.Columns.Add(column);
            }

            if (hasRowAction)
            {
                grid.SelectionChanged += (_, e) =>
                {
                    if (e.SelectedItems.FirstOrDefault() is not A2UIGridRow row)
                        return;
                    // relative paths in the action's context resolve against the tapped row
                    ctx.Dispatch("rowAction", row.Scope);
                    grid.Dispatcher.Dispatch(() => grid.SelectedItem = null);
                };
            }
            return grid;
        }

        var inline = Create();
        var title = AgentTheme.Text("", ShinyThemeKeys.Type.TitleMediumSize);
        title.FontAttributes = FontAttributes.Bold;
        title.IsVisible = false;
        var more = AgentTheme.LinkButton("");
        more.HorizontalOptions = LayoutOptions.Start;
        more.IsVisible = false;
        List<A2UIGridRow> all = [];

        ctx.Watch(s =>
        {
            var text = ctx.OptionalString("title", s);
            title.Text = text;
            title.IsVisible = !string.IsNullOrEmpty(text);
        });

        ctx.Watch(s =>
        {
            var rowsPath = A2UIValues.IsBinding(ctx.Component["rows"], out var path) ? ctx.Scope.Resolve(path) : "/";
            var rows = ctx.Value("rows", s) as JsonArray ?? [];
            all = rows.Select((r, i) => new A2UIGridRow(
                    (r as JsonObject)?.DeepClone() as JsonObject ?? new JsonObject(),
                    ctx.Scope.ForItem(Shiny.Controls.AgentUI.Json.JsonPointer.Append(rowsPath, i), i)))
                .ToList();

            var shown = all.Count > MaxInlineRows ? all.Take(MaxInlineRows).ToList() : all;
            SizeColumns(inline, specs, shown, culture);
            inline.ItemsSource = shown;
            // sized to its rows (plus the header) so it never scrolls vertically inside the surface
            inline.HeightRequest = (shown.Count + 1) * GridRowHeight + 12;
            more.IsVisible = all.Count > MaxInlineRows;
            more.Text = $"Show all {all.Count} rows";
        });

        more.Clicked += async (_, _) =>
        {
            // the full grid fills a modal page, where it owns its own scrolling
            var full = Create();
            SizeColumns(full, specs, all.Take(MaxInlineRows * 4).ToList(), culture);
            full.ItemsSource = all;
            ContentPage? page = null;
            var close = AgentTheme.LinkButton("Done");
            close.HorizontalOptions = LayoutOptions.End;
            page = new ContentPage
            {
                Title = title.Text ?? "",
                Content = new Grid
                {
                    Padding = 12,
                    RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
                    Children = { close }
                }
            }.Themed(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Surface);
            ((Grid)page.Content).Add(full, 0, 1);
            close.Clicked += async (_, _) => await page.Navigation.PopModalAsync();
            await (ctx.View.Window?.Page?.Navigation ?? ctx.View.Navigation).PushModalAsync(page);
        };

        return new VerticalStackLayout { Spacing = 6, Children = { title, inline, more } };
    }

    // An absolute width per column from the longest text it shows (header or cell), estimated from the
    // character count rather than measured: the grid's Auto columns measure cells off-screen, which
    // throws inside Android's TextView here. Clamped so one long value can't make a column huge.
    static void SizeColumns(ShinyDataGrid grid, List<JsonObject> specs, List<A2UIGridRow> rows, CultureInfo culture)
    {
        var changed = false;
        for (var i = 0; i < specs.Count && i < grid.Columns.Count; i++)
        {
            var key = A2UIValues.AsString(specs[i]["key"]) ?? "";
            var format = A2UIValues.AsString(specs[i]["format"]) ?? "text";
            var currency = A2UIValues.AsString(specs[i]["currency"]) ?? "USD";
            var longest = Math.Max(
                (grid.Columns[i].Title ?? "").Length + 2, // the sort glyph
                rows.Count == 0 ? 0 : rows.Max(r => FormatCell(r.Get(key, format), format, currency, culture)?.Length ?? 0));
            var width = new GridLength(Math.Clamp(longest * 8.5 + 28, 64, 260));
            if (grid.Columns[i].Width != width)
            {
                grid.Columns[i].Width = width;
                changed = true;
            }
        }

        // the grid builds its header when columns are added and doesn't watch Width, so re-add the same
        // columns (their bindings come with them) to rebuild the header at the new widths
        if (changed)
        {
            var columns = grid.Columns.ToList();
            grid.Columns.Clear();
            foreach (var column in columns)
                grid.Columns.Add(column);
        }
    }

    static string? FormatCell(object? value, string format, string currency, CultureInfo culture)
    {
        switch (value)
        {
            case null:
                return "";
            case double d when format == "currency":
                return A2UIFormatting.FormatCurrency(d, currency, null, true, culture);
            case double d when format == "percent":
                return d.ToString("P0", culture);
            case double d when format == "number":
                return A2UIFormatting.FormatNumber(d, null, true, culture);
            case DateTimeOffset date:
                return date.ToString("d", culture);
            case double d:
                return d.ToString(culture);
            default:
                return value.ToString();
        }
    }

    // ── display ──────────────────────────────────────────────────────────────────

    static View Gauge(A2UIRenderContext ctx)
    {
        var min = A2UIValues.ToNumber(ctx.Component["min"]) ?? 0;
        var max = A2UIValues.ToNumber(ctx.Component["max"]) ?? 100;
        var gauge = new Gauge
        {
            Minimum = min,
            Maximum = max > min ? max : min + 1,
            WidthRequest = 180,
            HeightRequest = 180,
            HorizontalOptions = LayoutOptions.Center
        };
        foreach (var band in (ctx.Component["ranges"] as JsonArray ?? []).OfType<JsonObject>())
        {
            gauge.Ranges.Add(new GaugeRange
            {
                Start = A2UIValues.ToNumber(band["from"]) ?? min,
                End = A2UIValues.ToNumber(band["to"]) ?? max,
                Color = (A2UIValues.AsString(band["tone"]) ?? "neutral") switch
                {
                    "good" => AgentTheme.Resolve(ShinyThemeKeys.Color.Success, Colors.SeaGreen),
                    "warning" => AgentTheme.Resolve(ShinyThemeKeys.Color.Warning, Colors.Orange),
                    "danger" => AgentTheme.Resolve(ShinyThemeKeys.Color.Critical, Colors.IndianRed),
                    _ => AgentTheme.Resolve(ShinyThemeKeys.Color.OutlineVariant, Colors.Gray)
                }
            });
        }
        ctx.Watch(s => gauge.Value = ctx.Number("value", s) ?? min);
        ctx.Watch(s =>
        {
            gauge.Title = ctx.OptionalString("label", s) ?? "";
            gauge.Units = ctx.OptionalString("units", s) ?? "";
            SemanticProperties.SetDescription(gauge, gauge.Title);
        });
        return gauge;
    }

    static View ProgressBar(A2UIRenderContext ctx)
    {
        var min = A2UIValues.ToNumber(ctx.Component["min"]) ?? 0;
        var max = A2UIValues.ToNumber(ctx.Component["max"]) ?? 1;
        var bar = new ProgressBar { Minimum = min, Maximum = max > min ? max : min + 1 };
        var label = AgentTheme.Text("", ShinyThemeKeys.Type.LabelLargeSize, ShinyThemeKeys.Color.OnSurfaceVariant);
        ctx.Watch(s =>
        {
            var value = ctx.Number("value", s);
            bar.IsIndeterminate = value is null;
            if (value is { } v)
                bar.Value = Math.Clamp(v, bar.Minimum, bar.Maximum);
        });
        ctx.Watch(s =>
        {
            label.Text = ctx.OptionalString("label", s);
            label.IsVisible = !string.IsNullOrEmpty(label.Text);
            SemanticProperties.SetDescription(bar, label.Text);
        });
        return new VerticalStackLayout { Spacing = 4, Children = { label, bar } };
    }

    static View Pill(A2UIRenderContext ctx)
    {
        var pill = new PillView
        {
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            Type = (ctx.Literal("tone") ?? "neutral") switch
            {
                "success" => PillType.Success,
                "info" => PillType.Info,
                "warning" => PillType.Warning,
                "caution" => PillType.Caution,
                "critical" => PillType.Critical,
                _ => PillType.None
            }
        };
        ctx.Watch(s => pill.Text = ctx.String("text", s));
        return pill;
    }

    static View Avatar(A2UIRenderContext ctx)
    {
        var avatar = new Avatar
        {
            HorizontalOptions = LayoutOptions.Start,
            Size = ctx.Literal("size") switch { "small" => 32, "large" => 56, _ => 40 }
        };
        ctx.Watch(s =>
        {
            avatar.Name = ctx.String("name", s);
            SemanticProperties.SetDescription(avatar, avatar.Name);
            // the catalog allows https pictures only
            var url = ctx.OptionalString("imageUrl", s);
            avatar.Uri = A2UIUrlPolicy.IsAllowedMediaUrl(url, out var trimmed) && trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? trimmed : null;
        });
        return avatar;
    }

    static View Timeline(A2UIRenderContext ctx)
    {
        var timeline = new TimelineView
        {
            IsScrollable = false,
            AllActive = true,
            ItemTemplate = new DataTemplate(() => new A2UITimelineItem())
        };
        ctx.Watch(s =>
        {
            var items = ctx.Value("items", s) as JsonArray ?? [];
            timeline.ItemsSource = items.OfType<JsonObject>().Select(o => (JsonObject)o.DeepClone()).ToList();
        });
        return timeline;
    }

    static View Markdown(A2UIRenderContext ctx)
    {
        var view = new MarkdownView { IsScrollEnabled = false };
        ctx.Watch(s => view.Markdown = ctx.String("text", s));
        return view;
    }

    // ── inputs ───────────────────────────────────────────────────────────────────

    static View ChipGroup(A2UIRenderContext ctx)
    {
        var options = A2UIOption.Parse(ctx.Component["options"] as JsonArray);
        var group = A2UIChoices.Chips(ctx, options, ctx.Literal("variant") != "single");
        var label = ctx.Component.Properties.ContainsKey("label") ? A2UIBasicRenderers.FieldLabel() : null;
        if (label is not null)
            ctx.Watch(s => label.Text = ctx.String("label", s));
        return A2UIBasicRenderers.Field(ctx, label, group);
    }

    static View TagEntry(A2UIRenderContext ctx)
    {
        var tags = new ObservableCollection<string>();
        var entry = new TagEntry { Tags = tags };
        if (A2UIValues.ToNumber(ctx.Component["maxTags"]) is { } maxTags)
            entry.MaxTags = (int)maxTags;

        var updating = false;
        ctx.Watch(s =>
        {
            var values = ctx.StringList("value", s);
            if (values.SequenceEqual(tags))
                return;
            updating = true;
            tags.Clear();
            foreach (var v in values)
                tags.Add(v);
            updating = false;
        });
        ctx.Watch(s => entry.Placeholder = ctx.OptionalString("placeholder", s) ?? "");
        entry.TagsChanged += (_, _) =>
        {
            if (!updating)
                ctx.Write("value", new JsonArray(tags.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()));
        };

        var label = ctx.Component.Properties.ContainsKey("label") ? A2UIBasicRenderers.FieldLabel() : null;
        if (label is not null)
            ctx.Watch(s => label.Text = ctx.String("label", s));
        return A2UIBasicRenderers.Field(ctx, label, entry);
    }

    static View DateRangePicker(A2UIRenderContext ctx)
    {
        var picker = new DateRangePicker { Culture = ctx.Surface.Host.Culture };
        var updating = false;
        ctx.Watch(s =>
        {
            updating = true;
            picker.StartDate = ParseDate(ctx.OptionalString("start", s));
            picker.EndDate = ParseDate(ctx.OptionalString("end", s));
            picker.MinDate = ParseDate(ctx.OptionalString("min", s));
            picker.MaxDate = ParseDate(ctx.OptionalString("max", s));
            updating = false;
        });
        picker.RangeSelected += (_, e) =>
        {
            if (updating)
                return;
            ctx.Write("start", e.Range is { } r ? JsonValue.Create(r.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) : null);
            ctx.Write("end", e.Range is { } r2 ? JsonValue.Create(r2.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) : null);
        };

        var label = ctx.Component.Properties.ContainsKey("label") ? A2UIBasicRenderers.FieldLabel() : null;
        if (label is not null)
        {
            ctx.Watch(s =>
            {
                label.Text = ctx.String("label", s);
                picker.Title = label.Text;
                SemanticProperties.SetDescription(picker, label.Text);
            });
        }
        return A2UIBasicRenderers.Field(ctx, label, picker);
    }

    static View RangeSlider(A2UIRenderContext ctx)
    {
        var min = A2UIValues.ToNumber(ctx.Component["min"]) ?? 0;
        var max = A2UIValues.ToNumber(ctx.Component["max"]) ?? 100;
        if (max <= min)
            max = min + 1;
        var slider = new RangeSlider
        {
            Minimum = min,
            Maximum = max,
            Step = A2UIValues.ToNumber(ctx.Component["step"]) is { } step and > 0 ? step : 0
        };
        var caption = new Label().FontToken(ShinyThemeKeys.Type.LabelLargeSize).Themed(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        var labelText = "";

        void UpdateCaption()
        {
            var range = $"{slider.LowerValue.ToString("0.##", CultureInfo.CurrentCulture)} – {slider.UpperValue.ToString("0.##", CultureInfo.CurrentCulture)}";
            caption.Text = string.IsNullOrEmpty(labelText) ? range : $"{labelText}: {range}";
        }

        ctx.Watch(s =>
        {
            labelText = ctx.OptionalString("label", s) ?? "";
            SemanticProperties.SetDescription(slider, labelText);
            UpdateCaption();
        });
        ctx.Watch(s =>
        {
            slider.LowerValue = Math.Clamp(ctx.Number("lower", s) ?? min, min, max);
            slider.UpperValue = Math.Clamp(ctx.Number("upper", s) ?? max, min, max);
            UpdateCaption();
        });
        slider.RangeChanged += (_, range) =>
        {
            UpdateCaption();
            ctx.Write("lower", JsonValue.Create(range.Lower));
            ctx.Write("upper", JsonValue.Create(range.Upper));
        };
        return A2UIBasicRenderers.Field(ctx, null, new VerticalStackLayout { Spacing = 2, Children = { caption, slider } });
    }

    static View Expander(A2UIRenderContext ctx)
    {
        var expander = new Expander { Content = ctx.RenderChild("child") };
        var updating = false;
        ctx.Watch(s =>
        {
            expander.HeaderText = ctx.String("header", s);
            SemanticProperties.SetDescription(expander, expander.HeaderText);
        });
        ctx.Watch(s =>
        {
            if (!ctx.Component.Properties.ContainsKey("expanded"))
                return;
            updating = true;
            expander.IsExpanded = ctx.Boolean("expanded", s);
            updating = false;
        });
        expander.ExpandedChanged += (_, e) =>
        {
            if (!updating)
                ctx.Write("expanded", JsonValue.Create(e.IsExpanded));
        };
        return expander;
    }

    static DateTime? ParseDate(string? raw)
        => DateOnly.TryParse(raw, CultureInfo.InvariantCulture, out var d) ? d.ToDateTime(TimeOnly.MinValue) : null;
}

/// <summary>One DataGrid row: the row object and the item scope its row action resolves against.</summary>
sealed class A2UIGridRow(JsonObject data, A2UIScope scope)
{
    public JsonObject Data { get; } = data;
    public A2UIScope Scope { get; } = scope;

    // typed values so sorting compares numbers as numbers and dates as dates
    public object? Get(string key, string format)
    {
        var node = this.Data[key];
        if (format == "date" && A2UIValues.AsString(node) is { } s
            && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
            return date;
        if (format is "number" or "currency" or "percent")
            return A2UIValues.ToNumber(node);
        return node is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.Number
            ? A2UIValues.ToNumber(node)
            : A2UIValues.ToDisplayString(node);
    }
}

/// <summary>A DataGrid cell for a JSON row, formatted in the user's culture.</summary>
sealed class A2UIGridCell : Label
{
    readonly Func<A2UIGridRow, string?> format;

    public A2UIGridCell(string key, string kind, Func<A2UIGridRow, string?> format, DataGridCellAlignment alignment)
    {
        this.format = format;
        this.Padding = new Thickness(12, 0);
        this.VerticalTextAlignment = TextAlignment.Center;
        this.LineBreakMode = LineBreakMode.TailTruncation;
        this.HorizontalTextAlignment = alignment switch
        {
            DataGridCellAlignment.End => TextAlignment.End,
            DataGridCellAlignment.Center => TextAlignment.Center,
            _ => TextAlignment.Start
        };
        this.FontToken(ShinyThemeKeys.Type.BodyMediumSize).Themed(TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        this.Text = this.BindingContext is A2UIGridRow row ? this.format(row) : null;
    }
}

/// <summary>A Timeline entry: title, then optional time and description, with a tone dot.</summary>
sealed class A2UITimelineItem : ContentView
{
    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (this.BindingContext is not JsonObject item)
        {
            this.Content = null;
            return;
        }

        var title = AgentTheme.Text(A2UIValues.ToDisplayString(item["title"]), ShinyThemeKeys.Type.BodyLargeSize);
        title.FontAttributes = FontAttributes.Bold;
        var stack = new VerticalStackLayout { Spacing = 2, Padding = new Thickness(0, 0, 0, 12) };

        var header = new HorizontalStackLayout { Spacing = 6 };
        if (A2UIValues.AsString(item["tone"]) is { } tone && tone != "neutral")
        {
            header.Children.Add(new Microsoft.Maui.Controls.Shapes.Ellipse
            {
                WidthRequest = 8,
                HeightRequest = 8,
                VerticalOptions = LayoutOptions.Center
            }.Themed(Microsoft.Maui.Controls.Shapes.Shape.FillProperty, tone switch
            {
                "success" => ShinyThemeKeys.Brush.Success,
                "info" => ShinyThemeKeys.Brush.Info,
                "warning" => ShinyThemeKeys.Brush.Warning,
                "critical" => ShinyThemeKeys.Brush.Critical,
                _ => ShinyThemeKeys.Brush.Outline
            }));
        }
        header.Children.Add(title);
        stack.Children.Add(header);

        if (A2UIValues.ToDisplayString(item["time"]) is { Length: > 0 } time)
            stack.Children.Add(AgentTheme.Text(time, ShinyThemeKeys.Type.BodySmallSize, ShinyThemeKeys.Color.OnSurfaceVariant));
        if (A2UIValues.ToDisplayString(item["description"]) is { Length: > 0 } description)
            stack.Children.Add(AgentTheme.Text(description, ShinyThemeKeys.Type.BodyMediumSize, ShinyThemeKeys.Color.OnSurfaceVariant));
        this.Content = stack;
    }
}
