using System.Diagnostics;
using Shiny.Controls.Office.Text;

namespace Shiny.Controls.Office.Presentation;

/// <summary>What a slide show is showing instead of the slide — PowerPoint's B and W keys.</summary>
public enum SlideShowScreen
{
    Slide,
    Black,
    White
}

/// <summary>
/// How one shape looks at this instant of a show: whether it is there, how opaque, and where.
/// </summary>
/// <param name="Visible">False before an entrance plays and after an exit has.</param>
/// <param name="Opacity">0-1.</param>
/// <param name="OffsetX">Displacement in slide units, for a fly.</param>
/// <param name="OffsetY">Displacement in slide units.</param>
/// <param name="Scale">Size about the shape's centre, 1 at rest.</param>
/// <param name="Rotation">Extra rotation in degrees.</param>
/// <param name="Reveal">For a wipe: how much of the shape shows, 0-1. 1 at rest.</param>
/// <param name="RevealFrom">Which edge a wipe reveals from.</param>
public readonly record struct ShapeAnimationState(
    bool Visible,
    double Opacity,
    double OffsetX,
    double OffsetY,
    double Scale,
    double Rotation,
    double Reveal,
    SlideTransitionDirection RevealFrom)
{
    public static readonly ShapeAnimationState Rest = new(true, 1, 0, 0, 1, 0, 1, SlideTransitionDirection.FromBottom);

    public static readonly ShapeAnimationState Hidden = Rest with { Visible = false };

    public bool IsRest => this == Rest;
}

/// <summary>Everything a host needs to paint one frame of a show.</summary>
/// <param name="SlideIndex">The deck index of the slide on screen.</param>
/// <param name="Slide">The slide on screen.</param>
/// <param name="Previous">The slide being transitioned away from, while a transition runs.</param>
/// <param name="Transition">The incoming slide's transition, while it runs.</param>
/// <param name="TransitionProgress">0-1 through the transition; 1 when none is running.</param>
/// <param name="Shapes">Animated shapes' states, by index into <see cref="Slide"/>'s shapes. Absent means at rest.</param>
public sealed record SlideShowFrame(
    int SlideIndex,
    Slide Slide,
    Slide? Previous,
    SlideTransition? Transition,
    double TransitionProgress,
    IReadOnlyDictionary<int, ShapeAnimationState> Shapes)
{
    public SlideShowScreen Screen { get; init; }

    /// <summary>Past the last slide: the black "End of slide show" screen.</summary>
    public bool IsEnd { get; init; }

    /// <summary>The shape states of the slide being left, while a transition runs.</summary>
    public IReadOnlyDictionary<int, ShapeAnimationState> PreviousShapes { get; init; } = new Dictionary<int, ShapeAnimationState>();
}

/// <summary>
/// Runs a slide show: which slide, which click of its animations, the transition in progress, the
/// clock, and the black/white screens.
/// </summary>
/// <remarks>
/// <para>
/// Host-independent: it never draws and never starts a timer. A host asks for a <see cref="Frame"/>
/// whenever it paints and keeps painting while <see cref="IsAnimating"/> — and calls <see cref="Tick"/>
/// on the same beat, which is what advances a slide timed to move on by itself.
/// </para>
/// <para>
/// Hidden slides are skipped, as in PowerPoint, except when the show is started on one: starting from
/// a hidden slide plays it, because that is what the person pressing the button is looking at.
/// </para>
/// </remarks>
public sealed class SlideShowController
{
    readonly SlideDeck deck;
    readonly Func<TimeSpan> clock;
    readonly TimeSpan started;

    int index;
    int click;
    TimeSpan clickStarted;
    TimeSpan slideStarted;
    TimeSpan transitionStarted;
    int? previousIndex;
    int previousClick;
    bool atEnd;
    TimeSpan pausedAt;
    TimeSpan pausedTotal;
    bool paused;

    /// <summary>Starts a show at <paramref name="startSlide"/>.</summary>
    /// <param name="clock">A time source, for tests; the real one is a stopwatch.</param>
    public SlideShowController(SlideDeck deck, int startSlide = 0, Func<TimeSpan>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(deck);

        this.deck = deck;
        if (clock is null)
        {
            var stopwatch = Stopwatch.StartNew();
            clock = () => stopwatch.Elapsed;
        }

        this.clock = clock;
        this.started = this.clock();
        this.index = Math.Clamp(startSlide, 0, Math.Max(0, deck.Slides.Count - 1));
        this.EnterSlide(this.index, transition: false);
    }

    public SlideDeck Deck => this.deck;

    /// <summary>The deck index of the slide on screen.</summary>
    public int SlideIndex => this.index;

    /// <summary>The slide on screen, or null for an empty deck.</summary>
    public Slide? Current => this.deck.Slides.ElementAtOrDefault(this.index);

    /// <summary>The click the slide's animations are at: 0 before the first press.</summary>
    public int Click => this.click;

    /// <summary>Past the last slide, on the end screen.</summary>
    public bool IsAtEnd => this.atEnd;

    public SlideShowScreen Screen { get; private set; }

    /// <summary>Raised when the slide, the click or the screen changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when the show should end — past the end screen, or a link to "End Show".</summary>
    public event EventHandler? Ended;

    /// <summary>The slides the show plays, in order: hidden ones are left out.</summary>
    public List<int> Order
        => Enumerable.Range(0, this.deck.Slides.Count).Where(i => !this.deck.Slides[i].IsHidden || i == this.index).ToList();

    /// <summary>One-based position in <see cref="Order"/> and how many there are — the presenter's "3 of 12".</summary>
    public (int Position, int Count) Progress
    {
        get
        {
            var order = this.Order;
            return (order.IndexOf(this.index) + 1, order.Count);
        }
    }

    /// <summary>The slide the next advance will show, or null at the last — for the presenter's preview.</summary>
    public int? NextSlideIndex
    {
        get
        {
            var order = this.Order;
            var at = order.IndexOf(this.index);
            return at >= 0 && at + 1 < order.Count ? order[at + 1] : null;
        }
    }

    /// <summary>Time since the show started, less any pause — the presenter view's timer.</summary>
    public TimeSpan Elapsed => (this.paused ? this.pausedAt : this.clock()) - this.started - this.pausedTotal;

    public bool IsPaused => this.paused;

    public void TogglePause()
    {
        if (this.paused)
        {
            this.pausedTotal += this.clock() - this.pausedAt;
            this.paused = false;
        }
        else
        {
            this.pausedAt = this.clock();
            this.paused = true;
        }

        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---- stepping ----

    /// <summary>
    /// The next thing: the slide's next click of animations, or the next slide.
    /// </summary>
    /// <param name="fromClick">
    /// True for a mouse click or tap. A slide whose transition turns off "On Mouse Click" is not
    /// advanced by one, though its animations still are; the keyboard always advances.
    /// </param>
    /// <returns>False when the show is over.</returns>
    public bool Next(bool fromClick = false)
    {
        if (this.Screen != SlideShowScreen.Slide)
        {
            // B/W take the first press to come back, as in PowerPoint.
            this.SetScreen(SlideShowScreen.Slide);
            return true;
        }

        if (this.atEnd)
        {
            this.Ended?.Invoke(this, EventArgs.Empty);
            return false;
        }

        if (this.Current is { } slide && this.click < this.Clicks(slide))
        {
            // A click still running its effects finishes them first, rather than being skipped.
            this.click++;
            this.clickStarted = this.clock();
            this.Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        if (fromClick && this.Current?.Transition is { AdvanceOnClick: false })
            return true;

        return this.Advance();
    }

    /// <summary>Back one click, or to the previous slide with all its animations played.</summary>
    public bool Previous()
    {
        if (this.Screen != SlideShowScreen.Slide)
        {
            this.SetScreen(SlideShowScreen.Slide);
            return true;
        }

        if (this.atEnd)
        {
            this.atEnd = false;
            this.Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        if (this.click > this.FirstClick(this.Current))
        {
            this.click--;

            // Stepping back shows the state at the end of the earlier click, not a replay of it.
            this.clickStarted = this.clock() - TimeSpan.FromDays(1);
            this.Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        var order = this.Order;
        var at = order.IndexOf(this.index);
        if (at <= 0)
            return false;

        this.EnterSlide(order[at - 1], transition: false);
        if (this.Current is { } slide)
        {
            this.click = this.Clicks(slide);
            this.clickStarted = this.clock() - TimeSpan.FromDays(1);
        }

        this.Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Jumps to a slide — a thumbnail in the presenter view, a slide link, a typed number.</summary>
    public void GoTo(int slideIndex, bool transition = true)
    {
        if (slideIndex < 0 || slideIndex >= this.deck.Slides.Count)
            return;

        this.atEnd = false;
        this.EnterSlide(slideIndex, transition);
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    public void First() => this.GoTo(this.Order.FirstOrDefault());

    public void Last() => this.GoTo(this.Order.LastOrDefault());

    /// <summary>Shows a black or white screen in place of the slide, or the slide again.</summary>
    public void SetScreen(SlideShowScreen screen)
    {
        if (this.Screen == screen)
            return;

        this.Screen = screen;
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleBlack() => this.SetScreen(this.Screen == SlideShowScreen.Black ? SlideShowScreen.Slide : SlideShowScreen.Black);

    public void ToggleWhite() => this.SetScreen(this.Screen == SlideShowScreen.White ? SlideShowScreen.Slide : SlideShowScreen.White);

    bool Advance()
    {
        var order = this.Order;
        var at = order.IndexOf(this.index);

        if (at < 0 || at + 1 >= order.Count)
        {
            // Past the last slide: the end screen, then out on the next press.
            this.atEnd = true;
            this.Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        this.EnterSlide(order[at + 1], transition: true);
        this.Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    void EnterSlide(int slide, bool transition)
    {
        var now = this.clock();

        this.previousIndex = transition && this.deck.Slides.Count > 0 ? this.index : null;
        this.previousClick = this.click;
        this.index = slide;
        this.slideStarted = now;
        this.transitionStarted = now;

        if (!transition || this.Current?.Transition is not { Kind: not SlideTransitionKind.None } incoming || incoming.Duration <= TimeSpan.Zero)
            this.previousIndex = null;

        // A slide whose first effects start "with previous" plays them on arrival, after the transition.
        this.click = this.FirstClick(this.Current);
        this.clickStarted = now + this.TransitionLength();
    }

    int FirstClick(Slide? slide)
        => 0;

    int Clicks(Slide slide) => SlideAnimationTimeline.ClickCount(slide.Animations);

    TimeSpan TransitionLength()
        => this.previousIndex is not null && this.Current?.Transition is { } transition ? transition.Duration : TimeSpan.Zero;

    // ---- the clock ----

    /// <summary>
    /// True while something is moving — a transition, an animation, or a timed advance still to come.
    /// </summary>
    /// <remarks>A host keeps redrawing and calling <see cref="Tick"/> while this holds.</remarks>
    public bool IsAnimating
    {
        get
        {
            var now = this.clock();

            if (this.previousIndex is not null && now - this.transitionStarted < this.TransitionLength())
                return true;

            if (this.Current is { } slide)
            {
                var elapsed = now - this.clickStarted;
                if (SlideAnimationTimeline.Schedule(slide.Animations).Any(x => x.Click == this.click && x.End > elapsed - TimeSpan.FromMilliseconds(50)))
                    return true;

                if (slide.Transition?.AdvanceAfter is not null && !this.atEnd)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Moves a timed slide on when its time is up. Returns true when something changed.
    /// </summary>
    /// <remarks>
    /// "After" counts from when the slide arrived; the slide only goes once every click of its
    /// animations has been played, so a timed slide with click animations still waits for the clicks.
    /// </remarks>
    public bool Tick()
    {
        if (this.paused || this.atEnd || this.Screen != SlideShowScreen.Slide || this.Current is not { } slide)
            return false;

        if (slide.Transition?.AdvanceAfter is not { } after)
            return false;

        var now = this.clock();
        if (now - this.slideStarted < after + this.TransitionLength() || this.click < this.Clicks(slide))
            return false;

        var clickEnd = SlideAnimationTimeline.Schedule(slide.Animations).Where(x => x.Click == this.click).Select(x => x.End).DefaultIfEmpty(TimeSpan.Zero).Max();
        if (now - this.clickStarted < clickEnd)
            return false;

        return this.Advance();
    }

    /// <summary>The frame to paint now.</summary>
    public SlideShowFrame? Frame()
    {
        if (this.Current is not { } slide)
            return null;

        var now = this.clock();
        var length = this.TransitionLength();
        var progress = length <= TimeSpan.Zero ? 1 : Math.Clamp((now - this.transitionStarted) / length, 0, 1);

        var previous = progress < 1 && this.previousIndex is { } p ? this.deck.Slides.ElementAtOrDefault(p) : null;

        return new SlideShowFrame(
            this.index,
            slide,
            previous,
            previous is null ? null : slide.Transition,
            previous is null ? 1 : progress,
            ShapeStates(slide, this.click, now - this.clickStarted))
        {
            Screen = this.Screen,
            IsEnd = this.atEnd,
            PreviousShapes = previous is null ? new Dictionary<int, ShapeAnimationState>() : ShapeStates(previous, int.MaxValue, TimeSpan.FromDays(1))
        };
    }

    /// <summary>
    /// Each animated shape's state at click <paramref name="click"/>, <paramref name="elapsed"/> into it.
    /// </summary>
    /// <remarks>
    /// Effects are applied in play order: a shape with an entrance is hidden until it starts, an exit
    /// hides it once done, and an emphasis in between transforms it. Earlier clicks are finished, later
    /// ones not started.
    /// </remarks>
    public static IReadOnlyDictionary<int, ShapeAnimationState> ShapeStates(Slide slide, int click, TimeSpan elapsed)
    {
        var result = new Dictionary<int, ShapeAnimationState>();
        if (slide.Animations.Count == 0)
            return result;

        var byId = new Dictionary<uint, List<int>>();
        for (var i = 0; i < slide.Shapes.Count; i++)
        {
            var shape = slide.Shapes[i];
            if (shape.Id == 0 || !shape.IsEditable)
                continue;

            if (!byId.TryGetValue(shape.Id, out var list))
                byId[shape.Id] = list = [];

            list.Add(i);
        }

        // A group's children move with it: map the group's id to every shape inside it.
        for (var i = 0; i < slide.Shapes.Count; i++)
        {
            if (slide.Shapes[i] is not { IsGroup: true, Element: { } group } groupShape)
                continue;

            for (var j = 0; j < slide.Shapes.Count; j++)
            {
                if (slide.Shapes[j].Element is { } child && child.Ancestors().Any(x => ReferenceEquals(x, group)))
                    byId.GetValueOrDefault(groupShape.Id)?.Add(j);
            }
        }

        var schedule = SlideAnimationTimeline.Schedule(slide.Animations);
        var firstSeen = new HashSet<uint>();

        foreach (var item in schedule)
        {
            if (!byId.TryGetValue(item.Animation.ShapeId, out var targets))
                continue;

            var progress = item.Click < click ? 1
                : item.Click > click ? 0
                : item.Animation.Duration <= TimeSpan.Zero ? (elapsed >= item.Start ? 1 : 0)
                : Math.Clamp((elapsed - item.Start) / item.Animation.Duration, 0, 1);

            var started = item.Click < click || (item.Click == click && elapsed >= item.Start);
            var klass = item.Animation.Effect == SlideAnimationEffect.Other ? item.Animation.ReadClass ?? SlideAnimationClass.Entrance : item.Animation.Class;

            foreach (var target in targets)
            {
                var state = result.TryGetValue(target, out var existing) ? existing : ShapeAnimationState.Rest;

                // The first effect a shape has decides how it starts: an entrance means it is not
                // there until the entrance plays.
                if (firstSeen.Add(item.Animation.ShapeId) && klass == SlideAnimationClass.Entrance && !started)
                    state = ShapeAnimationState.Hidden;

                state = Apply(state, item.Animation, klass, progress, started, slide, targets.Count > 0 ? slide.Shapes[target] : null);
                result[target] = state;
            }
        }

        foreach (var key in result.Where(x => x.Value.IsRest).Select(x => x.Key).ToList())
            result.Remove(key);

        return result;
    }

    static ShapeAnimationState Apply(ShapeAnimationState state, SlideAnimation animation, SlideAnimationClass klass, double p, bool started, Slide slide, SlideShape? shape)
    {
        var eased = Ease(p);

        switch (klass)
        {
            case SlideAnimationClass.Entrance:
                if (!started)
                    return state;

                state = state with { Visible = true };
                return animation.Effect switch
                {
                    SlideAnimationEffect.Fade or SlideAnimationEffect.Other => state with { Opacity = eased },
                    SlideAnimationEffect.FlyIn => state with { OffsetX = FlyOffset(animation.Direction, shape).X * (1 - eased), OffsetY = FlyOffset(animation.Direction, shape).Y * (1 - eased) },
                    SlideAnimationEffect.Wipe => state with { Reveal = eased, RevealFrom = animation.Direction },
                    SlideAnimationEffect.Zoom => state with { Scale = Math.Max(0.01, eased), Opacity = Math.Min(1, eased * 2) },
                    _ => state
                };

            case SlideAnimationClass.Exit:
                if (!started)
                    return state;

                if (p >= 1)
                    return state with { Visible = false };

                return animation.Effect switch
                {
                    SlideAnimationEffect.Disappear => state with { Visible = false },
                    SlideAnimationEffect.FadeOut or SlideAnimationEffect.Other => state with { Opacity = 1 - eased },
                    SlideAnimationEffect.FlyOut => state with { OffsetX = FlyOffset(animation.Direction, shape).X * eased, OffsetY = FlyOffset(animation.Direction, shape).Y * eased },
                    _ => state
                };

            default:
                if (!started)
                    return state;

                return animation.Effect switch
                {
                    SlideAnimationEffect.GrowShrink => state with { Scale = state.Scale * (1 + 0.5 * eased) },
                    SlideAnimationEffect.Spin => state with { Rotation = state.Rotation + 360 * eased },
                    SlideAnimationEffect.Pulse => state with { Scale = state.Scale * (1 + 0.08 * Math.Sin(Math.PI * p)) },
                    _ => state
                };
        }

        // Far enough to be off the slide from wherever the shape sits: the slide's own size plus the shape.
        (double X, double Y) FlyOffset(SlideTransitionDirection direction, SlideShape? target)
        {
            var width = (slide.Shapes.Count > 0 ? slide.Shapes.Max(x => x.X + x.Width) : 960) + (target?.Width ?? 0);
            var height = (slide.Shapes.Count > 0 ? slide.Shapes.Max(x => x.Y + x.Height) : 540) + (target?.Height ?? 0);

            return direction switch
            {
                SlideTransitionDirection.FromTop => (0, -height),
                SlideTransitionDirection.FromLeft => (-width, 0),
                SlideTransitionDirection.FromRight => (width, 0),
                _ => (0, height)
            };
        }
    }

    /// <summary>Ease-in-out, which is how PowerPoint's default smoothing reads.</summary>
    static double Ease(double t) => t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t);

    // ---- links ----

    /// <summary>
    /// The link under a point in slide coordinates — a linked run of text first, then a linked shape.
    /// </summary>
    public SlideHyperlink? HyperlinkAt(double slideX, double slideY, ITextMeasurer measurer)
    {
        if (this.Current is not { } slide)
            return null;

        var states = ShapeStates(slide, this.click, this.clock() - this.clickStarted);

        for (var i = slide.Shapes.Count - 1; i >= 0; i--)
        {
            var shape = slide.Shapes[i];
            if (states.TryGetValue(i, out var state) && !state.Visible)
                continue;

            if (slideX < shape.X || slideX > shape.X + shape.Width || slideY < shape.Y || slideY > shape.Y + shape.Height)
                continue;

            if (shape.Text is { } text && LinkInText(text, shape, slideX - shape.X, slideY - shape.Y, measurer) is { } textLink)
                return textLink;

            if (shape.Hyperlink is { } link)
                return link;
        }

        return null;
    }

    /// <summary>The media shape under a point, for a host that plays clips on click.</summary>
    public SlideShape? MediaAt(double slideX, double slideY)
        => this.Current?.Shapes.LastOrDefault(x => x.Media is not null &&
            slideX >= x.X && slideX <= x.X + x.Width && slideY >= x.Y && slideY <= x.Y + x.Height);

    static SlideHyperlink? LinkInText(ShapeTextBody body, SlideShape shape, double x, double y, ITextMeasurer measurer)
    {
        var layout = ShapeTextLayout.Layout(body, shape.Width, shape.Height, measurer);
        foreach (var block in layout.Paragraphs)
        {
            foreach (var line in block.Lines)
            {
                var top = layout.Top + block.Y + line.Y;
                if (y < top || y > top + line.Height)
                    continue;

                foreach (var run in line.Runs)
                {
                    var left = layout.Left + block.Indent + run.X;
                    if (x >= left && x <= left + run.Width && run.Style.Link is { } link)
                        return SlideHyperlinkCodec.Decode(link);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Follows a link inside the show: a slide jump or a show action moves the show. Returns the address
    /// for an external link, which the host opens; null when there is nothing left for the host to do.
    /// </summary>
    public string? Follow(SlideHyperlink link)
    {
        if (link.Slide is { } slide)
        {
            this.GoTo(slide);
            return null;
        }

        switch (link.Action)
        {
            case SlideShowJumps.NextSlide:
                this.Advance();
                return null;

            case SlideShowJumps.PreviousSlide:
                var order = this.Order;
                var at = order.IndexOf(this.index);
                if (at > 0)
                    this.GoTo(order[at - 1]);
                return null;

            case SlideShowJumps.FirstSlide:
                this.First();
                return null;

            case SlideShowJumps.LastSlide:
                this.Last();
                return null;

            case SlideShowJumps.EndShow:
                this.Ended?.Invoke(this, EventArgs.Empty);
                return null;
        }

        return link.Url;
    }
}
