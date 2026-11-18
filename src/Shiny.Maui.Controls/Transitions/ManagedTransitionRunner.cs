using Microsoft.Maui.Controls.Shapes;

namespace Shiny.Maui.Controls;

/// <summary>
/// The built-in transitions as plain MAUI property animations on two elements (normally the pages'
/// animation surfaces). This is what runs on the heads without a native hook - Windows, macOS
/// (AppKit), Mac Catalyst, Linux (GTK4) - and behind <see cref="PageTransition.AnimateAsync"/>.
/// The timeline mirrors the iOS keyframes, so a type looks the same wherever it runs.
/// </summary>
static class ManagedTransitionRunner
{
    internal const string AnimationName = "ShinyPageTransition";

    /// <summary>One property of one element over a window of the transition's timeline.</summary>
    internal sealed record Track(VisualElement Target, bool Incoming, double Begin, double Length, Action<VisualElement, double> Set);

    /// <summary>A hero on the incoming page, flown from where its partner was on the outgoing one.</summary>
    internal sealed record HeroFlight(VisualElement Target, Rect Source);


    /// <summary>
    /// Builds the tracks for <paramref name="plan"/> and puts the incoming element in its starting
    /// state at once, so it is never drawn at rest before the animation begins.
    /// </summary>
    internal static Choreography Prepare(PageTransitionPlan plan, VisualElement? from, VisualElement? to, Size bounds)
    {
        var choreography = new Choreography(Build(plan, from, to, bounds));
        choreography.ApplyStart();
        return choreography;
    }


    internal static Task RunAsync(
        PageTransitionPlan plan,
        VisualElement? from,
        VisualElement? to,
        Size bounds,
        IReadOnlyList<HeroFlight> heroes,
        CancellationToken cancellationToken
    ) => Prepare(plan, from, to, bounds).RunAsync(plan.Duration, heroes, cancellationToken);


    internal sealed class Choreography
    {
        readonly List<Track> tracks;
        readonly HashSet<VisualElement> clipped = new();

        internal Choreography(List<Track> tracks)
        {
            this.tracks = tracks;
            foreach (var track in tracks.Where(x => x.Target.Clip is null))
                this.clipped.Add(track.Target);
        }

        internal IEnumerable<VisualElement> Targets => this.tracks.Select(x => x.Target).Distinct();

        internal void ApplyStart()
        {
            foreach (var track in this.tracks)
                track.Set(track.Target, 0);
        }


        internal async Task RunAsync(TimeSpan duration, IReadOnlyList<HeroFlight> heroes, CancellationToken cancellationToken)
        {
            var flights = heroes.Select(PrepareFlight).Where(x => x is not null).Select(x => x!).ToList();
            var owner = this.tracks.Select(x => x.Target).FirstOrDefault() ?? flights.Select(x => x.Target).FirstOrDefault();

            try
            {
                if (owner is null || duration <= TimeSpan.Zero || cancellationToken.IsCancellationRequested)
                    return;

                var done = new TaskCompletionSource();
                using var registration = cancellationToken.Register(() => owner.Dispatcher.Dispatch(() => owner.AbortAnimation(AnimationName)));

                var animation = new Animation(p =>
                {
                    foreach (var track in this.tracks)
                        track.Set(track.Target, Math.Clamp((p - track.Begin) / track.Length, 0, 1));
                    foreach (var flight in flights)
                        flight.Apply(p);
                }, 0, 1, Easing.CubicInOut);

                animation.Commit(owner, AnimationName, 16, (uint)Math.Max(1, duration.TotalMilliseconds), Easing.Linear, (_, _) => done.TrySetResult());
                await done.Task;
            }
            finally
            {
                foreach (var target in this.Targets)
                {
                    PageTransitionHost.ResetElement(target);
                    if (this.clipped.Contains(target))
                        target.Clip = null;
                }
                foreach (var flight in flights)
                    flight.Land();
            }
        }
    }


    static List<Track> Build(PageTransitionPlan plan, VisualElement? from, VisualElement? to, Size bounds)
    {
        var tracks = new List<Track>();
        var type = Substitute(plan.Type);
        var isPop = plan.IsPop;
        var (vx, vy) = plan.Vector;
        var w = bounds.Width > 0 ? bounds.Width : 400;
        var h = bounds.Height > 0 ? bounds.Height : 800;
        var fullX = vx * w;
        var fullY = vy * h;

        void Add(VisualElement? target, bool incoming, Action<VisualElement, double> set, double begin = 0, double length = 1)
        {
            if (target is not null)
                tracks.Add(new Track(target, incoming, begin, Math.Max(0.001, length), set));
        }
        static double Lerp(double a, double b, double t) => a + (b - a) * t;
        Action<VisualElement, double> Opacity(double a, double b) => (e, t) => e.Opacity = Lerp(a, b, t);
        Action<VisualElement, double> Move(double x0, double y0, double x1, double y1) => (e, t) =>
        {
            e.TranslationX = Lerp(x0, x1, t);
            e.TranslationY = Lerp(y0, y1, t);
        };
        Action<VisualElement, double> Grow(double a, double b) => (e, t) => e.Scale = Lerp(a, b, t);

        switch (type)
        {
            case PageTransitionType.Fade:
                if (isPop)
                    Add(from, false, Opacity(1, 0));
                else
                    Add(to, true, Opacity(0, 1));
                break;

            case PageTransitionType.FadeThrough:
                Add(from, false, Opacity(1, 0), 0, 0.35);
                Add(to, true, Opacity(0, 1), 0.35, 0.65);
                Add(to, true, Grow(0.92, 1), 0.35, 0.65);
                break;

            case PageTransitionType.Slide:
            case PageTransitionType.Push:
                var share = type == PageTransitionType.Push ? 1.0 : 0.3;
                if (isPop)
                {
                    Add(to, true, Move(-fullX * share, -fullY * share, 0, 0));
                    Add(from, false, Move(0, 0, fullX, fullY));
                }
                else
                {
                    Add(to, true, Move(fullX, fullY, 0, 0));
                    Add(from, false, Move(0, 0, -fullX * share, -fullY * share));
                }
                break;

            case PageTransitionType.Cover:
                if (isPop)
                    Add(from, false, Move(0, 0, fullX, fullY));
                else
                    Add(to, true, Move(fullX, fullY, 0, 0));
                break;

            case PageTransitionType.Reveal:
                if (isPop)
                    Add(to, true, Move(-fullX, -fullY, 0, 0));
                else
                    Add(from, false, Move(0, 0, -fullX, -fullY));
                break;

            case PageTransitionType.SharedAxisX:
            case PageTransitionType.SharedAxisY:
                double ax, ay;
                if (type == PageTransitionType.SharedAxisX)
                    (ax, ay) = (plan.IsVertical ? 30 : vx * 30, 0);
                else
                    (ax, ay) = (0, plan.IsVertical ? vy * 30 : 30);
                var sign = isPop ? -1 : 1;

                Add(to, true, Move(ax * sign, ay * sign, 0, 0));
                Add(to, true, Opacity(0, 1), 0.3, 0.7);
                Add(from, false, Move(0, 0, -ax * sign, -ay * sign));
                Add(from, false, Opacity(1, 0), 0, 0.3);
                break;

            case PageTransitionType.SharedAxisZ:
                Add(to, true, Grow(isPop ? 1.1 : 0.8, 1));
                Add(to, true, Opacity(0, 1), 0.3, 0.7);
                Add(from, false, Grow(1, isPop ? 0.8 : 1.1));
                Add(from, false, Opacity(1, 0), 0, 0.3);
                break;

            case PageTransitionType.Zoom:
                if (isPop)
                {
                    Add(from, false, Opacity(1, 0));
                    Add(from, false, Grow(1, 0.85));
                }
                else
                {
                    Add(to, true, Opacity(0, 1));
                    Add(to, true, Grow(0.85, 1));
                }
                break;

            case PageTransitionType.CircularReveal:
                var center = new Point(w / 2, h / 2);
                var radius = Math.Sqrt(w * w + h * h) / 2;
                Action<VisualElement, double> Circle(double r0, double r1) => (e, t) =>
                {
                    var r = Math.Max(0.5, Lerp(r0, r1, t));
                    e.Clip = new EllipseGeometry(center, r, r);
                };
                if (isPop)
                    Add(from, false, Circle(radius, 0));
                else
                    Add(to, true, Circle(0, radius));
                break;

            case PageTransitionType.Flip:
                var horizontal = !plan.IsVertical;
                var turn = 90.0 * (horizontal ? vx : -vy) * (isPop ? -1 : 1);
                Action<VisualElement, double> Turn(double a, double b) => horizontal
                    ? (e, t) => e.RotationY = Lerp(a, b, t)
                    : (e, t) => e.RotationX = Lerp(a, b, t);

                Add(from, false, Turn(0, turn), 0, 0.5);
                Add(from, false, (e, t) => e.Opacity = t < 1 ? 1 : 0, 0, 0.5);
                Add(to, true, (e, t) => e.Opacity = t > 0 ? 1 : 0, 0.5, 0.001);
                Add(to, true, Turn(-turn, 0), 0.5, 0.5);
                break;
        }

        // The outgoing page is often already gone on these heads. Its share of the timeline is
        // then dead air, so the incoming page's tracks take the whole run; and where the type moves
        // only the outgoing page (a Cover pop, a Reveal push), the incoming page fades in instead
        // of just appearing.
        if (from is null && to is not null)
        {
            if (tracks.Count == 0)
                tracks.Add(new Track(to, true, 0, 1, Opacity(0, 1)));
            else
                tracks = tracks.Select(x => x with { Begin = 0, Length = 1 }).ToList();
        }

        return tracks;
    }


    /// <summary>
    /// What a type runs as here. Zoom is <see cref="PageTransitionType.NativeZoom"/>'s fallback
    /// everywhere but iOS 18; GTK4 has no 3D rotation or clip on a page's content, so flips and
    /// circular reveals become what reads closest.
    /// </summary>
    static PageTransitionType Substitute(PageTransitionType type) => type switch
    {
        PageTransitionType.NativeZoom => PageTransitionType.Zoom,
        PageTransitionType.Flip when OperatingSystem.IsLinux() => PageTransitionType.FadeThrough,
        PageTransitionType.CircularReveal when OperatingSystem.IsLinux() => PageTransitionType.Zoom,
        _ => type
    };


    #region Heroes

    /// <summary>A hero view's bounds relative to its page, following any scrolling on the way up.</summary>
    internal static Rect BoundsInPage(VisualElement view)
    {
        double x = view.X + view.TranslationX;
        double y = view.Y + view.TranslationY;
        for (var parent = view.Parent; parent is VisualElement element and not Page; parent = parent.Parent)
        {
            x += element.X + element.TranslationX;
            y += element.Y + element.TranslationY;
            if (element is ScrollView scroll)
            {
                x -= scroll.ScrollX;
                y -= scroll.ScrollY;
            }
        }
        return new Rect(x, y, view.Width, view.Height);
    }


    static ActiveFlight? PrepareFlight(HeroFlight flight)
    {
        var target = flight.Target;
        if (target.Width <= 0 || target.Height <= 0 || flight.Source.Width <= 0 || flight.Source.Height <= 0)
            return null;

        var end = BoundsInPage(target);
        return new ActiveFlight(
            target,
            flight.Source.X - end.X,
            flight.Source.Y - end.Y,
            flight.Source.Width / end.Width,
            flight.Source.Height / end.Height
        );
    }


    sealed class ActiveFlight
    {
        readonly double dx, dy, sx, sy, anchorX, anchorY;

        public ActiveFlight(VisualElement target, double dx, double dy, double sx, double sy)
        {
            this.Target = target;
            this.dx = dx;
            this.dy = dy;
            this.sx = sx;
            this.sy = sy;
            this.anchorX = target.AnchorX;
            this.anchorY = target.AnchorY;
            target.AnchorX = 0;
            target.AnchorY = 0;
            this.Apply(0);
        }

        public VisualElement Target { get; }

        public void Apply(double p)
        {
            var t = 1 - p;
            this.Target.TranslationX = this.dx * t;
            this.Target.TranslationY = this.dy * t;
            this.Target.ScaleX = 1 + (this.sx - 1) * t;
            this.Target.ScaleY = 1 + (this.sy - 1) * t;
        }

        public void Land()
        {
            this.Target.TranslationX = 0;
            this.Target.TranslationY = 0;
            this.Target.ScaleX = 1;
            this.Target.ScaleY = 1;
            this.Target.AnchorX = this.anchorX;
            this.Target.AnchorY = this.anchorY;
        }
    }

    #endregion
}
