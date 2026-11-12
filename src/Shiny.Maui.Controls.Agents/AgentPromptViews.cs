using System.Globalization;
using System.Text.Json.Nodes;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Agents;

/// <summary>
/// The inline UIs for the built-in UI tools (<see cref="AgentPromptKinds"/>), drawn inside the tool
/// call's card. Each answers with <see cref="AgentPrompt.Submit"/> or <see cref="AgentPrompt.Decline"/>.
/// </summary>
static class AgentPromptViews
{
    public static View Create(AgentPrompt prompt)
    {
        var body = prompt.Kind switch
        {
            AgentPromptKinds.Confirm => Confirm(prompt),
            AgentPromptKinds.ChooseOne => ChooseOne(prompt),
            AgentPromptKinds.ChooseMany => ChooseMany(prompt),
            AgentPromptKinds.Date => Date(prompt),
            AgentPromptKinds.DateRange => DateRange(prompt),
            AgentPromptKinds.Text => Text(prompt),
            _ => Actions(null, ("Dismiss", false, () => prompt.Decline()))
        };

        var title = AgentTheme.Text(prompt.Kind == AgentPromptKinds.Confirm ? prompt.Str("title") ?? "" : prompt.Title, ShinyThemeKeys.Type.TitleSmallSize);
        title.FontAttributes = FontAttributes.Bold;
        title.IsVisible = !string.IsNullOrEmpty(title.Text);

        var stack = new VerticalStackLayout { Spacing = 10, Children = { title, body } };
        SemanticProperties.SetDescription(stack, prompt.Title);
        return stack;
    }

    static View Confirm(AgentPrompt prompt)
    {
        var message = AgentTheme.Text(prompt.Str("message") ?? "", ShinyThemeKeys.Type.BodyMediumSize);
        return new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                message,
                Actions(null,
                    (prompt.Str("confirmText") ?? "Confirm", true, () => prompt.Submit(new JsonObject { ["confirmed"] = true })),
                    (prompt.Str("cancelText") ?? "Cancel", false, () => prompt.Submit(new JsonObject { ["confirmed"] = false })))
            }
        };
    }

    static View ChooseOne(AgentPrompt prompt)
    {
        var options = new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            Direction = Microsoft.Maui.Layouts.FlexDirection.Row
        };
        foreach (var option in prompt.List("options"))
        {
            var button = AgentTheme.TonalButton(option);
            button.Margin = new Thickness(0, 0, 8, 8);
            button.Clicked += (_, _) => prompt.Submit(new JsonObject { ["choice"] = option });
            options.Children.Add(button);
        }
        var skip = AgentTheme.LinkButton("Skip");
        skip.HorizontalOptions = LayoutOptions.Start;
        skip.Clicked += (_, _) => prompt.Decline();
        return new VerticalStackLayout { Spacing = 4, Children = { options, skip } };
    }

    static View ChooseMany(AgentPrompt prompt)
    {
        var min = (int)(prompt.Number("min") ?? 0);
        var max = prompt.Number("max") is { } m ? (int)m : int.MaxValue;
        var chips = new ChipGroup
        {
            SelectionMode = ChipSelectionMode.Multiple,
            ShowSelectionCheck = true,
            ItemsSource = prompt.List("options").ToList()
        };
        if (max < int.MaxValue)
            chips.MaxSelectionCount = max;

        var submit = AgentTheme.TonalButton("Done", primary: true);
        submit.IsEnabled = min == 0;
        chips.SelectionChanged += (_, e) => submit.IsEnabled = e.SelectedItems.Count >= min && e.SelectedItems.Count <= max;
        submit.Clicked += (_, _) => prompt.Submit(new JsonObject
        {
            ["choices"] = new JsonArray((chips.SelectedItems?.Cast<object>() ?? []).Select(o => (JsonNode?)JsonValue.Create(o.ToString())).ToArray())
        });
        return new VerticalStackLayout
        {
            Spacing = 10,
            Children = { chips, Actions(submit, ("Skip", false, () => prompt.Decline())) }
        };
    }

    static View Date(AgentPrompt prompt)
    {
        var picker = new DatePicker { Format = "D", HorizontalOptions = LayoutOptions.Start }
            .Themed(DatePicker.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
        if (prompt.Date("min") is { } min)
            picker.MinimumDate = min.ToDateTime(TimeOnly.MinValue);
        if (prompt.Date("max") is { } max)
            picker.MaximumDate = max.ToDateTime(TimeOnly.MinValue);
        picker.Date = Clamp(DateTime.Today, picker.MinimumDate, picker.MaximumDate);

        var submit = AgentTheme.TonalButton("Use this date", primary: true);
        submit.Clicked += (_, _) => prompt.Submit(new JsonObject
        {
            ["date"] = (picker.Date ?? DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        });
        return new VerticalStackLayout
        {
            Spacing = 10,
            Children = { picker, Actions(submit, ("Cancel", false, () => prompt.Decline())) }
        };
    }

    static DateTime Clamp(DateTime value, DateTime? min, DateTime? max)
        => min is { } lo && value < lo ? lo : max is { } hi && value > hi ? hi : value;

    static View DateRange(AgentPrompt prompt)
    {
        // the core range calendar, inline - the agent asked, the real control answers
        var calendar = new DateRangeCalendar
        {
            MinDate = prompt.Date("min")?.ToDateTime(TimeOnly.MinValue),
            MaxDate = prompt.Date("max")?.ToDateTime(TimeOnly.MinValue),
            HorizontalOptions = LayoutOptions.Fill,
            MaximumWidthRequest = 420
        };
        var summary = AgentTheme.Text("Tap a start and an end date", ShinyThemeKeys.Type.BodySmallSize, ShinyThemeKeys.Color.OnSurfaceVariant);
        var submit = AgentTheme.TonalButton("Use these dates", primary: true);
        submit.IsEnabled = false;
        calendar.RangeSelected += (_, e) =>
        {
            submit.IsEnabled = e.Range is not null;
            summary.Text = e.Range is { } r
                ? $"{r.Start.ToString("d", CultureInfo.CurrentCulture)} – {r.End.ToString("d", CultureInfo.CurrentCulture)} ({r.Days} days)"
                : "Tap a start and an end date";
        };
        submit.Clicked += (_, _) =>
        {
            if (calendar.Range is not { } range)
                return;
            prompt.Submit(new JsonObject
            {
                ["start"] = range.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["end"] = range.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            });
        };
        return new VerticalStackLayout
        {
            Spacing = 10,
            Children = { calendar, summary, Actions(submit, ("Cancel", false, () => prompt.Decline())) }
        };
    }

    static View Text(AgentPrompt prompt)
    {
        var multiline = prompt.Arguments["multiline"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
        InputView input = multiline
            ? new Editor { AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 80 }
            : new Entry { ReturnType = ReturnType.Send };
        input.Placeholder = prompt.Str("placeholder") ?? "";
        input.FontToken(ShinyThemeKeys.Type.BodyLargeSize)
            .Themed(InputView.TextColorProperty, ShinyThemeKeys.Color.OnSurface)
            .Themed(InputView.PlaceholderColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        SemanticProperties.SetDescription(input, prompt.Title);

        var submit = AgentTheme.TonalButton("Send", primary: true);
        submit.IsEnabled = false;
        input.TextChanged += (_, e) => submit.IsEnabled = !string.IsNullOrWhiteSpace(e.NewTextValue);
        void Send()
        {
            if (!string.IsNullOrWhiteSpace(input.Text))
                prompt.Submit(new JsonObject { ["text"] = input.Text });
        }
        submit.Clicked += (_, _) => Send();
        if (input is Entry entry)
            entry.Completed += (_, _) => Send();

        return new VerticalStackLayout
        {
            Spacing = 10,
            Children = { input, Actions(submit, ("Cancel", false, () => prompt.Decline())) }
        };
    }

    /// <summary>A row of buttons: an optional primary button first, then simple ones.</summary>
    internal static View Actions(Button? first, params (string Text, bool Primary, Action OnClick)[] buttons)
    {
        var row = new HorizontalStackLayout { Spacing = 8 };
        if (first is not null)
            row.Children.Add(first);
        foreach (var (text, primary, onClick) in buttons)
        {
            var button = primary ? AgentTheme.TonalButton(text, primary: true) : AgentTheme.LinkButton(text);
            button.Clicked += (_, _) => onClick();
            row.Children.Add(button);
        }
        return row;
    }
}
