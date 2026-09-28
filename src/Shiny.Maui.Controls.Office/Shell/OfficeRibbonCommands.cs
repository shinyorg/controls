using Shiny.Controls.Office.Shell;
using Shiny.Maui.Controls.Ribbons;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// Puts a ribbon's commands into an <see cref="OfficeCommandIndex"/>, so the title bar's search can find
/// and run every button on it.
/// </summary>
/// <remarks>
/// The MAUI ribbon is a descriptor model, so <see cref="Ribbon.GetCommands"/> sees every tab — including
/// ones never opened. Labels and enabled states are read when harvested; <see cref="SyncRibbon"/>
/// re-harvests as the user works so a renamed or newly-shown item is found too.
/// </remarks>
public static class OfficeRibbonCommands
{
    /// <summary>Adds (or replaces) every command on <paramref name="ribbon"/>.</summary>
    public static void AddRibbon(this OfficeCommandIndex index, Ribbon ribbon)
        => index.AddRange(ribbon.GetCommands().Select(ToCommand));


    /// <summary>
    /// Harvests now and again whenever the ribbon changes tab or runs a command. Dispose to stop.
    /// </summary>
    public static IDisposable SyncRibbon(this OfficeCommandIndex index, Ribbon ribbon)
    {
        index.AddRibbon(ribbon);

        void Tab(object? s, RibbonTabEventArgs e) => index.AddRibbon(ribbon);
        void Item(object? s, RibbonItemEventArgs e) => index.AddRibbon(ribbon);

        ribbon.TabChanged += Tab;
        ribbon.ItemInvoked += Item;

        return new Subscription(() =>
        {
            ribbon.TabChanged -= Tab;
            ribbon.ItemInvoked -= Item;
        });
    }


    /// <summary>One ribbon command as a searchable command.</summary>
    public static OfficeCommand ToCommand(RibbonCommandInfo info)
    {
        var keywords = new List<string>();
        if (!string.IsNullOrWhiteSpace(info.Tooltip) && info.Tooltip != info.Label)
            keywords.Add(info.Tooltip!);

        return new OfficeCommand(info.Label, info.Invoke)
        {
            Category = info.Category,
            Description = info.Description,
            Shortcut = info.Shortcut,
            Icon = info.Icon,
            Keywords = keywords,
            CanExecute = () => info.IsEnabled
        };
    }


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
