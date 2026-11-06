using System.Runtime.CompilerServices;

namespace Shiny.Maui.Controls;

/// <summary>
/// Runs <see cref="PageTransition.EntranceProperty"/>: a layout's children animate in one after
/// another when their page appears. Plain MAUI animation, so it behaves the same on every head and
/// needs no platform hook - <c>UsePageTransitions</c> is not required for it.
/// </summary>
static class PageEntrance
{
    sealed class State
    {
        public Page? Page;
        public bool Played;
        public bool Playing;
    }

    static readonly ConditionalWeakTable<Layout, State> states = new();


    internal static void OnEntranceChanged(BindableObject bindable, PageEntranceAnimation value)
    {
        if (bindable is not Layout layout)
            return;

        if (value == PageEntranceAnimation.None)
        {
            layout.Loaded -= OnLoaded;
            layout.ChildAdded -= OnChildAdded;
            foreach (var child in layout.Children.OfType<VisualElement>())
                Reset(child);
            return;
        }

        states.GetOrCreateValue(layout);
        layout.Loaded -= OnLoaded;
        layout.Loaded += OnLoaded;
        layout.ChildAdded -= OnChildAdded;
        layout.ChildAdded += OnChildAdded;

        // Hidden from the start, so the first frame the page draws does not show the content in
        // place for an instant before it animates in.
        foreach (var child in layout.Children.OfType<VisualElement>())
            Prepare(child, value);
    }


    static void OnChildAdded(object? sender, ElementEventArgs e)
    {
        if (sender is not Layout layout || e.Element is not VisualElement child)
            return;

        var state = states.GetOrCreateValue(layout);
        if (!state.Played || state.Playing)
            Prepare(child, PageTransition.GetEntrance(layout));
    }


    static void OnLoaded(object? sender, EventArgs e)
    {
        if (sender is not Layout layout)
            return;

        var state = states.GetOrCreateValue(layout);
        var page = FindPage(layout);
        if (page != state.Page)
        {
            if (state.Page is not null)
                state.Page.Appearing -= OnPageAppearing;

            state.Page = page;
            if (page is not null)
                page.Appearing += OnPageAppearing;
        }

        // Loaded comes after the page has already appeared the first time.
        _ = Play(layout, state);
    }


    static void OnPageAppearing(object? sender, EventArgs e)
    {
        if (sender is not Page page)
            return;

        foreach (var layout in FindEntranceLayouts(page))
        {
            var state = states.GetOrCreateValue(layout);
            if (state.Played && !PageTransition.GetEntranceReplay(layout))
                continue;

            state.Played = false;
            foreach (var child in layout.Children.OfType<VisualElement>())
                Prepare(child, PageTransition.GetEntrance(layout));

            _ = Play(layout, state);
        }
    }


    static async Task Play(Layout layout, State state)
    {
        if (state.Played || state.Playing)
            return;

        var animation = PageTransition.GetEntrance(layout);
        if (animation == PageEntranceAnimation.None)
            return;

        state.Playing = true;
        try
        {
            var delay = Math.Max(0, PageTransition.GetEntranceDelay(layout));
            var stagger = Math.Max(0, PageTransition.GetEntranceStagger(layout));
            var tasks = new List<Task>();
            var index = 0;

            foreach (var child in layout.Children.OfType<VisualElement>().ToList())
                tasks.Add(Animate(child, delay + stagger * index++));

            await Task.WhenAll(tasks);
        }
        finally
        {
            state.Playing = false;
            state.Played = true;
        }
    }


    static async Task Animate(VisualElement child, int delay)
    {
        if (delay > 0)
            await Task.Delay(delay);

        const uint length = 380;
        await Task.WhenAll(
            child.FadeToAsync(1, length, Easing.CubicOut),
            child.TranslateToAsync(0, 0, length, Easing.CubicOut),
            child.ScaleToAsync(1, length, Easing.CubicOut)
        );
    }


    static void Prepare(VisualElement child, PageEntranceAnimation animation)
    {
        child.AbortAnimation("FadeTo");
        child.AbortAnimation("TranslateTo");
        child.AbortAnimation("ScaleTo");
        Reset(child);

        child.Opacity = 0;
        switch (animation)
        {
            case PageEntranceAnimation.FadeUp:
                child.TranslationY = 24;
                break;

            case PageEntranceAnimation.SlideIn:
                child.TranslationX = child.FlowDirection == FlowDirection.RightToLeft ? 32 : -32;
                break;

            case PageEntranceAnimation.Scale:
                child.Scale = 0.92;
                break;
        }
    }


    static void Reset(VisualElement child)
    {
        child.Opacity = 1;
        child.TranslationX = 0;
        child.TranslationY = 0;
        child.Scale = 1;
    }


    static Page? FindPage(Element element)
    {
        for (var e = element.Parent; e is not null; e = e.Parent)
        {
            if (e is Page page)
                return page;
        }
        return null;
    }


    static IEnumerable<Layout> FindEntranceLayouts(IVisualTreeElement element)
    {
        if (element is Layout layout && PageTransition.GetEntrance(layout) != PageEntranceAnimation.None)
            yield return layout;

        foreach (var child in element.GetVisualChildren())
        {
            // A nested page has its own Appearing.
            if (child is Page)
                continue;

            foreach (var found in FindEntranceLayouts(child))
                yield return found;
        }
    }
}
