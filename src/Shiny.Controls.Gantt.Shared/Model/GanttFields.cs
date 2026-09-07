using System.Globalization;

namespace Shiny.Controls.Gantt;

/// <summary>
/// The task-pane field vocabulary: which names a column may bind to, how each resolves against a
/// <see cref="GanttTask"/>, and how the value is rendered.
/// </summary>
/// <remarks>
/// Shared so the two hosts' task grids agree to the character. A duration shown as "3d" on a phone
/// and "3.00:00:00" in a browser is the kind of divergence that never shows up in either host's tests
/// and is immediately obvious to anyone who uses both.
/// </remarks>
public static class GanttFields
{
    /// <summary>The task's name.</summary>
    public const string Name = "Name";

    /// <summary>The task's start date.</summary>
    public const string Start = "Start";

    /// <summary>The task's finish date.</summary>
    public const string End = "End";

    /// <summary>The task's duration, rendered in the plan's own units.</summary>
    public const string Duration = "Duration";

    /// <summary>Completion, rendered as a percentage.</summary>
    public const string Progress = "Progress";

    /// <summary>Who the task is assigned to.</summary>
    public const string Resource = "Resource";

    /// <summary>How far the task can slip before it becomes critical.</summary>
    public const string Slack = "Slack";

    /// <summary>The date the task must finish by, when one is set.</summary>
    public const string Deadline = "Deadline";

    /// <summary>Every built-in field name, for a designer or a docs table to enumerate.</summary>
    public static IReadOnlyList<string> All { get; } = [Name, Start, End, Duration, Progress, Resource, Slack, Deadline];


    /// <summary>
    /// The raw value behind a field. An unrecognised name falls through to
    /// <see cref="GanttTask.Fields"/>, which is how a plan loaded from JSON shows columns the control
    /// was never compiled against.
    /// </summary>
    public static object? ValueOf(GanttTask task, string field) => field switch
    {
        Name => task.Name,
        Start => task.Start,
        End => task.Kind == GanttTaskKind.Milestone ? task.Start : task.End,
        Duration => task.Duration,
        Progress => task.Progress,
        Resource => task.ResourceId ?? (task.Assignees.Count > 0 ? string.Join(", ", task.Assignees) : null),
        Slack => task.TotalSlack,
        Deadline => task.Deadline,
        _ => task.Fields.TryGetValue(field, out var custom) ? custom : null
    };


    /// <summary>The value rendered for display, honouring an optional format string.</summary>
    public static string TextOf(GanttTask task, string field, string? format = null, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var value = ValueOf(task, field);

        return value switch
        {
            null => string.Empty,
            string s => s,
            DateTimeOffset d => d.ToString(format ?? "d", culture),
            TimeSpan t => format is null ? FormatDuration(t, culture) : t.ToString(format, culture),
            double n when field == Progress => n.ToString(format ?? "P0", culture),
            double n => n.ToString(format ?? "0.##", culture),
            IFormattable f => f.ToString(format, culture),
            _ => value.ToString() ?? string.Empty
        };
    }


    /// <summary>
    /// Durations read as "3d" or "4h", never as "3.00:00:00".
    /// </summary>
    /// <remarks>
    /// A plan's natural unit is days until it is short enough to be hours, and a raw
    /// <see cref="TimeSpan"/> string is unreadable at either. The dash for zero is deliberate: a
    /// milestone's duration column should say "there is nothing here", not "0".
    /// </remarks>
    public static string FormatDuration(TimeSpan value, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;

        if (value == TimeSpan.Zero)
            return "-";

        var negative = value < TimeSpan.Zero;
        var magnitude = negative ? -value : value;

        var text = magnitude.TotalDays >= 1
            ? magnitude.TotalDays.ToString("0.##", culture) + "d"
            : magnitude.TotalHours >= 1
                ? magnitude.TotalHours.ToString("0.##", culture) + "h"
                : magnitude.TotalMinutes.ToString("0", culture) + "m";

        return negative ? "-" + text : text;
    }
}
