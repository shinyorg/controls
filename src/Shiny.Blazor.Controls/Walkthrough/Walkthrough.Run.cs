using System.Globalization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

/// <summary>The run loop, and the styles the markup renders from.</summary>
public partial class Walkthrough
{
    static string Px(double value) => value.ToString("0.##", CultureInfo.InvariantCulture) + "px";

    static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);


    /// <summary>Custom properties the whole tour reads — the scrim, the card colours, the travel time.</summary>
    string RootStyle
    {
        get
        {
            var style =
                $"--shiny-wt-opacity:{Num(Math.Clamp(this.OverlayOpacity, 0, 1))};" +
                $"--shiny-wt-move:{Num(Math.Max(0, this.SpotlightMoveDuration))}ms;" +
                $"--shiny-wt-max-width:{this.MaxCalloutWidth};" +
                $"--shiny-wt-tail-offset:{Px(this.tailOffset)};";

            if (!string.IsNullOrWhiteSpace(this.OverlayColor))
                style += $"--shiny-wt-scrim:{this.OverlayColor};";

            if (!string.IsNullOrWhiteSpace(this.CalloutColor))
                style += $"--shiny-wt-card:{this.CalloutColor};";

            if (!string.IsNullOrWhiteSpace(this.CalloutTextColor))
                style += $"--shiny-wt-fg:{this.CalloutTextColor};";

            if (this.RingThickness > 0 && !string.IsNullOrWhiteSpace(this.RingColor))
            {
                style += $"--shiny-wt-ring:{this.RingColor};";
                style += $"--shiny-wt-ring-width:{Px(this.RingThickness)};";
            }

            var step = this.CurrentStepItem;
            if (step is not null)
            {
                style += $"--shiny-wt-in:{Num(Math.Max(0, step.DurationIn))}ms;";
                style += $"--shiny-wt-out:{Num(Math.Max(0, step.DurationOut))}ms;";
            }

            return style;
        }
    }


    /// <summary>
    /// The cut-out. Sized and rounded to the step's shape, with the dim carried entirely by its
    /// box-shadow — which is why moving between targets is one CSS transition rather than a redraw.
    /// </summary>
    string SpotStyle
    {
        get
        {
            var rect = this.spot;
            if (rect is null || rect.Width <= 0 || rect.Height <= 0)
            {
                // Nothing to highlight: collapse to a point in the middle of the viewport so the dim
                // still covers everything and the next step's spotlight grows out of the centre.
                var cx = this.viewport?.CenterX ?? 0;
                var cy = this.viewport?.CenterY ?? 0;
                return $"left:{Px(cx)};top:{Px(cy)};width:0;height:0;border-radius:0;";
            }

            var step = this.CurrentStepItem;
            var shape = step?.Highlight ?? this.Highlight;
            var radius = shape switch
            {
                WalkthroughHighlight.Rectangle => "0",
                WalkthroughHighlight.Circle => "50%",
                WalkthroughHighlight.Ellipse => "50%",
                _ => Px(step?.HighlightCornerRadius ?? this.HighlightCornerRadius)
            };

            var box = rect;

            // A circle covers the target rather than being inscribed in it, so a wide button is
            // enclosed instead of clipped at the ends.
            if (shape == WalkthroughHighlight.Circle)
            {
                var diameter = Math.Sqrt((rect.Width * rect.Width) + (rect.Height * rect.Height));
                box = new TooltipRect
                {
                    X = rect.CenterX - (diameter / 2),
                    Y = rect.CenterY - (diameter / 2),
                    Width = diameter,
                    Height = diameter
                };
            }

            return
                $"left:{Px(box.X)};top:{Px(box.Y)};" +
                $"width:{Px(box.Width)};height:{Px(box.Height)};" +
                $"border-radius:{radius};";
        }
    }


    string CalloutClass
    {
        get
        {
            var css = $"shiny-walkthrough__callout shiny-walkthrough__callout--{this.placement}";
            css += $" shiny-walkthrough__callout--{this.EffectiveDisplay.ToString().ToLowerInvariant()}";

            var animation = this.CurrentStepItem?.AnimationIn ?? WalkthroughAnimation.Fade;
            css += $" shiny-walkthrough__callout--in-{animation.ToString().ToLowerInvariant()}";

            if (this.calloutPlaced)
                css += " is-placed";

            return css;
        }
    }


    string CalloutStyle => $"left:{Px(this.calloutLeft)};top:{Px(this.calloutTop)};";


    /// <summary>
    /// The four transparent panels that frame the cut-out when a step wants its control live.
    /// </summary>
    /// <remarks>
    /// Hit testing has no notion of a hole: the scrim's box-shadow is not hit-testable at all, and one
    /// full-viewport catcher would block the very control the step is asking the user to try. Four
    /// panels around the gap is the only arrangement that dims everything and still lets a click land.
    /// </remarks>
    IEnumerable<string> Shields
    {
        get
        {
            var rect = this.spot;
            var view = this.viewport;
            if (rect is null || view is null)
                yield break;

            var top = Math.Clamp(rect.Top, 0, view.Height);
            var bottom = Math.Clamp(rect.Bottom, 0, view.Height);
            var left = Math.Clamp(rect.Left, 0, view.Width);
            var right = Math.Clamp(rect.Right, 0, view.Width);

            yield return $"left:0;top:0;width:100%;height:{Px(top)};";
            yield return $"left:0;top:{Px(bottom)};width:100%;height:{Px(Math.Max(0, view.Height - bottom))};";
            yield return $"left:0;top:{Px(top)};width:{Px(left)};height:{Px(Math.Max(0, bottom - top))};";
            yield return $"left:{Px(right)};top:{Px(top)};width:{Px(Math.Max(0, view.Width - right))};height:{Px(Math.Max(0, bottom - top))};";
        }
    }


    // ---------------------------------------------------------------------------------------------
    // The run
    // ---------------------------------------------------------------------------------------------

    async Task RunAsync(int fromIndex)
    {
        var visible = this.VisibleSteps;
        if (visible.Count == 0 || this.module is null)
            return;

        this.running = true;
        this.chromeVisible = true;
        this.runCancel = new CancellationTokenSource();
        var token = this.runCancel.Token;

        await this.SetRunningAsync(true);

        this.viewport = await this.module.InvokeAsync<TooltipRect>("viewport");

        if (this.LockScroll)
            await this.module.InvokeVoidAsync("lockScroll", true);

        if (this.EnableKeyboard)
            await this.module.InvokeVoidAsync("observeKeys", this.instanceId, this.selfRef);

        await this.module.InvokeVoidAsync("observe", this.instanceId, this.selfRef);

        if (this.Started.HasDelegate)
            await this.Started.InvokeAsync();

        this.StateHasChanged();

        // A frame for the scrim to exist at zero opacity before it is told to fade in — a transition
        // has nothing to run from if the element and its end state arrive in the same paint.
        await Task.Yield();
        this.scrimVisible = true;
        this.StateHasChanged();

        var reason = WalkthroughEndReason.Completed;
        var index = Math.Clamp(fromIndex, 0, visible.Count - 1);

        try
        {
            while (!token.IsCancellationRequested)
            {
                // Re-read every iteration: a step's IsVisible can be bound, and the previous step's
                // Entered callback is exactly where an app flips one.
                visible = this.VisibleSteps;
                if (visible.Count == 0)
                    break;

                index = Math.Clamp(index, 0, visible.Count - 1);
                var step = visible[index];

                await this.EnterStepAsync(step, index, token);
                if (token.IsCancellationRequested && this.pendingSignal is null)
                {
                    reason = WalkthroughEndReason.Stopped;
                    break;
                }

                var delta = await this.WaitForMoveAsync();
                this.move = null;
                await this.LeaveStepAsync(step);

                if (delta == SignalStop)
                {
                    reason = WalkthroughEndReason.Stopped;
                    break;
                }

                if (delta == SignalSkip)
                {
                    reason = WalkthroughEndReason.Skipped;
                    break;
                }

                var next = index + delta;
                if (next < 0)
                {
                    // Back on the first step stays put. Dropping a user out of a tour because they
                    // pressed Back once is never what they meant.
                    index = 0;
                    continue;
                }

                if (next >= this.VisibleSteps.Count)
                {
                    reason = WalkthroughEndReason.Completed;
                    break;
                }

                index = next;
            }
        }
        finally
        {
            await this.EndRunAsync(reason);
        }
    }


    async Task EnterStepAsync(WalkthroughStep step, int index, CancellationToken token)
    {
        this.CurrentStepItem = step;
        this.CurrentStepIndex = index;
        this.calloutPlaced = false;
        this.StateHasChanged();

        if (step.Entered.HasDelegate)
            await step.Entered.InvokeAsync();

        if (this.module is null)
            return;

        if (!string.IsNullOrWhiteSpace(step.Target) && (step.ScrollToTarget ?? this.ScrollToTarget))
        {
            await this.module.InvokeVoidAsync("scrollIntoView", step.Target);

            // scrollIntoView is smooth, so the rect is still moving when it returns. Measuring now
            // would highlight where the target was rather than where it lands.
            await Task.Delay(320, token);
        }

        if (token.IsCancellationRequested)
            return;

        this.viewport = await this.module.InvokeAsync<TooltipRect>("viewport");
        await this.MeasureSpotAsync(step);

        if (step.AdvanceOnTargetClick && !string.IsNullOrWhiteSpace(step.Target))
        {
            await this.module.InvokeVoidAsync("watchTargetClick", this.instanceId, step.Target, this.selfRef);
            this.watchingClicks = true;
        }

        this.StateHasChanged();

        // The callout has to exist and carry this step's content before it can be measured for
        // placement — its size is what decides which side of the spotlight it goes on.
        await Task.Yield();
        await this.PlaceCalloutAsync();

        this.calloutPlaced = true;
        this.StateHasChanged();

        if (this.StepChanged.HasDelegate)
            await this.StepChanged.InvokeAsync(step.Name);

        this.StartDwell(step);
    }


    async Task MeasureSpotAsync(WalkthroughStep step)
    {
        if (this.module is null)
            return;

        var shape = step.Highlight ?? this.Highlight;
        if (!this.UseOverlay || shape == WalkthroughHighlight.None || string.IsNullOrWhiteSpace(step.Target))
        {
            this.spot = null;
            return;
        }

        var rect = await this.module.InvokeAsync<TooltipRect?>("measure", step.Target);
        if (rect is null)
        {
            this.spot = null;
            return;
        }

        var pad = step.HighlightPadding ?? this.HighlightPadding;
        this.spot = new TooltipRect
        {
            X = rect.X - pad,
            Y = rect.Y - pad,
            Width = Math.Max(0, rect.Width + (pad * 2)),
            Height = Math.Max(0, rect.Height + (pad * 2))
        };
    }


    async Task PlaceCalloutAsync()
    {
        if (this.module is null)
            return;

        var step = this.CurrentStepItem;
        object? anchor = this.spot is { Width: > 0, Height: > 0 }
            ? this.spot
            : (object?)step?.Target;

        var preferred = anchor is null
            ? TooltipPlacement.Center
            : (step?.Placement ?? TooltipPlacement.Auto);

        var result = await this.module.InvokeAsync<TooltipPlacementResult?>(
            "place",
            this.calloutRef,
            anchor,
            preferred.ToString().ToLowerInvariant(),
            this.CalloutOffset,
            this.ScreenMargin,
            20d
        );

        if (result is null)
            return;

        this.placement = result.Placement ?? "bottom";
        this.tailOffset = result.TailOffset;
        this.calloutLeft = result.Left;
        this.calloutTop = result.Top;
    }


    async Task LeaveStepAsync(WalkthroughStep step)
    {
        this.CancelDwell();

        if (this.watchingClicks && this.module is not null)
        {
            await this.module.InvokeVoidAsync("unwatchTargetClick", this.instanceId);
            this.watchingClicks = false;
        }

        if (step.Left.HasDelegate)
            await step.Left.InvokeAsync();

        // Let the exit transition play before the next step's content replaces the callout's.
        this.calloutPlaced = false;
        this.StateHasChanged();
        await Task.Delay(Math.Max(0, step.DurationOut));
    }


    Task<int> WaitForMoveAsync()
    {
        // A signal that arrived while the step was arriving is taken here rather than dropped.
        if (this.pendingSignal is { } pending)
        {
            this.pendingSignal = null;
            return Task.FromResult(pending);
        }

        this.move = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        return this.move.Task;
    }


    void StartDwell(WalkthroughStep step)
    {
        if (step.Duration <= 0)
            return;

        this.dwell = new CancellationTokenSource();
        var token = this.dwell.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(step.Duration, token);
                await this.InvokeAsync(this.NextAsync);
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }


    void CancelDwell()
    {
        this.dwell?.Cancel();
        this.dwell?.Dispose();
        this.dwell = null;
    }


    async Task EndRunAsync(WalkthroughEndReason reason)
    {
        this.running = false;
        this.CancelDwell();
        this.move = null;
        this.pendingSignal = null;

        if (this.module is not null)
        {
            try
            {
                if (this.watchingClicks)
                {
                    await this.module.InvokeVoidAsync("unwatchTargetClick", this.instanceId);
                    this.watchingClicks = false;
                }

                await this.module.InvokeVoidAsync("unobserveKeys", this.instanceId);
                await this.module.InvokeVoidAsync("unobserve", this.instanceId);

                if (this.LockScroll)
                    await this.module.InvokeVoidAsync("lockScroll", false);
            }
            catch (JSDisconnectedException) { }
            catch (ObjectDisposedException) { }
        }

        // The spotlight shrinking away as the dim lifts is what makes the end read as "you are back in
        // the app" rather than a layer blinking out.
        this.spot = null;
        this.scrimVisible = false;
        this.CurrentStepItem = null;
        this.StateHasChanged();
        await Task.Delay(240);

        this.CurrentStepIndex = -1;
        this.chromeVisible = false;
        this.StateHasChanged();

        var remembered = reason == WalkthroughEndReason.Completed
            || (reason == WalkthroughEndReason.Skipped && this.RememberOnSkip);

        if (remembered && this.Store is not null && !string.IsNullOrWhiteSpace(this.RememberRunKey))
            await this.Store.SetHasRunAsync(this.RememberRunKey!, true);

        await this.SetRunningAsync(false);

        if (this.Ended.HasDelegate)
            await this.Ended.InvokeAsync(reason);

        switch (reason)
        {
            case WalkthroughEndReason.Completed when this.Completed.HasDelegate:
                await this.Completed.InvokeAsync();
                break;

            case WalkthroughEndReason.Skipped when this.Skipped.HasDelegate:
                await this.Skipped.InvokeAsync();
                break;
        }

        this.runCancel?.Dispose();
        this.runCancel = null;
    }


    async Task SetRunningAsync(bool value)
    {
        this.IsRunning = value;
        if (this.IsRunningChanged.HasDelegate)
            await this.IsRunningChanged.InvokeAsync(value);
    }


    async Task OnBackdropClickAsync(MouseEventArgs e)
    {
        if (this.AdvanceOnBackdropClick)
            await this.NextAsync();
    }


    /// <summary>Called from JS when the highlighted element is used, for "tap Save to continue" steps.</summary>
    [JSInvokable]
    public Task OnTargetClickedJs() => this.InvokeAsync(this.NextAsync);


    /// <summary>Called from JS for arrow / Enter / Escape while the tour is up.</summary>
    [JSInvokable]
    public Task OnKeyJs(string action) => this.InvokeAsync(() => action switch
    {
        "next" => this.NextAsync(),
        "back" => this.BackAsync(),
        "skip" => this.SkipAsync(),
        _ => Task.CompletedTask
    });


    /// <summary>Called from JS when the page scrolls or resizes under a running tour.</summary>
    [JSInvokable]
    public async Task OnViewportChangedJs()
    {
        if (!this.running || this.module is null)
            return;

        await this.InvokeAsync(async () =>
        {
            this.viewport = await this.module.InvokeAsync<TooltipRect>("viewport");

            if (this.CurrentStepItem is { } step)
                await this.MeasureSpotAsync(step);

            await this.PlaceCalloutAsync();
            this.StateHasChanged();
        });
    }


    public async ValueTask DisposeAsync()
    {
        this.disposed = true;
        this.CancelDwell();
        this.runCancel?.Cancel();
        this.runCancel?.Dispose();

        if (this.module is not null)
        {
            try
            {
                if (this.watchingClicks)
                    await this.module.InvokeVoidAsync("unwatchTargetClick", this.instanceId);

                await this.module.InvokeVoidAsync("unobserveKeys", this.instanceId);
                await this.module.InvokeVoidAsync("unobserve", this.instanceId);

                // Never leave the page unscrollable because a tour was disposed mid-run.
                if (this.LockScroll && this.running)
                    await this.module.InvokeVoidAsync("lockScroll", false);

                await this.module.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            catch (ObjectDisposedException) { }
        }

        this.selfRef?.Dispose();
        GC.SuppressFinalize(this);
    }
}
