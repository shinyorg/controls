using Shiny.Controls.Office.Icons;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Shiny.Controls.Office.Shell;

/// <summary>
/// One segment on the left of the status bar — "Page 1 of 3", "197 words", "English (US)",
/// "Average: 4  Count: 3  Sum: 12".
/// </summary>
/// <remarks>
/// Observable, so an editor keeps one instance per segment and just sets <see cref="Text"/> as the caret
/// moves. The status bar redraws the segment; nothing is rebuilt.
/// </remarks>
public sealed class OfficeStatusItem : INotifyPropertyChanged
{
    string? text;
    string? tooltip;
    bool isVisible = true;
    bool isClickable;

    public OfficeStatusItem() { }

    public OfficeStatusItem(string id, string? text = null)
    {
        this.Id = id;
        this.text = text;
    }

    /// <summary>A key the host recognises when the segment is clicked.</summary>
    public string Id { get; init; } = string.Empty;

    public string? Text
    {
        get => this.text;
        set => this.Set(ref this.text, value);
    }

    public string? Tooltip
    {
        get => this.tooltip;
        set => this.Set(ref this.tooltip, value);
    }

    /// <summary>Hidden segments take no room. Excel's aggregates hide when a single cell is selected.</summary>
    public bool IsVisible
    {
        get => this.isVisible;
        set => this.Set(ref this.isVisible, value);
    }

    /// <summary>Drawn as a button, raising the status bar's <c>ItemClicked</c> — "Page 1 of 3" opens Go To.</summary>
    public bool IsClickable
    {
        get => this.isClickable;
        set => this.Set(ref this.isClickable, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}


/// <summary>The words the status bar segments say, formatted the way Office formats them.</summary>
public static class OfficeStatusText
{
    /// <summary>"Page 2 of 7". Pages are 1-based.</summary>
    public static string Page(int page, int pageCount)
        => $"Page {Math.Max(1, page)} of {Math.Max(1, pageCount)}";

    /// <summary>"Slide 3 of 12".</summary>
    public static string Slide(int slide, int slideCount)
        => $"Slide {Math.Max(1, slide)} of {Math.Max(1, slideCount)}";

    /// <summary>"1 word", "1,204 words", or "12 of 1,204 words" while a selection is counted.</summary>
    public static string Words(int count, int? selected = null, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var total = count.ToString("N0", culture);

        if (selected is { } s && s > 0)
            return $"{s.ToString("N0", culture)} of {total} words";

        return count == 1 ? "1 word" : $"{total} words";
    }

    /// <summary>"1,204 characters".</summary>
    public static string Characters(int count, CultureInfo? culture = null)
        => count == 1 ? "1 character" : $"{count.ToString("N0", culture ?? CultureInfo.CurrentCulture)} characters";

    /// <summary>
    /// Excel's selection summary — "Average: 4  Count: 3  Sum: 12" — or null when there is nothing to
    /// summarise (Excel shows it only for a multi-cell selection holding numbers).
    /// </summary>
    /// <param name="values">The numeric values in the selection.</param>
    /// <param name="nonEmptyCount">How many cells are non-empty, numbers or not — Excel's Count.</param>
    public static string? Aggregates(IReadOnlyCollection<double> values, int nonEmptyCount, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;

        if (nonEmptyCount < 2)
            return null;

        if (values.Count == 0)
            return $"Count: {nonEmptyCount.ToString("N0", culture)}";

        var sum = values.Sum();
        var average = sum / values.Count;

        return $"Average: {Number(average, culture)}  Count: {nonEmptyCount.ToString("N0", culture)}  Sum: {Number(sum, culture)}";
    }

    /// <summary>A status-bar number: grouped, and no more decimals than it needs (up to ten, like Excel).</summary>
    public static string Number(double value, CultureInfo? culture = null)
        => Math.Round(value, 10).ToString("#,##0.##########", culture ?? CultureInfo.CurrentCulture);
}


/// <summary>How far through a save the document is. Drives the title bar's status text.</summary>
public enum OfficeSaveState
{
    /// <summary>Nothing to say — a new document that has never been touched.</summary>
    None,

    /// <summary>Written to wherever it lives.</summary>
    Saved,

    /// <summary>Written to this device only — TextSpace's "Saved locally".</summary>
    SavedLocally,

    /// <summary>A save is in flight.</summary>
    Saving,

    /// <summary>Edited since the last save.</summary>
    Unsaved,

    /// <summary>The last save failed.</summary>
    Error
}


/// <summary>The title bar's save-state words.</summary>
public static class OfficeSaveStateText
{
    public static string? For(OfficeSaveState state) => state switch
    {
        OfficeSaveState.Saved => "Saved",
        OfficeSaveState.SavedLocally => "Saved locally",
        OfficeSaveState.Saving => "Saving…",
        OfficeSaveState.Unsaved => "Unsaved changes",
        OfficeSaveState.Error => "Couldn't save",
        _ => null
    };
}


/// <summary>What the mode dropdown beside Share is set to.</summary>
public enum OfficeEditMode
{
    /// <summary>Edit the document directly.</summary>
    Editing,

    /// <summary>Edits become suggestions (tracked changes).</summary>
    Reviewing,

    /// <summary>Read only.</summary>
    Viewing
}


/// <summary>The mode dropdown's entries.</summary>
public static class OfficeEditModes
{
    public static string Title(OfficeEditMode mode) => mode switch
    {
        OfficeEditMode.Reviewing => "Reviewing",
        OfficeEditMode.Viewing => "Viewing",
        _ => "Editing"
    };

    public static string Description(OfficeEditMode mode) => mode switch
    {
        OfficeEditMode.Reviewing => "Edits become suggestions",
        OfficeEditMode.Viewing => "Read or print final document",
        _ => "Edit document directly"
    };

    public static OfficeShellIcon Icon(OfficeEditMode mode) => mode switch
    {
        OfficeEditMode.Reviewing => OfficeShellIcon.Reviewing,
        OfficeEditMode.Viewing => OfficeShellIcon.Viewing,
        _ => OfficeShellIcon.Editing
    };

    public static IReadOnlyList<OfficeEditMode> All { get; } = [OfficeEditMode.Editing, OfficeEditMode.Reviewing, OfficeEditMode.Viewing];
}


/// <summary>One of the view-mode buttons on the right of the status bar.</summary>
/// <param name="Id">A stable key — "read", "print", "web", "normal", "pageLayout", "sorter".</param>
/// <param name="Text">The tooltip.</param>
/// <param name="Icon">The artwork.</param>
public sealed record OfficeViewMode(string Id, string Text, OfficeShellIcon Icon);


/// <summary>The three view modes each app offers.</summary>
public static class OfficeViewModes
{
    public static readonly OfficeViewMode Read = new("read", "Read Mode", OfficeShellIcon.ReadMode);
    public static readonly OfficeViewMode Print = new("print", "Print Layout", OfficeShellIcon.PrintLayout);
    public static readonly OfficeViewMode Web = new("web", "Web Layout", OfficeShellIcon.WebLayout);

    public static readonly OfficeViewMode Normal = new("normal", "Normal", OfficeShellIcon.NormalView);
    public static readonly OfficeViewMode PageLayout = new("pageLayout", "Page Layout", OfficeShellIcon.PageLayoutView);
    public static readonly OfficeViewMode PageBreak = new("pageBreak", "Page Break Preview", OfficeShellIcon.PageBreakView);

    public static readonly OfficeViewMode SlideNormal = new("normal", "Normal", OfficeShellIcon.NormalView);
    public static readonly OfficeViewMode Sorter = new("sorter", "Slide Sorter", OfficeShellIcon.SlideSorter);
    public static readonly OfficeViewMode Reading = new("reading", "Reading View", OfficeShellIcon.ReadMode);

    public static IReadOnlyList<OfficeViewMode> For(OfficeApp app) => app switch
    {
        OfficeApp.Excel => [Normal, PageLayout, PageBreak],
        OfficeApp.PowerPoint => [SlideNormal, Sorter, Reading],
        OfficeApp.OneNote => [],
        _ => [Read, Print, Web]
    };

    /// <summary>The mode each app opens in.</summary>
    public static string DefaultId(OfficeApp app) => app switch
    {
        OfficeApp.Word => Print.Id,
        _ => "normal"
    };
}
