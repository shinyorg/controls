using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

public partial class FloatingToolbar
{
    /// <summary>
    /// The size the bar was last placed at. Placing writes an explicit rect, so the bar's size is
    /// whatever we last told it — and the only way to tell a real re-measure from the echo of our
    /// own write is to compare against this.
    /// </summary>
    Size lastPlacedSize;


    /// <summary>
    /// Hands the bar back its natural size so the next measurement is of the new content.
    /// </summary>
    /// <remarks>
    /// The strip outlives a single open, so it carries the size it had last time — which is why the
    /// expectation is cleared too. Without that, showing a bar that has changed orientation since it
    /// was last up places it at the old shape and the Border clips everything that no longer fits.
    /// </remarks>
    void Remeasure()
    {
        if (this.strip is null)
            return;

        this.lastPlacedSize = Size.Zero;
        AbsoluteLayout.SetLayoutBounds(
            this.strip,
            new Rect(0, 0, AbsoluteLayout.AutoSize, AbsoluteLayout.AutoSize)
        );
    }


    void OnUnloaded()
    {
        // The bar lives in the page's overlay layer, not under this element, so navigating away
        // would otherwise leave it on screen over the next page.
        this.StopTimers();
        this.CloseMenus();

        if (this.strip is not null)
            this.layer?.Children.Remove(this.strip);

        this.RemoveCatcher();
        this.isShown = false;
        this.ownTriggers?.Unbind();
    }


    void OnIsOpenChanged(bool open)
    {
        if (open == this.isShown)
            return;

        if (open)
            _ = this.ShowCoreAsync();
        else
            _ = this.HideCoreAsync();
    }


    async Task ShowCoreAsync()
    {
        // Resolved from the anchor first, then from the toolbar: the anchor is always on a page, but
        // the toolbar element may be declared in a resource dictionary or attached rather than placed.
        var root = PageOverlay.GetOrCreateRoot((Element?)this.currentTarget ?? this.Anchor ?? this)
                   ?? PageOverlay.GetOrCreateRoot(this);

        if (root is null)
        {
            // Not on a page yet. Loaded replays this.
            return;
        }

        this.layer = PageOverlay.GetOrCreateLayer<PageOverlay.FloatingToolbarLayer>(root, PageOverlay.Layers.FloatingToolbar);
        this.menuLayer = PageOverlay.GetOrCreateLayer<PageOverlay.FloatingToolbarMenuLayer>(root, PageOverlay.Layers.FloatingToolbarMenu);
        this.isShown = true;

        this.EnsureStrip();
        var view = this.strip!;

        this.InstallCatcher();

        if (!this.layer!.Children.Contains(view))
            this.layer.Children.Add(view);

        view.Opacity = 0;
        AbsoluteLayout.SetLayoutFlags(view, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.None);
        AbsoluteLayout.SetLayoutBounds(view, new Rect(0, 0, AbsoluteLayout.AutoSize, AbsoluteLayout.AutoSize));

        this.RebuildStrip();

        // A bar with no measured size places into the corner, so it has to have a handler and a
        // layout pass before it can be positioned.
        await WaitForSizeAsync(view);

        if (!this.IsOpen)
        {
            await this.HideCoreAsync();
            return;
        }

        var placement = this.Reposition();
        await AnchoredPopoverAnimator.InAsync(view, placement, this.Animation, this.AnimationLength);

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
        this.CloseMenus();

        if (this.strip is not null)
        {
            await AnchoredPopoverAnimator.OutAsync(this.strip, this.Animation, this.AnimationLength);
            this.layer?.Children.Remove(this.strip);
        }

        this.RemoveCatcher();

        this.Closed?.Invoke(this, EventArgs.Empty);
        if (this.ClosedCommand?.CanExecute(null) == true)
            this.ClosedCommand.Execute(null);
    }


    void EnsureStrip()
    {
        if (this.strip is not null)
            return;

        this.strip = new FloatingToolbarStrip();
        this.strip.ItemInvoked += this.OnItemInvoked;
        this.strip.MenuRequested += this.OnMenuRequested;

        // Placing writes an explicit rect, which then dictates the size - so the bar can never
        // re-measure to fit new content, and the measurement is circular. Rebuilds break the cycle
        // by putting the bounds back to AutoSize and waiting for the natural size to arrive here.
        // Without this, turning Vertical on kept the one-row height and clipped every item but the
        // first, and turning it back off kept the narrow column and clipped the row.
        this.strip.SizeChanged += (_, _) =>
        {
            if (!this.isShown)
                return;

            // Only when the size is not the one we ourselves just wrote. Placing sets an explicit
            // rect, which raises SizeChanged in turn, so without this the two would chase each other.
            if (this.strip is not null && new Size(this.strip.Width, this.strip.Height) != this.lastPlacedSize)
                this.Reposition();
        };

        // Crossing from the target onto the bar has to cancel the pending close, or a hover bar can
        // be seen but never reached.
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) =>
        {
            this.hideTimer?.Stop();
            this.hideTimer = null;
        };
        pointer.PointerExited += (_, _) => this.RequestHide();
        this.strip.GestureRecognizers.Add(pointer);
    }


    /// <summary>Places the bar against whatever it is currently anchored to, and says which side it took.</summary>
    TooltipPlacement Reposition()
    {
        var view = this.strip;
        var root = this.layer?.Parent as Layout;
        if (view is null || root is null || root.Width <= 0 || root.Height <= 0)
            return TooltipPlacement.Center;

        var container = new Size(root.Width, root.Height);
        var anchor = this.currentTarget ?? this.Anchor;

        // A null rect — offscreen, hidden, or not laid out — falls through to a centred bar, which is
        // the only honest place to put something pointing at nothing.
        var targetRect = anchor is null ? null : ViewGeometry.BoundsIn(anchor, root);

        var layout = TooltipPlacementSolver.Solve(
            targetRect ?? new Rect(container.Width / 2, container.Height / 2, 0, 0),
            new Size(view.Width, view.Height),
            container,
            targetRect is null ? TooltipPlacement.Center : this.Placement,
            this.Offset,
            this.ScreenMargin,
            0
        );

        AbsoluteLayout.SetLayoutBounds(view, layout.Bubble);
        this.lastPlacedSize = layout.Bubble.Size;

        return layout.Placement;
    }


    static async Task WaitForSizeAsync(View view)
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
            // The timeout is the point: a view that never gets a handler must not leave the open
            // sequence parked forever.
            await Task.WhenAny(tcs.Task, Task.Delay(400));
        }
        finally
        {
            view.SizeChanged -= OnSized;
        }
    }


    // ---------------------------------------------------------------------------------------------
    // Menus
    // ---------------------------------------------------------------------------------------------

    void OnMenuRequested(object? sender, ToolbarItemEventArgs e)
    {
        // A menu opened from the bar replaces any menu already up; one opened from inside a menu
        // stacks on top of it, which is what makes a submenu a submenu.
        var depth = sender is FloatingToolbarStrip s && this.menus.Contains(s)
            ? this.menus.IndexOf(s) + 1
            : 0;

        this.CloseMenus(depth);
        this.OpenMenu(e.Item, e.ItemView, depth);
    }


    void OpenMenu(ShinyToolbarItem item, View anchorView, int depth)
    {
        var root = this.menuLayer?.Parent as Layout;
        if (this.menuLayer is null || root is null)
            return;

        var menu = new FloatingToolbarStrip
        {
            Orientation = ToolbarOrientation.Vertical,
            // Always labelled: a column of bare glyphs is a puzzle, not a menu.
            ShowLabels = true,
            ItemSize = this.ItemSize,
            ForegroundColor = this.ForegroundColor,
            Opacity = 0
        };
        menu.StrokeShape = this.BuildCorner();
        menu.ItemInvoked += this.OnItemInvoked;
        menu.MenuRequested += this.OnMenuRequested;
        menu.Build(item.Children.ToList(), 0, null);

        this.menus.Add(menu);
        this.menuLayer.Children.Add(menu);

        AbsoluteLayout.SetLayoutFlags(menu, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.None);
        AbsoluteLayout.SetLayoutBounds(menu, new Rect(0, 0, AbsoluteLayout.AutoSize, AbsoluteLayout.AutoSize));

        _ = this.PlaceMenuAsync(menu, anchorView, root, depth);
    }


    async Task PlaceMenuAsync(FloatingToolbarStrip menu, View anchorView, Layout root, int depth)
    {
        await WaitForSizeAsync(menu);

        if (!this.menus.Contains(menu))
            return;

        var targetRect = ViewGeometry.BoundsIn(anchorView, root);
        var container = new Size(root.Width, root.Height);

        // A submenu flies out to the side; a first-level dropdown falls below its button, the way a
        // menu bar's does.
        var preferred = depth > 0 || this.Orientation == ToolbarOrientation.Vertical
            ? TooltipPlacement.Right
            : TooltipPlacement.Bottom;

        var layout = TooltipPlacementSolver.Solve(
            targetRect ?? new Rect(container.Width / 2, container.Height / 2, 0, 0),
            new Size(menu.Width, menu.Height),
            container,
            targetRect is null ? TooltipPlacement.Center : preferred,
            4,
            this.ScreenMargin,
            0
        );

        AbsoluteLayout.SetLayoutBounds(menu, layout.Bubble);
        menu.Opacity = 1;
    }


    /// <summary>Closes every menu at or below <paramref name="depth"/>.</summary>
    void CloseMenus(int depth = 0)
    {
        for (var i = this.menus.Count - 1; i >= depth; i--)
        {
            var menu = this.menus[i];
            menu.ItemInvoked -= this.OnItemInvoked;
            menu.MenuRequested -= this.OnMenuRequested;
            this.menuLayer?.Children.Remove(menu);
            this.menus.RemoveAt(i);
        }
    }


    // ---------------------------------------------------------------------------------------------
    // Invocation and dismissal
    // ---------------------------------------------------------------------------------------------

    void OnItemInvoked(object? sender, ToolbarItemEventArgs e)
    {
        var target = this.currentTarget ?? this.Anchor;
        var args = new FloatingToolbarItemEventArgs(e.Item, target, target?.BindingContext);

        this.CloseMenus();

        if (e.Item.Command?.CanExecute(e.Item.CommandParameter) == true)
            e.Item.Command.Execute(e.Item.CommandParameter);

        this.ItemClicked?.Invoke(this, args);

        if (this.ItemClickedCommand?.CanExecute(args) == true)
            this.ItemClickedCommand.Execute(args);

        if (this.DismissOnItemClick)
            this.Hide();
    }


    void InstallCatcher()
    {
        if (!this.DismissOnTapOutside || this.catcher is not null || this.layer is null)
            return;

        this.catcher = Tooltip.BuildCatcher((_, _) => this.Hide());
        this.layer.Children.Insert(0, this.catcher);
        AbsoluteLayout.SetLayoutFlags(this.catcher, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.All);
        AbsoluteLayout.SetLayoutBounds(this.catcher, new Rect(0, 0, 1, 1));
    }


    void RemoveCatcher()
    {
        if (this.catcher is null)
            return;

        this.layer?.Children.Remove(this.catcher);
        this.catcher = null;
    }


    void StartAutoDismiss()
    {
        if (this.AutoDismissDelay <= 0)
            return;

        var owner = this.SafeDispatcher();
        if (owner is null)
            return;

        this.dismissTimer = owner.CreateTimer();
        this.dismissTimer.Interval = TimeSpan.FromMilliseconds(this.AutoDismissDelay);
        this.dismissTimer.IsRepeating = false;
        this.dismissTimer.Tick += (_, _) => this.Hide();
        this.dismissTimer.Start();
    }


    void StopTimers()
    {
        this.showTimer?.Stop();
        this.hideTimer?.Stop();
        this.dismissTimer?.Stop();
        this.showTimer = null;
        this.hideTimer = null;
        this.dismissTimer = null;
    }
}
