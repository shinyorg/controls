using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using global::Shiny.Controls.Keyframe;
using Shiny.Blazor.Controls.Keyframe.Compilation;

namespace Shiny.Blazor.Controls.Keyframe;

/// <summary>
/// A list whose items animate in, out and into their new places: Appear on first show, Add and Remove
/// as Items changes (a removed item stays in the page until its exit has played), and FLIP moves for
/// everything that shifts. Mode None is the off switch.
/// </summary>
/// <typeparam name="TItem">The item type.</typeparam>
public partial class TransitionGroup<TItem>
{
    [Inject] IJSRuntime JS { get; set; } = default!;

    /// <summary>The items, in order.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<TItem> Items { get; set; } = [];

    /// <summary>Identity: how an item is recognised across changes. Defaults to the item itself.</summary>
    [Parameter] public Func<TItem, object>? Key { get; set; }

    /// <summary>Renders one item.</summary>
    [Parameter] public RenderFragment<TItem>? ItemTemplate { get; set; }

    /// <summary>Keyframes (the default) animates; Platform leaves it to your CSS; None never animates.</summary>
    [Parameter] public ItemTransitionMode Mode { get; set; } = ItemTransitionMode.Keyframes;

    /// <summary>How items animate in the first time the list shows. Defaults to None.</summary>
    [Parameter] public ItemTransition Appear { get; set; } = ItemTransition.None;

    /// <summary>How an added item animates in. Defaults to Fade.</summary>
    [Parameter] public ItemTransition Add { get; set; } = ItemTransition.Fade;

    /// <summary>How a removed item animates out: the preset played backwards. Defaults to Fade.</summary>
    [Parameter] public ItemTransition Remove { get; set; } = ItemTransition.Fade;

    /// <summary>Slide items that shift to their new place (FLIP). Defaults to true.</summary>
    [Parameter] public bool Move { get; set; } = true;

    /// <summary>A custom Appear: a Keyframes written inside, which wins over <see cref="Appear"/>.</summary>
    [Parameter] public RenderFragment? AppearKeyframes { get; set; }

    /// <summary>A custom Add: a Keyframes written inside, which wins over <see cref="Add"/>.</summary>
    [Parameter] public RenderFragment? AddKeyframes { get; set; }

    /// <summary>A custom Remove: a Keyframes written inside (played as written), which wins over <see cref="Remove"/>.</summary>
    [Parameter] public RenderFragment? RemoveKeyframes { get; set; }

    /// <summary>How long the presets run. Defaults to 250ms.</summary>
    [Parameter] public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>The presets' curve. Defaults to CubicOut.</summary>
    [Parameter] public string? Easing { get; set; } = "CubicOut";

    /// <summary>How long a move takes. Defaults to <see cref="Duration"/>.</summary>
    [Parameter] public TimeSpan? MoveDuration { get; set; }

    /// <summary>A move's curve. Defaults to Emphasized.</summary>
    [Parameter] public string? MoveEasing { get; set; } = "Emphasized";

    /// <summary>Added between items that enter (or leave) together, so a batch cascades.</summary>
    [Parameter] public TimeSpan Stagger { get; set; }

    /// <summary>How the list responds to <c>prefers-reduced-motion</c>. Auto changes it at once when it is on.</summary>
    [Parameter] public ReducedMotionBehavior ReducedMotion { get; set; } = ReducedMotionBehavior.Auto;

    [Parameter] public string? Class { get; set; }
    [Parameter] public string? Style { get; set; }
    [Parameter] public string? ItemClass { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    sealed class Entry(string key, TItem item)
    {
        public string Key { get; } = key;
        public TItem Item { get; set; } = item;
        public bool Leaving { get; set; }
        public bool Hidden { get; set; }
    }

    readonly KeyframeTemplateSlot appearSlot = new();
    readonly KeyframeTemplateSlot addSlot = new();
    readonly KeyframeTemplateSlot removeSlot = new();

    List<Entry> entries = [];
    ElementReference container;
    IJSObjectReference? module;
    IJSObjectReference? handle;
    DotNetObjectReference<ItemCallbacks>? callbacks;

    List<string> pendingEnter = [];
    List<string> pendingLeave = [];
    bool pendingMove;
    bool appearing;
    bool rendered;
    bool holdRender;
    bool disposed;

    bool Animates => this.Mode == ItemTransitionMode.Keyframes;

    string CssClass => string.IsNullOrWhiteSpace(this.Class) ? "shiny-transition-group" : "shiny-transition-group " + this.Class;

    string ItemCssClass => string.IsNullOrWhiteSpace(this.ItemClass) ? "shiny-transition-item" : "shiny-transition-item " + this.ItemClass;

    string KeyOf(TItem item)
    {
        var key = this.Key is null ? item : this.Key(item);
        return key switch
        {
            null => "",
            string s => s,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => key.ToString() ?? ""
        };
    }

    protected override bool ShouldRender() => !this.holdRender;

    protected override async Task OnParametersSetAsync()
    {
        var items = this.Items ?? [];

        if (!this.rendered || !this.Animates || this.handle is null)
        {
            this.entries = items.Select(i => new Entry(this.KeyOf(i), i)).ToList();
            this.appearing = !this.rendered && this.Animates && (this.Appear != ItemTransition.None || this.AppearKeyframes is not null);
            return;
        }

        var next = items.Select(i => (Key: this.KeyOf(i), Item: i)).ToList();
        var changed = next.Count != this.entries.Count(e => !e.Leaving) ||
                      next.Select(n => n.Key).Where((k, i) => i >= this.entries.Count || this.entries[i].Key != k || this.entries[i].Leaving).Any();

        if (!changed)
        {
            // Same keys in the same order: just refresh the items themselves.
            foreach (var (entry, item) in this.entries.Zip(next))
                entry.Item = item.Item;
            return;
        }

        // Where everything is on screen right now, before the page changes under it.
        this.holdRender = true;
        try
        {
            await this.handle.InvokeVoidAsync("snapshot");
        }
        catch (Exception ex) when (KeyframeModule.IsGone(ex) || ex is JSException)
        {
        }
        finally
        {
            this.holdRender = false;
        }

        this.Merge(next);
        this.pendingMove = true;
    }

    /// <summary>
    /// The new order, with each leaving item kept where it was so it can animate out in place.
    /// An item that comes back while it is leaving simply stays.
    /// </summary>
    void Merge(List<(string Key, TItem Item)> next)
    {
        var old = this.entries;
        var oldByKey = old.ToDictionary(e => e.Key);
        var nextKeys = next.Select(n => n.Key).ToHashSet();

        var merged = next.Select(n =>
        {
            if (oldByKey.TryGetValue(n.Key, out var existing))
            {
                existing.Item = n.Item;
                var wasLeaving = existing.Leaving;
                existing.Leaving = false;
                existing.Hidden = false;
                if (wasLeaving)
                    this.pendingEnter.Add(n.Key);
                return existing;
            }

            this.pendingEnter.Add(n.Key);
            return new Entry(n.Key, n.Item) { Hidden = true };
        }).ToList();

        for (var i = 0; i < old.Count; i++)
        {
            var entry = old[i];
            var gone = !nextKeys.Contains(entry.Key);
            if (gone)
            {
                if (!entry.Leaving)
                    this.pendingLeave.Add(entry.Key);
                entry.Leaving = true;
                merged.Insert(Math.Min(i, merged.Count), entry);
            }
        }

        this.entries = merged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            this.rendered = true;
            if (!this.Animates)
                return;

            await this.AttachAsync();
            if (this.appearing && this.handle is not null)
            {
                this.appearing = false;
                await this.UpdateAsync(this.entries.Select(e => e.Key).ToList(), [], appear: true);
            }
            return;
        }

        if (this.handle is null || !this.pendingMove)
        {
            // A change that needed no JS (or Mode None/Platform) still has to drop removed items.
            this.DropLeaving();
            return;
        }

        var entering = this.pendingEnter;
        var leaving = this.pendingLeave;
        this.pendingEnter = [];
        this.pendingLeave = [];
        this.pendingMove = false;
        await this.UpdateAsync(entering, leaving, appear: false);
    }

    void DropLeaving()
    {
        if (this.Animates && this.handle is not null)
            return;

        var removed = this.entries.RemoveAll(e => e.Leaving);
        if (removed > 0)
            this.StateHasChanged();
    }

    async Task AttachAsync()
    {
        this.module = await KeyframeModule.GetAsync(this.JS);
        if (this.module is null || this.disposed)
            return;

        try
        {
            this.callbacks = DotNetObjectReference.Create(new ItemCallbacks(this.OnItemLeftAsync));
            this.handle = await this.module.InvokeAsync<IJSObjectReference>("attachTransitionGroup", this.container, this.callbacks);
        }
        catch (Exception ex) when (KeyframeModule.IsGone(ex) || ex is JSException)
        {
            this.handle = null;
        }

        if (this.disposed)
            await this.ReleaseAsync();
    }

    async Task UpdateAsync(List<string> entering, List<string> leaving, bool appear)
    {
        if (this.handle is null)
            return;

        var enter = appear ? this.Program(this.appearSlot, this.Appear, exit: false) : this.Program(this.addSlot, this.Add, exit: false);
        var leave = this.Program(this.removeSlot, this.Remove, exit: true);
        var moveMs = this.Move ? (this.MoveDuration ?? this.Duration).TotalMilliseconds : 0d;

        try
        {
            await this.handle.InvokeVoidAsync(
                "update",
                entering.ToArray(),
                leaving.ToArray(),
                enter,
                leave,
                moveMs,
                EasingSyntax.ToCss(this.MoveEasing ?? this.Easing),
                this.Stagger.TotalMilliseconds,
                ReducedMotionText(this.ReducedMotion));
        }
        catch (Exception ex) when (KeyframeModule.IsGone(ex) || ex is JSException)
        {
            // Without the script the list must still settle: removed items go now.
            this.entries.RemoveAll(e => e.Leaving);
        }
    }

    internal static string ReducedMotionText(ReducedMotionBehavior behavior) => behavior switch
    {
        ReducedMotionBehavior.Play => "play",
        ReducedMotionBehavior.Instant => "instant",
        _ => "auto"
    };

    string? Program(KeyframeTemplateSlot slot, ItemTransition preset, bool exit)
    {
        if (slot.Template is { } template)
            return ProgramWriter.WriteSingle(ItemTransitionPresets.Compile(template));

        if (preset == ItemTransition.None)
            return null;

        var tracks = exit ? ItemTransitionPresets.Exit(preset) : ItemTransitionPresets.Enter(preset);
        return ProgramWriter.WriteSingle(ItemTransitionPresets.Compile(tracks, this.Duration, this.Easing));
    }

    async Task OnItemLeftAsync(string key)
    {
        if (this.disposed)
            return;

        await this.InvokeAsync(() =>
        {
            var removed = this.entries.RemoveAll(e => e.Key == key && e.Leaving);
            if (removed > 0)
                this.StateHasChanged();
        });
    }

    async Task ReleaseAsync()
    {
        var current = this.handle;
        this.handle = null;
        if (current is not null)
        {
            try
            {
                await current.InvokeVoidAsync("dispose");
            }
            catch (Exception ex) when (KeyframeModule.IsGone(ex) || ex is JSException)
            {
            }

            await current.DisposeQuietlyAsync();
        }

        this.callbacks?.Dispose();
        this.callbacks = null;
    }

    public async ValueTask DisposeAsync()
    {
        this.disposed = true;
        await this.ReleaseAsync();
        GC.SuppressFinalize(this);
    }
}


/// <summary>The .NET end of the item-left callback.</summary>
internal sealed class ItemCallbacks(Func<string, Task> left)
{
    [JSInvokable]
    public Task OnItemLeft(string key) => left(key);
}
