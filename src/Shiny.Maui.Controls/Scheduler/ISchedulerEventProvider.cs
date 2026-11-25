namespace Shiny.Maui.Controls.Scheduler;

public interface ISchedulerEventProvider
{
    Task<IReadOnlyList<SchedulerEvent>> GetEvents(DateTimeOffset start, DateTimeOffset end);
    void OnEventSelected(SchedulerEvent selectedEvent);
    bool CanCalendarSelect(DateOnly selectedDate);
    void OnCalendarDateSelected(DateOnly selectedDate);
    void OnAgendaTimeSelected(DateTimeOffset selectedTime);
    bool CanSelectAgendaTime(DateTimeOffset selectedTime);

    /// <summary>
    /// Gates whether this event can be dragged/resized at all - return false to leave the event fixed
    /// (e.g. read-only calendars, past events). Asked when the agenda lays out its events (while
    /// <c>AllowEventDrag</c> or <c>AllowEventResize</c> is on), so a locked event gets no grips, no
    /// drag cursor and a lock indicator, and asked again when a gesture arms. Keep it cheap, and
    /// reload the view if the answer for an event changes.
    /// </summary>
    /// <remarks>
    /// Defaults to false, so a provider that ignores drag/drop can never have its events moved even
    /// if an app sets <c>AllowEventDrag</c>. The opt-in is required on both the view and the provider.
    /// </remarks>
    bool CanChangeEvent(SchedulerEvent evt) => false;

    /// <summary>
    /// Called continuously as the event is dragged, before the change is committed. Return false to
    /// reject this position - the control shows the rejected state and will not commit there.
    /// Must be cheap: this runs on every snap boundary crossed.
    /// </summary>
    bool CanChangeEventTo(SchedulerEventChange change) => true;

    /// <summary>
    /// Called once when the gesture completes. The control has already applied the change
    /// optimistically. Return true to keep it, false to revert to the original time. Exceptions are
    /// treated as false and surfaced via <c>SchedulerAgendaView.EventChangeFailed</c>.
    /// </summary>
    Task<bool> OnEventChanged(SchedulerEventChange change) => Task.FromResult(false);
}
