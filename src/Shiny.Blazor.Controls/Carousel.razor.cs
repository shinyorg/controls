using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

public partial class Carousel<TItem> : IAsyncDisposable
{
    ElementReference viewportRef;
    ElementReference containerRef;
    IJSObjectReference? module;
    bool disposed;
    DotNetObjectReference<Carousel<TItem>>? selfRef;
    readonly HashSet<int> loaded = new();
    string? lastOptionSig;
    bool initialized;

    [Inject] IJSRuntime JS { get; set; } = default!;

    // ----- Data / templates -----

    [Parameter] public IReadOnlyList<TItem>? Items { get; set; }
    [Parameter] public RenderFragment<TItem>? ItemTemplate { get; set; }

    /// <summary>Rendered for the thumbnail strip (one per item) when <see cref="ShowThumbnails"/> is set.</summary>
    [Parameter] public RenderFragment<TItem>? ThumbnailTemplate { get; set; }

    /// <summary>Rendered in place of an item that has not yet been lazily loaded.</summary>
    [Parameter] public RenderFragment? PlaceholderTemplate { get; set; }

    /// <summary>Rendered when <see cref="Items"/> is null or empty.</summary>
    [Parameter] public RenderFragment? EmptyTemplate { get; set; }

    // ----- Behaviour -----

    /// <summary>Scroll-linked visual effect applied to each slide.</summary>
    [Parameter] public CarouselEffect Effect { get; set; } = CarouselEffect.None;

    [Parameter] public CarouselOrientation Orientation { get; set; } = CarouselOrientation.Horizontal;

    /// <summary>Where the active slide settles within the viewport.</summary>
    [Parameter] public CarouselAlign Align { get; set; } = CarouselAlign.Center;

    /// <summary>Wrap around past the first/last slide.</summary>
    [Parameter] public bool Loop { get; set; } = true;

    /// <summary>Right-to-left layout and drag direction (horizontal only).</summary>
    [Parameter] public bool Rtl { get; set; }

    /// <summary>Enable pointer drag (mouse + touch).</summary>
    [Parameter] public bool Draggable { get; set; } = true;

    /// <summary>Free-scroll dragging with momentum and no snapping.</summary>
    [Parameter] public bool DragFree { get; set; }

    /// <summary>How many slides advance per arrow/indicator step.</summary>
    [Parameter] public int SlidesToScroll { get; set; } = 1;

    // ----- Auto motion -----

    /// <summary>Auto-advance through the snap points on a timer.</summary>
    [Parameter] public bool AutoPlay { get; set; }

    /// <summary>Milliseconds each snap is shown before auto-advancing.</summary>
    [Parameter] public int AutoPlayInterval { get; set; } = 4000;

    /// <summary>Continuous marquee-style scrolling (distinct from discrete <see cref="AutoPlay"/>).</summary>
    [Parameter] public bool AutoScroll { get; set; }

    /// <summary>Auto-scroll speed in pixels per second.</summary>
    [Parameter] public double AutoScrollSpeed { get; set; } = 40;

    /// <summary>Pause auto motion while the pointer is over the carousel or it has focus.</summary>
    [Parameter] public bool PauseOnHover { get; set; } = true;

    // ----- Lazy loading -----

    /// <summary>Only render item templates within <see cref="LazyLoadBuffer"/> of the current item.</summary>
    [Parameter] public bool LazyLoad { get; set; }

    /// <summary>Number of neighbours on each side of the current item to keep rendered.</summary>
    [Parameter] public int LazyLoadBuffer { get; set; } = 1;

    // ----- Effect tuning -----

    [Parameter] public double FocusedItemScale { get; set; } = 1.0;
    [Parameter] public double UnfocusedItemScale { get; set; } = 0.82;

    /// <summary>Parallax intensity (0 = none) for <see cref="CarouselEffect.Parallax"/>.</summary>
    [Parameter] public double ParallaxFactor { get; set; } = 0.4;

    /// <summary>Minimum opacity for <see cref="CarouselEffect.Opacity"/>.</summary>
    [Parameter] public double MinOpacity { get; set; } = 0.18;

    // ----- Sizing -----

    /// <summary>Number of slides visible at once. When set, slides are sized to fill the viewport.</summary>
    [Parameter] public double? SlidesPerView { get; set; }

    /// <summary>Slides size to their own content instead of a fixed width.</summary>
    [Parameter] public bool VariableWidths { get; set; }

    [Parameter] public double ItemWidth { get; set; } = 280;
    [Parameter] public double ItemHeight { get; set; } = 320;
    [Parameter] public double ItemSpacing { get; set; } = 16;

    /// <summary>Viewport height for vertical carousels.</summary>
    [Parameter] public double ViewportHeight { get; set; } = 320;

    // ----- Chrome -----

    [Parameter] public bool ShowIndicators { get; set; } = true;
    [Parameter] public bool ShowArrows { get; set; } = true;
    [Parameter] public bool ShowCounter { get; set; }
    [Parameter] public bool ShowProgress { get; set; }
    [Parameter] public bool ShowThumbnails { get; set; }
    [Parameter] public string? AriaLabel { get; set; }

    // ----- Binding / events -----

    /// <summary>The selected snap index (two-way bindable).</summary>
    [Parameter] public int CurrentPosition { get; set; }
    [Parameter] public EventCallback<int> CurrentPositionChanged { get; set; }
    [Parameter] public EventCallback<TItem> ItemSelected { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }

    // ----- Derived -----

    int Count => Items?.Count ?? 0;
    int SnapCount { get; set; } = 1;
    bool Horizontal => Orientation == CarouselOrientation.Horizontal;

    string SizeClass => SlidesPerView.HasValue ? "size-spv" : VariableWidths ? "size-var" : "size-fixed";
    string OrientClass => Horizontal ? "orient-h" : "orient-v";
    string EffectClass => "fx-" + Effect.ToString().ToLowerInvariant();

    string RootClasses => string.Join(' ',
        "shiny-carousel", OrientClass, SizeClass, EffectClass,
        Rtl && Horizontal ? "is-rtl" : "",
        Draggable ? "is-draggable" : "").Trim();

    string RootStyle =>
        $"--carousel-item-width:{Css(ItemWidth)}px;--carousel-item-height:{Css(ItemHeight)}px;" +
        $"--carousel-spacing:{Css(ItemSpacing)}px;--carousel-viewport-h:{Css(ViewportHeight)}px;" +
        $"--carousel-spv:{Css(SlidesPerView ?? 1)};" +
        $"--carousel-focused-scale:{Css(FocusedItemScale)};--carousel-unfocused-scale:{Css(UnfocusedItemScale)};";

    static string Css(double d) => d.ToString(CultureInfo.InvariantCulture);

    bool IsLoaded(int index) => !LazyLoad || loaded.Contains(index);

    // ----- Lifecycle -----

    protected override void OnParametersSet()
    {
        if (CurrentPosition < 0)
            CurrentPosition = 0;

        EnsureLoaded(CurrentPosition);
    }

    void EnsureLoaded(int center)
    {
        if (!LazyLoad || Count == 0)
            return;

        for (var d = -LazyLoadBuffer; d <= LazyLoadBuffer; d++)
        {
            var i = center + d;
            if (Loop)
                i = ((i % Count) + Count) % Count;
            if (i >= 0 && i < Count)
                loaded.Add(i);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Count == 0)
            return;

        if (firstRender || module is null)
        {
            if (module is null)
            {
                var loaded = await JS.InvokeAsync<IJSObjectReference>(
                    "import", "./_content/Shiny.Blazor.Controls/carousel.js");
                if (disposed) { await loaded.ReleaseLateAsync(); return; }
                module = loaded;
            }
            selfRef ??= DotNetObjectReference.Create(this);
        }

        var sig = OptionSignature();
        if (!initialized)
        {
            // Mark initialized *before* awaiting init. The JS engine's init invokes
            // OnEngineInit, which calls StateHasChanged and synchronously re-enters
            // OnAfterRenderAsync. If the guard were only set after the await, that
            // re-entry would still see initialized == false and call init again —
            // an unbounded init/render/engine-init loop that pins the WASM thread.
            initialized = true;
            lastOptionSig = sig;
            await module.InvokeVoidAsync("init", viewportRef, containerRef, selfRef, BuildOptions());
        }
        else if (sig != lastOptionSig)
        {
            lastOptionSig = sig;
            await module.InvokeVoidAsync("reInit", viewportRef, containerRef, BuildOptions());
        }
    }

    string OptionSignature() =>
        $"{Effect}|{Orientation}|{Align}|{Loop}|{Rtl}|{Draggable}|{DragFree}|{SlidesToScroll}|" +
        $"{AutoPlay}|{AutoPlayInterval}|{AutoScroll}|{AutoScrollSpeed}|{PauseOnHover}|" +
        $"{FocusedItemScale}|{UnfocusedItemScale}|{ParallaxFactor}|{MinOpacity}|" +
        $"{SlidesPerView}|{VariableWidths}|{Count}";

    CarouselJsOptions BuildOptions() => new()
    {
        Effect = Effect.ToString().ToLowerInvariant(),
        Orientation = Horizontal ? "h" : "v",
        Rtl = Rtl && Horizontal,
        Align = Align.ToString().ToLowerInvariant(),
        Loop = Loop,
        Draggable = Draggable,
        DragFree = DragFree,
        SlidesToScroll = Math.Max(1, SlidesToScroll),
        AutoPlay = AutoPlay,
        Interval = AutoPlayInterval,
        AutoScroll = AutoScroll,
        AutoScrollSpeed = AutoScrollSpeed,
        PauseOnHover = PauseOnHover,
        FocusedScale = FocusedItemScale,
        UnfocusedScale = UnfocusedItemScale,
        Parallax = ParallaxFactor,
        MinOpacity = MinOpacity,
        Count = Count,
        StartIndex = CurrentPosition
    };

    // ----- Navigation -----

    public async Task NextAsync()
    {
        if (module is not null)
            await module.InvokeVoidAsync("scrollNext", viewportRef);
    }

    public async Task PreviousAsync()
    {
        if (module is not null)
            await module.InvokeVoidAsync("scrollPrev", viewportRef);
    }

    /// <summary>Scroll to a snap index.</summary>
    public async Task GoToAsync(int snapIndex)
    {
        if (module is not null)
            await module.InvokeVoidAsync("scrollTo", viewportRef, snapIndex);
    }

    /// <summary>Scroll to the snap that contains a specific item index.</summary>
    public async Task GoToSlideAsync(int slideIndex)
    {
        if (module is not null)
            await module.InvokeVoidAsync("scrollToSlide", viewportRef, slideIndex);
    }

    async Task OnThumbClicked(int index)
    {
        await GoToSlideAsync(index);
        if (ItemSelected.HasDelegate && Items is not null && index < Items.Count)
            await ItemSelected.InvokeAsync(Items[index]);
    }

    async Task OnItemClicked(int index)
    {
        if (Items is null || index >= Items.Count)
            return;
        if (ItemSelected.HasDelegate)
            await ItemSelected.InvokeAsync(Items[index]);
    }

    Task OnKeyDown(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e) => e.Key switch
    {
        "ArrowRight" or "ArrowDown" => NextAsync(),
        "ArrowLeft" or "ArrowUp" => PreviousAsync(),
        "Home" => GoToAsync(0),
        "End" => GoToAsync(SnapCount - 1),
        _ => Task.CompletedTask
    };

    // ----- JS callbacks -----

    /// <summary>Engine measured/built its snap points.</summary>
    [JSInvokable]
    public Task OnEngineInit(int snapCount, int selectedIndex)
    {
        var snaps = Math.Max(1, snapCount);
        var changed = snaps != SnapCount;
        SnapCount = snaps;
        if (CurrentPosition != selectedIndex)
        {
            CurrentPosition = selectedIndex;
            changed = true;
        }
        // Only re-render when something actually changed — an unconditional
        // StateHasChanged here re-enters the render pipeline needlessly.
        if (changed)
            StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>The selected snap changed (drag, settle, autoplay).</summary>
    [JSInvokable]
    public async Task OnSelect(int index)
    {
        if (index == CurrentPosition)
            return;

        CurrentPosition = index;
        EnsureLoaded(index * Math.Max(1, SlidesToScroll));

        if (CurrentPositionChanged.HasDelegate)
            await CurrentPositionChanged.InvokeAsync(index);

        StateHasChanged();
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        if (module is not null)
        {
            try
            {
                await module.InvokeVoidAsync("dispose", viewportRef);
                await module.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
        }
        selfRef?.Dispose();
    }

    sealed class CarouselJsOptions
    {
        public string Effect { get; set; } = "none";
        public string Orientation { get; set; } = "h";
        public bool Rtl { get; set; }
        public string Align { get; set; } = "center";
        public bool Loop { get; set; }
        public bool Draggable { get; set; } = true;
        public bool DragFree { get; set; }
        public int SlidesToScroll { get; set; } = 1;
        public bool AutoPlay { get; set; }
        public int Interval { get; set; }
        public bool AutoScroll { get; set; }
        public double AutoScrollSpeed { get; set; }
        public bool PauseOnHover { get; set; }
        public double FocusedScale { get; set; }
        public double UnfocusedScale { get; set; }
        public double Parallax { get; set; }
        public double MinOpacity { get; set; }
        public int Count { get; set; }
        public int StartIndex { get; set; }
    }
}
