using Shiny.Controls.Office.Shell;

namespace Shiny.Blazor.Controls.Office;

/// <summary>Feeds a ribbon's commands into the title bar's command search.</summary>
public static class OfficeRibbonCommands
{
    /// <summary>The id prefix harvested commands carry, so a re-harvest replaces rather than duplicates them.</summary>
    public const string IdPrefix = "ribbon:";

    /// <summary>Adds (or refreshes) every command the ribbon has indexed.</summary>
    public static void AddRibbon(this OfficeCommandIndex index, Ribbon ribbon)
        => index.AddRange(ribbon.GetCommands().Select(ToCommand));

    /// <summary>
    /// Adds the ribbon's commands now and again whenever the ribbon indexes more — each tab adds its
    /// commands the first time it renders. Dispose the result to stop.
    /// </summary>
    public static IDisposable SyncRibbon(this OfficeCommandIndex index, Ribbon ribbon)
    {
        index.AddRibbon(ribbon);

        void Handler(object? sender, EventArgs e) => index.AddRibbon(ribbon);
        ribbon.CommandsChanged += Handler;
        return new Subscription(() => ribbon.CommandsChanged -= Handler);
    }

    /// <summary>One ribbon command as a search entry.</summary>
    public static OfficeCommand ToCommand(RibbonCommandInfo info)
        => new(info.Label, info.InvokeAsync)
        {
            Id = IdPrefix + info.Key,
            Category = info.Category,
            Description = info.Description,
            Shortcut = info.Shortcut,
            Icon = info.Icon,
            Keywords = info.Tooltip is { } tip && tip != info.Label ? [tip] : [],
            CanExecute = () => info.IsEnabled
        };

    sealed class Subscription(Action dispose) : IDisposable
    {
        Action? dispose = dispose;

        public void Dispose()
        {
            this.dispose?.Invoke();
            this.dispose = null;
        }
    }
}
