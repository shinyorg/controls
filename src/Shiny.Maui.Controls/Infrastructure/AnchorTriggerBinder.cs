namespace Shiny.Maui.Controls.Infrastructure;

/// <summary>
/// Wires a <see cref="TooltipTrigger"/> onto an anchor view and reports back through three callbacks.
/// </summary>
/// <remarks>
/// Extracted from <see cref="Tooltip"/> so <see cref="FloatingToolbar"/> gets the same behaviour
/// rather than a second version of it. Everything awkward about anchoring to an arbitrary MAUI view
/// lives here — a <see cref="Button"/> that never routes touch to its gesture recognizers, a long
/// press that cannot be timed from a pan — and those are exactly the details that do not survive
/// being reimplemented from memory.
/// <para>
/// The binder owns only the <b>opening</b> side. How long a popup lingers after the pointer leaves,
/// and whether it can be crossed to, are the owner's business.
/// </para>
/// </remarks>
sealed class AnchorTriggerBinder(Func<IDispatcher?> dispatcher)
{
    View? anchor;
    TapGestureRecognizer? tap;
    PointerGestureRecognizer? pointer;
    Button? clickAnchor;
    ImageButton? imageClickAnchor;
    DragTouchHook? pressHook;
    IDispatcherTimer? longPressTimer;


    /// <summary>Open it.</summary>
    public Action? Show { get; set; }

    /// <summary>Close it.</summary>
    public Action? Hide { get; set; }

    /// <summary>Flip it — what a tap does, so a second tap on the anchor puts it away.</summary>
    public Action? Toggle { get; set; }

    /// <summary>How long a press has to be held before <see cref="TooltipTrigger.LongPress"/> fires.</summary>
    public int LongPressDelay { get; set; } = 450;


    /// <summary>Drops any previous wiring and attaches <paramref name="trigger"/> to <paramref name="target"/>.</summary>
    public void Bind(View? target, TooltipTrigger trigger)
    {
        this.Unbind();

        if (target is null)
            return;

        this.anchor = target;

        switch (trigger)
        {
            case TooltipTrigger.Tap:
                // Button and ImageButton consume touch natively and never route it to their
                // GestureRecognizers, so a TapGestureRecognizer on one of them is silently dead.
                // Those two anchor through Clicked instead — exclusively, or a platform that did
                // deliver both would toggle twice and land back where it started.
                switch (target)
                {
                    case Button button:
                        this.clickAnchor = button;
                        button.Clicked += this.OnClicked;
                        break;

                    case ImageButton imageButton:
                        this.imageClickAnchor = imageButton;
                        imageButton.Clicked += this.OnClicked;
                        break;

                    default:
                        this.tap = new TapGestureRecognizer();
                        this.tap.Tapped += this.OnTapped;
                        target.GestureRecognizers.Add(this.tap);
                        break;
                }
                break;

            case TooltipTrigger.LongPress:
                // A pan cannot time a press — it does not begin until the finger has already moved —
                // so the hold is measured from the native touch-down instead.
                this.pressHook = new DragTouchHook(target)
                {
                    Pressed = this.OnPressed,
                    Released = this.OnReleased
                };
                break;

            case TooltipTrigger.Hover:
                this.pointer = new PointerGestureRecognizer();
                this.pointer.PointerEntered += this.OnPointerEntered;
                this.pointer.PointerExited += this.OnPointerExited;
                target.GestureRecognizers.Add(this.pointer);
                break;

            case TooltipTrigger.Focus:
                target.Focused += this.OnFocused;
                target.Unfocused += this.OnUnfocused;
                break;
        }
    }


    /// <summary>Takes every listener back off the anchor.</summary>
    public void Unbind()
    {
        this.longPressTimer?.Stop();
        this.longPressTimer = null;

        if (this.anchor is null)
            return;

        if (this.tap is not null)
        {
            this.tap.Tapped -= this.OnTapped;
            this.anchor.GestureRecognizers.Remove(this.tap);
            this.tap = null;
        }

        if (this.clickAnchor is not null)
        {
            this.clickAnchor.Clicked -= this.OnClicked;
            this.clickAnchor = null;
        }

        if (this.imageClickAnchor is not null)
        {
            this.imageClickAnchor.Clicked -= this.OnClicked;
            this.imageClickAnchor = null;
        }

        if (this.pointer is not null)
        {
            this.pointer.PointerEntered -= this.OnPointerEntered;
            this.pointer.PointerExited -= this.OnPointerExited;
            this.anchor.GestureRecognizers.Remove(this.pointer);
            this.pointer = null;
        }

        this.anchor.Focused -= this.OnFocused;
        this.anchor.Unfocused -= this.OnUnfocused;

        if (this.pressHook is not null)
        {
            this.pressHook.Pressed = null;
            this.pressHook.Released = null;
            this.pressHook = null;
        }

        this.anchor = null;
    }


    void OnTapped(object? sender, TappedEventArgs e) => this.Toggle?.Invoke();

    void OnClicked(object? sender, EventArgs e) => this.Toggle?.Invoke();

    void OnFocused(object? sender, FocusEventArgs e) => this.Show?.Invoke();

    void OnUnfocused(object? sender, FocusEventArgs e) => this.Hide?.Invoke();

    void OnPointerEntered(object? sender, PointerEventArgs e) => this.Show?.Invoke();

    void OnPointerExited(object? sender, PointerEventArgs e) => this.Hide?.Invoke();


    void OnPressed()
    {
        // Resolved here rather than at construction: an element that is not on a window yet has no
        // dispatcher, and asking for one throws. Nothing needs it until a press is actually held.
        var owner = dispatcher();
        if (owner is null)
            return;

        this.longPressTimer?.Stop();
        this.longPressTimer = owner.CreateTimer();
        this.longPressTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, this.LongPressDelay));
        this.longPressTimer.IsRepeating = false;
        this.longPressTimer.Tick += (_, _) => this.Show?.Invoke();
        this.longPressTimer.Start();
    }


    void OnReleased()
    {
        this.longPressTimer?.Stop();
        this.longPressTimer = null;
    }
}
