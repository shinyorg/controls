using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

public partial class ParallaxList<TItem> : IAsyncDisposable
{
    ElementReference scrollRef;
    ElementReference heroRef;
    IJSObjectReference? module;
    bool disposed;
    DotNetObjectReference<ParallaxList<TItem>>? dotnetRef;
    bool initialized;
    (double Factor, double Header, double MinHeader, bool Collapse, bool Fade) pushedOptions;

    [Parameter] public IReadOnlyList<TItem>? Items { get; set; }
    [Parameter] public RenderFragment<TItem>? ItemTemplate { get; set; }
    [Parameter] public RenderFragment? HeroTemplate { get; set; }
    [Parameter] public RenderFragment? EmptyTemplate { get; set; }

    [Parameter] public double HeaderHeight { get; set; } = 240;
    [Parameter] public double MinHeaderHeight { get; set; } = 0;
    [Parameter] public double ParallaxFactor { get; set; } = 0.5;
    [Parameter] public bool CollapseToSticky { get; set; } = false;
    [Parameter] public bool FadeHeaderOnScroll { get; set; } = false;
    [Parameter] public string? Height { get; set; }
    [Parameter] public string? CssClass { get; set; }

    [Parameter] public EventCallback<TItem> ItemSelected { get; set; }
    [Parameter] public EventCallback<ParallaxScrollEventArgs> Scrolled { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }

    string ContainerStyle => Height is null
        ? string.Empty
        : $"height:{Height};";

    string HeroStyle =>
        $"height:{HeaderHeight}px;" +
        (CollapseToSticky ? $"min-height:{MinHeaderHeight}px;" : string.Empty);

    async Task OnItemClicked(TItem item)
    {
        if (ItemSelected.HasDelegate)
            await ItemSelected.InvokeAsync(item);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var options = (ParallaxFactor, HeaderHeight, MinHeaderHeight, CollapseToSticky, FadeHeaderOnScroll);

        if (firstRender)
        {
            pushedOptions = options;
            var loaded = await JS.InvokeAsync<IJSObjectReference>(
                "import", "./_content/Shiny.Blazor.Controls/parallax-list.js");
            if (disposed) { await loaded.ReleaseLateAsync(); return; }
            module = loaded;
            dotnetRef = DotNetObjectReference.Create(this);
            await module.InvokeVoidAsync("init", scrollRef, heroRef, dotnetRef, BuildJsOptions());
            initialized = true;
        }
        else if (initialized && module is not null && options != pushedOptions)
        {
            // only push when an option actually changed — pushing on every render
            // re-enters update() which raises Scrolled, re-rendering forever
            pushedOptions = options;
            await module.InvokeVoidAsync("update_", scrollRef, BuildJsOptions());
        }
    }

    // a named DTO, not an anonymous type: trimmed/AOT publish strips anonymous-type
    // constructor parameter names, which the JS interop serializer requires
    ParallaxJsOptions BuildJsOptions() => new()
    {
        Factor = ParallaxFactor,
        HeaderHeight = HeaderHeight,
        MinHeaderHeight = MinHeaderHeight,
        Collapse = CollapseToSticky,
        Fade = FadeHeaderOnScroll
    };

    sealed class ParallaxJsOptions
    {
        public double Factor { get; set; }
        public double HeaderHeight { get; set; }
        public double MinHeaderHeight { get; set; }
        public bool Collapse { get; set; }
        public bool Fade { get; set; }
    }

    [JSInvokable]
    public Task OnScrollFromJs(double offset, double translation, double visibleHeight)
    {
        if (Scrolled.HasDelegate)
            return Scrolled.InvokeAsync(new ParallaxScrollEventArgs(offset, translation, visibleHeight));
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        try
        {
            if (module is not null)
            {
                await module.InvokeVoidAsync("dispose", scrollRef);
                await module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException) { }
        dotnetRef?.Dispose();
    }
}

public class ParallaxScrollEventArgs : EventArgs
{
    public ParallaxScrollEventArgs(double verticalOffset, double headerTranslation, double headerVisibleHeight)
    {
        VerticalOffset = verticalOffset;
        HeaderTranslation = headerTranslation;
        HeaderVisibleHeight = headerVisibleHeight;
    }

    public double VerticalOffset { get; }
    public double HeaderTranslation { get; }
    public double HeaderVisibleHeight { get; }
}
