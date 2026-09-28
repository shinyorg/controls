using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.Ribbons;

public partial class Ribbon
{
    public static readonly BindableProperty HeaderEndContentProperty = BindableProperty.Create(
        nameof(HeaderEndContent),
        typeof(View),
        typeof(Ribbon),
        null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(Ribbon), () => ((Ribbon)b).OnHeaderEndChanged((View?)n))
    );

    /// <summary>
    /// A view at the far end of the tab strip, after the quick access row — the Comments, mode and
    /// Share buttons an Office window puts there. The strip only reserves the room.
    /// </summary>
    public View? HeaderEndContent
    {
        get => (View?)this.GetValue(HeaderEndContentProperty);
        set => this.SetValue(HeaderEndContentProperty, value);
    }


    void OnHeaderEndChanged(View? content)
    {
        this.headerEndHost.Content = content;
        this.headerEndHost.IsVisible = content is not null;
    }


    /// <summary>
    /// Every command on the bar — buttons, toggles, split buttons, the lines of every dropdown, the
    /// entries of every gallery that names them, and the quick access row — for a command search.
    /// </summary>
    /// <remarks>
    /// Walked fresh on each call from the item model, so it always reflects the current labels and
    /// enabled states, and includes tabs the user has never opened: the MAUI ribbon is a descriptor
    /// model, so nothing has to have been drawn to be found. Hidden tabs, groups and items are left out.
    /// </remarks>
    public IReadOnlyList<RibbonCommandInfo> GetCommands()
    {
        var list = new List<RibbonCommandInfo>();

        foreach (var tab in this.tabs.Where(x => x.IsVisible))
        {
            foreach (var group in tab.Groups.Where(x => x.IsVisible))
            {
                foreach (var item in Flatten(group.Items))
                    this.Collect(list, item, tab, group, tab.IsEnabled && group.IsEnabled);
            }
        }

        foreach (var item in this.quickAccess.Where(x => x.IsVisible))
            this.Collect(list, item, null, null, true);

        return list;
    }


    static IEnumerable<RibbonItem> Flatten(IEnumerable<RibbonItem> items)
    {
        foreach (var item in items)
        {
            if (!item.IsVisible)
                continue;

            if (item is RibbonRow row)
            {
                foreach (var inner in Flatten(row.Items))
                    yield return inner;
            }
            else
                yield return item;
        }
    }


    void Collect(List<RibbonCommandInfo> list, RibbonItem item, RibbonTab? tab, RibbonGroup? group, bool enabled)
    {
        var label = !string.IsNullOrWhiteSpace(item.Text) ? item.Text! : item.Tooltip;
        enabled &= item.IsEnabled;

        RibbonCommandInfo Make(string text, Action run, bool on)
            => new(text, run)
            {
                Tooltip = item.Tooltip,
                Description = item.Description,
                Shortcut = item.Shortcut,
                Icon = item.Icon,
                Tab = tab,
                Group = group,
                Item = item,
                IsEnabled = on
            };

        switch (item)
        {
            case RibbonSplitButton split when !string.IsNullOrWhiteSpace(label):
                list.Add(Make(label!, () => this.Invoke(split), enabled));
                this.CollectMenu(list, split.Menu, label!, item, tab, group, enabled);
                break;

            case RibbonMenuButton menu when !string.IsNullOrWhiteSpace(label):
                this.CollectMenu(list, menu.Menu, label!, item, tab, group, enabled);
                break;

            case RibbonButton button when !string.IsNullOrWhiteSpace(label):
                list.Add(Make(label!, () => this.Invoke(button), enabled));
                break;

            case RibbonGallery gallery:
                foreach (var (text, choice) in gallery.Choices())
                {
                    var picked = choice;
                    list.Add(Make($"{label ?? group?.Title} › {text}", () => gallery.Pick(picked), enabled));
                }
                break;
        }
    }


    void CollectMenu(
        List<RibbonCommandInfo> list,
        IEnumerable<RibbonMenuEntry> entries,
        string path,
        RibbonItem item,
        RibbonTab? tab,
        RibbonGroup? group,
        bool enabled)
    {
        foreach (var entry in entries)
        {
            if (!entry.IsVisible || entry.IsSeparator || string.IsNullOrWhiteSpace(entry.Text))
                continue;

            var label = $"{path} › {entry.Text}";

            if (entry.HasChildren)
            {
                this.CollectMenu(list, entry.Children, label, item, tab, group, enabled && entry.IsEnabled);
                continue;
            }

            var picked = entry;
            list.Add(new RibbonCommandInfo(label, () =>
            {
                picked.Invoke();
                this.RefreshStates();
                this.NotifyItemInvoked(item, group, tab);
            })
            {
                Icon = entry.Icon,
                Tab = tab,
                Group = group,
                Item = item,
                IsEnabled = enabled && entry.IsEnabled
            });
        }
    }
}


/// <summary>
/// One command on a ribbon, as a command search sees it — the label, where it lives, its shortcut and
/// a way to run it without the button being on screen.
/// </summary>
/// <remarks>Returned by <see cref="Ribbon.GetCommands"/>.</remarks>
public sealed class RibbonCommandInfo
{
    internal RibbonCommandInfo(string label, Action invoke)
    {
        this.Label = label;
        this.Invoke = invoke;
    }

    /// <summary>The item's label, or its tooltip when it has no label (an icon-only button).</summary>
    public string Label { get; }

    public string? Tooltip { get; init; }

    public string? Description { get; init; }

    public string? Shortcut { get; init; }

    public ImageSource? Icon { get; init; }

    /// <summary>The tab it is on, or null for the quick access row.</summary>
    public RibbonTab? Tab { get; init; }

    public RibbonGroup? Group { get; init; }

    /// <summary>The item it came from — for a dropdown line, the button that owns the dropdown.</summary>
    public RibbonItem? Item { get; init; }

    /// <summary>Whether it could run when the list was taken.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>"Home › Font" — where the command lives, for a search result's second column.</summary>
    public string? Category
        => this.Tab?.Title is not { } tab
            ? this.Group?.Title
            : this.Group?.Title is { } group ? $"{tab} › {group}" : tab;

    /// <summary>Runs the command exactly as pressing its button would.</summary>
    public Action Invoke { get; }
}
