using Shiny.Blazor.Controls.Scheduler;

namespace Sample.Blazor;


/// <summary>
/// Mirrors the MAUI sample's SampleSchedulerProvider: a deterministic weekly
/// pattern with overlapping events, all-day spans, multi-day events, and an
/// overnight event — plus a simulated network delay.
/// </summary>
public class SampleEventProvider : ISchedulerEventProvider
{
    static readonly string[] CategoryColors =
    [
        "#4285F4", // Blue - Meetings
        "#0F9D58", // Green - Personal
        "#DB4437", // Red - Important
        "#F4B400", // Yellow - Reminders
        "#AB47BC", // Purple - Projects
        "#00ACC1", // Cyan - Travel
    ];

    public string? LastSelectedEvent { get; private set; }
    public DateOnly? LastSelectedCalendarDate { get; private set; }
    public DateTimeOffset? LastSelectedAgendaTime { get; private set; }

    /// <summary>Lets pages re-render their status display when a selection is recorded.</summary>
    public event Action? Changed;

    // Days are generated once and then handed back as the same instances, so an event you drag
    // stays where you dropped it instead of being regenerated on the next load.
    readonly Dictionary<DateTime, List<SchedulerEvent>> dayCache = [];

    public async Task<IReadOnlyList<SchedulerEvent>> GetEvents(DateTimeOffset start, DateTimeOffset end)
    {
        await Task.Delay(300); // simulate network

        var events = new List<SchedulerEvent>();
        var current = start.LocalDateTime.Date;
        var endDate = end.LocalDateTime.Date;

        while (current <= endDate)
        {
            if (!dayCache.TryGetValue(current, out var dayEvents))
            {
                dayEvents = BuildDay(current);
                dayCache[current] = dayEvents;
            }
            events.AddRange(dayEvents);
            current = current.AddDays(1);
        }
        return events;
    }


    static List<SchedulerEvent> BuildDay(DateTime current)
    {
        var events = new List<SchedulerEvent>();
        {
            var dow = current.DayOfWeek;

            if (dow is >= DayOfWeek.Monday and <= DayOfWeek.Friday)
            {
                Add(events, "Team Standup", "Daily sync with the team", CategoryColors[0],
                    current.AddHours(9), current.AddHours(9).AddMinutes(30));
                Add(events, "Lunch Break", "Take a break", CategoryColors[1],
                    current.AddHours(12), current.AddHours(13));
            }

            if (dow == DayOfWeek.Monday)
                Add(events, "Sprint Planning", "Plan the week's work", CategoryColors[4],
                    current.AddHours(10), current.AddHours(11).AddMinutes(30));

            // Tuesday: 3-way overlap from 2-3pm to exercise the overlap layout
            if (dow == DayOfWeek.Tuesday)
            {
                Add(events, "Project Alpha", "Architecture review", CategoryColors[4],
                    current.AddHours(13).AddMinutes(30), current.AddHours(15));
                Add(events, "Client Call", "Q1 deliverables discussion", CategoryColors[2],
                    current.AddHours(14), current.AddHours(15).AddMinutes(30));
                Add(events, "Code Review", "PR #342 review", CategoryColors[0],
                    current.AddHours(14).AddMinutes(30), current.AddHours(16));
            }

            if (dow == DayOfWeek.Wednesday)
            {
                Add(events, "Design Review", "Review new mockups", CategoryColors[4],
                    current.AddHours(14), current.AddHours(15));
                Add(events, "1:1 with Manager", "Weekly check-in", CategoryColors[2],
                    current.AddHours(15).AddMinutes(30), current.AddHours(16));
            }

            if (dow == DayOfWeek.Friday)
                Add(events, "Sprint Retro", "What went well, what didn't", CategoryColors[4],
                    current.AddHours(16), current.AddHours(17));

            if (dow is DayOfWeek.Tuesday or DayOfWeek.Thursday)
                Add(events, "Gym", "Afternoon workout", CategoryColors[1],
                    current.AddHours(17).AddMinutes(30), current.AddHours(18).AddMinutes(30));

            // Overnight event on Saturdays (8pm - 1am next day)
            if (dow == DayOfWeek.Saturday)
                Add(events, "Game Night", "Board games at Dave's place", CategoryColors[1],
                    current.AddHours(20), current.AddDays(1).AddHours(1));

            // All-day: first Monday = sprint start (spans the week)
            if (dow == DayOfWeek.Monday && current.Day <= 7)
                AddAllDay(events, "Sprint Start", CategoryColors[3], current, current.AddDays(5));

            if (current.Day == 15)
                AddAllDay(events, "Company Holiday", CategoryColors[2], current, current.AddDays(1));

            // Multi-day conference (second week, Tue-Thu)
            if (dow == DayOfWeek.Tuesday && current.Day is > 7 and <= 14)
                AddAllDay(events, "Tech Conference", CategoryColors[5], current, current.AddDays(3));

            // Multi-day vacation (third week, Mon-Fri)
            if (dow == DayOfWeek.Monday && current.Day is > 14 and <= 21)
                AddAllDay(events, "Vacation", CategoryColors[1], current, current.AddDays(5));

            if (current.Day == 20)
                AddAllDay(events, "Sarah's Birthday", CategoryColors[3], current, current.AddDays(1));

            // Multi-day deadline spanning a weekend (last Thu-Mon)
            if (dow == DayOfWeek.Thursday && current.Day > 24)
                AddAllDay(events, "Release Deadline", CategoryColors[2], current, current.AddDays(4));
        }
        return events;
    }

    static void Add(List<SchedulerEvent> events, string title, string description, string color, DateTime start, DateTime end)
        => events.Add(new SchedulerEvent
        {
            Title = title,
            Description = description,
            Color = color,
            Start = new DateTimeOffset(start),
            End = new DateTimeOffset(end)
        });

    static void AddAllDay(List<SchedulerEvent> events, string title, string color, DateTime start, DateTime end)
        => events.Add(new SchedulerEvent
        {
            Title = title,
            IsAllDay = true,
            Color = color,
            Start = new DateTimeOffset(start),
            End = new DateTimeOffset(end)
        });

    public void OnEventSelected(SchedulerEvent selectedEvent)
    {
        LastSelectedEvent = $"{selectedEvent.Title} ({selectedEvent.Start.LocalDateTime:g})";
        Changed?.Invoke();
    }

    public bool CanCalendarSelect(DateOnly selectedDate) => true;

    public void OnCalendarDateSelected(DateOnly selectedDate)
    {
        LastSelectedCalendarDate = selectedDate;
        Changed?.Invoke();
    }

    public void OnAgendaTimeSelected(DateTimeOffset selectedTime)
    {
        LastSelectedAgendaTime = selectedTime;
        Changed?.Invoke();
    }

    public bool CanSelectAgendaTime(DateTimeOffset selectedTime) => true;

    // ------------- drag / resize -------------

    public string? LastChange { get; private set; }

    /// <summary>Lunch is immovable - it demonstrates the per-event gate: no grips, and a lock on the block.</summary>
    public bool CanChangeEvent(SchedulerEvent evt) =>
        !evt.IsAllDay && evt.Title != "Lunch Break";

    /// <summary>Nothing before 07:00, to show a rejected position live under the pointer.</summary>
    public bool CanChangeEventTo(SchedulerEventChange change) =>
        change.NewStart.LocalDateTime.TimeOfDay >= TimeSpan.FromHours(7);

    public async Task<bool> OnEventChanged(SchedulerEventChange change)
    {
        await Task.Delay(600); // simulate a save round trip

        // 10% of saves fail, so optimistic-commit-then-revert is visible rather than theoretical.
        if (Random.Shared.Next(10) == 0)
        {
            LastChange = $"SAVE FAILED — '{change.Event.Title}' snapped back";
            Changed?.Invoke();
            return false;
        }

        LastChange = $"{change.Kind} {change.Event.Title} → {change.NewStart.LocalDateTime:g} - {change.NewEnd.LocalDateTime:t}";
        Changed?.Invoke();
        return true;
    }
}
