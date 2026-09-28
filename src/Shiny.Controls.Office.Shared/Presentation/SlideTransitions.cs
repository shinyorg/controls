using System.Globalization;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using Shiny.Controls.Office.Editing;
using PSlide = DocumentFormat.OpenXml.Presentation.Slide;

namespace Shiny.Controls.Office.Presentation;

/// <summary>The transitions the editor offers — PowerPoint's Subtle and Exciting basics.</summary>
public enum SlideTransitionKind
{
    /// <summary>No transition: the slide simply replaces the last one.</summary>
    None,
    Fade,
    Push,
    Wipe,
    Split,
    Reveal,
    Cover,
    Zoom,
    Morph,

    /// <summary>A transition the editor does not offer (blinds, checker...). Played as a fade, kept as written.</summary>
    Other
}

/// <summary>Which way a transition or an animation moves.</summary>
/// <remarks>
/// Named from the viewer's side — "from bottom" is the incoming slide rising into place — which is how
/// PowerPoint's Effect Options menu reads. The file stores the direction of travel instead, and the
/// mapping lives in one place in <see cref="SlideTransitionXml"/>.
/// </remarks>
public enum SlideTransitionDirection
{
    FromBottom,
    FromTop,
    FromLeft,
    FromRight,
    HorizontalIn,
    HorizontalOut,
    VerticalIn,
    VerticalOut,
    In,
    Out
}

/// <summary>How one slide arrives in a slide show.</summary>
/// <param name="Kind">The effect.</param>
/// <param name="Direction">Effect option; ignored by the effects that have none (Fade, Morph).</param>
/// <param name="Duration">How long the transition runs.</param>
public sealed record SlideTransition(SlideTransitionKind Kind, SlideTransitionDirection Direction, TimeSpan Duration)
{
    /// <summary>A mouse click moves on to the next slide. PowerPoint's default, and ours.</summary>
    public bool AdvanceOnClick { get; init; } = true;

    /// <summary>Moves on by itself after this long, or never when null.</summary>
    public TimeSpan? AdvanceAfter { get; init; }

    /// <summary>The effect's default direction — what PowerPoint picks when it is first applied.</summary>
    public static SlideTransitionDirection DefaultDirection(SlideTransitionKind kind) => kind switch
    {
        SlideTransitionKind.Push or SlideTransitionKind.Cover => SlideTransitionDirection.FromBottom,
        SlideTransitionKind.Wipe or SlideTransitionKind.Reveal => SlideTransitionDirection.FromRight,
        SlideTransitionKind.Split => SlideTransitionDirection.VerticalOut,
        SlideTransitionKind.Zoom => SlideTransitionDirection.In,
        _ => SlideTransitionDirection.FromBottom
    };

    /// <summary>The effect options a kind offers, for a ribbon's Effect Options menu.</summary>
    public static IReadOnlyList<SlideTransitionDirection> DirectionsFor(SlideTransitionKind kind) => kind switch
    {
        SlideTransitionKind.Push or SlideTransitionKind.Wipe or SlideTransitionKind.Cover =>
            [SlideTransitionDirection.FromBottom, SlideTransitionDirection.FromTop, SlideTransitionDirection.FromLeft, SlideTransitionDirection.FromRight],
        SlideTransitionKind.Reveal => [SlideTransitionDirection.FromRight, SlideTransitionDirection.FromLeft],
        SlideTransitionKind.Split =>
            [SlideTransitionDirection.VerticalOut, SlideTransitionDirection.VerticalIn, SlideTransitionDirection.HorizontalOut, SlideTransitionDirection.HorizontalIn],
        SlideTransitionKind.Zoom => [SlideTransitionDirection.In, SlideTransitionDirection.Out],
        _ => []
    };

    /// <summary>The default length PowerPoint gives each effect.</summary>
    public static TimeSpan DefaultDuration(SlideTransitionKind kind) => kind switch
    {
        SlideTransitionKind.Fade => TimeSpan.FromSeconds(0.7),
        SlideTransitionKind.Push or SlideTransitionKind.Wipe or SlideTransitionKind.Cover or SlideTransitionKind.Reveal => TimeSpan.FromSeconds(1),
        SlideTransitionKind.Split => TimeSpan.FromSeconds(1.25),
        SlideTransitionKind.Zoom => TimeSpan.FromSeconds(0.5),
        SlideTransitionKind.Morph => TimeSpan.FromSeconds(2),
        _ => TimeSpan.Zero
    };

    /// <summary>A transition with its defaults, ready to apply.</summary>
    public static SlideTransition Create(SlideTransitionKind kind)
        => new(kind, DefaultDirection(kind), DefaultDuration(kind));

    /// <summary>The name PowerPoint's gallery shows.</summary>
    public static string NameOf(SlideTransitionKind kind) => kind switch
    {
        SlideTransitionKind.None => "None",
        SlideTransitionKind.Other => "Custom",
        _ => kind.ToString()
    };

    /// <summary>The name an Effect Options menu shows for a direction.</summary>
    public static string NameOf(SlideTransitionDirection direction) => direction switch
    {
        SlideTransitionDirection.FromBottom => "From Bottom",
        SlideTransitionDirection.FromTop => "From Top",
        SlideTransitionDirection.FromLeft => "From Left",
        SlideTransitionDirection.FromRight => "From Right",
        SlideTransitionDirection.HorizontalIn => "Horizontal In",
        SlideTransitionDirection.HorizontalOut => "Horizontal Out",
        SlideTransitionDirection.VerticalIn => "Vertical In",
        SlideTransitionDirection.VerticalOut => "Vertical Out",
        SlideTransitionDirection.In => "In",
        _ => "Out"
    };
}

/// <summary>
/// Reads and writes <c>p:transition</c>.
/// </summary>
/// <remarks>
/// <para>
/// The base schema only knows three speeds (<c>spd</c> fast/med/slow) and has no Reveal or Morph. An
/// exact duration is PowerPoint 2010's <c>p14:dur</c>, and those two effects are 2010 and 2015
/// extensions — each only legal inside <c>mc:AlternateContent</c>, with a plain transition as the
/// fallback for older readers. So a transition is written plainly when it can be, and wrapped only when
/// it must be, which keeps the common case identical to what PowerPoint itself writes for it.
/// </para>
/// <para>
/// Built as XML text and parsed into the slide, rather than through the SDK's typed classes, because
/// the alternate-content wrapper and the p14 attribute have no typed surface that survives a round trip
/// intact.
/// </para>
/// </remarks>
static class SlideTransitionXml
{
    const string P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    const string P14 = "http://schemas.microsoft.com/office/powerpoint/2010/main";
    const string P159 = "http://schemas.microsoft.com/office/powerpoint/2015/09/main";
    const string Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    /// <summary>The slide's transition element — a <c>p:transition</c> or the alternate content wrapping one.</summary>
    public static OpenXmlElement? Find(PSlide slide)
        => slide.ChildElements.FirstOrDefault(IsTransitionSlot);

    static bool IsTransitionSlot(OpenXmlElement element)
        => (element.LocalName == "transition" && element.NamespaceUri == P) ||
           (element.LocalName == "AlternateContent" && element.Descendants().Any(x => x.LocalName == "transition" && x.NamespaceUri == P));

    public static SlideTransition? Read(PSlide slide)
    {
        if (Find(slide) is not { } slot)
            return null;

        var element = slot;
        if (slot.LocalName == "AlternateContent")
        {
            // The richest branch this reader understands: p14/p159 choices first, then the fallback.
            element = slot.ChildElements
                .Where(x => x.LocalName == "Choice")
                .Select(x => x.ChildElements.FirstOrDefault(c => c.LocalName == "transition"))
                .FirstOrDefault(x => x is not null)
                ?? slot.ChildElements.FirstOrDefault(x => x.LocalName == "Fallback")?.ChildElements.FirstOrDefault(c => c.LocalName == "transition");

            if (element is null)
                return null;
        }

        return Parse(element);
    }

    static SlideTransition Parse(OpenXmlElement transition)
    {
        var speed = Attribute(transition, "spd");
        var exact = Attribute(transition, "dur", P14);

        var effect = transition.ChildElements.FirstOrDefault(x => x.LocalName != "sndAc" && x.LocalName != "extLst");
        var (kind, direction) = effect is null ? (SlideTransitionKind.None, SlideTransitionDirection.FromBottom) : Classify(effect);

        var duration = exact is not null && double.TryParse(exact, NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)
            ? TimeSpan.FromMilliseconds(ms)
            : speed switch
            {
                "fast" => TimeSpan.FromSeconds(0.5),
                "med" => TimeSpan.FromSeconds(0.75),
                "slow" => TimeSpan.FromSeconds(1),
                _ => effect is null ? TimeSpan.Zero : TimeSpan.FromSeconds(1)
            };

        TimeSpan? after = Attribute(transition, "advTm") is { } tm && double.TryParse(tm, NumberStyles.Float, CultureInfo.InvariantCulture, out var afterMs)
            ? TimeSpan.FromMilliseconds(afterMs)
            : null;

        return new SlideTransition(kind, direction, duration)
        {
            AdvanceOnClick = Attribute(transition, "advClick") is not ("0" or "false"),
            AdvanceAfter = after
        };
    }

    static (SlideTransitionKind, SlideTransitionDirection) Classify(OpenXmlElement effect)
    {
        var dir = Attribute(effect, "dir");
        return effect.LocalName switch
        {
            "fade" => (SlideTransitionKind.Fade, SlideTransitionDirection.FromBottom),
            "push" => (SlideTransitionKind.Push, FromTravel(dir, "u")),
            "wipe" => (SlideTransitionKind.Wipe, FromTravel(dir, "l")),
            "cover" => (SlideTransitionKind.Cover, FromTravel(dir, "l")),
            "reveal" => (SlideTransitionKind.Reveal, FromTravel(dir, "l")),
            "split" => (SlideTransitionKind.Split, (Attribute(effect, "orient") ?? "horz", dir ?? "out") switch
            {
                ("vert", "in") => SlideTransitionDirection.VerticalIn,
                ("vert", _) => SlideTransitionDirection.VerticalOut,
                (_, "in") => SlideTransitionDirection.HorizontalIn,
                _ => SlideTransitionDirection.HorizontalOut
            }),
            "zoom" => (SlideTransitionKind.Zoom, dir == "out" ? SlideTransitionDirection.Out : SlideTransitionDirection.In),
            "morph" => (SlideTransitionKind.Morph, SlideTransitionDirection.FromBottom),
            _ => (SlideTransitionKind.Other, SlideTransitionDirection.FromBottom)
        };
    }

    /// <summary>
    /// The file's direction of travel to the viewer's "from" — a slide travelling up arrives from the
    /// bottom.
    /// </summary>
    static SlideTransitionDirection FromTravel(string? dir, string fallback) => (dir ?? fallback) switch
    {
        "u" => SlideTransitionDirection.FromBottom,
        "d" => SlideTransitionDirection.FromTop,
        "r" => SlideTransitionDirection.FromLeft,
        _ => SlideTransitionDirection.FromRight
    };

    static string Travel(SlideTransitionDirection direction) => direction switch
    {
        SlideTransitionDirection.FromBottom => "u",
        SlideTransitionDirection.FromTop => "d",
        SlideTransitionDirection.FromLeft => "r",
        _ => "l"
    };

    /// <summary>
    /// Replaces the slide's transition, or removes it for null or <see cref="SlideTransitionKind.None"/>
    /// with no timing — a cut that advances on click is no element at all.
    /// </summary>
    public static void Write(PSlide slide, SlideTransition? transition, OpenXmlElement? preserved = null)
    {
        Find(slide)?.Remove();

        if (transition is null)
            return;

        var plain = transition.Kind == SlideTransitionKind.None && transition.AdvanceOnClick && transition.AdvanceAfter is null;
        if (plain)
            return;

        var element = preserved?.CloneNode(true) ?? Build(transition);

        // p:transition sits after p:clrMapOvr and before p:timing and p:extLst. Anywhere else is a file
        // PowerPoint refuses to open.
        OpenXmlElement? after = slide.ChildElements.LastOrDefault(x => x.LocalName is "cSld" or "clrMapOvr");
        if (after is not null)
            slide.InsertAfter(element, after);
        else
            slide.PrependChild(element);
    }

    static OpenXmlElement Build(SlideTransition transition)
    {
        var speed = SpeedFor(transition.Duration);
        var exact = (int)Math.Round(transition.Duration.TotalMilliseconds);
        var needsP14 = transition.Kind is SlideTransitionKind.Reveal or SlideTransitionKind.Morph ||
                       (transition.Kind != SlideTransitionKind.None && Math.Abs(exact - SpeedMilliseconds(speed)) > 1);

        var common = $"spd=\"{speed}\"" +
            (transition.AdvanceOnClick ? string.Empty : " advClick=\"0\"") +
            (transition.AdvanceAfter is { } after ? $" advTm=\"{(int)Math.Round(after.TotalMilliseconds)}\"" : string.Empty);

        if (!needsP14)
            return Parse($"<p:transition xmlns:p=\"{P}\" {common}>{Effect(transition, fallback: false)}</p:transition>");

        var requires = transition.Kind == SlideTransitionKind.Morph ? "p159" : "p14";
        var xml =
            $"<mc:AlternateContent xmlns:mc=\"{Mc}\" xmlns:p=\"{P}\">" +
            $"<mc:Choice xmlns:p14=\"{P14}\" xmlns:p159=\"{P159}\" Requires=\"{requires}\">" +
            $"<p:transition {common} p14:dur=\"{exact}\">{Effect(transition, fallback: false)}</p:transition>" +
            "</mc:Choice>" +
            "<mc:Fallback>" +
            $"<p:transition {common}>{Effect(transition, fallback: true)}</p:transition>" +
            "</mc:Fallback>" +
            "</mc:AlternateContent>";

        return Parse(xml);
    }

    static string Effect(SlideTransition transition, bool fallback) => transition.Kind switch
    {
        SlideTransitionKind.Fade => "<p:fade/>",
        SlideTransitionKind.Push => $"<p:push dir=\"{Travel(transition.Direction)}\"/>",
        SlideTransitionKind.Wipe => $"<p:wipe dir=\"{Travel(transition.Direction)}\"/>",
        SlideTransitionKind.Cover => $"<p:cover dir=\"{Travel(transition.Direction)}\"/>",
        SlideTransitionKind.Split => transition.Direction switch
        {
            SlideTransitionDirection.VerticalIn => "<p:split orient=\"vert\" dir=\"in\"/>",
            SlideTransitionDirection.HorizontalIn => "<p:split dir=\"in\"/>",
            SlideTransitionDirection.HorizontalOut => "<p:split/>",
            _ => "<p:split orient=\"vert\"/>"
        },
        SlideTransitionKind.Zoom => transition.Direction == SlideTransitionDirection.Out ? "<p:zoom dir=\"out\"/>" : "<p:zoom/>",

        // Neither exists before PowerPoint 2010/2016; the fallback is the nearest thing an older reader
        // can play, which is what PowerPoint itself writes.
        SlideTransitionKind.Reveal => fallback
            ? "<p:fade/>"
            : $"<p14:reveal dir=\"{(transition.Direction == SlideTransitionDirection.FromLeft ? "r" : "l")}\"/>",
        SlideTransitionKind.Morph => fallback ? "<p:fade/>" : "<p159:morph option=\"byObject\"/>",
        _ => string.Empty
    };

    static string SpeedFor(TimeSpan duration) => duration.TotalMilliseconds switch
    {
        <= 600 => "fast",
        <= 875 => "med",
        _ => "slow"
    };

    static int SpeedMilliseconds(string speed) => speed switch
    {
        "fast" => 500,
        "med" => 750,
        _ => 1000
    };

    static OpenXmlElement Parse(string xml)
    {
        // Through XLinq first so the namespaces are resolved, then into the SDK as an unknown element
        // it will serialise verbatim. The slide re-reads it by local name either way.
        var parsed = XElement.Parse(xml);
        return parsed.Name.LocalName == "transition"
            ? new DocumentFormat.OpenXml.Presentation.Transition(parsed.ToString(SaveOptions.DisableFormatting))
            : new AlternateContent(parsed.ToString(SaveOptions.DisableFormatting));
    }

    static string? Attribute(OpenXmlElement element, string localName, string? namespaceUri = null)
    {
        foreach (var attribute in element.GetAttributes())
        {
            if (attribute.LocalName == localName && (namespaceUri is null || attribute.NamespaceUri == namespaceUri))
                return string.IsNullOrEmpty(attribute.Value) ? null : attribute.Value;
        }

        return null;
    }
}

/// <summary>Sets a slide's transition, putting the previous element back verbatim on undo.</summary>
public sealed record SetSlideTransitionCommand(int Slide, SlideTransition? Transition) : SlideCommand
{
    public override string Name => "Transition";

    /// <summary>An exact element to restore instead of building one — how undo keeps an unknown effect intact.</summary>
    internal OpenXmlElement? Preserved { get; init; }

    internal bool Restore { get; init; }

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (context.PartAt(this.Slide)?.Slide is not { } slide)
            return new NoOpSlideCommand();

        var previous = SlideTransitionXml.Find(slide)?.CloneNode(true);
        var inverse = new SetSlideTransitionCommand(this.Slide, SlideTransitionXml.Read(slide))
        {
            Preserved = previous,
            Restore = true
        };

        if (this.Restore)
        {
            SlideTransitionXml.Find(slide)?.Remove();
            if (this.Preserved is not null)
                SlideTransitionXml.Write(slide, new SlideTransition(SlideTransitionKind.Other, default, default), this.Preserved);
        }
        else
        {
            SlideTransitionXml.Write(slide, this.Transition);
        }

        context.Reproject(this.Slide);
        return inverse;
    }
}
