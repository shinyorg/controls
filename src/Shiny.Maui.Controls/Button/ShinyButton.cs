using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

/// <summary>
/// A button that knows what it is doing: a leading and a trailing icon slot, a real working state,
/// and success/error states, all wired to the theme and to its <see cref="Command"/>.
/// </summary>
/// <remarks>
/// <para><see cref="Microsoft.Maui.Controls.Button"/> renders text and one image and nothing else —
/// there is no way to put a spinner inside it, so "submit, spin, tick" ends up hand-assembled on
/// every page out of a Grid, an ActivityIndicator and a swapped label. This control is that
/// assembly, done once.</para>
/// <para>The composition is the same as <see cref="Fab"/>: a <see cref="Border"/> for the painted
/// surface with a grid of slots inside it. What it adds is the <see cref="State"/> machine and the
/// <see cref="Command"/> integration — a command that cannot execute disables the button, and an
/// async one drives <see cref="ButtonState.Busy"/> by itself.</para>
/// <para>The icon slots each take an <see cref="ImageSource"/>, the name of a motion icon, or an
/// arbitrary <see cref="View"/>, in ascending order of precedence. Motion icons get their colour and
/// their playback from the button for free: they play a cycle on tap and loop while busy.</para>
/// </remarks>
public partial class ShinyButton : ContentView
{
    const double DefaultIconSize = 20;
    const double DefaultIconSpacing = 8;
    const double DefaultMinimumHeight = 44;

    readonly Border border;
    readonly Grid rootGrid;
    readonly Grid innerGrid;
    readonly ContentView leftHost;
    readonly ContentView rightHost;
    readonly ContentView busyHost;
    readonly ContentView busyOverlay;
    readonly Label textLabel;

    // Only used by the stacked layouts, where both icons share a row - see UpdateContentLayout.
    readonly HorizontalStackLayout iconStack;

    // Built once and toggled by opacity rather than assigned per state: swapping VisualElement.Shadow
    // while the user is interacting unfocuses the content inside it on Android.
    readonly Shadow shadow;


    // Each slot's own content, rebuilt only when the icon properties change, so a motion icon is not
    // torn down and restarted every time the state moves.
    View? leftOwn;
    View? rightOwn;

    // Set by a ButtonGroup while this button is one of its segments. See SetSegmentCorners.
    CornerRadius? segmentCorners;

    // Cached overrides, reused across state transitions for the same reason.
    View? busyIndicator;
    View? successIndicator;
    View? errorIndicator;
    ContentView? busyIndicatorHost;

    IDispatcherTimer? revertTimer;
    bool commandCanExecute = true;
    bool subscribedToCommand;
    bool syncingBusy;
    bool isLoaded;
    string? ownedDescription;

    // Held in fields so Unloaded can detach them - see TrackCommandFlag.
    INotifyPropertyChanged? flagNotifier;
    PropertyChangedEventHandler? flagHandler;

    public ShinyButton()
    {
        this.textLabel = new Label
        {
            LineBreakMode = LineBreakMode.NoWrap,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            IsVisible = false
        };

        this.leftHost = NewSlot();
        this.rightHost = NewSlot();
        this.busyHost = NewSlot();

        this.iconStack = new HorizontalStackLayout
        {
            Spacing = 0,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        this.innerGrid = new Grid
        {
            // Spacing is applied from UpdateContentLayout rather than declared here, because a Grid
            // pads between cells whether or not both hold a visible child - a hidden icon must not
            // indent the label. Same reason as Fab.UpdateContentLayout.
            ColumnSpacing = 0,
            RowSpacing = 0,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        this.busyOverlay = new ContentView
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false
        };

        this.rootGrid = new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        this.rootGrid.Add(this.innerGrid);
        this.rootGrid.Add(this.busyOverlay);


        // A SolidColorBrush rather than Brush.Black so the shadow colour can follow the theme's own
        // shadow token - a pack that lightens shadows should lighten these too.
        var shadowBrush = new SolidColorBrush();
        shadowBrush.SetDynamicResource(SolidColorBrush.ColorProperty, Themes.ShinyThemeKeys.Color.Shadow);

        this.shadow = new Shadow
        {
            Brush = shadowBrush,
            Opacity = 0.22f,
            Radius = 6,
            Offset = new Point(0, 2)
        };

        this.border = new Border
        {
            Padding = new Thickness(16, 10),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerMediumRadius),
            Content = this.rootGrid,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };

        // The gesture goes on the button itself, not on the inner Border. The two look equivalent -
        // the Border fills the ContentView, so the hit area is identical - but a recognizer on a child
        // is invisible to UI automation: a test (or DevFlow) that resolves this control by its
        // AutomationId and taps it finds no recognizer and the tap silently does nothing. Same
        // placement, and same reason, as IconTextTool.
        var tap = new TapGestureRecognizer();
        tap.Tapped += this.OnTapped;
        this.GestureRecognizers.Add(tap);

        this.Content = this.border;
        this.MinimumHeightRequest = DefaultMinimumHeight;
        this.HorizontalOptions = LayoutOptions.Start;
        this.VerticalOptions = LayoutOptions.Center;

        this.Loaded += this.OnLoaded;
        this.Unloaded += this.OnUnloaded;

        this.UpdateContentLayout();
        this.ApplyFontSize();
        this.ApplyCornerRadius();
        this.ApplyText();
        this.ApplyIcons();
        this.ApplyAppearance();
        this.ApplyState();

        // Last line: replays any styled property that was applied before the children existed.
        // See StyleGuard.
        StyleGuard.MarkReady(this, typeof(ShinyButton));
    }

    static ContentView NewSlot() => new()
    {
        HorizontalOptions = LayoutOptions.Center,
        VerticalOptions = LayoutOptions.Center,
        IsVisible = false
    };

    /// <summary>Raised on tap, before <see cref="Command"/> runs.</summary>
    public event EventHandler? Clicked;

    /// <summary>Raised whenever <see cref="State"/> changes, however it changed.</summary>
    public event EventHandler<ButtonStateChangedEventArgs>? StateChanged;


    /// <summary>
    /// Combines MAUI's own enabled state with the button's two extra reasons to be disabled: a
    /// <see cref="Command"/> that cannot execute, and <see cref="DisableWhileBusy"/>.
    /// </summary>
    /// <remarks>
    /// This is the mechanism <see cref="Microsoft.Maui.Controls.Button"/> itself uses, and the reason
    /// the button never writes <see cref="VisualElement.IsEnabled"/>: doing that would overwrite a
    /// consumer's binding, and a command going executable again would silently re-enable a button the
    /// consumer had deliberately switched off.
    /// </remarks>
    protected override bool IsEnabledCore
        => base.IsEnabledCore
            && this.commandCanExecute
            && !(this.DisableWhileBusy && this.State is ButtonState.Busy);


    // ---------------------------------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------------------------------

    void UpdateContentLayout()
    {
        var hasLeft = this.leftHost.IsVisible;
        var hasRight = this.rightHost.IsVisible || this.busyHost.IsVisible;
        var hasText = this.textLabel.IsVisible;

        this.innerGrid.Children.Clear();
        this.innerGrid.RowDefinitions.Clear();
        this.innerGrid.ColumnDefinitions.Clear();

        // Both containers are emptied first: a View has exactly one parent, and the hosts move between
        // the grid and the icon row when ContentLayout changes.
        this.iconStack.Children.Clear();

        if (this.ContentLayout is ButtonContentLayout.Sides)
        {
            for (var i = 0; i < 4; i++)
                this.innerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            this.innerGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            this.innerGrid.Add(this.leftHost, 0, 0);
            this.innerGrid.Add(this.textLabel, 1, 0);
            this.innerGrid.Add(this.rightHost, 2, 0);
            this.innerGrid.Add(this.busyHost, 3, 0);

            this.innerGrid.ColumnSpacing = hasText && (hasLeft || hasRight) ? this.IconSpacing : 0;
            this.innerGrid.RowSpacing = 0;
        }
        else
        {
            // Stacked: both icons share one row, the label the other. Which row comes first is the
            // whole difference between Top and Bottom.
            //
            // The icons go into their own HorizontalStackLayout rather than into three grid columns
            // with the label column-spanning them. That earlier shape looked equivalent and was not:
            // MAUI's Grid grows the spanned Auto columns to fit the wider label, so a single icon in
            // column 0 ended up drawn about a sixth of the way in from the left instead of centred
            // over the text. One cell, one stack, centred - no spanning arithmetic to get wrong.
            this.innerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            this.innerGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            this.innerGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var iconRow = this.ContentLayout is ButtonContentLayout.Top ? 0 : 1;
            var textRow = iconRow == 0 ? 1 : 0;

            this.iconStack.Children.Add(this.leftHost);
            this.iconStack.Children.Add(this.rightHost);
            this.iconStack.Children.Add(this.busyHost);
            this.iconStack.Spacing = hasLeft && hasRight ? this.IconSpacing : 0;
            this.iconStack.IsVisible = hasLeft || hasRight;

            this.innerGrid.Add(this.iconStack, 0, iconRow);
            this.innerGrid.Add(this.textLabel, 0, textRow);

            this.innerGrid.ColumnSpacing = 0;
            this.innerGrid.RowSpacing = hasText && (hasLeft || hasRight) ? this.IconSpacing / 2 : 0;
        }
    }

    void ApplyCornerRadius()
    {
        var shape = new RoundRectangle();

        if (this.segmentCorners is CornerRadius corners)
            shape.CornerRadius = corners;
        else
            shape.SetCornerTokenOrValue(this.CornerRadius, ShinyThemeKeys.Shape.CornerMediumRadius);

        this.border.StrokeShape = shape;
    }


    /// <summary>
    /// Squares off the corners a <see cref="ButtonGroup"/> has joined, or hands the button back its own
    /// rounding when it leaves the group. Null restores the normal behaviour.
    /// </summary>
    /// <remarks>
    /// It has to be applied from inside <see cref="ApplyCornerRadius"/> rather than by a container
    /// writing <c>border.StrokeShape</c>: that method rebuilds the shape from scratch on every corner,
    /// appearance or state change, so a shape set from outside would be silently thrown away the next
    /// time anything about the button moved. The group resolves the radius numerically, because the
    /// unset case arrives as a dynamic resource and two of its corners have to be zeroed — see
    /// <see cref="ButtonGroup"/>.
    /// </remarks>
    internal void SetSegmentCorners(CornerRadius? corners)
    {
        if (Nullable.Equals(this.segmentCorners, corners))
            return;

        this.segmentCorners = corners;
        StyleGuard.WhenReady<ShinyButton>(this, static b => b.ApplyCornerRadius());
    }

    void ApplyFontSize()
        => this.textLabel.SetTokenOrValue(Label.FontSizeProperty, this.FontSize, ShinyThemeKeys.Type.BodyLargeSize);


    // ---------------------------------------------------------------------------------------------
    // Content
    // ---------------------------------------------------------------------------------------------

    void ApplyText()
    {
        var text = this.State switch
        {
            ButtonState.Busy => this.BusyText ?? this.Text,
            ButtonState.Success => this.SuccessText ?? this.Text,
            ButtonState.Error => this.ErrorText ?? this.Text,
            _ => this.Text
        };

        this.textLabel.Text = text ?? string.Empty;
        this.textLabel.IsVisible = !string.IsNullOrEmpty(text);

        this.ApplyDescription(text);
        this.UpdateContentLayout();
    }

    /// <summary>
    /// Keeps the accessible name in step with the state text, without ever taking it away.
    /// </summary>
    /// <remarks>
    /// <para><see cref="VisualElement.AutomationId"/> is set-once in MAUI — assigning it twice throws
    /// — so a button that relabels itself across states has to keep one stable id and move only the
    /// description.</para>
    /// <para>Two things this must not do. It must not blank the description when there is no text: an
    /// icon-only button's description is the caller's, and the docs tell them to set it. And it must
    /// not overwrite a description the caller set: <see cref="ownedDescription"/> records what this
    /// method last wrote, so anything else in there belongs to the caller and is left alone.</para>
    /// </remarks>
    void ApplyDescription(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var current = SemanticProperties.GetDescription(this);
        if (!string.IsNullOrEmpty(current) && current != this.ownedDescription)
            return;

        SemanticProperties.SetDescription(this, text);
        this.ownedDescription = text;
    }

    void ApplyIcons()
    {
        this.leftOwn = this.BuildSlotContent(this.LeftIconView, this.LeftMotionIcon, this.LeftIcon);
        this.rightOwn = this.BuildSlotContent(this.RightIconView, this.RightMotionIcon, this.RightIcon);

        this.ApplyIconSize();
        this.ApplyState();
        this.ApplyForeground();
    }

    View? BuildSlotContent(View? view, string? motionIcon, ImageSource? image)
    {
        if (view is not null)
            return view;

        if (!string.IsNullOrWhiteSpace(motionIcon))
            return this.CreateMotionIcon(motionIcon!);

        if (image is not null)
        {
            return new Image
            {
                Source = image,
                Aspect = Aspect.AspectFit,
                WidthRequest = this.IconSize,
                HeightRequest = this.IconSize,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
        }

        return null;
    }

    void ApplyIconSize()
    {
        foreach (var v in this.AllSlotViews())
            SizeIcon(v, this.IconSize);
    }

    static void SizeIcon(View? view, double size)
    {
        // A consumer-supplied view is theirs to size; only the ones the button built are resized.
        if (view is Image or ActivityIndicator || IsMotionIcon(view))
        {
            view!.WidthRequest = size;
            view.HeightRequest = size;
        }
    }

    IEnumerable<View?> AllSlotViews()
    {
        yield return this.leftOwn;
        yield return this.rightOwn;
        yield return this.busyIndicator;
        yield return this.successIndicator;
        yield return this.errorIndicator;
    }


    // ---------------------------------------------------------------------------------------------
    // Busy / state indicators
    // ---------------------------------------------------------------------------------------------

    void RebuildBusyIndicator()
    {
        this.DetachBusyIndicator();
        this.busyIndicator = null;
        this.ApplyState();
        this.ApplyForeground();
    }

    View EnsureBusyIndicator()
    {
        if (this.busyIndicator is not null)
            return this.busyIndicator;

        if (this.BusyIconView is not null)
        {
            this.busyIndicator = this.BusyIconView;
        }
        else if (!string.IsNullOrWhiteSpace(this.BusyMotionIcon))
        {
            this.busyIndicator = this.CreateMotionIcon(this.BusyMotionIcon!, looping: true);
        }
        else
        {
            // Clearing BusyMotionIcon falls back to the platform's own busy affordance.
            this.busyIndicator = new ActivityIndicator
            {
                IsRunning = true,
                WidthRequest = this.IconSize,
                HeightRequest = this.IconSize,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
        }

        SizeIcon(this.busyIndicator, this.IconSize);
        return this.busyIndicator;
    }

    View? EnsureStateIndicator(ButtonState state)
    {
        ref var cached = ref this.successIndicator;
        if (state is ButtonState.Error)
            cached = ref this.errorIndicator;

        if (cached is not null)
            return cached;

        var motion = state is ButtonState.Success ? this.SuccessMotionIcon : this.ErrorMotionIcon;
        var image = state is ButtonState.Success ? this.SuccessIcon : this.ErrorIcon;

        // These two motion properties default to a name ("check"/"warning"), unlike the slot ones
        // which default to null. So an explicit image has to beat the *default* motion icon or
        // SuccessIcon/ErrorIcon would be unreachable - but it must not beat a motion icon the caller
        // actually chose.
        var motionChosen = state is ButtonState.Success
            ? this.IsSet(SuccessMotionIconProperty)
            : this.IsSet(ErrorMotionIconProperty);

        if (image is not null && !motionChosen)
            motion = null;

        cached = this.BuildSlotContent(null, motion, image);
        SizeIcon(cached, this.IconSize);
        return cached;
    }

    /// <summary>
    /// Drops a cached state glyph so the next entry into that state rebuilds it.
    /// </summary>
    /// <remarks>
    /// Needed because the glyphs are cached to keep their animation intact across transitions, which
    /// also means a later change to <see cref="SuccessMotionIcon"/> and friends would otherwise keep
    /// re-showing the first glyph ever built — the button silently reporting a stale outcome.
    /// </remarks>
    void RebuildStateIndicator(ButtonState state)
    {
        if (state is ButtonState.Success)
            this.successIndicator = null;
        else
            this.errorIndicator = null;

        this.ApplyState();
        this.ApplyForeground();
    }

    void DetachBusyIndicator()
    {
        if (this.busyIndicatorHost is not null)
        {
            this.busyIndicatorHost.Content = null;
            this.busyIndicatorHost.IsVisible = false;
            this.busyIndicatorHost = null;
        }

        // A View has one parent, so the indicator has to leave its old host before it can enter
        // another - the overlay and the two slots are three different hosts.
        this.busyOverlay.Content = null;
        this.busyOverlay.IsVisible = false;

        this.SetMotionPlaying(this.busyIndicator, false);
    }

    /// <summary>
    /// Puts the right thing in each slot for the current <see cref="State"/>. Everything the state
    /// machine does to the visual tree goes through here.
    /// </summary>
    void ApplyState()
    {
        var state = this.State;

        this.DetachBusyIndicator();
        this.innerGrid.Opacity = 1;

        // Reset both slots to their own content, then apply whatever this state overrides.
        this.leftHost.Content = this.leftOwn;
        this.rightHost.Content = this.rightOwn;

        if (state is ButtonState.Busy)
        {
            var indicator = this.EnsureBusyIndicator();

            switch (this.BusyMode)
            {
                case ButtonBusyMode.ReplaceLeftIcon:
                    // The indicator and the icon are both IconSize square, so the button cannot
                    // change size while it works.
                    this.leftHost.Content = indicator;
                    this.busyIndicatorHost = this.leftHost;
                    break;

                case ButtonBusyMode.ReplaceContent:
                    // Opacity rather than IsVisible: the content keeps its layout space, so the
                    // button holds the width it had before it went busy without any width pinning.
                    this.innerGrid.Opacity = 0;
                    this.busyOverlay.Content = indicator;
                    this.busyOverlay.IsVisible = true;
                    break;

                case ButtonBusyMode.KeepContent:
                    this.busyHost.Content = indicator;
                    this.busyIndicatorHost = this.busyHost;
                    break;
            }

            if (this.busyIndicatorHost is not null)
                this.busyIndicatorHost.IsVisible = true;

            this.SetMotionPlaying(indicator, true);
        }
        else if (state is ButtonState.Success or ButtonState.Error)
        {
            var indicator = this.EnsureStateIndicator(state);
            if (indicator is not null)
            {
                this.leftHost.Content = indicator;
                this.PlayMotionIcon(indicator);
            }
        }

        this.leftHost.IsVisible = this.leftHost.Content is not null;
        this.rightHost.IsVisible = this.rightHost.Content is not null;

        this.UpdateContentLayout();
    }


    // ---------------------------------------------------------------------------------------------
    // State machine
    // ---------------------------------------------------------------------------------------------

    void OnStateChanged(ButtonState from, ButtonState to)
    {
        this.SyncIsBusy(to);
        this.RefreshIsEnabledProperty();

        this.ApplyText();
        this.ApplyState();
        this.ApplyForeground();

        this.StopRevertTimer();
        if (to is ButtonState.Success or ButtonState.Error && this.StateRevertDelay > TimeSpan.Zero)
            this.StartRevertTimer(this.StateRevertDelay);

        this.StateChanged?.Invoke(this, new ButtonStateChangedEventArgs(from, to));
    }

    void OnIsBusyChanged(bool isBusy)
    {
        if (this.syncingBusy)
            return;

        if (isBusy)
        {
            this.State = ButtonState.Busy;
        }
        else if (this.State is ButtonState.Busy)
        {
            // Only unwinds the busy state. A view model clearing its flag at the end of an operation
            // is exactly when Success or Error is being shown, so it must not cut that short.
            this.State = ButtonState.Normal;
        }
    }

    void SyncIsBusy(ButtonState state)
    {
        this.syncingBusy = true;
        try
        {
            this.IsBusy = state is ButtonState.Busy;
        }
        finally
        {
            this.syncingBusy = false;
        }
    }

    void StartRevertTimer(TimeSpan delay)
    {
        var dispatcher = this.Dispatcher ?? Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        this.revertTimer = dispatcher.CreateTimer();
        this.revertTimer.Interval = delay;
        this.revertTimer.IsRepeating = false;
        this.revertTimer.Tick += this.OnRevertTick;
        this.revertTimer.Start();
    }

    void OnRevertTick(object? sender, EventArgs e)
    {
        this.StopRevertTimer();
        if (this.State is ButtonState.Success or ButtonState.Error)
            this.State = ButtonState.Normal;
    }

    void StopRevertTimer()
    {
        if (this.revertTimer is null)
            return;

        this.revertTimer.Tick -= this.OnRevertTick;
        this.revertTimer.Stop();
        this.revertTimer = null;
    }


    // ---------------------------------------------------------------------------------------------
    // Command
    // ---------------------------------------------------------------------------------------------

    void OnCommandChanged(ICommand? oldCommand, ICommand? newCommand)
    {
        if (oldCommand is not null)
            oldCommand.CanExecuteChanged -= this.OnCanExecuteChanged;

        this.subscribedToCommand = false;
        this.SubscribeToCommand();
        this.RefreshCanExecute();
    }

    // Guarded because the subscription is torn down on Unloaded and put back on Loaded - a button on
    // a page that is navigated away from and back to must not end up subscribed twice.
    void SubscribeToCommand()
    {
        if (this.subscribedToCommand || this.Command is null)
            return;

        this.Command.CanExecuteChanged += this.OnCanExecuteChanged;
        this.subscribedToCommand = true;
    }

    void UnsubscribeFromCommand()
    {
        if (!this.subscribedToCommand || this.Command is null)
            return;

        this.Command.CanExecuteChanged -= this.OnCanExecuteChanged;
        this.subscribedToCommand = false;
    }

    void OnCanExecuteChanged(object? sender, EventArgs e) => this.RefreshCanExecute();

    void RefreshCanExecute()
    {
        var canExecute = this.Command?.CanExecute(this.CommandParameter) ?? true;
        if (canExecute == this.commandCanExecute)
            return;

        this.commandCanExecute = canExecute;
        this.RefreshIsEnabledProperty();
    }

    void OnLoaded(object? sender, EventArgs e)
    {
        this.isLoaded = true;
        this.SubscribeToCommand();
        this.RefreshCanExecute();
        this.ReconcileCommandFlag();

        // Playback is deferred until now - see CanAnimate - so a button that was already busy before
        // it reached the screen has to be told to start.
        this.ApplyState();

        // Unloaded cancelled any pending revert. Without re-arming it here, a Success or Error entered
        // moments before the page was navigated away from is held forever - and because State is
        // TwoWay by default, so is the bound view model.
        if (this.revertTimer is null
            && this.State is ButtonState.Success or ButtonState.Error
            && this.StateRevertDelay > TimeSpan.Zero)
        {
            this.StartRevertTimer(this.StateRevertDelay);
        }
    }

    void OnUnloaded(object? sender, EventArgs e)
    {
        this.isLoaded = false;

        // A view model's command outlives the page it was shown on, so leaving these subscriptions in
        // place roots the button (and its page) for as long as the view model lives.
        this.UnsubscribeFromCommand();
        this.DetachCommandFlag();
        this.StopRevertTimer();

        // Stop the ticker rather than leaving it running behind a page nobody is looking at.
        this.SetMotionPlaying(this.busyIndicator, false);
    }


    // ---------------------------------------------------------------------------------------------
    // Interaction
    // ---------------------------------------------------------------------------------------------

    void OnTapped(object? sender, TappedEventArgs e) => this.Invoke();

    /// <summary>
    /// Everything a tap does. Separated from the gesture handler so it can be driven without one —
    /// same shape as <see cref="IconTextTool.Invoke"/>.
    /// </summary>
    internal void Invoke()
    {
        if (!this.IsEnabled)
            return;

        if (this.UseFeedback)
            FeedbackHelper.Execute(this, nameof(this.Clicked));

        if (this.MotionIconPlayOnClick)
            this.PlaySlotMotionIcons();

        this.FlashPress();
        this.Clicked?.Invoke(this, EventArgs.Empty);
        this.RunCommand();
    }

    void RunCommand()
    {
        var command = this.Command;
        if (command is null || !command.CanExecute(this.CommandParameter))
            return;

        if (!this.AutoBusy || !AsyncCommandProbe.IsAsync(command))
        {
            command.Execute(this.CommandParameter);
            return;
        }

        this.State = ButtonState.Busy;
        command.Execute(this.CommandParameter);

        var task = AsyncCommandProbe.GetExecutionTask(command);
        if (task is not null)
        {
            this.TrackCommand(task);
        }
        else if (AsyncCommandProbe.IsRunning(command) == true && command is INotifyPropertyChanged notifier)
        {
            // A command that reports only a flag still tells us when it flips, so there is no need
            // to poll it.
            this.TrackCommandFlag(notifier);
        }
        else
        {
            // It exposed an async shape but finished inside Execute. Nothing to wait for.
            this.EndAutoBusy(faulted: false);
        }
    }

    async void TrackCommand(Task task)
    {
        var faulted = false;
        try
        {
            await task;
        }
        catch
        {
            // The command owns its own error handling; the button only reflects that it failed.
            faulted = true;
        }

        this.EndAutoBusy(faulted);
    }

    /// <summary>
    /// Watches a flag-only async command for its <c>IsRunning</c> going false.
    /// </summary>
    /// <remarks>
    /// The handler is held in a field rather than being a local closure so <c>Unloaded</c> can detach
    /// it. A command lives on the view model, which outlives the page, so an attached handler roots
    /// the button and everything above it — and if the command never raises again (a cancellation, an
    /// abandoned operation) a closure-only subscription would never come off at all.
    /// </remarks>
    void TrackCommandFlag(INotifyPropertyChanged notifier)
    {
        this.DetachCommandFlag();

        this.flagNotifier = notifier;
        this.flagHandler = (sender, _) =>
        {
            if (AsyncCommandProbe.IsRunning(sender as ICommand) != false)
                return;

            this.DetachCommandFlag();
            this.EndAutoBusy(faulted: false);
        };

        notifier.PropertyChanged += this.flagHandler;
    }

    void DetachCommandFlag()
    {
        if (this.flagNotifier is not null && this.flagHandler is not null)
            this.flagNotifier.PropertyChanged -= this.flagHandler;

        this.flagNotifier = null;
        this.flagHandler = null;
    }

    /// <summary>
    /// Re-syncs with a flag-only async command after the button has been off screen, where it was not
    /// listening. Without this a button that went busy, then had its page navigated away from, comes
    /// back spinning forever with no way to retry.
    /// </summary>
    void ReconcileCommandFlag()
    {
        if (this.State is not ButtonState.Busy || this.Command is null)
            return;

        switch (AsyncCommandProbe.IsRunning(this.Command))
        {
            case false:
                // It finished while we were away.
                this.EndAutoBusy(faulted: false);
                break;

            case true when this.Command is INotifyPropertyChanged notifier:
                this.TrackCommandFlag(notifier);
                break;
        }
    }

    void EndAutoBusy(bool faulted)
    {
        // Only unwind if the button is still the one holding the busy state. A command that set
        // State itself - to Success, typically - has already had the last word.
        if (this.State is not ButtonState.Busy)
            return;

        this.State = faulted && this.ShowErrorOnFault ? ButtonState.Error : ButtonState.Normal;
    }

    // Fire-and-forget press feedback. Deliberately runs before the command so the animation is not
    // delayed by it, and touches Opacity only: swapping Shadow or ZIndex here would cancel the very
    // gesture the user is in the middle of (Shadow unfocuses content on Android, ZIndex reparents the
    // native view and fires ACTION_CANCEL).
    async void FlashPress()
    {
        if (this.PressedOpacity >= 1)
            return;

        try
        {
            await this.FadeToAsync(this.PressedOpacity, 60);
            await this.FadeToAsync(1, 90);
        }
        catch
        {
            this.Opacity = 1;
        }
    }
}


/// <summary>Carries a <see cref="ShinyButton.State"/> transition.</summary>
/// <param name="From">The state being left.</param>
/// <param name="To">The state being entered.</param>
public sealed record ButtonStateChangedEventArgs(ButtonState From, ButtonState To);
