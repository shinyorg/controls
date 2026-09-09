using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

/// <summary>
/// A themed tooltip bubble that points at a target view.
/// </summary>
/// <remarks>
/// <para>
/// Two shapes, both valid. Wrap the thing it describes and the tooltip finds its own target:
/// </para>
/// <code>
/// &lt;shiny:Tooltip Text="Saves without closing" Placement="Top" Trigger="LongPress"&gt;
///     &lt;Button Text="Apply" /&gt;
/// &lt;/shiny:Tooltip&gt;
/// </code>
/// <para>
/// Or leave it empty and point it at something else, which is what a bound, view-model-driven tooltip
/// wants — it does not have to sit anywhere near its target in the markup:
/// </para>
/// <code>
/// &lt;shiny:Tooltip Target="{x:Reference SaveButton}"
///                Text="Nothing to save yet"
///                IsOpen="{Binding ShowSaveHint}"
///                Command="{Binding DismissHint}" /&gt;
/// </code>
/// <para>
/// The bubble is not drawn where the tooltip is declared. It goes into a layer above the page's
/// content, so it is never clipped by the scroll view, card or grid cell the target happens to live
/// in — which is the failure mode that makes people give up on in-tree popovers.
/// </para>
/// </remarks>
public partial class Tooltip : ContentView
{
    const double ScaleFrom = 0.9;
    const double SlideDistance = 12;

    TooltipBubble? bubble;
    BoxView? catcher;
    AbsoluteLayout? layer;

    ScrollView? watchedScroll;

    /// <summary>
    /// Opening is delegated: every awkward detail of anchoring to an arbitrary view lives in the
    /// binder, which <see cref="FloatingToolbar"/> shares rather than reimplementing.
    /// </summary>
    AnchorTriggerBinder? triggers;

    IDispatcherTimer? showTimer;
    IDispatcherTimer? dismissTimer;

    bool latestTarget;
    bool workerRunning;
    bool isShown;

    public Tooltip()
    {
        this.Loaded += this.OnLoaded;
        this.Unloaded += this.OnUnloaded;

        // Last line: replays any styled property that was applied before the fields existed.
        // See StyleGuard.
        StyleGuard.MarkReady(this, typeof(Tooltip));
    }


    /// <summary>Raised once the bubble is on screen.</summary>
    public event EventHandler? Opened;

    /// <summary>Raised once the bubble has left.</summary>
    public event EventHandler? Closed;

    /// <summary>Raised when the bubble itself is tapped, before <see cref="DismissOnTap"/> acts.</summary>
    public event EventHandler? Tapped;


    /// <summary>
    /// The view the bubble points at: an explicit <see cref="Target"/>, a <see cref="TargetName"/>
    /// resolved through the page's name scope, or — when the tooltip is wrapping something — its own
    /// content.
    /// </summary>
    public View? Anchor => this.Target ?? this.ResolveTargetName() ?? this.Content;


    /// <summary>Opens the tooltip. Equivalent to setting <see cref="IsOpen"/>.</summary>
    public void Show() => this.IsOpen = true;

    /// <summary>Closes the tooltip.</summary>
    public void Hide() => this.IsOpen = false;

    public void Toggle() => this.IsOpen = !this.IsOpen;


    /// <summary>
    /// Lifecycle for an attached tooltip, which has no parent and so never gets a Loaded of its own.
    /// Driven from the target view's instead — see <see cref="TooltipProperties"/>.
    /// </summary>
    internal void SendAttachedLoaded() => this.OnLoaded(this, EventArgs.Empty);

    internal void SendAttachedUnloaded() => this.OnUnloaded(this, EventArgs.Empty);


    void OnLoaded(object? sender, EventArgs e)
    {
        this.RewireTrigger();

        // A tooltip whose IsOpen bound true before the page was laid out has nothing to measure
        // against yet, so the open is replayed here.
        if (this.IsOpen && !this.isShown)
            this.OnIsOpenChanged(true);
    }


    void OnUnloaded(object? sender, EventArgs e)
    {
        // Navigating away has to take the bubble with it: it lives in the page's overlay layer, not
        // under this element, so nothing else would remove it.
        this.StopTimers();
        this.TeardownBubble();
        this.isShown = false;
        this.latestTarget = false;
    }


    View? ResolveTargetName()
    {
        if (string.IsNullOrWhiteSpace(this.TargetName))
            return null;

        // Walk out through the name scopes: a tooltip inside a template resolves against the template,
        // then the page.
        Element? current = this;
        while (current is not null)
        {
            if (current.FindByName(this.TargetName) is View found)
                return found;

            current = current.Parent;
        }
        return null;
    }


    // ---------------------------------------------------------------------------------------------
    // Triggers
    // ---------------------------------------------------------------------------------------------

    void RewireTrigger()
    {
        this.triggers ??= new AnchorTriggerBinder(() => this.SafeDispatcher())
        {
            Show = this.Show,
            Hide = this.Hide,
            Toggle = this.Toggle
        };
        this.triggers.LongPressDelay = this.LongPressDelay;
        this.triggers.Bind(this.Anchor, this.Trigger);
    }


    void Unwire() => this.triggers?.Unbind();


    /// <summary>The dispatcher, or null off a window — where asking for it throws.</summary>
    IDispatcher? SafeDispatcher()
    {
        try
        {
            return this.Dispatcher;
        }
        catch
        {
            return null;
        }
    }



    // ---------------------------------------------------------------------------------------------
    // Open / close
    // ---------------------------------------------------------------------------------------------

    void OnIsOpenChanged(bool open)
    {
        this.showTimer?.Stop();
        this.showTimer = null;

        if (open && this.ShowDelay > 0)
        {
            this.showTimer = this.Dispatcher.CreateTimer();
            this.showTimer.Interval = TimeSpan.FromMilliseconds(this.ShowDelay);
            this.showTimer.IsRepeating = false;
            this.showTimer.Tick += (_, _) =>
            {
                // The pointer may have left again while the delay ran.
                if (this.IsOpen)
                    this.Drive(true);
            };
            this.showTimer.Start();
            return;
        }

        this.Drive(open);
    }


    /// <summary>
    /// Records the state wanted and lets a single worker walk towards it. Without this a fast
    /// open-close — a pointer crossing a button, a bound flag flipping twice — drops the second change
    /// and leaves the bubble stuck in the first animation's end state.
    /// </summary>
    async void Drive(bool open)
    {
        this.latestTarget = open;
        if (this.workerRunning)
            return;

        this.workerRunning = true;
        try
        {
            while (this.isShown != this.latestTarget)
            {
                if (this.latestTarget)
                    await this.ShowCoreAsync();
                else
                    await this.HideCoreAsync();
            }
        }
        finally
        {
            this.workerRunning = false;
        }
    }


    async Task ShowCoreAsync()
    {
        // Resolved from the anchor first, then from the tooltip itself. The anchor is always on the
        // page; the tooltip element may not be — declared in a resource dictionary, or attached rather
        // than placed.
        var root = PageOverlay.GetOrCreateRoot((Element?)this.Anchor ?? this) ?? PageOverlay.GetOrCreateRoot(this);
        if (root is null)
        {
            // Not on a page yet. OnLoaded replays this.
            this.isShown = this.latestTarget;
            return;
        }

        this.layer = PageOverlay.GetOrCreateLayer<PageOverlay.TooltipLayer>(root, PageOverlay.Layers.Tooltip);
        this.isShown = true;

        this.EnsureBubble();
        var view = this.bubble!;

        this.InstallCatcher();

        if (!this.layer!.Children.Contains(view))
            this.layer.Children.Add(view);

        view.Opacity = 0;
        view.Scale = 1;
        view.TranslationX = 0;
        view.TranslationY = 0;

        AbsoluteLayout.SetLayoutFlags(view, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.None);
        AbsoluteLayout.SetLayoutBounds(view, new Rect(0, 0, AbsoluteLayout.AutoSize, AbsoluteLayout.AutoSize));

        // Let the bubble get a handler and a measured size before asking where it goes: a Label has no
        // size at all until its platform view exists, and placing against a zero-sized bubble puts it
        // in the corner.
        await this.WaitForSizeAsync(view);

        if (!this.latestTarget)
        {
            this.isShown = true; // let the worker loop run the hide
            return;
        }

        var placement = this.Reposition();
        this.WatchAnchor();
        await this.AnimateInAsync(view, placement);

        this.StartAutoDismiss();
        this.Opened?.Invoke(this, EventArgs.Empty);
        if (this.OpenedCommand?.CanExecute(null) == true)
            this.OpenedCommand.Execute(null);
    }


    async Task HideCoreAsync()
    {
        this.isShown = false;
        this.dismissTimer?.Stop();
        this.dismissTimer = null;
        this.UnwatchAnchor();

        var view = this.bubble;
        if (view is not null)
        {
            await this.AnimateOutAsync(view);
            this.layer?.Children.Remove(view);
        }

        this.RemoveCatcher();

        this.Closed?.Invoke(this, EventArgs.Empty);
        if (this.ClosedCommand?.CanExecute(null) == true)
            this.ClosedCommand.Execute(null);
    }


    /// <summary>Places the bubble against its anchor, and returns the side it ended up on.</summary>
    TooltipPlacement Reposition()
    {
        var view = this.bubble;
        var root = this.layer?.Parent as Layout;
        if (view is null || root is null || root.Width <= 0 || root.Height <= 0)
            return TooltipPlacement.Center;

        var container = new Size(root.Width, root.Height);
        var anchor = this.Anchor;

        // A null rect — offscreen, hidden, or not laid out — falls through to a centred bubble with no
        // tail, which is the only honest thing to point at nothing with.
        var targetRect = anchor is null ? null : ViewGeometry.BoundsIn(anchor, root);

        var layout = view.Place(
            targetRect,
            container,
            targetRect is null ? TooltipPlacement.Center : this.Placement,
            this.Offset,
            this.ScreenMargin,
            this.ShowTail
        );

        AbsoluteLayout.SetLayoutBounds(view, layout.Bubble);
        return layout.Placement;
    }


    /// <summary>Waits until the bubble has a real size, so it can be placed rather than guessed at.</summary>
    async Task WaitForSizeAsync(TooltipBubble view)
    {
        if (view.Width > 0 && view.Height > 0)
            return;

        var tcs = new TaskCompletionSource();

        void OnSized(object? sender, EventArgs e)
        {
            if (view.Width > 0 && view.Height > 0)
                tcs.TrySetResult();
        }

        view.SizeChanged += OnSized;
        try
        {
            // A timeout rather than an open wait: a bubble that never gets a size (a head that failed
            // to create the handler) would otherwise hang the open forever, and with it every later
            // open, because the worker loop never returns.
            await Task.WhenAny(tcs.Task, Task.Delay(500));
        }
        finally
        {
            view.SizeChanged -= OnSized;
        }
    }


    // ---------------------------------------------------------------------------------------------
    // The bubble and its catcher
    // ---------------------------------------------------------------------------------------------

    void EnsureBubble()
    {
        if (this.bubble is null)
        {
            this.bubble = new TooltipBubble();
            this.bubble.SetBinding(BindableObject.BindingContextProperty, new Binding(nameof(this.BindingContext), source: this));

            var tap = new TapGestureRecognizer();
            tap.Tapped += this.OnBubbleTapped;
            this.bubble.GestureRecognizers.Add(tap);
        }

        this.ApplyBubbleStyle();
    }


    void ApplyBubbleStyle()
    {
        var view = this.bubble;
        if (view is null)
            return;

        view.Title = this.Title;
        view.Text = this.Text;
        view.BubbleContent = this.ContentTemplate?.CreateContent() as View;
        view.ShowTail = this.ShowTail;
        view.TailSize = this.TailSize;
        view.BubbleColor = this.BubbleColor;
        view.TextColor = this.TextColor;
        view.BorderColor = this.BorderColor;
        view.BorderThickness = this.BorderThickness;
        view.CornerRadius = this.CornerRadius;
        view.BubblePadding = this.BubblePadding;
        view.MaxBubbleWidth = this.MaxBubbleWidth;
        view.HasShadow = this.HasShadow;

        if (this.isShown)
            this.Reposition();
    }


    void OnBubbleTapped(object? sender, TappedEventArgs e)
    {
        this.Tapped?.Invoke(this, EventArgs.Empty);

        if (this.Command?.CanExecute(this.CommandParameter) == true)
            this.Command.Execute(this.CommandParameter);

        if (this.DismissOnTap)
            this.Hide();
    }


    /// <summary>
    /// Puts a transparent full-page catcher under the bubble so a tap outside closes it. Skipped for
    /// hover and focus, where swallowing the page's taps would be plainly wrong — those close when the
    /// pointer or the focus moves on.
    /// </summary>
    void InstallCatcher()
    {
        var wants = this.DismissOnTapOutside && this.Trigger is not TooltipTrigger.Hover and not TooltipTrigger.Focus;
        if (!wants || this.layer is null)
            return;

        this.catcher ??= BuildCatcher(this.OnCatcherTapped);

        if (!this.layer.Children.Contains(this.catcher))
            this.layer.Children.Insert(0, this.catcher);
    }


    internal static BoxView BuildCatcher(EventHandler<TappedEventArgs> onTapped)
    {
        var box = new BoxView { Color = Colors.Transparent, BackgroundColor = Colors.Transparent };
        AbsoluteLayout.SetLayoutFlags(box, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.All);
        AbsoluteLayout.SetLayoutBounds(box, new Rect(0, 0, 1, 1));

        var tap = new TapGestureRecognizer();
        tap.Tapped += onTapped;
        box.GestureRecognizers.Add(tap);
        return box;
    }


    void OnCatcherTapped(object? sender, TappedEventArgs e) => this.Hide();


    void RemoveCatcher()
    {
        if (this.catcher is not null)
            this.layer?.Children.Remove(this.catcher);
    }


    void TeardownBubble()
    {
        if (this.bubble is not null)
            this.layer?.Children.Remove(this.bubble);

        this.RemoveCatcher();
        this.UnwatchAnchor();
        this.Unwire();
    }


    void StartAutoDismiss()
    {
        if (this.AutoDismissDelay <= 0)
            return;

        this.dismissTimer = this.Dispatcher.CreateTimer();
        this.dismissTimer.Interval = TimeSpan.FromMilliseconds(this.AutoDismissDelay);
        this.dismissTimer.IsRepeating = false;
        this.dismissTimer.Tick += (_, _) => this.Hide();
        this.dismissTimer.Start();
    }


    void StopTimers()
    {
        this.showTimer?.Stop();
        this.dismissTimer?.Stop();
        this.showTimer = null;
        this.dismissTimer = null;

        // The long-press timer belongs to the binder now, and unbinding is what stops it.
        this.triggers?.Unbind();
    }


    /// <summary>
    /// Follows the anchor while the bubble is up. A tooltip on a control in a scroll view is pointing
    /// at a moving thing, and one left behind at the old coordinates is worse than none.
    /// </summary>
    void WatchAnchor()
    {
        var anchor = this.Anchor;
        if (anchor is null)
            return;

        anchor.SizeChanged += this.OnAnchorMoved;

        this.watchedScroll = ViewGeometry.EnclosingScrollView(anchor);
        if (this.watchedScroll is not null)
            this.watchedScroll.Scrolled += this.OnAnchorScrolled;
    }


    void UnwatchAnchor()
    {
        if (this.Anchor is { } anchor)
            anchor.SizeChanged -= this.OnAnchorMoved;

        if (this.watchedScroll is not null)
        {
            this.watchedScroll.Scrolled -= this.OnAnchorScrolled;
            this.watchedScroll = null;
        }
    }


    void OnAnchorMoved(object? sender, EventArgs e)
    {
        if (this.isShown)
            this.Reposition();
    }


    void OnAnchorScrolled(object? sender, ScrolledEventArgs e)
    {
        if (this.isShown)
            this.Reposition();
    }


    // ---------------------------------------------------------------------------------------------
    // Animation
    // ---------------------------------------------------------------------------------------------

    async Task AnimateInAsync(TooltipBubble view, TooltipPlacement placement)
    {
        // The tail is what the bubble grows out of, so its offset becomes the anchor fraction the
        // shared animator scales from.
        var along = placement is TooltipPlacement.Top or TooltipPlacement.Bottom
            ? (view.Width > 0 ? view.TailOffset / view.Width : 0.5)
            : (view.Height > 0 ? view.TailOffset / view.Height : 0.5);

        await AnchoredPopoverAnimator.InAsync(view, placement, this.Animation, this.AnimationDuration, along);
    }


    Task AnimateOutAsync(TooltipBubble view)
        => AnchoredPopoverAnimator.OutAsync(view, this.Animation, this.AnimationDuration);
}
