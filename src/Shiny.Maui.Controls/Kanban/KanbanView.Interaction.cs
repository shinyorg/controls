using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Kanban.Internal;

namespace Shiny.Maui.Controls.Kanban;

/// <summary>
/// The drag: picking a card up, working out where it would land, and committing that.
/// </summary>
/// <remarks>
/// <para>
/// The drag runs on a <see cref="PanGestureRecognizer"/> on every platform rather than on
/// Drag/DropGestureRecognizer. The platform recognizers are broken on Mac Catalyst
/// (dotnet/maui#23627) and missing entirely from the AppKit and GTK4 hosts, and even where they do
/// work <c>DragEventArgs</c> carries no pointer position - which on a board would mean knowing a
/// card was dropped on a column but not <em>where</em> in it. A pan reports a usable delta
/// everywhere, and the board turns that delta into a column, a swimlane and an index itself.
/// </para>
/// <para>
/// Nothing is re-laid out while a finger is down. The card travels on <c>TranslationX/Y</c>, the
/// insertion line is painted into a separate absolute layer, and the model is not touched until the
/// gesture ends. That is not only for smoothness: on Android a layout pass under an in-flight
/// gesture cancels it, so a board that inserted a placeholder between two cards would drop the very
/// drag that asked for it.
/// </para>
/// </remarks>
public partial class KanbanView
{
    const double AutoScrollZone = 56;
    const double AutoScrollStep = 14;
    static readonly TimeSpan AutoScrollInterval = TimeSpan.FromMilliseconds(16);

    readonly List<DragTouchHook> dragHooks = [];

    KanbanCard? dragCard;
    View? dragHost;
    Rect dragOrigin;
    Point dragPoint;
    LaneCell? dropCell;
    int dropIndex = -1;
    IDispatcherTimer? autoScroll;


    // =============================================================================================
    // Wiring
    // =============================================================================================

    void WireCard(View host, KanbanCard card)
    {
        var tap = new TapGestureRecognizer
        {
            // Command rather than Tapped: a command can be invoked from a headless test, and
            // Tapped cannot be raised at all without a platform behind it.
            Command = new Command(() => this.OnCardTapped(card))
        };
        host.GestureRecognizers.Add(tap);

        if (!this.AllowDragDrop || this.IsReadOnly || card.IsLocked)
            return;

        var column = this.Board.Column(card.ColumnId);
        if (column is not null && !column.AllowDrag)
            return;

        // The scroller has to be told to keep its hands off from the raw touch down, not from the
        // pan: by the time a pan reports Started, a UIScrollView has already cancelled the touches
        // it delivered and Android's scroller has already claimed the gesture.
        var hook = new DragTouchHook(host);
        hook.Pressed = () => hook.LockScroller(true);
        hook.Released = () => hook.LockScroller(false);
        this.dragHooks.Add(hook);

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) =>
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    this.BeginDrag(card, host);
                    break;

                case GestureStatus.Running:
                    this.UpdateDrag(e.TotalX, e.TotalY);
                    break;

                // Android reports zeroed totals on the final event, so the drop commits from the
                // target the last Running event resolved rather than from anything on this one.
                case GestureStatus.Completed:
                    this.CompleteDrag();
                    break;

                case GestureStatus.Canceled:
                    this.CancelDrag();
                    break;
            }
        };
        host.GestureRecognizers.Add(pan);
    }


    void WireColumnCollapse(KanbanColumnHeaderView header)
    {
        if (!this.AllowColumnCollapse || this.IsReadOnly)
            return;

        header.Chevron.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => this.ToggleColumn(header.Column))
        });
    }


    void ReleaseDragHooks() => this.dragHooks.Clear();


    void OnCardTapped(KanbanCard card)
    {
        this.SelectedCard = card;
        this.CardTapped?.Invoke(this, new KanbanCardEventArgs(card));

        if (this.CardTappedCommand?.CanExecute(card) == true)
            this.CardTappedCommand.Execute(card);
    }


    // =============================================================================================
    // The drag itself
    // =============================================================================================

    void BeginDrag(KanbanCard card, View host)
    {
        this.CancelDrag();

        if (ViewGeometry.BoundsIn(host, this.bodyContent) is not { } origin)
            return;

        this.dragCard = card;
        this.dragHost = host;
        this.dragOrigin = origin;
        this.dragPoint = origin.Center;

        host.Opacity = 0.9;
        DragTouchHook.Raise(host, true);

        FeedbackHelper.Execute(this, "DragStarted");
        this.StartAutoScroll();
    }


    void UpdateDrag(double totalX, double totalY)
    {
        if (this.dragHost is null || this.dragCard is null)
            return;

        this.dragHost.TranslationX = totalX;
        this.dragHost.TranslationY = totalY;

        this.dragPoint = new Point(this.dragOrigin.Center.X + totalX, this.dragOrigin.Center.Y + totalY);
        this.ResolveDropTarget();
    }


    /// <summary>
    /// Works out which lane and which slot the pointer is over, and paints the insertion line there.
    /// </summary>
    /// <remarks>
    /// The lane is resolved from the cell hosts' own bounds rather than from the column widths the
    /// view laid out with. They should agree - but when they do not, because a template gave a
    /// header an intrinsic width the grid honoured, the cards are where the cells are, not where the
    /// arithmetic says they are.
    /// </remarks>
    void ResolveDropTarget()
    {
        this.dropCell = null;
        this.dropIndex = -1;
        this.dropIndicator.IsVisible = false;

        if (this.dragCard is null)
            return;

        LaneCell? hit = null;
        var bestDistance = double.MaxValue;

        foreach (var cell in this.laneCells)
        {
            if (ViewGeometry.BoundsIn(cell.Host, this.bodyContent) is not { } bounds)
                continue;

            if (bounds.Contains(this.dragPoint))
            {
                hit = cell;
                break;
            }

            // Dragging into the gutter between two columns, or past the last one, should still land
            // somewhere - a drop that silently does nothing because the finger was four pixels wide
            // of a column is indistinguishable from a broken board.
            var distance = Distance(bounds, this.dragPoint);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                hit = cell;
            }
        }

        if (hit is null)
            return;

        var verdict = this.Board.Evaluate(this.dragCard, hit.Lane.Column.Id, hit.Lane.Swimlane?.Id);
        if (!verdict.Allowed)
            return;

        this.dropCell = hit;
        this.dropIndex = this.ResolveIndex(hit, this.dragPoint.Y);
        this.PaintIndicator(hit, this.dropIndex);
    }


    int ResolveIndex(LaneCell cell, double y)
    {
        for (var i = 0; i < cell.CardViews.Count; i++)
        {
            if (ViewGeometry.BoundsIn(cell.CardViews[i], this.bodyContent) is not { } bounds)
                continue;

            // The card being dragged has been translated out from under itself, so measuring it
            // would place the line wherever the finger happens to be. Its original slot is the one
            // that matters, and PlanMove is what takes it back out of the count.
            if (ReferenceEquals(cell.CardViews[i], this.dragHost))
            {
                if (y < this.dragOrigin.Center.Y)
                    return i;

                continue;
            }

            if (y < bounds.Center.Y)
                return i;
        }

        return cell.CardViews.Count;
    }


    void PaintIndicator(LaneCell cell, int index)
    {
        if (ViewGeometry.BoundsIn(cell.Host, this.bodyContent) is not { } host)
            return;

        var left = host.X + this.ColumnPadding.Left;
        var width = Math.Max(8, host.Width - this.ColumnPadding.HorizontalThickness);
        double y;

        if (index < cell.CardViews.Count &&
            ViewGeometry.BoundsIn(cell.CardViews[index], this.bodyContent) is { } target)
        {
            y = target.Y - (this.CardSpacing / 2);
        }
        else if (cell.CardViews.Count > 0 &&
                 ViewGeometry.BoundsIn(cell.CardViews[^1], this.bodyContent) is { } last)
        {
            y = last.Bottom + (this.CardSpacing / 2);
        }
        else
        {
            y = host.Y + this.ColumnPadding.Top;
        }

        AbsoluteLayout.SetLayoutBounds(this.dropIndicator, new Rect(left, y - 1.5, width, 3));
        this.dropIndicator.IsVisible = true;
    }


    void CompleteDrag()
    {
        var card = this.dragCard;
        var cell = this.dropCell;
        var index = this.dropIndex;

        this.EndDrag();

        if (card is null)
            return;

        if (cell is null)
        {
            this.RaiseRejected(card, KanbanDropRejection.UnknownColumn, null);
            return;
        }

        var move = this.Board.PlanMove(card, cell.Lane.Column.Id, cell.Lane.Swimlane?.Id, index);

        if (move is null)
        {
            var reason = this.Board.Evaluate(card, cell.Lane.Column.Id, cell.Lane.Swimlane?.Id).Reason;
            this.RaiseRejected(card, reason, cell.Lane.Column.Id);
            return;
        }

        this.CommitMove(move);
    }


    /// <summary>
    /// Runs a planned move past <see cref="CardMoving"/>, applies it, and re-renders.
    /// </summary>
    /// <remarks>
    /// A no-op move still goes through here rather than being short-circuited: dropping a card back
    /// where it started is the user cancelling, and a handler that is logging every move wants to
    /// see that it ended in nothing rather than see nothing at all.
    /// </remarks>
    bool CommitMove(KanbanMove move)
    {
        var moving = new KanbanCardMovingEventArgs(move);
        this.CardMoving?.Invoke(this, moving);

        if (moving.Cancel)
        {
            this.RenderBoard();
            return false;
        }

        if (move.IsNoOp)
        {
            this.RenderBoard();
            return true;
        }

        this.Board.Apply(move);

        FeedbackHelper.Execute(this, "ItemDropped");

        // Deferred a tick: rebuilding the tree while the gesture is still unwinding pulls the
        // native views out from under the platform recognizer that is still reporting on them.
        this.Dispatcher.Dispatch(() =>
        {
            this.RebuildBoard();

            this.CardMoved?.Invoke(this, new KanbanCardMovedEventArgs(move));

            if (this.CardMovedCommand?.CanExecute(move) == true)
                this.CardMovedCommand.Execute(move);
        });

        return true;
    }


    void RaiseRejected(KanbanCard card, KanbanDropRejection reason, string? columnId)
    {
        this.RenderBoard();
        this.DropRejected?.Invoke(this, new KanbanDropRejectedEventArgs(card, this.Board.Column(columnId), reason));
    }


    /// <summary>Drops any in-flight drag and springs the card back. Safe to call when nothing is dragging.</summary>
    internal void CancelDrag()
    {
        if (this.dragCard is null)
            return;

        this.EndDrag();
    }


    void EndDrag()
    {
        this.StopAutoScroll();

        if (this.dragHost is not null)
        {
            this.dragHost.TranslationX = 0;
            this.dragHost.TranslationY = 0;
            this.dragHost.Opacity = 1;
            DragTouchHook.Raise(this.dragHost, false);
        }

        this.dragCard = null;
        this.dragHost = null;
        this.dropCell = null;
        this.dropIndex = -1;
        this.dropIndicator.IsVisible = false;
    }


    // =============================================================================================
    // Auto-scroll
    // =============================================================================================

    /// <remarks>
    /// A board is wider and taller than its viewport by design, so a drag that cannot scroll can
    /// only ever move a card between the columns that happen to be on screen - which on a phone is
    /// one and a half of them.
    /// </remarks>
    void StartAutoScroll()
    {
        this.StopAutoScroll();

        this.autoScroll = this.Dispatcher.CreateTimer();
        this.autoScroll.Interval = AutoScrollInterval;
        this.autoScroll.Tick += this.OnAutoScrollTick;
        this.autoScroll.Start();
    }


    void StopAutoScroll()
    {
        if (this.autoScroll is null)
            return;

        this.autoScroll.Tick -= this.OnAutoScrollTick;
        this.autoScroll.Stop();
        this.autoScroll = null;
    }


    void OnAutoScrollTick(object? sender, EventArgs e)
    {
        if (this.dragCard is null)
            return;

        var viewportX = this.dragPoint.X - this.bodyScroll.ScrollX;
        var viewportY = this.dragPoint.Y - this.bodyScroll.ScrollY;

        var dx = Nudge(viewportX, this.bodyScroll.Width);
        var dy = Nudge(viewportY, this.bodyScroll.Height);

        if (dx == 0 && dy == 0)
            return;

        var x = Math.Clamp(this.bodyScroll.ScrollX + dx, 0, Math.Max(0, this.bodyContent.Width - this.bodyScroll.Width));
        var y = Math.Clamp(this.bodyScroll.ScrollY + dy, 0, Math.Max(0, this.bodyContent.Height - this.bodyScroll.Height));

        this.bodyScroll.ScrollToAsync(x, y, false);
        this.ResolveDropTarget();
    }


    static double Nudge(double position, double extent)
    {
        if (extent <= 0)
            return 0;

        if (position < AutoScrollZone)
            return -AutoScrollStep;

        if (position > extent - AutoScrollZone)
            return AutoScrollStep;

        return 0;
    }


    static double Distance(Rect rect, Point point)
    {
        var dx = Math.Max(Math.Max(rect.X - point.X, 0), point.X - rect.Right);
        var dy = Math.Max(Math.Max(rect.Y - point.Y, 0), point.Y - rect.Bottom);
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
