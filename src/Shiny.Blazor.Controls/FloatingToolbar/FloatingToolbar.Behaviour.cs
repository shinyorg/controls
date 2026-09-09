using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

public partial class FloatingToolbar
{
    // ---------------------------------------------------------------------------------------------
    // Triggers, called from floating-toolbar.js
    // ---------------------------------------------------------------------------------------------

    /// <inheritdoc cref="ShowAsync" />
    [JSInvokable]
    public Task OnTriggerShow(int index) => this.ScheduleShowAsync(index);

    /// <inheritdoc cref="HideAsync" />
    [JSInvokable]
    public Task OnTriggerHide(int index) => this.ScheduleHideAsync();

    /// <summary>
    /// A tap on the target. Moving between targets re-anchors instead of closing: the second row's
    /// tap would otherwise read as "close", and the bar would flicker off the very row asked for.
    /// </summary>
    [JSInvokable]
    public Task OnTriggerToggle(int index)
    {
        if (this.isOpen && index != this.currentIndex)
            return this.SetOpenAsync(true, index);

        return this.isOpen ? this.HideAsync() : this.ScheduleShowAsync(index);
    }


    /// <summary>The page scrolled or resized under an open bar.</summary>
    [JSInvokable]
    public async Task OnViewportChangedJs()
    {
        if (this.isOpen)
            await this.PlaceAsync();
    }


    async Task ScheduleShowAsync(int index)
    {
        this.CancelHide();

        if (this.ShowDelay <= 0)
        {
            await this.SetOpenAsync(true, index);
            return;
        }

        this.showCts?.Cancel();
        this.showCts = new CancellationTokenSource();
        var token = this.showCts.Token;

        try
        {
            await Task.Delay(this.ShowDelay, token);
            await this.SetOpenAsync(true, index);
        }
        catch (TaskCanceledException)
        {
            // The pointer left before the delay was up.
        }
    }


    /// <summary>
    /// Closes after <see cref="HideDelay"/> rather than at once, so the pointer can cross the gap
    /// between the target and the bar without the bar disappearing on the way.
    /// </summary>
    async Task ScheduleHideAsync()
    {
        this.showCts?.Cancel();

        if (this.HideDelay <= 0)
        {
            await this.HideAsync();
            return;
        }

        this.hideCts?.Cancel();
        this.hideCts = new CancellationTokenSource();
        var token = this.hideCts.Token;

        try
        {
            await Task.Delay(this.HideDelay, token);
            await this.HideAsync();
        }
        catch (TaskCanceledException)
        {
            // The pointer arrived on the bar in time.
        }
    }


    void CancelHide()
    {
        this.hideCts?.Cancel();
        this.hideCts = null;
    }


    /// <summary>Reaching the bar cancels the pending close — the other half of the grace period.</summary>
    void OnBarPointerEnter() => this.CancelHide();

    Task OnBarPointerLeave() => this.ScheduleHideAsync();


    // ---------------------------------------------------------------------------------------------
    // Open / close
    // ---------------------------------------------------------------------------------------------

    async Task SetOpenAsync(bool open, int index)
    {
        if (open == this.isOpen && index == this.currentIndex)
            return;

        this.currentIndex = index;

        if (open)
        {
            this.isOpen = true;
            this.isRendered = true;
            // Placement needs the bar measured, which needs it rendered first.
            this.pendingPlace = true;
            this.StateHasChanged();
        }
        else
        {
            this.isOpen = false;
            this.CloseMenus(0);
            this.dismissCts?.Cancel();

            // Back to the start state and hold for the transition: removing the element outright
            // would make every exit animation a no-op, because there is nothing left to animate.
            if (this.isEntered && this.AnimationLength > 0 && this.Animation != TooltipAnimation.None)
            {
                this.isEntered = false;
                this.StateHasChanged();
                await Task.Delay(this.AnimationLength);
            }

            this.isEntered = false;

            if (this.module is not null)
            {
                try
                {
                    await this.module.InvokeVoidAsync("close", this.barEl);
                    await this.module.InvokeVoidAsync("unobserve", this.instanceId);
                }
                catch (JSDisconnectedException) { }
            }

            this.isRendered = false;
            this.StateHasChanged();
            await this.Closed.InvokeAsync();
        }

        if (this.IsOpen != this.isOpen)
        {
            this.IsOpen = this.isOpen;
            await this.IsOpenChanged.InvokeAsync(this.isOpen);
        }
    }


    async Task PlaceAsync()
    {
        if (this.module is null)
            return;

        try
        {
            var result = await this.module.InvokeAsync<PlacementResult?>(
                "placeAt",
                this.barEl,
                this.instanceId,
                this.currentIndex,
                PlacementName(this.Placement),
                this.Offset,
                this.ScreenMargin
            );

            // The placer flips when the asked-for side has no room, so the side the animation grows
            // out of has to come back from it rather than being assumed.
            if (result?.Placement is { Length: > 0 } side && side != this.resolvedSide)
            {
                this.resolvedSide = side;
                this.StateHasChanged();
            }
        }
        catch (JSDisconnectedException) { }
    }


    /// <summary>A named type: an anonymous one does not survive trimming over JS interop.</summary>
    sealed record PlacementResult(string? Placement, double Left, double Top);


    static string PlacementName(TooltipPlacement placement) => placement switch
    {
        TooltipPlacement.Top => "top",
        TooltipPlacement.Bottom => "bottom",
        TooltipPlacement.Left => "left",
        TooltipPlacement.Right => "right",
        TooltipPlacement.Center => "center",
        _ => "auto"
    };


    static string TriggerName(TooltipTrigger trigger) => trigger switch
    {
        TooltipTrigger.Hover => "hover",
        TooltipTrigger.Click => "click",
        TooltipTrigger.Focus => "focus",
        TooltipTrigger.HoverOrFocus => "hoverorfocus",
        TooltipTrigger.LongPress => "longpress",
        _ => "manual"
    };


    // ---------------------------------------------------------------------------------------------
    // Items and menus
    // ---------------------------------------------------------------------------------------------

    async Task OnCellClicked(ToolbarItem item, int depth)
    {
        if (item.IsDisabled)
            return;

        if (item.HasChildren)
        {
            // Opening from the bar replaces whatever menu is up; opening from inside a menu stacks
            // on it, which is what makes a submenu a submenu.
            var alreadyOpen = this.menus.Any(m => ReferenceEquals(m.Owner, item));
            this.CloseMenus(depth);

            if (!alreadyOpen)
            {
                this.menus.Add(new MenuState { Items = item.Children!, Depth = depth, Owner = item });
                this.pendingPlace = true;
            }

            this.StateHasChanged();
            return;
        }

        this.CloseMenus(0);

        var args = new FloatingToolbarItemEventArgs(item, this.currentIndex);
        await this.ItemClicked.InvokeAsync(args);

        if (this.DismissOnItemClick)
            await this.HideAsync();
        else
            this.StateHasChanged();
    }


    void CloseMenus(int depth)
    {
        if (this.menus.Count > depth)
            this.menus.RemoveRange(depth, this.menus.Count - depth);
    }


    // ---------------------------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------------------------

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            this.module = await this.JS.InvokeAsync<IJSObjectReference>(
                "import",
                "./_content/Shiny.Blazor.Controls/floating-toolbar.js"
            );
            this.selfRef = DotNetObjectReference.Create(this);
        }

        if (this.module is null)
            return;

        await this.SyncBindingAsync();

        if (!this.pendingPlace)
            return;

        // Cleared before the awaits: a re-render caused by the measure below would otherwise come
        // straight back in here and place again, forever.
        this.pendingPlace = false;

        try
        {
            await this.module.InvokeVoidAsync("open", this.barEl);
            await this.MeasureFitAsync();
            await this.PlaceAsync();
            await this.PlaceMenusAsync();
            await this.module.InvokeVoidAsync("observe", this.instanceId, this.selfRef);

            // The enter class only transitions if the browser has painted the start state first,
            // which is why this is a second render rather than part of the one above.
            if (!this.isEntered)
            {
                this.isEntered = true;
                this.StateHasChanged();
            }

            this.StartAutoDismiss();
            await this.Opened.InvokeAsync();
        }
        catch (JSDisconnectedException) { }
    }


    /// <summary>Re-binds only when the selector or trigger actually changed.</summary>
    async Task SyncBindingAsync()
    {
        var selector = this.Target;
        if (selector is null && this.ChildContent is not null)
            selector = null; // the wrapped anchor is bound by element below

        if (selector == this.boundSelector && this.Trigger == this.boundTrigger)
            return;

        this.boundSelector = selector;
        this.boundTrigger = this.Trigger;

        try
        {
            if (String.IsNullOrWhiteSpace(selector))
            {
                await this.module!.InvokeVoidAsync("unbind", this.instanceId);
                this.boundCount = 0;
                return;
            }

            this.boundCount = await this.module!.InvokeAsync<int>(
                "bind",
                this.instanceId,
                selector,
                TriggerName(this.Trigger),
                this.selfRef,
                this.LongPressDelay
            );
        }
        catch (JSDisconnectedException) { }
    }


    /// <summary>Measures how many cells actually fit, then re-renders if that changed the split.</summary>
    async Task MeasureFitAsync()
    {
        if (!this.OverflowEnabled || this.MaxVisibleItems > 0)
            return;

        var fits = await this.module!.InvokeAsync<int>(
            "fitCount",
            this.barEl,
            this.Orientation == ToolbarOrientation.Vertical ? "vertical" : "horizontal",
            this.ScreenMargin
        );

        if (fits > 0 && fits != this.fitCount)
        {
            this.fitCount = fits;
            this.StateHasChanged();
        }
    }


    async Task PlaceMenusAsync()
    {
        for (var i = 0; i < this.menus.Count; i++)
        {
            var menu = this.menus[i];
            if (menu.Placed)
                continue;

            try
            {
                await this.module!.InvokeVoidAsync("open", menu.Element);
                // A submenu flies out to the side; a first-level dropdown falls below its button.
                await this.module.InvokeVoidAsync(
                    "place",
                    menu.Element,
                    this.barEl,
                    menu.Depth > 0 || this.Orientation == ToolbarOrientation.Vertical ? "right" : "bottom",
                    4,
                    this.ScreenMargin,
                    0
                );
                menu.Placed = true;
            }
            catch (JSDisconnectedException) { }
        }
    }


    void StartAutoDismiss()
    {
        if (this.AutoDismissDelay <= 0)
            return;

        this.dismissCts?.Cancel();
        this.dismissCts = new CancellationTokenSource();
        var token = this.dismissCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(this.AutoDismissDelay, token);
                await this.InvokeAsync(this.HideAsync);
            }
            catch (TaskCanceledException) { }
        });
    }


    public async ValueTask DisposeAsync()
    {
        this.showCts?.Cancel();
        this.hideCts?.Cancel();
        this.dismissCts?.Cancel();

        if (this.module is not null)
        {
            try
            {
                await this.module.InvokeVoidAsync("unbind", this.instanceId);
                await this.module.InvokeVoidAsync("unobserve", this.instanceId);
                await this.module.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
        }

        this.selfRef?.Dispose();
    }
}
