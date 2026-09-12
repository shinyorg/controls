using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Shiny.Controls.MotionIcons;

namespace Shiny.Blazor.Controls;

/// <summary>
/// A button that knows what it is doing: a leading and a trailing icon slot, a real working state,
/// and success/error states, all wired to the theme.
/// </summary>
/// <remarks>
/// <para>The parameter surface mirrors the MAUI <c>ShinyButton</c> one-for-one, with two differences
/// the platforms force. There is no <c>ICommand</c> on the web, so the command-state integration is
/// MAUI-only; its equivalent here is that <see cref="Clicked"/> is awaited — an <c>async</c> handler
/// holds the button busy for exactly as long as it runs, which is the same outcome by a shorter
/// route.</para>
/// <para>The other difference is colour. Motion icons default to <c>currentColor</c> in the browser,
/// so they inherit the button's own colour including the hover and disabled states with nothing to
/// wire up.</para>
/// </remarks>
public partial class ShinyButton : IDisposable
{
    // The rendered state, which is not always the State parameter: an async Clicked handler drives it
    // from the inside. Parameters win whenever the parent actually changes one - see OnParametersSet.
    ButtonState currentState;
    ButtonState lastStateParameter;
    bool lastIsBusyParameter;

    // The slot icons the button drives on click. See PlaySlotMotionIcons for why the button holds
    // these rather than letting the icons trigger themselves.
    MotionIcon? leftMotion;
    MotionIcon? rightMotion;

    CancellationTokenSource? revertCts;

    // Group membership. A button that is not inside a ButtonGroup never touches any of this.
    ButtonGroup? registeredGroup;
    bool appearanceSupplied;


    // ---------------------------------------------------------------------------------------------
    // Content
    // ---------------------------------------------------------------------------------------------

    /// <summary>The button's label.</summary>
    [Parameter] public string? Text { get; set; }

    /// <summary>Explicit label colour. Overrides <see cref="Appearance"/> and <see cref="Type"/>.</summary>
    [Parameter] public string? TextColor { get; set; }

    /// <summary>Label size in px. The default, <c>-1</c>, lets the theme's type scale decide.</summary>
    [Parameter] public double FontSize { get; set; } = -1d;

    /// <summary>Label font family. Unset inherits from the page.</summary>
    [Parameter] public string? FontFamily { get; set; }

    /// <summary>Label weight, as a CSS <c>font-weight</c> value. Unset follows the theme's label weight.</summary>
    [Parameter] public string? FontWeight { get; set; }


    // ---------------------------------------------------------------------------------------------
    // Surface
    // ---------------------------------------------------------------------------------------------

    /// <summary>How much of the button is painted.</summary>
    [Parameter] public ButtonAppearance Appearance { get; set; } = ButtonAppearance.Filled;

    /// <summary>
    /// The <see cref="ButtonGroup"/> this button is a segment of, if any. Public because a private
    /// cascaded parameter compiles, runs, and is silently skipped.
    /// </summary>
    [CascadingParameter] public ButtonGroup? Group { get; set; }

    /// <summary>
    /// What the button actually paints as. Identical to <see cref="Appearance"/> everywhere except
    /// inside a group that is in a selection mode, where the group owns the selected/unselected look.
    /// </summary>
    ButtonAppearance EffectiveAppearance
        => this.registeredGroup?.AppearanceFor(this, this.appearanceSupplied) ?? this.Appearance;

    /// <summary>
    /// <c>aria-pressed</c> for a segment of a selection group, and nothing at all otherwise — an
    /// ordinary action button that reports a pressed state is announced as a toggle it is not.
    /// </summary>
    string? AriaPressed
        => this.registeredGroup is { SelectionMode: not ButtonGroupSelectionMode.None } group
            ? group.IsSelected(this) ? "true" : "false"
            : null;

    /// <summary>
    /// Re-renders the segment after its group's selection moved — but only once there is something to
    /// re-render. A group pushes state onto its segments while they are still registering, during its
    /// own first render pass, and asking for a repaint before the render handle exists throws.
    /// </summary>
    internal void NotifyGroupChanged()
    {
        if (this.hasRendered)
            this.InvokeAsync(this.StateHasChanged);
    }

    bool hasRendered;

    /// <summary>Which semantic colour family the button draws from.</summary>
    [Parameter] public ButtonType Type { get; set; } = ButtonType.Primary;

    /// <summary>Explicit fill. Wins over <see cref="Appearance"/>/<see cref="Type"/>.</summary>
    [Parameter] public string? ButtonBackgroundColor { get; set; }

    /// <summary>Explicit outline colour.</summary>
    [Parameter] public string? BorderColor { get; set; }

    /// <summary>
    /// Outline thickness in px. The default, <c>-1</c>, lets <see cref="Appearance"/> decide.
    /// </summary>
    [Parameter] public double BorderThickness { get; set; } = -1d;

    /// <summary>Corner radius in px. The default, <c>-1</c>, lets the theme's shape scale decide.</summary>
    [Parameter] public double CornerRadius { get; set; } = -1d;

    /// <summary>
    /// The inset between the button's edge and its content, as a CSS <c>padding</c> value.
    /// Unset follows the theme's density.
    /// </summary>
    [Parameter] public string? ContentPadding { get; set; }

    /// <summary>
    /// Drop shadow. Unset lets <see cref="Appearance"/> decide — on for
    /// <see cref="ButtonAppearance.Elevated"/>, off otherwise.
    /// </summary>
    [Parameter] public bool? HasShadow { get; set; }

    /// <summary>Whether the button stretches to fill its container.</summary>
    [Parameter] public bool FullWidth { get; set; }


    // ---------------------------------------------------------------------------------------------
    // Icons
    // ---------------------------------------------------------------------------------------------

    /// <summary>An image URL, or raw SVG/HTML markup, for the leading slot.</summary>
    [Parameter] public string? LeftIcon { get; set; }

    /// <summary>An image URL, or raw SVG/HTML markup, for the trailing slot.</summary>
    [Parameter] public string? RightIcon { get; set; }

    /// <summary>
    /// The name of a motion icon for the leading slot — see <see cref="MotionIconLibrary"/>. Takes
    /// precedence over <see cref="LeftIcon"/>, and plays a cycle on click when
    /// <see cref="MotionIconPlayOnClick"/> is set.
    /// </summary>
    [Parameter] public string? LeftMotionIcon { get; set; }

    /// <summary>A motion icon for the trailing slot. Takes precedence over <see cref="RightIcon"/>.</summary>
    [Parameter] public string? RightMotionIcon { get; set; }

    /// <summary>Arbitrary content for the leading slot. Wins over both icon parameters.</summary>
    [Parameter] public RenderFragment? LeftIconContent { get; set; }

    /// <summary>Arbitrary content for the trailing slot.</summary>
    [Parameter] public RenderFragment? RightIconContent { get; set; }

    /// <summary>Icon width and height in px.</summary>
    [Parameter] public double IconSize { get; set; } = 20d;

    /// <summary>
    /// Icon colour. Unset leaves motion icons on <c>currentColor</c>, so they follow the label
    /// through hover and disabled without any work.
    /// </summary>
    [Parameter] public string? IconColor { get; set; }

    /// <summary>Gap between an icon and the label, in px. The default, <c>-1</c>, follows the theme's spacing.</summary>
    [Parameter] public double IconSpacing { get; set; } = -1d;

    /// <summary>Where the icons sit relative to the text.</summary>
    [Parameter] public ButtonContentLayout ContentLayout { get; set; } = ButtonContentLayout.Sides;

    /// <summary>Whether motion icons in the slots play one cycle when the button is clicked.</summary>
    [Parameter] public bool MotionIconPlayOnClick { get; set; } = true;

    /// <summary>Stroke weight for motion icons, in their own 24-unit space.</summary>
    [Parameter] public double MotionIconStrokeWidth { get; set; } = 2d;


    // ---------------------------------------------------------------------------------------------
    // State
    // ---------------------------------------------------------------------------------------------

    /// <summary>What the button is doing. Supports <c>@bind-State</c>.</summary>
    [Parameter] public ButtonState State { get; set; } = ButtonState.Normal;

    /// <summary>Raised whenever the state changes, however it changed.</summary>
    [Parameter] public EventCallback<ButtonState> StateChanged { get; set; }

    /// <summary>
    /// Shorthand for <see cref="State"/> being <see cref="ButtonState.Busy"/>. Supports
    /// <c>@bind-IsBusy</c>.
    /// </summary>
    /// <remarks>
    /// Setting it false only unwinds the busy state — it will not cut a
    /// <see cref="ButtonState.Success"/> or <see cref="ButtonState.Error"/> short, because a parent
    /// clearing its busy flag at the end of an operation is exactly when the outcome is being shown.
    /// </remarks>
    [Parameter] public bool IsBusy { get; set; }

    /// <summary>Raised when the busy state changes.</summary>
    [Parameter] public EventCallback<bool> IsBusyChanged { get; set; }

    /// <summary>What the busy state does to the content.</summary>
    [Parameter] public ButtonBusyMode BusyMode { get; set; } = ButtonBusyMode.ReplaceLeftIcon;

    /// <summary>Stands in for <see cref="Text"/> while busy. Null keeps the label as-is.</summary>
    [Parameter] public string? BusyText { get; set; }

    /// <summary>Stands in for <see cref="Text"/> in the success state.</summary>
    [Parameter] public string? SuccessText { get; set; }

    /// <summary>Stands in for <see cref="Text"/> in the error state.</summary>
    [Parameter] public string? ErrorText { get; set; }

    /// <summary>
    /// The motion icon used as the busy indicator. Defaults to <c>loader</c>; clearing it falls back
    /// to a CSS spinner.
    /// </summary>
    [Parameter] public string? BusyMotionIcon { get; set; } = "loader";

    /// <summary>Content of your own to use as the busy indicator. Wins over <see cref="BusyMotionIcon"/>.</summary>
    [Parameter] public RenderFragment? BusyContent { get; set; }

    /// <summary>The motion icon shown in the success state. Defaults to <c>check</c>.</summary>
    [Parameter] public string? SuccessMotionIcon { get; set; } = "check";

    /// <summary>The motion icon shown in the error state. Defaults to <c>warning</c>.</summary>
    [Parameter] public string? ErrorMotionIcon { get; set; } = "warning";

    /// <summary>
    /// How long the success and error states hold before returning to normal.
    /// <see cref="TimeSpan.Zero"/> holds forever.
    /// </summary>
    [Parameter] public TimeSpan StateRevertDelay { get; set; } = TimeSpan.FromSeconds(1.5);

    /// <summary>Whether the button stops accepting clicks while busy.</summary>
    [Parameter] public bool DisableWhileBusy { get; set; } = true;

    /// <summary>
    /// Whether the button holds itself busy for the duration of an <c>async</c>
    /// <see cref="Clicked"/> handler.
    /// </summary>
    /// <remarks>
    /// A synchronous handler never flickers: the task is checked for completion before any state
    /// change is made.
    /// </remarks>
    [Parameter] public bool AutoBusy { get; set; } = true;

    /// <summary>
    /// Whether a <see cref="Clicked"/> handler that throws puts the button into
    /// <see cref="ButtonState.Error"/>. The exception is still rethrown either way.
    /// </summary>
    [Parameter] public bool ShowErrorOnFault { get; set; } = true;


    // ---------------------------------------------------------------------------------------------
    // Interaction
    // ---------------------------------------------------------------------------------------------

    /// <summary>Raised on click. An <c>async</c> handler is awaited — see <see cref="AutoBusy"/>.</summary>
    [Parameter] public EventCallback<MouseEventArgs> Clicked { get; set; }

    /// <summary>Disables the button outright.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Extra classes for the button element.</summary>
    [Parameter] public string? CssClass { get; set; }

    /// <summary>Anything else is splatted onto the button element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }


    // ---------------------------------------------------------------------------------------------
    // Parameter / internal state reconciliation
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Records whether the caller actually supplied <see cref="Appearance"/>, which a plain parameter
    /// cannot say: its default is a real value, so a segment that never mentioned an appearance is
    /// indistinguishable from one that asked for <c>Filled</c>. A <see cref="ButtonGroup"/> in a
    /// selection mode needs the difference — an appearance the author set wins as the unselected look,
    /// and the default must not.
    /// </summary>
    public override Task SetParametersAsync(ParameterView parameters)
    {
        if (parameters.TryGetValue<ButtonAppearance>(nameof(this.Appearance), out _))
            this.appearanceSupplied = true;

        return base.SetParametersAsync(parameters);
    }


    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
            this.hasRendered = true;
    }


    protected override void OnInitialized()
    {
        this.currentState = this.IsBusy ? ButtonState.Busy : this.State;
        this.lastStateParameter = this.State;
        this.lastIsBusyParameter = this.IsBusy;

        // A button whose very first render is already Success or Error still has to revert. The
        // OnParametersSet path cannot arm it - the parameter has not *changed* on that first pass -
        // so it is armed here instead.
        this.ScheduleRevert();
    }

    protected override void OnParametersSet()
    {
        // Group membership, if any. Registration order is render order, which is what gives a segment
        // its index; the group is told nothing that would make it re-render, or the pair would spin.
        if (!ReferenceEquals(this.registeredGroup, this.Group))
        {
            this.registeredGroup?.Unregister(this);
            this.registeredGroup = this.Group;
            this.registeredGroup?.Register(this);
        }

        // These two shadows record what the PARENT last supplied, and nothing else may write them.
        // That is the whole mechanism: "did the parent change it" is only a meaningful question while
        // they track the parent. Writing the internal state into them (as SetStateAsync used to)
        // makes the parent's next *unchanged* value look like a change, which then undoes whatever
        // the button just did - killing an async handler's busy state one render after it started.
        var parentSetState = this.State != this.lastStateParameter;
        var parentSetIsBusy = this.IsBusy != this.lastIsBusyParameter;

        this.lastStateParameter = this.State;
        this.lastIsBusyParameter = this.IsBusy;

        if (parentSetState)
        {
            this.MoveTo(this.State);
        }
        else if (parentSetIsBusy)
        {
            if (this.IsBusy)
                this.MoveTo(ButtonState.Busy);
            else if (this.currentState is ButtonState.Busy)
                // Only unwinds Busy - a parent clearing its flag must not cut Success or Error short.
                this.MoveTo(ButtonState.Normal);
        }
    }

    /// <summary>
    /// Moves the rendered state without notifying anyone — for transitions the parent already knows
    /// about, because it is the one that asked for them.
    /// </summary>
    void MoveTo(ButtonState state)
    {
        // Guarded so a parent re-supplying the state it already set does not re-arm the revert timer
        // and silently extend the hold.
        if (this.currentState == state)
            return;

        this.currentState = state;
        this.ScheduleRevert();
    }

    ButtonState CurrentState => this.currentState;

    async Task SetStateAsync(ButtonState state)
    {
        if (this.currentState == state)
            return;

        this.currentState = state;
        this.ScheduleRevert();

        if (this.StateChanged.HasDelegate)
            await this.StateChanged.InvokeAsync(state);

        if (this.IsBusyChanged.HasDelegate)
            await this.IsBusyChanged.InvokeAsync(state is ButtonState.Busy);

        this.StateHasChanged();
    }

    void ScheduleRevert()
    {
        this.revertCts?.Cancel();
        this.revertCts?.Dispose();
        this.revertCts = null;

        if (this.currentState is not (ButtonState.Success or ButtonState.Error))
            return;

        if (this.StateRevertDelay <= TimeSpan.Zero)
            return;

        var cts = new CancellationTokenSource();
        this.revertCts = cts;
        _ = this.RevertAfterAsync(this.StateRevertDelay, cts.Token);
    }

    async Task RevertAfterAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
            return;

        // Back onto the renderer's context: Task.Delay resumes on a thread pool thread and touching
        // component state from there races the render loop.
        await this.InvokeAsync(async () =>
        {
            if (this.currentState is ButtonState.Success or ButtonState.Error)
                await this.SetStateAsync(ButtonState.Normal);
        });
    }


    // ---------------------------------------------------------------------------------------------
    // Click
    // ---------------------------------------------------------------------------------------------

    async Task HandleClick(MouseEventArgs e)
    {
        if (this.IsDisabled)
            return;

        this.PlaySlotMotionIcons();

        // The group moves its selection before the handler runs, so a handler that reads SelectedIndex
        // sees the click it is reacting to rather than the one before it.
        if (this.registeredGroup is not null)
            await this.registeredGroup.NotifyClickedAsync(this);

        if (!this.Clicked.HasDelegate)
            return;

        var work = this.Clicked.InvokeAsync(e);

        // A synchronous handler has already finished, so going busy would be a one-frame flicker for
        // nothing.
        if (!this.AutoBusy || work.IsCompleted)
        {
            await work;
            return;
        }

        await this.SetStateAsync(ButtonState.Busy);

        try
        {
            await work;
        }
        catch
        {
            if (this.ShowErrorOnFault)
                await this.EndAutoBusyAsync(ButtonState.Error);
            else
                await this.EndAutoBusyAsync(ButtonState.Normal);

            // The handler's exception is the app's to deal with; the button only reflected it.
            throw;
        }

        await this.EndAutoBusyAsync(ButtonState.Normal);
    }

    async Task EndAutoBusyAsync(ButtonState state)
    {
        // Only unwind if the button is still the one holding the busy state. A handler that set State
        // itself - to Success, typically - has already had the last word.
        if (this.currentState is ButtonState.Busy)
            await this.SetStateAsync(state);
    }


    // ---------------------------------------------------------------------------------------------
    // Rendering
    // ---------------------------------------------------------------------------------------------

    bool IsDisabled => this.Disabled || (this.DisableWhileBusy && this.currentState is ButtonState.Busy);

    /// <summary>
    /// The accessible name, but only when the label is not already providing one.
    /// </summary>
    /// <remarks>
    /// Emitted only when there is text, so that an icon-only button gets no
    /// <c>aria-label=""</c> — which would give it an empty accessible name and leave a screen reader
    /// announcing nothing but "button". With no text the caller's own splatted <c>aria-label</c>
    /// becomes the only source, which is what the docs tell them to supply.
    /// </remarks>
    string? AriaLabel => string.IsNullOrEmpty(this.EffectiveText) ? null : this.EffectiveText;

    string? EffectiveText => this.currentState switch
    {
        ButtonState.Busy => this.BusyText ?? this.Text,
        ButtonState.Success => this.SuccessText ?? this.Text,
        ButtonState.Error => this.ErrorText ?? this.Text,
        _ => this.Text
    };

    bool HasOwnLeftSlot
        => this.LeftIconContent is not null
            || !string.IsNullOrWhiteSpace(this.LeftMotionIcon)
            || !string.IsNullOrWhiteSpace(this.LeftIcon);

    bool HasOwnRightSlot
        => this.RightIconContent is not null
            || !string.IsNullOrWhiteSpace(this.RightMotionIcon)
            || !string.IsNullOrWhiteSpace(this.RightIcon);

    bool ShowLeftSlot
        => this.HasOwnLeftSlot
            || this.StateIndicator is not null
            || (this.currentState is ButtonState.Busy && this.BusyMode is ButtonBusyMode.ReplaceLeftIcon);

    bool ShowRightSlot => this.HasOwnRightSlot;

    RenderFragment? LeftSlot
        => this.BuildSlot(this.LeftIconContent, this.LeftMotionIcon, this.LeftIcon,
            icon => this.leftMotion = icon);

    RenderFragment? RightSlot
        => this.BuildSlot(this.RightIconContent, this.RightMotionIcon, this.RightIcon,
            icon => this.rightMotion = icon);

    /// <summary>
    /// Whether the leading slot is currently showing the caller's own icon rather than the busy or
    /// state indicator — the same condition the markup branches on. A captured reference outlives
    /// the render that replaced it, so playback has to check that the icon it holds is still the one
    /// on screen.
    /// </summary>
    bool LeftSlotShowsOwnIcon
        => !(this.currentState is ButtonState.Busy && this.BusyMode is ButtonBusyMode.ReplaceLeftIcon)
            && this.StateIndicator is null;

    /// <summary>
    /// Runs one cycle of the leading and trailing icons on click.
    /// </summary>
    /// <remarks>
    /// <para>The button owns playback rather than letting the icons trigger themselves, which is what
    /// the MAUI side does for the same reason: an icon that listens for its own press only hears the
    /// clicks that land on the glyph, so clicking the label — most of the button — animated nothing.
    /// Every icon the button builds is therefore <see cref="MotionTrigger.Manual"/>, and the button
    /// plays it from its own click.</para>
    /// <para>Deliberately not the busy indicator or the state icons: those are driven by
    /// <see cref="State"/> and a click must not restart them mid-cycle.</para>
    /// </remarks>
    void PlaySlotMotionIcons()
    {
        if (!this.MotionIconPlayOnClick)
            return;

        if (this.LeftSlotShowsOwnIcon)
            this.leftMotion?.Play();

        this.rightMotion?.Play();
    }

    /// <summary>The success/error glyph, or null in any other state (or if the caller cleared it).</summary>
    RenderFragment? StateIndicator
    {
        get
        {
            var name = this.currentState switch
            {
                ButtonState.Success => this.SuccessMotionIcon,
                ButtonState.Error => this.ErrorMotionIcon,
                _ => null
            };

            return string.IsNullOrWhiteSpace(name)
                ? null
                // Appear rather than Manual: the glyph is only in the DOM while the state holds, so
                // becoming visible *is* the cue to draw itself on. No refs, no interop.
                : this.MotionIconFragment(name!, MotionTrigger.Appear, loop: false);
        }
    }

    /// <summary>
    /// Picks the busy indicator: caller-supplied content, else a motion icon, else the CSS spinner.
    /// </summary>
    /// <param name="fallbackSpinner">
    /// The spinner, passed in from the markup rather than built here. CSS isolation only stamps its
    /// scope attribute onto elements that appear in <c>.razor</c> markup, so a span built with
    /// <see cref="RenderTreeBuilder"/> would never match the scoped <c>.shiny-btn__spinner</c> rule
    /// and would render as an invisible box.
    /// </param>
    RenderFragment ResolveBusyIndicator(RenderFragment fallbackSpinner)
    {
        if (this.BusyContent is not null)
            return this.BusyContent;

        if (!string.IsNullOrWhiteSpace(this.BusyMotionIcon))
            return this.MotionIconFragment(this.BusyMotionIcon!, MotionTrigger.Loop, loop: true);

        return fallbackSpinner;
    }

    string SpinnerStyle => Invariant($"width:{this.IconSize}px;height:{this.IconSize}px;");

    RenderFragment? BuildSlot(
        RenderFragment? content, string? motionIcon, string? icon, Action<MotionIcon> capture)
    {
        if (content is not null)
            return content;

        if (!string.IsNullOrWhiteSpace(motionIcon))
            // Manual, with the instance captured: the button plays it. See PlaySlotMotionIcons.
            return this.MotionIconFragment(motionIcon!, MotionTrigger.Manual, loop: false, capture);

        if (string.IsNullOrWhiteSpace(icon))
            return null;

        var value = icon!;
        return builder =>
        {
            if (IsImageUrl(value))
            {
                builder.OpenElement(0, "img");
                builder.AddAttribute(1, "src", value);
                builder.AddAttribute(2, "alt", string.Empty);
                builder.CloseElement();
            }
            else
            {
                builder.AddMarkupContent(3, value);
            }
        };
    }

    RenderFragment MotionIconFragment(
        string name, MotionTrigger trigger, bool loop, Action<MotionIcon>? capture = null) => builder =>
    {
        builder.OpenComponent<MotionIcon>(0);
        builder.AddComponentParameter(1, nameof(MotionIcon.Icon), name);
        builder.AddComponentParameter(2, nameof(MotionIcon.Trigger), trigger);
        builder.AddComponentParameter(3, nameof(MotionIcon.Size), this.IconSize);
        builder.AddComponentParameter(4, nameof(MotionIcon.StrokeWidth), this.MotionIconStrokeWidth);
        // Left on currentColor unless the caller asked otherwise, so the icon tracks the button's
        // hover and disabled colours for free.
        builder.AddComponentParameter(5, nameof(MotionIcon.Color), this.IconColor ?? "currentColor");
        // Zero or less repeats forever.
        builder.AddComponentParameter(6, nameof(MotionIcon.RepeatCount), loop ? 0 : 1);

        if (capture is not null)
            builder.AddComponentReferenceCapture(7, instance => capture((MotionIcon)instance));

        builder.CloseComponent();
    };

    string ButtonCssClass
    {
        get
        {
            var sb = new StringBuilder("shiny-btn");
            sb.Append(" shiny-btn--").Append(Lower(this.EffectiveAppearance.ToString()));
            sb.Append(" shiny-btn--").Append(Lower(this.Type.ToString()));

            if (this.ContentLayout is not ButtonContentLayout.Sides)
                sb.Append(" shiny-btn--stack-").Append(Lower(this.ContentLayout.ToString()));

            if (this.currentState is not ButtonState.Normal)
                sb.Append(" shiny-btn--").Append(Lower(this.currentState.ToString()));

            if (this.currentState is ButtonState.Busy && this.BusyMode is ButtonBusyMode.ReplaceContent)
                sb.Append(" shiny-btn--hide-content");

            if (this.HasShadow ?? this.Appearance is ButtonAppearance.Elevated)
                sb.Append(" shiny-btn--shadow");

            if (this.FullWidth)
                sb.Append(" shiny-btn--full");

            if (!string.IsNullOrEmpty(this.CssClass))
                sb.Append(' ').Append(this.CssClass);

            return sb.ToString();
        }
    }

    string InlineStyle
    {
        get
        {
            var sb = new StringBuilder();

            // Only emit what the caller explicitly set. An inline declaration wins over the scoped
            // stylesheet outright, so writing these unconditionally would make every themed rule in
            // ShinyButton.razor.css dead on arrival.
            if (this.CornerRadius >= 0)
                sb.Append(Invariant($"--shiny-btn-radius:{this.CornerRadius}px;"));

            if (this.IconSpacing >= 0)
                sb.Append(Invariant($"--shiny-btn-gap:{this.IconSpacing}px;"));

            sb.Append(Invariant($"--shiny-btn-icon:{this.IconSize}px;"));

            if (this.FontSize >= 0)
                sb.Append(Invariant($"font-size:{this.FontSize}px;"));

            if (!string.IsNullOrEmpty(this.ContentPadding))
                sb.Append("padding:").Append(this.ContentPadding).Append(';');

            if (!string.IsNullOrEmpty(this.FontFamily))
                sb.Append("font-family:").Append(this.FontFamily).Append(';');

            if (!string.IsNullOrEmpty(this.FontWeight))
                sb.Append("font-weight:").Append(this.FontWeight).Append(';');

            // Explicit colours win over the appearance/type tokens by overriding the same custom
            // properties the stylesheet reads, so there is no specificity fight.
            if (!string.IsNullOrEmpty(this.ButtonBackgroundColor))
                sb.Append("--shiny-btn-bg:").Append(this.ButtonBackgroundColor).Append(';');

            if (!string.IsNullOrEmpty(this.TextColor))
                sb.Append("--shiny-btn-fg:").Append(this.TextColor).Append(';');

            if (!string.IsNullOrEmpty(this.BorderColor))
                sb.Append("--shiny-btn-stroke:").Append(this.BorderColor).Append(';');

            if (this.BorderThickness >= 0)
                sb.Append(Invariant($"--shiny-btn-stroke-width:{this.BorderThickness}px;"));

            return sb.ToString();
        }
    }

    static string Lower(string value) => value.ToLowerInvariant();

    static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);

    static bool IsImageUrl(string s)
        => s.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith('/')
            || s.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || s.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || s.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
            || s.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        this.registeredGroup?.Unregister(this);
        this.registeredGroup = null;

        this.revertCts?.Cancel();
        this.revertCts?.Dispose();
        this.revertCts = null;
    }
}
