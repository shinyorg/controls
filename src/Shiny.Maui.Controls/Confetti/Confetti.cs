namespace Shiny.Maui.Controls;

/// <summary>
/// Attached properties that make any view throw confetti when it is tapped.
/// </summary>
/// <example>
/// <code>
/// &lt;Button Text="Ship it" shiny:Confetti.Trigger="Tap" shiny:Confetti.Preset="Stars" /&gt;
/// </code>
/// </example>
/// <remarks>
/// <para>
/// Buttons (<see cref="Button"/>, <see cref="ImageButton"/>, <see cref="ShinyButton"/>,
/// <see cref="Fab"/>) are hooked through their <c>Clicked</c> event - MAUI's own buttons ignore
/// gesture recognizers outright - and burst from their centre. Everything else gets a
/// <see cref="TapGestureRecognizer"/> and bursts from the exact point that was touched.
/// </para>
/// <para>
/// Nothing about the view's own handling changes: the confetti fires alongside its click, not
/// instead of it.
/// </para>
/// </remarks>
public static class Confetti
{
    /// <summary>The gesture that fires confetti. <see cref="ConfettiTrigger.None"/> (the default) detaches it.</summary>
    public static readonly BindableProperty TriggerProperty = BindableProperty.CreateAttached(
        "Trigger",
        typeof(ConfettiTrigger),
        typeof(Confetti),
        ConfettiTrigger.None,
        propertyChanged: OnTriggerChanged
    );

    /// <summary>Which preset to fire. Ignored when <see cref="OptionsProperty"/> is set.</summary>
    public static readonly BindableProperty PresetProperty = BindableProperty.CreateAttached(
        "Preset",
        typeof(ConfettiPreset),
        typeof(Confetti),
        ConfettiPreset.Burst
    );

    /// <summary>A custom burst. Its origin is replaced by the point that was tapped.</summary>
    public static readonly BindableProperty OptionsProperty = BindableProperty.CreateAttached(
        "Options",
        typeof(ConfettiOptions),
        typeof(Confetti),
        null
    );

    static readonly BindableProperty HookProperty = BindableProperty.CreateAttached(
        "Hook",
        typeof(ConfettiGestureHook),
        typeof(Confetti),
        null
    );

    public static ConfettiTrigger GetTrigger(BindableObject view) => (ConfettiTrigger)view.GetValue(TriggerProperty);
    public static void SetTrigger(BindableObject view, ConfettiTrigger value) => view.SetValue(TriggerProperty, value);

    public static ConfettiPreset GetPreset(BindableObject view) => (ConfettiPreset)view.GetValue(PresetProperty);
    public static void SetPreset(BindableObject view, ConfettiPreset value) => view.SetValue(PresetProperty, value);

    public static ConfettiOptions? GetOptions(BindableObject view) => (ConfettiOptions?)view.GetValue(OptionsProperty);
    public static void SetOptions(BindableObject view, ConfettiOptions? value) => view.SetValue(OptionsProperty, value);

    internal static ConfettiGestureHook? GetHook(BindableObject view) => (ConfettiGestureHook?)view.GetValue(HookProperty);


    static void OnTriggerChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not View view)
            return;

        GetHook(view)?.Detach();
        view.ClearValue(HookProperty);

        var trigger = (ConfettiTrigger)newValue;
        if (trigger != ConfettiTrigger.None)
            view.SetValue(HookProperty, new ConfettiGestureHook(view, trigger));
    }


    /// <summary>Fires the view's configured burst from <paramref name="origin"/> (a fraction of the page), or from its centre.</summary>
    internal static Task FireFrom(View view, Point? origin)
    {
        origin ??= ConfettiHost.NormalizedCenter(view);
        return ConfettiHost.FireAsync(view, ConfettiHost.Shots(GetPreset(view), GetOptions(view), origin));
    }
}


/// <summary>The subscription one view's <see cref="Confetti.TriggerProperty"/> made, so it can be undone.</summary>
sealed class ConfettiGestureHook
{
    readonly View view;
    readonly TapGestureRecognizer? tap;

    public ConfettiGestureHook(View view, ConfettiTrigger trigger)
    {
        this.view = view;
        this.view.Loaded += this.OnLoaded;
        if (this.view.IsLoaded)
            ConfettiHost.Prepare(view);

        switch (view)
        {
            case Button button:
                button.Clicked += this.OnClicked;
                break;

            case ImageButton imageButton:
                imageButton.Clicked += this.OnClicked;
                break;

            case ShinyButton shinyButton:
                shinyButton.Clicked += this.OnClicked;
                break;

            case Fab fab:
                fab.Clicked += this.OnClicked;
                break;

            default:
                this.tap = new TapGestureRecognizer
                {
                    NumberOfTapsRequired = trigger == ConfettiTrigger.DoubleTap ? 2 : 1
                };
                this.tap.Tapped += this.OnTapped;
                view.GestureRecognizers.Add(this.tap);
                break;
        }
    }

    internal TapGestureRecognizer? Recognizer => this.tap;

    public void Detach()
    {
        this.view.Loaded -= this.OnLoaded;

        switch (this.view)
        {
            case Button button:
                button.Clicked -= this.OnClicked;
                break;
            case ImageButton imageButton:
                imageButton.Clicked -= this.OnClicked;
                break;
            case ShinyButton shinyButton:
                shinyButton.Clicked -= this.OnClicked;
                break;
            case Fab fab:
                fab.Clicked -= this.OnClicked;
                break;
        }

        if (this.tap is not null)
        {
            this.tap.Tapped -= this.OnTapped;
            this.view.GestureRecognizers.Remove(this.tap);
        }
    }

    void OnLoaded(object? sender, EventArgs e) => ConfettiHost.Prepare(this.view);

    void OnClicked(object? sender, EventArgs e) => _ = Confetti.FireFrom(this.view, null);

    void OnTapped(object? sender, TappedEventArgs e)
        => _ = Confetti.FireFrom(this.view, ConfettiHost.Normalize(this.view, root => e.GetPosition(root)));
}
