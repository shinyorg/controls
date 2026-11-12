using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Themes;
using MauiPage = Microsoft.Maui.Controls.Page;

namespace Shiny.Maui.Controls.Agents;

/// <summary>
/// The 18 components of the A2UI v1.0 basic catalog, drawn with native MAUI views and painted from
/// the Shiny theme tokens.
/// </summary>
static class A2UIBasicRenderers
{
    public static A2UIRendererRegistry AddTo(A2UIRendererRegistry registry) => registry
        .Register("Text", Text)
        .Register("Image", Image)
        .Register("Icon", Icon)
        .Register("Video", Video)
        .Register("AudioPlayer", AudioPlayer)
        .Register("Row", Row)
        .Register("Column", Column)
        .Register("List", List)
        .Register("Card", Card)
        .Register("Tabs", Tabs)
        .Register("Divider", Divider)
        .Register("Modal", Modal)
        .Register("Button", Button)
        .Register("TextField", TextField)
        .Register("CheckBox", CheckBox)
        .Register("ChoicePicker", ChoicePicker)
        .Register("Slider", Slider)
        .Register("DateTimeInput", DateTimeInput);

    // ── display ──────────────────────────────────────────────────────────────────

    static View Text(A2UIRenderContext ctx)
    {
        var variant = ctx.Literal("variant") ?? "body";
        var label = new Label { LineBreakMode = LineBreakMode.WordWrap };
        label.FontToken(variant switch
        {
            "h1" => ShinyThemeKeys.Type.HeadlineLargeSize,
            "h2" => ShinyThemeKeys.Type.HeadlineMediumSize,
            "h3" => ShinyThemeKeys.Type.HeadlineSmallSize,
            "h4" => ShinyThemeKeys.Type.TitleLargeSize,
            "h5" => ShinyThemeKeys.Type.TitleMediumSize,
            "caption" => ShinyThemeKeys.Type.BodySmallSize,
            _ => ShinyThemeKeys.Type.BodyMediumSize
        });
        label.Themed(Label.TextColorProperty, variant == "caption" ? ShinyThemeKeys.Color.OnSurfaceVariant : ShinyThemeKeys.Color.OnSurface);
        if (variant is "h1" or "h2" or "h3" or "h4" or "h5")
        {
            label.FontAttributes = FontAttributes.Bold;
            SemanticProperties.SetHeadingLevel(label, variant switch
            {
                "h1" => SemanticHeadingLevel.Level1,
                "h2" => SemanticHeadingLevel.Level2,
                "h3" => SemanticHeadingLevel.Level3,
                "h4" => SemanticHeadingLevel.Level4,
                _ => SemanticHeadingLevel.Level5
            });
        }

        ctx.Watch(s => InlineMarkdown.Apply(label, ctx.String("text", s)));
        return label;
    }

    static View Image(A2UIRenderContext ctx)
    {
        var image = new Microsoft.Maui.Controls.Image
        {
            Aspect = (ctx.Literal("fit") ?? "fill") switch
            {
                "contain" or "scaleDown" => Aspect.AspectFit,
                "cover" => Aspect.AspectFill,
                "none" => Aspect.Center,
                _ => Aspect.Fill
            }
        };

        var variant = ctx.Literal("variant") ?? "mediumFeature";
        View result = image;
        switch (variant)
        {
            case "icon":
                image.WidthRequest = image.HeightRequest = 24;
                image.HorizontalOptions = LayoutOptions.Start;
                break;
            case "avatar":
                image.WidthRequest = image.HeightRequest = 48;
                image.Aspect = Aspect.AspectFill;
                image.Clip = new EllipseGeometry(new Point(24, 24), 24, 24);
                image.HorizontalOptions = LayoutOptions.Start;
                break;
            case "smallFeature":
                image.HeightRequest = 96;
                break;
            case "largeFeature":
                image.HeightRequest = 280;
                break;
            case "header":
                image.HeightRequest = 200;
                image.Aspect = image.Aspect == Aspect.Fill ? Aspect.AspectFill : image.Aspect;
                break;
            default:
                image.HeightRequest = 180;
                break;
        }

        ctx.Watch(s =>
        {
            image.Source = A2UIMedia.ImageSource(ctx.Surface, ctx.String("url", s));
            if (ctx.OptionalString("description", s) is { } description)
                SemanticProperties.SetDescription(image, description);
        });
        return result;
    }

    static View Icon(A2UIRenderContext ctx)
    {
        var host = new ContentView { HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center };
        ctx.Watch(s =>
        {
            var name = ctx.Component["name"];
            if (name is JsonObject o && o["svgPath"] is { } svg)
            {
                var path = new Microsoft.Maui.Controls.Shapes.Path
                {
                    WidthRequest = 24,
                    HeightRequest = 24,
                    Aspect = Stretch.Uniform
                }.Themed(Shape.FillProperty, ShinyThemeKeys.Brush.OnSurface);
                try
                {
                    path.Data = (Geometry?)new PathGeometryConverter().ConvertFromInvariantString(ctx.Surface.EvaluateString(svg, s));
                }
                catch (Exception)
                {
                    // a malformed path draws nothing rather than failing the surface
                }
                host.Content = path;
                return;
            }

            var iconName = ctx.Surface.EvaluateString(name, s);
            var glyph = new Label
            {
                Text = A2UIIcons.Glyph(iconName),
                FontSize = 20,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                MinimumWidthRequest = 24
            }.Themed(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
            SemanticProperties.SetDescription(glyph, iconName);
            host.Content = glyph;
        });
        return host;
    }

    static View Video(A2UIRenderContext ctx)
    {
        // A poster that opens the video in the system player. Core has no media element and the
        // MediaElement add-on isn't a dependency of this package.
        var poster = new Microsoft.Maui.Controls.Image { Aspect = Aspect.AspectFill, HeightRequest = 200 };
        var play = new Label
        {
            Text = "▶",
            FontSize = 40,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            TextColor = Colors.White,
            Shadow = new Shadow { Radius = 6, Opacity = 0.6f, Brush = Colors.Black }
        };
        var frame = new Border
        {
            HeightRequest = 200,
            StrokeThickness = 0,
            StrokeShape = AgentTheme.Corner(ShinyThemeKeys.Shape.CornerMediumRadius),
            Content = new Grid { Children = { poster, play } }
        }.Themed(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.InverseSurface);

        string? url = null;
        ctx.Watch(s =>
        {
            url = ctx.String("url", s);
            poster.Source = A2UIMedia.ImageSource(ctx.Surface, ctx.OptionalString("posterUrl", s));
            frame.Opacity = A2UIMedia.OpenableUri(ctx.Surface, url) is null ? 0.5 : 1;
        });
        SemanticProperties.SetDescription(frame, "Play video");
        frame.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => A2UIMedia.Open(ctx.Surface, url)) });
        return frame;
    }

    static View AudioPlayer(A2UIRenderContext ctx)
    {
        var title = AgentTheme.Text("", ShinyThemeKeys.Type.BodyMediumSize);
        var row = new Border
        {
            Padding = new Thickness(12, 8),
            StrokeThickness = 0,
            StrokeShape = AgentTheme.Corner(ShinyThemeKeys.Shape.CornerFullRadius),
            HorizontalOptions = LayoutOptions.Start,
            Content = new HorizontalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    new Label { Text = "▶", FontSize = 16, VerticalOptions = LayoutOptions.Center }
                        .Themed(Label.TextColorProperty, ShinyThemeKeys.Color.Primary),
                    title
                }
            }
        }.Themed(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHigh);
        title.VerticalOptions = LayoutOptions.Center;

        string? url = null;
        ctx.Watch(s =>
        {
            url = ctx.String("url", s);
            title.Text = ctx.OptionalString("description", s) is { Length: > 0 } d ? d : "Play audio";
            row.Opacity = A2UIMedia.OpenableUri(ctx.Surface, url) is null ? 0.5 : 1;
        });
        SemanticProperties.SetDescription(row, "Play audio");
        row.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => A2UIMedia.Open(ctx.Surface, url)) });
        return row;
    }

    // ── layout ───────────────────────────────────────────────────────────────────

    static double? Weight(A2UIRenderContext ctx, A2UIChild child)
        => child.Component?["weight"] is { } w && A2UIValues.ToNumber(w) is { } n && n > 0 ? n : null;

    static View Row(A2UIRenderContext ctx)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        var justify = ctx.Literal("justify") ?? "start";
        var align = ctx.Literal("align") ?? "stretch";

        ctx.WatchChildren("children", children =>
        {
            grid.Children.Clear();
            grid.ColumnDefinitions.Clear();
            var weights = children.Select(c => Weight(ctx, c.Child)).ToList();
            var weighted = weights.Any(w => w is not null);
            var col = 0;

            void Filler()
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                col++;
            }

            if (!weighted && justify is "end" or "center" or "spaceAround" or "spaceEvenly")
                Filler();

            for (var i = 0; i < children.Count; i++)
            {
                if (i > 0 && !weighted && justify is "spaceBetween" or "spaceAround" or "spaceEvenly")
                    Filler();

                grid.ColumnDefinitions.Add(weights[i] is { } w
                    ? new ColumnDefinition(new GridLength(w, GridUnitType.Star))
                    : new ColumnDefinition(!weighted && justify == "stretch" ? GridLength.Star : GridLength.Auto));

                var view = children[i].View;
                view.VerticalOptions = CrossAxis(align);
                grid.Add(view, col++, 0);
            }

            if (!weighted && justify is "start" or "center" or "spaceAround" or "spaceEvenly")
                Filler();
        });
        return grid;
    }

    static View Column(A2UIRenderContext ctx)
    {
        var host = new ContentView { Padding = 0 };
        var align = ctx.Literal("align") ?? "stretch";

        ctx.WatchChildren("children", children =>
        {
            var weights = children.Select(c => Weight(ctx, c.Child)).ToList();
            if (weights.Any(w => w is not null))
            {
                // weights only mean something when the column has a height to share (a weighted Row
                // cell, a fixed-height host); unbounded, star rows size to their content
                var grid = new Grid { RowSpacing = 8 };
                for (var i = 0; i < children.Count; i++)
                {
                    grid.RowDefinitions.Add(weights[i] is { } w ? new RowDefinition(new GridLength(w, GridUnitType.Star)) : new RowDefinition(GridLength.Auto));
                    children[i].View.HorizontalOptions = CrossAxis(align);
                    grid.Add(children[i].View, 0, i);
                }
                host.Content = grid;
            }
            else
            {
                var stack = new VerticalStackLayout { Spacing = 8 };
                foreach (var (_, view) in children)
                {
                    view.HorizontalOptions = CrossAxis(align);
                    stack.Children.Add(view);
                }
                host.Content = stack;
            }
        });
        return host;
    }

    static View List(A2UIRenderContext ctx)
    {
        // never virtualised: an A2UI list is a handful of agent-composed rows, and inside an agent
        // message it has to size to its content rather than scroll
        var horizontal = ctx.Literal("direction") == "horizontal";
        var align = ctx.Literal("align") ?? "stretch";
        StackBase stack = horizontal ? new HorizontalStackLayout { Spacing = 8 } : new VerticalStackLayout { Spacing = 8 };

        ctx.WatchChildren("children", children =>
        {
            stack.Children.Clear();
            foreach (var (_, view) in children)
            {
                if (horizontal)
                    view.VerticalOptions = CrossAxis(align);
                else
                    view.HorizontalOptions = CrossAxis(align);
                stack.Children.Add(view);
            }
        });

        return horizontal
            ? new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = stack }
            : stack;
    }

    static LayoutOptions CrossAxis(string align) => align switch
    {
        "start" => LayoutOptions.Start,
        "center" => LayoutOptions.Center,
        "end" => LayoutOptions.End,
        _ => LayoutOptions.Fill
    };

    static View Card(A2UIRenderContext ctx)
    {
        var showBorder = ctx.ShowBorder;
        var border = new Border
        {
            Padding = showBorder ? 16 : 0,
            StrokeShape = AgentTheme.Corner(ShinyThemeKeys.Shape.CornerMediumRadius)
        };
        if (showBorder)
        {
            border.Themed(Border.StrokeProperty, ShinyThemeKeys.Brush.OutlineVariant)
                .Themed(Border.StrokeThicknessProperty, ShinyThemeKeys.Border.Thin)
                .Themed(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerLow);
        }
        else
        {
            // hosted in a message: the message is the container, so the card drops its own chrome
            border.StrokeThickness = 0;
            border.BackgroundColor = Colors.Transparent;
        }

        ctx.MarkChildrenInsideCard();
        border.Content = ctx.RenderChild("child");
        return border;
    }

    static View Tabs(A2UIRenderContext ctx)
    {
        // wraps rather than scrolling sideways, so a long tab strip never adds a scroller
        var header = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        var body = new Grid();
        var buttons = new List<(Button Button, BoxView Line)>();
        var pages = new List<View>();

        void Select(int index)
        {
            for (var i = 0; i < pages.Count; i++)
            {
                var selected = i == index;
                pages[i].IsVisible = selected;
                buttons[i].Line.IsVisible = selected;
                buttons[i].Button.Themed(Microsoft.Maui.Controls.Button.TextColorProperty, selected ? ShinyThemeKeys.Color.Primary : ShinyThemeKeys.Color.OnSurfaceVariant);
                buttons[i].Button.FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None;
            }
        }

        var tabs = ctx.Component["tabs"] as JsonArray ?? [];
        foreach (var tab in tabs.OfType<JsonObject>())
        {
            var index = pages.Count;
            var button = AgentTheme.LinkButton("");
            var line = new BoxView { HeightRequest = 2 }.Themed(BoxView.ColorProperty, ShinyThemeKeys.Color.Primary);
            ctx.Watch(s => button.Text = ctx.Surface.EvaluateString(tab["title"], s));
            button.Clicked += (_, _) => Select(index);
            header.Children.Add(new VerticalStackLayout { Spacing = 0, Children = { button, line } });
            buttons.Add((button, line));

            var childId = A2UIValues.AsString(tab["child"]) ?? "";
            var page = ctx.Render(new A2UIChild(childId, ctx.Surface.GetComponent(childId), ctx.Scope));
            body.Children.Add(page);
            pages.Add(page);
        }
        Select(0);

        var divider = new BoxView { HeightRequest = 1 }.Themed(BoxView.ColorProperty, ShinyThemeKeys.Color.OutlineVariant);
        return new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                header,
                divider,
                body
            }
        };
    }

    static View Divider(A2UIRenderContext ctx)
    {
        var vertical = ctx.Literal("axis") == "vertical";
        var line = new BoxView
        {
            HeightRequest = vertical ? -1 : 1,
            WidthRequest = vertical ? 1 : -1,
            VerticalOptions = vertical ? LayoutOptions.Fill : LayoutOptions.Center,
            HorizontalOptions = vertical ? LayoutOptions.Center : LayoutOptions.Fill,
            MinimumHeightRequest = vertical ? 16 : 1
        }.Themed(BoxView.ColorProperty, ShinyThemeKeys.Color.OutlineVariant);
        return line;
    }

    static View Modal(A2UIRenderContext ctx)
    {
        MauiPage? open = null;
        A2UINode? contentNode = null;

        async void Close()
        {
            if (open is null)
                return;
            var page = open;
            open = null;
            if (contentNode is not null)
            {
                ctx.Node.Owner.RemoveChild(ctx.Node, contentNode);
                contentNode = null;
            }
            try
            {
                if (page.Navigation.ModalStack.Contains(page))
                    await page.Navigation.PopModalAsync(false);
            }
            catch (Exception)
            {
                // already gone
            }
        }

        async void Open()
        {
            if (open is not null || A2UIValues.AsString(ctx.Component["content"]) is not { } contentId)
                return;

            var navigation = ctx.View.Window?.Page?.Navigation ?? ctx.View.Navigation;
            contentNode = ctx.Node.Owner.CreateChild(ctx.Node, new A2UIChild(contentId, ctx.Surface.GetComponent(contentId), ctx.Scope));
            open = A2UIModalPage.Create(contentNode.Slot, Close);
            await navigation.PushModalAsync(open, false);
        }

        ctx.OnDispose(Close);

        if (A2UIValues.AsString(ctx.Component["trigger"]) is not { } triggerId)
            return A2UIPlaceholders.Missing();

        var triggerComponent = ctx.Surface.GetComponent(triggerId);
        var trigger = ctx.RenderTrigger(new A2UIChild(triggerId, triggerComponent, ctx.Scope), Open);
        if (triggerComponent?.Type != "Button")
            trigger.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(Open) });
        return trigger;
    }

    // ── inputs ───────────────────────────────────────────────────────────────────

    static View Button(A2UIRenderContext ctx)
    {
        var variant = ctx.Literal("variant") ?? "default";
        var trigger = ctx.Trigger;
        var hasAction = ctx.Component["action"] is JsonObject;

        void Activate()
        {
            trigger?.Invoke();
            if (hasAction)
                ctx.Dispatch();
        }

        View view;
        Action<bool> setEnabled;
        var childId = A2UIValues.AsString(ctx.Component["child"]);
        var label = childId is null ? null : ctx.Surface.GetComponent(childId);

        if (label is { Type: "Text" } || label is null)
        {
            // a labelled button is a real Button, so it gets the platform's focus and press states
            var button = new Microsoft.Maui.Controls.Button
            {
                Padding = new Thickness(18, 8),
                MinimumHeightRequest = 40,
                CornerRadius = 20
            }.FontToken(ShinyThemeKeys.Type.LabelLargeSize);
            StyleButton(button, variant);
            if (childId is not null)
                ctx.Consume(childId);
            if (label is not null)
                ctx.Watch(s => button.Text = ctx.Surface.EvaluateString(label["text"], s));
            button.HorizontalOptions = LayoutOptions.Start;
            button.Clicked += (_, _) => Activate();
            view = button;
            setEnabled = enabled => button.IsEnabled = enabled;
        }
        else
        {
            // anything else (an Icon, a Row) is drawn as the child inside a tappable chip
            var enabled = true;
            var chip = new Border
            {
                Padding = new Thickness(12, 8),
                StrokeShape = AgentTheme.Corner(ShinyThemeKeys.Shape.CornerFullRadius),
                HorizontalOptions = LayoutOptions.Start,
                Content = ctx.RenderChild("child")
            };
            StyleChip(chip, variant);
            chip.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => { if (enabled) Activate(); }) });
            SemanticProperties.SetDescription(chip, "Button");
            view = chip;
            setEnabled = e =>
            {
                enabled = e;
                chip.Opacity = e ? 1 : 0.45;
            };
        }

        // a Button's checks gate it: it stays disabled until every check passes
        if (ctx.Component.Checks is { Count: > 0 })
            ctx.Watch(s => setEnabled(ctx.Checks(s).IsValid));

        return view;
    }

    static void StyleButton(Microsoft.Maui.Controls.Button button, string variant)
    {
        switch (variant)
        {
            case "primary":
                button.Themed(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Primary)
                    .Themed(Microsoft.Maui.Controls.Button.TextColorProperty, ShinyThemeKeys.Color.OnPrimary);
                button.BorderWidth = 0;
                break;
            case "borderless":
                button.BackgroundColor = Colors.Transparent;
                button.BorderWidth = 0;
                button.Themed(Microsoft.Maui.Controls.Button.TextColorProperty, ShinyThemeKeys.Color.Primary);
                break;
            default:
                button.BackgroundColor = Colors.Transparent;
                button.BorderWidth = 1;
                button.Themed(Microsoft.Maui.Controls.Button.BorderColorProperty, ShinyThemeKeys.Color.Outline)
                    .Themed(Microsoft.Maui.Controls.Button.TextColorProperty, ShinyThemeKeys.Color.Primary);
                break;
        }
    }

    static void StyleChip(Border chip, string variant)
    {
        switch (variant)
        {
            case "primary":
                chip.StrokeThickness = 0;
                chip.Themed(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.PrimaryContainer);
                break;
            case "borderless":
                chip.StrokeThickness = 0;
                chip.BackgroundColor = Colors.Transparent;
                break;
            default:
                chip.StrokeThickness = 1;
                chip.BackgroundColor = Colors.Transparent;
                chip.Themed(Border.StrokeProperty, ShinyThemeKeys.Brush.Outline);
                break;
        }
    }

    static View TextField(A2UIRenderContext ctx)
    {
        var variant = ctx.Literal("variant") ?? "shortText";
        InputView input = variant == "longText"
            ? new Editor { AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 80 }
            : new Entry
            {
                IsPassword = variant == "obscured",
                Keyboard = variant == "number" ? Keyboard.Numeric : Keyboard.Default
            };
        input.FontToken(ShinyThemeKeys.Type.BodyLargeSize)
            .Themed(InputView.TextColorProperty, ShinyThemeKeys.Color.OnSurface)
            .Themed(InputView.PlaceholderColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        var updating = false;
        ctx.Watch(s =>
        {
            var value = ctx.String("value", s);
            // the field the user is typing in is the source of truth until it loses focus
            if (input.Text == value || (input.IsFocused && ctx.IsTouched))
                return;
            updating = true;
            input.Text = value;
            updating = false;
        });
        ctx.Watch(s => input.Placeholder = ctx.OptionalString("placeholder", s) ?? "");
        input.TextChanged += (_, e) =>
        {
            if (!updating)
                ctx.Write("value", JsonValue.Create(e.NewTextValue ?? ""));
        };
        ctx.KeepWhileFocused(input);

        var label = ctx.Component.Properties.ContainsKey("label") ? FieldLabel() : null;
        if (label is not null)
        {
            ctx.Watch(s =>
            {
                label.Text = ctx.String("label", s);
                SemanticProperties.SetDescription(input, label.Text);
            });
        }
        return Field(ctx, label, input);
    }

    static View CheckBox(A2UIRenderContext ctx)
    {
        var box = new Microsoft.Maui.Controls.CheckBox { VerticalOptions = LayoutOptions.Center }
            .Themed(Microsoft.Maui.Controls.CheckBox.ColorProperty, ShinyThemeKeys.Color.Primary);
        var label = AgentTheme.Text("", ShinyThemeKeys.Type.BodyLargeSize);
        label.VerticalOptions = LayoutOptions.Center;

        var updating = false;
        ctx.Watch(s =>
        {
            var value = ctx.Boolean("value", s);
            if (box.IsChecked == value)
                return;
            updating = true;
            box.IsChecked = value;
            updating = false;
        });
        ctx.Watch(s =>
        {
            label.Text = ctx.String("label", s);
            SemanticProperties.SetDescription(box, label.Text);
        });
        box.CheckedChanged += (_, e) =>
        {
            if (!updating)
                ctx.Write("value", JsonValue.Create(e.Value));
        };
        label.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => box.IsChecked = !box.IsChecked) });

        var row = new HorizontalStackLayout { Spacing = 4, Children = { box, label } };
        return Field(ctx, null, row);
    }

    static View ChoicePicker(A2UIRenderContext ctx)
    {
        var multiple = ctx.Literal("variant") == "multipleSelection";
        var chips = ctx.Literal("displayStyle") == "chips";
        var filterable = ctx.Component["filterable"] is JsonValue fv && fv.TryGetValue<bool>(out var f) && f;
        var options = A2UIOption.Parse(ctx.Component["options"] as JsonArray);

        var label = ctx.Component.Properties.ContainsKey("label") ? FieldLabel() : null;
        if (label is not null)
            ctx.Watch(s => label.Text = ctx.String("label", s));

        View picker;
        if (chips)
        {
            picker = A2UIChoices.Chips(ctx, options, multiple);
        }
        else
        {
            picker = A2UIChoices.Checks(ctx, options, multiple, filterable);
            filterable = false; // handled inside
        }

        if (filterable)
        {
            var filter = new SearchBar { Placeholder = "Filter" };
            var inner = picker;
            filter.TextChanged += (_, e) =>
            {
                if (inner is ChipGroup group)
                    group.ItemsSource = options.Where(o => string.IsNullOrWhiteSpace(e.NewTextValue) || o.Label.Contains(e.NewTextValue, StringComparison.CurrentCultureIgnoreCase)).ToList();
            };
            picker = new VerticalStackLayout { Spacing = 4, Children = { filter, inner } };
        }
        return Field(ctx, label, picker);
    }

    static View Slider(A2UIRenderContext ctx)
    {
        var min = A2UIValues.ToNumber(ctx.Component["min"]) ?? 0;
        var max = A2UIValues.ToNumber(ctx.Component["max"]) ?? 100;
        if (max <= min)
            max = min + 1;
        var steps = A2UIValues.ToNumber(ctx.Component["steps"]);

        var slider = new Controls.Slider
        {
            Minimum = min,
            Maximum = max,
            Step = steps is > 0 ? (max - min) / steps.Value : 0
        };
        var caption = new Label().FontToken(ShinyThemeKeys.Type.LabelLargeSize).Themed(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        string labelText = "";

        void UpdateCaption() => caption.Text = string.IsNullOrEmpty(labelText)
            ? slider.Value.ToString("0.##", CultureInfo.CurrentCulture)
            : $"{labelText}: {slider.Value.ToString("0.##", CultureInfo.CurrentCulture)}";

        ctx.Watch(s =>
        {
            labelText = ctx.OptionalString("label", s) ?? "";
            SemanticProperties.SetDescription(slider, labelText);
            UpdateCaption();
        });
        ctx.Watch(s =>
        {
            slider.Value = Math.Clamp(ctx.Number("value", s) ?? min, min, max);
            UpdateCaption();
        });
        slider.ValueChangedEvent += (_, v) =>
        {
            UpdateCaption();
            ctx.Write("value", JsonValue.Create(v));
        };
        return Field(ctx, null, new VerticalStackLayout { Spacing = 2, Children = { caption, slider } });
    }

    static View DateTimeInput(A2UIRenderContext ctx)
    {
        var enableDate = ctx.Component["enableDate"] is JsonValue d && d.TryGetValue<bool>(out var ed) && ed;
        var enableTime = ctx.Component["enableTime"] is JsonValue t && t.TryGetValue<bool>(out var et) && et;
        if (!enableDate && !enableTime)
            enableDate = true;

        var date = new DatePicker { IsVisible = enableDate, Format = "D" }
            .Themed(DatePicker.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
        var time = new TimePicker { IsVisible = enableTime, Format = "t" }
            .Themed(TimePicker.TextColorProperty, ShinyThemeKeys.Color.OnSurface);

        var updating = false;
        ctx.Watch(s =>
        {
            updating = true;
            var raw = ctx.String("value", s);
            if (TryParseDateTime(raw, out var value, out var timeOfDay))
            {
                date.Date = value.Date;
                time.Time = timeOfDay;
            }
            if (ParseDate(ctx.OptionalString("min", s)) is { } minDate)
                date.MinimumDate = minDate;
            if (ParseDate(ctx.OptionalString("max", s)) is { } maxDate)
                date.MaximumDate = maxDate;
            updating = false;
        });

        void Write()
        {
            if (updating)
                return;
            var day = date.Date ?? DateTime.Today;
            var tod = time.Time ?? TimeSpan.Zero;
            var text = enableDate && enableTime
                ? day.Date.Add(tod).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)
                : enableDate
                    ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : new TimeOnly(tod.Hours, tod.Minutes).ToString("HH:mm", CultureInfo.InvariantCulture);
            ctx.Write("value", JsonValue.Create(text));
        }
        date.DateSelected += (_, _) => Write();
        time.TimeSelected += (_, _) => Write();

        var label = ctx.Component.Properties.ContainsKey("label") ? FieldLabel() : null;
        if (label is not null)
        {
            ctx.Watch(s =>
            {
                label.Text = ctx.String("label", s);
                SemanticProperties.SetDescription(date, label.Text);
                SemanticProperties.SetDescription(time, label.Text);
            });
        }
        return Field(ctx, label, new HorizontalStackLayout { Spacing = 12, Children = { date, time } });
    }

    static bool TryParseDateTime(string raw, out DateTime value, out TimeSpan timeOfDay)
    {
        value = DateTime.Today;
        timeOfDay = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        if (TimeOnly.TryParse(raw, CultureInfo.InvariantCulture, out var timeOnly) && !raw.Contains('-'))
        {
            timeOfDay = timeOnly.ToTimeSpan();
            return true;
        }
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dto))
        {
            value = dto.LocalDateTime;
            timeOfDay = value.TimeOfDay;
            return true;
        }
        return false;
    }

    static DateTime? ParseDate(string? raw)
        => raw is { Length: > 0 } && DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d.Date : null;

    // ── field chrome ─────────────────────────────────────────────────────────────

    internal static Label FieldLabel()
        => new Label().FontToken(ShinyThemeKeys.Type.LabelLargeSize).Themed(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

    /// <summary>An input with its label above and its first check failure below (shown once the user has edited it).</summary>
    internal static View Field(A2UIRenderContext ctx, Label? label, View input)
    {
        var stack = new VerticalStackLayout { Spacing = 4 };
        if (label is not null)
            stack.Children.Add(label);
        stack.Children.Add(input);

        if (ctx.Component.Checks is { Count: > 0 })
        {
            var error = new Label { IsVisible = false, LineBreakMode = LineBreakMode.WordWrap }
                .FontToken(ShinyThemeKeys.Type.BodySmallSize);
            stack.Children.Add(error);
            ctx.Watch(s =>
            {
                var result = ctx.Checks(s);
                var failure = result.Failures.FirstOrDefault();
                error.IsVisible = ctx.IsTouched && failure is not null;
                error.Text = failure?.Message ?? "";
                error.Themed(Label.TextColorProperty, failure?.Severity == "error" ? ShinyThemeKeys.Color.Error : ShinyThemeKeys.Color.OnSurfaceVariant);
                SemanticProperties.SetHint(input, error.IsVisible ? error.Text : null);
            });
        }
        return stack;
    }
}

/// <summary>A labelled option (ChoicePicker, ChipGroup). <see cref="ToString"/> is the label, which is what ChipGroup draws.</summary>
sealed class A2UIOption(JsonNode? label, string value)
{
    public JsonNode? LabelSource { get; } = label;
    public string Value { get; } = value;
    public string Label { get; set; } = A2UIValues.ToDisplayString(label);

    public override string ToString() => this.Label;

    public static List<A2UIOption> Parse(JsonArray? options)
        => options?.OfType<JsonObject>()
               .Select(o => new A2UIOption(o["label"], A2UIValues.AsString(o["value"]) ?? A2UIValues.ToDisplayString(o["value"])))
               .ToList()
           ?? [];
}

/// <summary>The two ChoicePicker / ChipGroup styles: chips (the core ChipGroup) and check rows.</summary>
static class A2UIChoices
{
    public static ChipGroup Chips(A2UIRenderContext ctx, List<A2UIOption> options, bool multiple)
    {
        var group = new ChipGroup
        {
            SelectionMode = multiple ? ChipSelectionMode.Multiple : ChipSelectionMode.Single,
            ShowSelectionCheck = multiple,
            ItemsSource = options
        };
        var updating = false;

        // option labels may be bound; ChipGroup draws ToString(), so a label change re-sets the source
        ctx.Watch(s =>
        {
            var changed = false;
            foreach (var option in options)
            {
                var text = ctx.Surface.EvaluateString(option.LabelSource, s);
                changed |= text != option.Label;
                option.Label = text;
            }
            if (changed)
            {
                updating = true;
                group.ItemsSource = null;
                group.ItemsSource = options;
                updating = false;
            }
        });

        ctx.Watch(s =>
        {
            var selected = ctx.StringList("value", s);
            updating = true;
            var items = options.Where(o => selected.Contains(o.Value)).ToList();
            if (multiple)
                group.SelectedItems = items.Cast<object>().ToList();
            else
                group.SelectedItem = items.FirstOrDefault();
            updating = false;
        });

        group.SelectionChanged += (_, e) =>
        {
            if (updating)
                return;
            var values = e.SelectedItems.OfType<A2UIOption>().Select(o => (JsonNode?)JsonValue.Create(o.Value)).ToArray();
            ctx.Write("value", new JsonArray(values));
        };
        return group;
    }

    public static View Checks(A2UIRenderContext ctx, List<A2UIOption> options, bool multiple, bool filterable)
    {
        var stack = new VerticalStackLayout { Spacing = 0 };
        var rows = new List<(A2UIOption Option, View Row, Func<bool> IsChecked, Action<bool> SetChecked, Label Label)>();
        var group = "a2ui-" + Guid.NewGuid().ToString("N");
        var updating = false;

        void WriteSelection()
        {
            if (updating)
                return;
            var values = rows.Where(r => r.IsChecked()).Select(r => (JsonNode?)JsonValue.Create(r.Option.Value)).ToArray();
            ctx.Write("value", new JsonArray(values));
        }

        foreach (var option in options)
        {
            var label = AgentTheme.Text(option.Label, ShinyThemeKeys.Type.BodyLargeSize);
            label.VerticalOptions = LayoutOptions.Center;
            View control;
            Func<bool> isChecked;
            Action<bool> setChecked;
            if (multiple)
            {
                var box = new CheckBox { VerticalOptions = LayoutOptions.Center }.Themed(CheckBox.ColorProperty, ShinyThemeKeys.Color.Primary);
                box.CheckedChanged += (_, _) => WriteSelection();
                isChecked = () => box.IsChecked;
                setChecked = v => box.IsChecked = v;
                control = box;
                label.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => box.IsChecked = !box.IsChecked) });
            }
            else
            {
                var radio = new RadioButton { GroupName = group, VerticalOptions = LayoutOptions.Center };
                radio.CheckedChanged += (_, e) =>
                {
                    if (e.Value)
                        WriteSelection();
                };
                isChecked = () => radio.IsChecked;
                setChecked = v => radio.IsChecked = v;
                control = radio;
                label.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => radio.IsChecked = true) });
            }
            var row = new HorizontalStackLayout { Spacing = 4, Children = { control, label } };
            rows.Add((option, row, isChecked, setChecked, label));
            stack.Children.Add(row);
        }

        ctx.Watch(s =>
        {
            foreach (var r in rows)
            {
                r.Option.Label = ctx.Surface.EvaluateString(r.Option.LabelSource, s);
                r.Label.Text = r.Option.Label;
                SemanticProperties.SetDescription(r.Row, r.Option.Label);
            }
        });
        ctx.Watch(s =>
        {
            var selected = ctx.StringList("value", s);
            updating = true;
            foreach (var r in rows)
                r.SetChecked(selected.Contains(r.Option.Value));
            updating = false;
        });

        if (!filterable)
            return stack;

        var filter = new SearchBar { Placeholder = "Filter" };
        filter.TextChanged += (_, e) =>
        {
            foreach (var r in rows)
                r.Row.IsVisible = string.IsNullOrWhiteSpace(e.NewTextValue) || r.Option.Label.Contains(e.NewTextValue, StringComparison.CurrentCultureIgnoreCase);
        };
        return new VerticalStackLayout { Spacing = 4, Children = { filter, stack } };
    }
}

/// <summary>Media URLs under the A2UI URL policy: http(s) with a host, <c>data:</c>, or relative to the host's base URI.</summary>
static class A2UIMedia
{
    public static ImageSource? ImageSource(A2UISurface surface, string? url)
    {
        if (!A2UIUrlPolicy.IsAllowedMediaUrl(url, out var trimmed))
            return null;

        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = trimmed.IndexOf(',');
            if (comma < 0 || !trimmed[..comma].Contains(";base64", StringComparison.OrdinalIgnoreCase))
                return null;
            try
            {
                var bytes = Convert.FromBase64String(trimmed[(comma + 1)..]);
                return Microsoft.Maui.Controls.ImageSource.FromStream(() => new MemoryStream(bytes));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        return Resolve(surface, trimmed) is { } uri ? Microsoft.Maui.Controls.ImageSource.FromUri(uri) : null;
    }

    public static Uri? OpenableUri(A2UISurface surface, string? url)
        => A2UIUrlPolicy.IsAllowedMediaUrl(url, out var trimmed) && Resolve(surface, trimmed) is { } uri ? uri : null;

    public static void Open(A2UISurface surface, string? url)
    {
        if (OpenableUri(surface, url) is { } uri)
            _ = Launcher.Default.OpenAsync(uri);
    }

    static Uri? Resolve(A2UISurface surface, string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
            return absolute;
        return surface.Host.BaseUri is { } baseUri && Uri.TryCreate(baseUri, url, out var relative) ? relative : null;
    }
}

/// <summary>The basic catalog's icon names as glyphs every platform font has.</summary>
static class A2UIIcons
{
    static readonly Dictionary<string, string> Glyphs = new(StringComparer.Ordinal)
    {
        ["accountCircle"] = "◉", ["add"] = "+", ["arrowBack"] = "←", ["arrowForward"] = "→", ["attachFile"] = "📎",
        ["calendarToday"] = "📅", ["call"] = "📞", ["camera"] = "📷", ["check"] = "✓", ["close"] = "✕",
        ["delete"] = "🗑", ["download"] = "⤓", ["edit"] = "✎", ["event"] = "📅", ["error"] = "⛔",
        ["fastForward"] = "⏩", ["favorite"] = "♥", ["favoriteOff"] = "♡", ["folder"] = "📁", ["help"] = "?",
        ["home"] = "⌂", ["info"] = "ℹ", ["locationOn"] = "📍", ["lock"] = "🔒", ["lockOpen"] = "🔓",
        ["mail"] = "✉", ["menu"] = "☰", ["moreVert"] = "⋮", ["moreHoriz"] = "⋯", ["notificationsOff"] = "🔕",
        ["notifications"] = "🔔", ["pause"] = "⏸", ["payment"] = "💳", ["person"] = "👤", ["phone"] = "📱",
        ["photo"] = "🖼", ["play"] = "▶", ["print"] = "🖨", ["refresh"] = "↻", ["rewind"] = "⏪",
        ["search"] = "🔍", ["send"] = "➤", ["settings"] = "⚙", ["share"] = "⇪", ["shoppingCart"] = "🛒",
        ["skipNext"] = "⏭", ["skipPrevious"] = "⏮", ["star"] = "★", ["starHalf"] = "⯪", ["starOff"] = "☆",
        ["stop"] = "⏹", ["upload"] = "⤒", ["visibility"] = "👁", ["visibilityOff"] = "⊘", ["volumeDown"] = "🔉",
        ["volumeMute"] = "🔈", ["volumeOff"] = "🔇", ["volumeUp"] = "🔊", ["warning"] = "⚠"
    };

    public static string Glyph(string name) => Glyphs.TryGetValue(name, out var glyph) ? glyph : "•";
}

/// <summary>The modal a Modal component opens: the content in a card over a scrim, closed by the scrim or ✕.</summary>
static class A2UIModalPage
{
    public static ContentPage Create(View content, Action close)
    {
        var closeButton = AgentTheme.LinkButton("✕");
        closeButton.HorizontalOptions = LayoutOptions.End;
        SemanticProperties.SetDescription(closeButton, "Close");
        closeButton.Clicked += (_, _) => close();

        var card = new Border
        {
            Padding = new Thickness(20, 12, 20, 20),
            Margin = 24,
            MaximumWidthRequest = 560,
            StrokeThickness = 0,
            StrokeShape = AgentTheme.Corner(ShinyThemeKeys.Shape.CornerLargeRadius),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
            Content = new Grid
            {
                RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
                Children = { closeButton }
            }
        }.Themed(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHigh);
        // sized to its content: a ScrollView in an auto-sized card measures to nothing on some heads
        ((Grid)card.Content!).Add(content, 0, 1);

        var scrim = new BoxView { Color = Color.FromRgba(0, 0, 0, 0.45) };
        scrim.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(close) });

        var page = new ContentPage
        {
            BackgroundColor = Colors.Transparent,
            Content = new Grid { Children = { scrim, card } }
        };
        Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page.SetModalPresentationStyle(page.On<iOS>(), Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.UIModalPresentationStyle.OverFullScreen);
        return page;
    }
}

/// <summary>The basic catalog Text's "simple Markdown": bold, italic and inline code, nothing else.</summary>
static class InlineMarkdown
{
    public static void Apply(Label label, string text)
    {
        if (text.IndexOfAny(['*', '_', '`']) < 0)
        {
            label.FormattedText = null;
            label.Text = text;
            return;
        }

        var formatted = new FormattedString();
        var i = 0;
        var buffer = new System.Text.StringBuilder();
        while (i < text.Length)
        {
            if (TryMarker(text, ref i, "**", out var bold) || TryMarker(text, ref i, "__", out bold))
            {
                Flush();
                formatted.Spans.Add(new Span { Text = bold, FontAttributes = FontAttributes.Bold });
            }
            else if (TryMarker(text, ref i, "*", out var italic) || TryMarker(text, ref i, "_", out italic))
            {
                Flush();
                formatted.Spans.Add(new Span { Text = italic, FontAttributes = FontAttributes.Italic });
            }
            else if (TryMarker(text, ref i, "`", out var code))
            {
                Flush();
                formatted.Spans.Add(new Span { Text = code, FontFamily = DeviceInfo.Platform == DevicePlatform.Android ? "monospace" : "Menlo" });
            }
            else
            {
                buffer.Append(text[i++]);
            }
        }
        Flush();
        label.Text = null;
        label.FormattedText = formatted;

        void Flush()
        {
            if (buffer.Length == 0)
                return;
            formatted.Spans.Add(new Span { Text = buffer.ToString() });
            buffer.Clear();
        }
    }

    static bool TryMarker(string text, ref int i, string marker, out string inner)
    {
        inner = "";
        if (string.CompareOrdinal(text, i, marker, 0, marker.Length) != 0)
            return false;
        var end = text.IndexOf(marker, i + marker.Length, StringComparison.Ordinal);
        if (end <= i + marker.Length)
            return false;
        inner = text[(i + marker.Length)..end];
        i = end + marker.Length;
        return true;
    }
}
