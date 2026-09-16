namespace Shiny.Controls.Office;

/// <summary>
/// Subscribes a controller to a model it does not own without the model keeping the controller alive.
/// </summary>
/// <remarks>
/// <para>
/// The documents - a <c>WordDocument</c>, <c>Workbook</c>, <c>SlideDeck</c>, <c>NotebookDocument</c> - are
/// handed to a control by the app and usually outlive it: a view model holds the open file while the
/// page showing it is pushed and popped. A plain <c>document.ContentChanged += ...</c> from the controller
/// made the document root the controller, and through the controller's own <c>Changed</c> the control and
/// the whole page around it - every visit to the page leaked one more. No host calls <c>Dispose</c>
/// reliably (MAUI never does), so the fix cannot depend on anybody unsubscribing.
/// </para>
/// <para>
/// The forwarder holds the target weakly and removes itself the first time it is raised after the target
/// has gone. The callback must not capture the target - pass a <c>static</c> lambda - or the forwarder
/// would pin it again.
/// </para>
/// </remarks>
static class WeakEvent
{
    /// <param name="target">The subscriber, held weakly.</param>
    /// <param name="source">The publisher, passed back to <paramref name="unsubscribe"/>.</param>
    /// <param name="onRaised">Runs against the live target. Must be <c>static</c>.</param>
    /// <param name="unsubscribe">Removes the forwarder from the source. Must be <c>static</c>.</param>
    /// <remarks>
    /// Both callbacks take what they need as arguments so that neither closes over anything. That is
    /// not style: when a constructor has other lambdas capturing <c>this</c>, the compiler folds
    /// <c>this</c> into the same closure class as any captured local or parameter - so an innocent
    /// <c>h =&gt; document.ContentChanged -= h</c> quietly holds the controller strongly again.
    /// </remarks>
    public static EventHandler Forward<TTarget, TSource>(
        TTarget target,
        TSource source,
        Action<TTarget> onRaised,
        Action<TSource, EventHandler> unsubscribe)
        where TTarget : class
    {
        var reference = new WeakReference<TTarget>(target);
        EventHandler? forwarder = null;

        forwarder = (_, _) =>
        {
            if (reference.TryGetTarget(out var alive))
                onRaised(alive);
            else
                unsubscribe(source, forwarder!);
        };

        return forwarder;
    }
}
