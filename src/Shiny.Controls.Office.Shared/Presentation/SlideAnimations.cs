using System.Globalization;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using Shiny.Controls.Office.Editing;
using PSlide = DocumentFormat.OpenXml.Presentation.Slide;

namespace Shiny.Controls.Office.Presentation;

/// <summary>The three families PowerPoint's animation gallery is split into.</summary>
public enum SlideAnimationClass
{
    Entrance,
    Emphasis,
    Exit
}

/// <summary>The animation effects the editor offers.</summary>
public enum SlideAnimationEffect
{
    // Entrance
    Appear,
    Fade,
    FlyIn,
    Wipe,
    Zoom,

    // Emphasis
    GrowShrink,
    Spin,
    Pulse,

    // Exit
    Disappear,
    FadeOut,
    FlyOut,

    /// <summary>An effect the editor does not offer. Kept exactly as written and played as a fade.</summary>
    Other
}

/// <summary>What starts an animation — the Start box on PowerPoint's Animations tab.</summary>
public enum SlideAnimationTrigger
{
    OnClick,
    WithPrevious,
    AfterPrevious
}

/// <summary>
/// One effect on one shape.
/// </summary>
/// <param name="ShapeId">The target's <c>cNvPr</c> id (<see cref="SlideShape.Id"/>).</param>
/// <param name="Effect">What it does.</param>
/// <param name="Trigger">What starts it.</param>
/// <param name="Duration">How long it runs.</param>
/// <param name="Delay">How long after its trigger it starts.</param>
public sealed record SlideAnimation(
    uint ShapeId,
    SlideAnimationEffect Effect,
    SlideAnimationTrigger Trigger,
    TimeSpan Duration,
    TimeSpan Delay)
{
    /// <summary>Where a Fly or Wipe comes from. Ignored by the effects that have no direction.</summary>
    public SlideTransitionDirection Direction { get; init; } = SlideTransitionDirection.FromBottom;

    /// <summary>The effect's family.</summary>
    public SlideAnimationClass Class => ClassOf(this.Effect);

    /// <summary>For <see cref="SlideAnimationEffect.Other"/>: the family the file says it belongs to.</summary>
    internal SlideAnimationClass? ReadClass { get; init; }

    /// <summary>
    /// The effect's <c>p:par</c> exactly as read, for an effect this editor cannot rebuild — kept so a
    /// PowerPoint "Bounce" survives being reordered around.
    /// </summary>
    internal OpenXmlElement? Source { get; init; }

    public static SlideAnimationClass ClassOf(SlideAnimationEffect effect) => effect switch
    {
        SlideAnimationEffect.GrowShrink or SlideAnimationEffect.Spin or SlideAnimationEffect.Pulse => SlideAnimationClass.Emphasis,
        SlideAnimationEffect.Disappear or SlideAnimationEffect.FadeOut or SlideAnimationEffect.FlyOut => SlideAnimationClass.Exit,
        _ => SlideAnimationClass.Entrance
    };

    /// <summary>An effect with PowerPoint's defaults: on click, half a second, no delay.</summary>
    public static SlideAnimation Create(uint shapeId, SlideAnimationEffect effect) => new(
        shapeId,
        effect,
        SlideAnimationTrigger.OnClick,
        DefaultDuration(effect),
        TimeSpan.Zero);

    public static TimeSpan DefaultDuration(SlideAnimationEffect effect) => effect switch
    {
        SlideAnimationEffect.Appear or SlideAnimationEffect.Disappear => TimeSpan.Zero,
        SlideAnimationEffect.GrowShrink or SlideAnimationEffect.Spin => TimeSpan.FromSeconds(2),
        _ => TimeSpan.FromSeconds(0.5)
    };

    /// <summary>The name PowerPoint's gallery shows.</summary>
    public static string NameOf(SlideAnimationEffect effect) => effect switch
    {
        SlideAnimationEffect.FlyIn => "Fly In",
        SlideAnimationEffect.GrowShrink => "Grow/Shrink",
        SlideAnimationEffect.FadeOut => "Fade Out",
        SlideAnimationEffect.FlyOut => "Fly Out",
        SlideAnimationEffect.Other => "Custom",
        _ => effect.ToString()
    };

    /// <summary>Whether the effect takes a direction (Effect Options).</summary>
    public static bool HasDirection(SlideAnimationEffect effect)
        => effect is SlideAnimationEffect.FlyIn or SlideAnimationEffect.FlyOut or SlideAnimationEffect.Wipe;

    /// <summary>The effects in gallery order.</summary>
    public static IReadOnlyList<SlideAnimationEffect> Gallery { get; } =
    [
        SlideAnimationEffect.Appear, SlideAnimationEffect.Fade, SlideAnimationEffect.FlyIn, SlideAnimationEffect.Wipe, SlideAnimationEffect.Zoom,
        SlideAnimationEffect.GrowShrink, SlideAnimationEffect.Spin, SlideAnimationEffect.Pulse,
        SlideAnimationEffect.Disappear, SlideAnimationEffect.FadeOut, SlideAnimationEffect.FlyOut
    ];
}

/// <summary>
/// Reads and writes a slide's <c>p:timing</c> tree.
/// </summary>
/// <remarks>
/// <para>
/// PowerPoint's timing tree is a SMIL-like nest: a root time node holds the main sequence; the main
/// sequence holds one <c>p:par</c> per click; each click holds one <c>p:par</c> per "after previous"
/// group, offset by when that group starts; each group holds the effects, each an effect <c>p:par</c>
/// carrying <c>presetID</c>/<c>presetClass</c>/<c>nodeType</c> with the behaviours (<c>p:set</c>,
/// <c>p:anim</c>, <c>p:animEffect</c>...) underneath.
/// </para>
/// <para>
/// The editor's model is PowerPoint's own animation pane — a flat, ordered list with a trigger per row
/// — and the tree is regenerated from it on every change. Anything else in the timing root, such as a
/// trigger sequence that plays when a shape is clicked, is carried across untouched; and an effect this
/// editor has no preset for keeps its original <c>p:par</c>.
/// </para>
/// </remarks>
static class SlideTimingXml
{
    const string P = "http://schemas.openxmlformats.org/presentationml/2006/main";

    public static OpenXmlElement? Find(PSlide slide)
        => slide.ChildElements.FirstOrDefault(x => x.LocalName == "timing" && x.NamespaceUri == P);

    // ---- reading ----

    public static IReadOnlyList<SlideAnimation> Read(PSlide slide)
    {
        if (Find(slide) is not { } timing || MainSequence(timing) is not { } main)
            return [];

        var result = new List<SlideAnimation>();

        foreach (var click in Children(main, "par"))
        {
            foreach (var group in Children(TimeNode(click), "par"))
            {
                var groupDelay = DelayOf(TimeNode(group));

                foreach (var effect in Children(TimeNode(group), "par"))
                {
                    if (ReadEffect(effect, groupDelay) is { } animation)
                        result.Add(animation);
                }
            }
        }

        return result;
    }

    /// <summary>The main sequence's own time node, whose children are the clicks.</summary>
    static OpenXmlElement? MainSequence(OpenXmlElement timing)
        => timing.Descendants()
            .FirstOrDefault(x => x.LocalName == "cTn" && Attribute(x, "nodeType") == "mainSeq");

    static OpenXmlElement? TimeNode(OpenXmlElement? par)
        => par?.ChildElements.FirstOrDefault(x => x.LocalName == "cTn");

    static IEnumerable<OpenXmlElement> Children(OpenXmlElement? timeNode, string localName)
        => timeNode?.ChildElements.FirstOrDefault(x => x.LocalName == "childTnLst")?.ChildElements.Where(x => x.LocalName == localName)
           ?? [];

    static SlideAnimation? ReadEffect(OpenXmlElement par, TimeSpan groupDelay)
    {
        if (TimeNode(par) is not { } node)
            return null;

        var target = node.Descendants().FirstOrDefault(x => x.LocalName == "spTgt");
        if (target is null || !uint.TryParse(Attribute(target, "spid"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return null;

        var trigger = Attribute(node, "nodeType") switch
        {
            "withEffect" => SlideAnimationTrigger.WithPrevious,
            "afterEffect" => SlideAnimationTrigger.AfterPrevious,
            _ => SlideAnimationTrigger.OnClick
        };

        var presetClass = Attribute(node, "presetClass");
        var presetId = int.TryParse(Attribute(node, "presetID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : -1;
        var subtype = int.TryParse(Attribute(node, "presetSubtype"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : 0;

        var effect = (presetClass, presetId) switch
        {
            ("entr", 1) => SlideAnimationEffect.Appear,
            ("entr", 10) => SlideAnimationEffect.Fade,
            ("entr", 2) => SlideAnimationEffect.FlyIn,
            ("entr", 22) => SlideAnimationEffect.Wipe,
            ("entr", 53) => SlideAnimationEffect.Zoom,
            ("emph", 6) => SlideAnimationEffect.GrowShrink,
            ("emph", 8) => SlideAnimationEffect.Spin,
            ("emph", 26) => SlideAnimationEffect.Pulse,
            ("exit", 1) => SlideAnimationEffect.Disappear,
            ("exit", 10) => SlideAnimationEffect.FadeOut,
            ("exit", 2) => SlideAnimationEffect.FlyOut,
            _ => SlideAnimationEffect.Other
        };

        // The longest behaviour is the effect's length; an Appear is a one-millisecond set.
        var duration = node.Descendants()
            .Where(x => x.LocalName == "cTn")
            .Select(x => double.TryParse(Attribute(x, "dur"), NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) ? ms : 0)
            .DefaultIfEmpty(0)
            .Max();

        if (effect is SlideAnimationEffect.Appear or SlideAnimationEffect.Disappear)
            duration = 0;

        // The effect's own delay: the group's offset is what "after previous" already means.
        var delay = DelayOf(node);
        _ = groupDelay;

        return new SlideAnimation(id, effect, trigger, TimeSpan.FromMilliseconds(duration), delay < TimeSpan.Zero ? TimeSpan.Zero : delay)
        {
            Direction = DirectionFromSubtype(subtype),
            ReadClass = presetClass switch
            {
                "emph" => SlideAnimationClass.Emphasis,
                "exit" => SlideAnimationClass.Exit,
                _ => SlideAnimationClass.Entrance
            },
            Source = effect == SlideAnimationEffect.Other ? par.CloneNode(true) : null
        };
    }

    static TimeSpan DelayOf(OpenXmlElement? timeNode)
    {
        var condition = timeNode?.ChildElements.FirstOrDefault(x => x.LocalName == "stCondLst")?.ChildElements.FirstOrDefault(x => x.LocalName == "cond");
        return double.TryParse(Attribute(condition, "delay"), NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)
            ? TimeSpan.FromMilliseconds(ms)
            : TimeSpan.Zero;
    }

    /// <summary>PowerPoint's direction subtypes: 1 top, 2 right, 4 bottom, 8 left.</summary>
    static SlideTransitionDirection DirectionFromSubtype(int subtype) => subtype switch
    {
        1 => SlideTransitionDirection.FromTop,
        2 => SlideTransitionDirection.FromRight,
        8 => SlideTransitionDirection.FromLeft,
        _ => SlideTransitionDirection.FromBottom
    };

    static int SubtypeOf(SlideTransitionDirection direction) => direction switch
    {
        SlideTransitionDirection.FromTop => 1,
        SlideTransitionDirection.FromRight => 2,
        SlideTransitionDirection.FromLeft => 8,
        _ => 4
    };

    // ---- writing ----

    /// <summary>
    /// Replaces the slide's main sequence with <paramref name="animations"/>, keeping any other sequence
    /// already in the timing root. An empty list with nothing else to keep removes <c>p:timing</c>.
    /// </summary>
    /// <param name="shapesWithText">Shape ids with a text body, which get a <c>p:bldP</c> entry.</param>
    public static void Write(PSlide slide, IReadOnlyList<SlideAnimation> animations, ISet<uint> shapesWithText)
    {
        var old = Find(slide);

        // Interactive (trigger) sequences and anything else that is not the main sequence.
        var kept = old is null
            ? []
            : old.Descendants()
                .Where(x => x.LocalName == "seq" && x.ChildElements.FirstOrDefault(c => c.LocalName == "cTn") is { } node && Attribute(node, "nodeType") != "mainSeq")
                .Select(x => x.CloneNode(true))
                .ToList();

        old?.Remove();

        if (animations.Count == 0 && kept.Count == 0)
            return;

        var ids = new IdCounter();
        var xml = new StringBuilder();
        xml.Append($"<p:timing xmlns:p=\"{P}\"><p:tnLst><p:par>");
        xml.Append($"<p:cTn id=\"{ids.Next()}\" dur=\"indefinite\" restart=\"never\" nodeType=\"tmRoot\"><p:childTnLst>");

        if (animations.Count > 0)
        {
            xml.Append("<p:seq concurrent=\"1\" nextAc=\"seek\">");
            xml.Append($"<p:cTn id=\"{ids.Next()}\" dur=\"indefinite\" nodeType=\"mainSeq\"><p:childTnLst>");

            foreach (var click in SlideAnimationTimeline.Clicks(animations))
            {
                xml.Append($"<p:par><p:cTn id=\"{ids.Next()}\" fill=\"hold\"><p:stCondLst><p:cond delay=\"indefinite\"/>");

                // An automatic first group starts when the main sequence (always id 2 here) begins,
                // rather than on a click - PowerPoint's own spelling of "with/after previous" on the
                // first effect of a slide.
                if (click.IsFirstAutomatic)
                    xml.Append("<p:cond evt=\"onBegin\" delay=\"0\"><p:tn val=\"2\"/></p:cond>");

                xml.Append("</p:stCondLst><p:childTnLst>");

                foreach (var group in click.Groups)
                {
                    xml.Append($"<p:par><p:cTn id=\"{ids.Next()}\" fill=\"hold\"><p:stCondLst><p:cond delay=\"{Ms(group.Start)}\"/></p:stCondLst><p:childTnLst>");

                    foreach (var (animation, offset) in group.Effects)
                        xml.Append(EffectXml(animation, offset, ids, shapesWithText.Contains(animation.ShapeId)));

                    xml.Append("</p:childTnLst></p:cTn></p:par>");
                }

                xml.Append("</p:childTnLst></p:cTn></p:par>");
            }

            xml.Append("</p:childTnLst></p:cTn>");
            xml.Append("<p:prevCondLst><p:cond evt=\"onPrev\" delay=\"0\"><p:tgtEl><p:sldTgt/></p:tgtEl></p:cond></p:prevCondLst>");
            xml.Append("<p:nextCondLst><p:cond evt=\"onNext\" delay=\"0\"><p:tgtEl><p:sldTgt/></p:tgtEl></p:cond></p:nextCondLst>");
            xml.Append("</p:seq>");
        }

        // Kept sequences are renumbered too: every time node in the tree needs an id unique to the
        // slide, and the ones they carried were only unique among the tree they came from.
        foreach (var sequence in kept)
        {
            var parsed = XElement.Parse(sequence.OuterXml);
            var remap = new Dictionary<string, string>();

            foreach (var node in parsed.Descendants().Where(x => x.Name.LocalName == "cTn"))
            {
                var fresh = ids.Next().ToString(CultureInfo.InvariantCulture);
                if (node.Attribute("id")?.Value is { } previousId)
                    remap[previousId] = fresh;

                node.SetAttributeValue("id", fresh);
            }

            foreach (var reference in parsed.Descendants().Where(x => x.Name.LocalName == "tn"))
            {
                if (reference.Attribute("val")?.Value is { } val && remap.TryGetValue(val, out var mapped))
                    reference.SetAttributeValue("val", mapped);
            }

            xml.Append(parsed.ToString(SaveOptions.DisableFormatting));
        }

        xml.Append("</p:childTnLst></p:cTn></p:par></p:tnLst>");

        // A build entry per animated shape with text: PowerPoint lists what is built "as one object" and
        // repairs a deck whose text shapes are animated without one.
        var built = animations.Where(x => shapesWithText.Contains(x.ShapeId) && x.Source is null).Select(x => x.ShapeId).Distinct().ToList();
        if (built.Count > 0)
        {
            xml.Append("<p:bldLst>");
            foreach (var id in built)
                xml.Append($"<p:bldP spid=\"{id}\" grpId=\"0\" animBg=\"1\"/>");

            xml.Append("</p:bldLst>");
        }

        xml.Append("</p:timing>");

        var element = new DocumentFormat.OpenXml.Presentation.Timing(Normalize(xml.ToString()));

        // After p:transition (or its alternate content), before p:extLst.
        OpenXmlElement? after = slide.ChildElements.LastOrDefault(x => x.LocalName is "cSld" or "clrMapOvr" or "transition" or "AlternateContent");
        if (after is not null)
            slide.InsertAfter(element, after);
        else
            slide.PrependChild(element);
    }

    /// <summary>Re-serialises through XLinq so the kept sequences' namespaces are declared where they are used.</summary>
    static string Normalize(string xml) => XElement.Parse(xml).ToString(SaveOptions.DisableFormatting);

    sealed class IdCounter
    {
        int next = 1;

        public int Next() => this.next++;
    }

    static string Ms(TimeSpan value) => ((long)Math.Round(value.TotalMilliseconds)).ToString(CultureInfo.InvariantCulture);

    static string EffectXml(SlideAnimation animation, TimeSpan offset, IdCounter ids, bool hasText)
    {
        var nodeType = animation.Trigger switch
        {
            SlideAnimationTrigger.WithPrevious => "withEffect",
            SlideAnimationTrigger.AfterPrevious => "afterEffect",
            _ => "clickEffect"
        };

        if (animation.Source is { } source)
        {
            // Rewritten ids and trigger, verbatim everything else.
            var clone = XElement.Parse(source.OuterXml);
            foreach (var node in clone.Descendants().Prepend(clone).Where(x => x.Name.LocalName == "cTn"))
                node.SetAttributeValue("id", ids.Next());

            var effectNode = clone.Elements().FirstOrDefault(x => x.Name.LocalName == "cTn");
            effectNode?.SetAttributeValue("nodeType", nodeType);
            effectNode?.Element(effectNode.Name.Namespace + "stCondLst")?.Elements().FirstOrDefault()?.SetAttributeValue("delay", Ms(offset));
            return clone.ToString(SaveOptions.DisableFormatting);
        }

        var spid = animation.ShapeId.ToString(CultureInfo.InvariantCulture);
        var duration = Math.Max(1, (long)Math.Round(animation.Duration.TotalMilliseconds));
        var (presetClass, presetId) = Preset(animation.Effect);
        var subtype = SlideAnimation.HasDirection(animation.Effect) ? SubtypeOf(animation.Direction) : 0;
        var group = hasText ? " grpId=\"0\"" : string.Empty;

        var xml = new StringBuilder();
        xml.Append($"<p:par><p:cTn id=\"{ids.Next()}\" presetID=\"{presetId}\" presetClass=\"{presetClass}\" presetSubtype=\"{subtype}\" fill=\"hold\"{group} nodeType=\"{nodeType}\">");
        xml.Append($"<p:stCondLst><p:cond delay=\"{Ms(offset)}\"/></p:stCondLst><p:childTnLst>");

        string Target() => $"<p:tgtEl><p:spTgt spid=\"{spid}\"/></p:tgtEl>";

        string Visibility(string value, long delay = 0) =>
            $"<p:set><p:cBhvr><p:cTn id=\"{ids.Next()}\" dur=\"1\" fill=\"hold\"><p:stCondLst><p:cond delay=\"{delay}\"/></p:stCondLst></p:cTn>{Target()}" +
            $"<p:attrNameLst><p:attrName>style.visibility</p:attrName></p:attrNameLst></p:cBhvr><p:to><p:strVal val=\"{value}\"/></p:to></p:set>";

        string Anim(string attribute, string from, string to) =>
            $"<p:anim calcmode=\"lin\" valueType=\"num\"><p:cBhvr additive=\"base\"><p:cTn id=\"{ids.Next()}\" dur=\"{duration}\" fill=\"hold\"/>{Target()}" +
            $"<p:attrNameLst><p:attrName>{attribute}</p:attrName></p:attrNameLst></p:cBhvr>" +
            $"<p:tavLst><p:tav tm=\"0\"><p:val><p:strVal val=\"{from}\"/></p:val></p:tav><p:tav tm=\"100000\"><p:val><p:strVal val=\"{to}\"/></p:val></p:tav></p:tavLst></p:anim>";

        string Filter(string transition, string filter) =>
            $"<p:animEffect transition=\"{transition}\" filter=\"{filter}\"><p:cBhvr><p:cTn id=\"{ids.Next()}\" dur=\"{duration}\"/>{Target()}</p:cBhvr></p:animEffect>";

        var (flyAttribute, flyOff) = animation.Direction switch
        {
            SlideTransitionDirection.FromTop => ("ppt_y", "0-#ppt_h/2"),
            SlideTransitionDirection.FromLeft => ("ppt_x", "0-#ppt_w/2"),
            SlideTransitionDirection.FromRight => ("ppt_x", "1+#ppt_w/2"),
            _ => ("ppt_y", "1+#ppt_h/2")
        };

        var home = flyAttribute == "ppt_x" ? "#ppt_x" : "#ppt_y";

        switch (animation.Effect)
        {
            case SlideAnimationEffect.Appear:
                xml.Append(Visibility("visible"));
                break;

            case SlideAnimationEffect.Fade:
                xml.Append(Visibility("visible"));
                xml.Append(Filter("in", "fade"));
                break;

            case SlideAnimationEffect.FlyIn:
                xml.Append(Visibility("visible"));
                xml.Append(Anim(flyAttribute, flyOff, home));
                break;

            case SlideAnimationEffect.Wipe:
                xml.Append(Visibility("visible"));
                xml.Append(Filter("in", animation.Direction switch
                {
                    SlideTransitionDirection.FromTop => "wipe(down)",
                    SlideTransitionDirection.FromLeft => "wipe(right)",
                    SlideTransitionDirection.FromRight => "wipe(left)",
                    _ => "wipe(up)"
                }));
                break;

            case SlideAnimationEffect.Zoom:
                xml.Append(Visibility("visible"));
                xml.Append(Anim("ppt_w", "0", "#ppt_w"));
                xml.Append(Anim("ppt_h", "0", "#ppt_h"));
                break;

            case SlideAnimationEffect.GrowShrink:
                xml.Append($"<p:animScale><p:cBhvr><p:cTn id=\"{ids.Next()}\" dur=\"{duration}\" fill=\"hold\"/>{Target()}</p:cBhvr><p:by x=\"150000\" y=\"150000\"/></p:animScale>");
                break;

            case SlideAnimationEffect.Spin:
                xml.Append($"<p:animRot by=\"21600000\"><p:cBhvr><p:cTn id=\"{ids.Next()}\" dur=\"{duration}\" fill=\"hold\"/>{Target()}<p:attrNameLst><p:attrName>r</p:attrName></p:attrNameLst></p:cBhvr></p:animRot>");
                break;

            case SlideAnimationEffect.Pulse:
                xml.Append($"<p:animScale><p:cBhvr><p:cTn id=\"{ids.Next()}\" dur=\"{Math.Max(1, duration / 2)}\" autoRev=\"1\" fill=\"hold\"/>{Target()}</p:cBhvr><p:by x=\"105000\" y=\"105000\"/></p:animScale>");
                break;

            case SlideAnimationEffect.Disappear:
                xml.Append(Visibility("hidden"));
                break;

            case SlideAnimationEffect.FadeOut:
                xml.Append(Filter("out", "fade"));
                xml.Append(Visibility("hidden", Math.Max(0, duration - 1)));
                break;

            case SlideAnimationEffect.FlyOut:
                xml.Append(Anim(flyAttribute, home, flyOff));
                xml.Append(Visibility("hidden", Math.Max(0, duration - 1)));
                break;
        }

        xml.Append("</p:childTnLst></p:cTn></p:par>");
        return xml.ToString();
    }

    static (string Class, int Id) Preset(SlideAnimationEffect effect) => effect switch
    {
        SlideAnimationEffect.Appear => ("entr", 1),
        SlideAnimationEffect.Fade => ("entr", 10),
        SlideAnimationEffect.FlyIn => ("entr", 2),
        SlideAnimationEffect.Wipe => ("entr", 22),
        SlideAnimationEffect.Zoom => ("entr", 53),
        SlideAnimationEffect.GrowShrink => ("emph", 6),
        SlideAnimationEffect.Spin => ("emph", 8),
        SlideAnimationEffect.Pulse => ("emph", 26),
        SlideAnimationEffect.Disappear => ("exit", 1),
        SlideAnimationEffect.FadeOut => ("exit", 10),
        SlideAnimationEffect.FlyOut => ("exit", 2),
        _ => ("entr", 10)
    };

    /// <summary>Every shape id the timing tree targets, so a delete can prune what would dangle.</summary>
    public static ISet<uint> TargetIds(PSlide slide)
    {
        var result = new HashSet<uint>();
        if (Find(slide) is not { } timing)
            return result;

        foreach (var target in timing.Descendants().Where(x => x.LocalName is "spTgt" or "bldP" or "bldGraphic" or "bldOleChart" or "bldDgm"))
        {
            if (uint.TryParse(Attribute(target, "spid"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                result.Add(id);
        }

        return result;
    }

    static string? Attribute(OpenXmlElement? element, string localName)
    {
        if (element is null)
            return null;

        foreach (var attribute in element.GetAttributes())
        {
            if (attribute.LocalName == localName)
                return string.IsNullOrEmpty(attribute.Value) ? null : attribute.Value;
        }

        return null;
    }
}

/// <summary>
/// When each animation on a slide plays: the list broken into clicks, and each click into the groups
/// that start together.
/// </summary>
/// <remarks>
/// Shared by the writer, which needs the groups to build the timing tree, and the slide show, which
/// needs the absolute start of each effect to play it — so the two can never disagree about what
/// "after previous" means.
/// </remarks>
public static class SlideAnimationTimeline
{
    /// <summary>One effect, placed.</summary>
    public readonly record struct Scheduled(int Index, SlideAnimation Animation, int Click, TimeSpan Start, TimeSpan End);

    /// <summary>A click's groups; the first click of a slide may be automatic (it starts with previous).</summary>
    internal sealed record Click(bool IsFirstAutomatic, IReadOnlyList<Group> Groups);

    internal sealed record Group(TimeSpan Start, IReadOnlyList<(SlideAnimation Animation, TimeSpan Offset)> Effects);

    internal static IReadOnlyList<Click> Clicks(IReadOnlyList<SlideAnimation> animations)
    {
        var clicks = new List<Click>();
        var groups = new List<Group>();
        var effects = new List<(SlideAnimation, TimeSpan)>();
        var groupStart = TimeSpan.Zero;
        var clickEnd = TimeSpan.Zero;
        var firstAutomatic = animations.Count > 0 && animations[0].Trigger != SlideAnimationTrigger.OnClick;

        void CloseGroup()
        {
            if (effects.Count > 0)
                groups.Add(new Group(groupStart, effects.ToList()));

            effects.Clear();
        }

        void CloseClick()
        {
            CloseGroup();
            if (groups.Count > 0)
                clicks.Add(new Click(clicks.Count == 0 && firstAutomatic, groups.ToList()));

            groups.Clear();
            groupStart = TimeSpan.Zero;
            clickEnd = TimeSpan.Zero;
        }

        foreach (var animation in animations)
        {
            switch (animation.Trigger)
            {
                case SlideAnimationTrigger.OnClick:
                    CloseClick();
                    break;

                case SlideAnimationTrigger.AfterPrevious:
                    CloseGroup();
                    groupStart = clickEnd;
                    break;
            }

            effects.Add((animation, animation.Delay));
            clickEnd = Max(clickEnd, groupStart + animation.Delay + animation.Duration);
        }

        CloseClick();
        return clicks;
    }

    /// <summary>
    /// Every effect with the click it belongs to and its start and end within that click.
    /// </summary>
    /// <remarks>
    /// Click 0 is the automatic group, when the slide opens with "with previous"/"after previous"
    /// effects; otherwise clicks are numbered from 1, one per press.
    /// </remarks>
    public static IReadOnlyList<Scheduled> Schedule(IReadOnlyList<SlideAnimation> animations)
    {
        var result = new List<Scheduled>();
        var index = 0;
        var clicks = Clicks(animations);

        for (var c = 0; c < clicks.Count; c++)
        {
            var number = clicks[0].IsFirstAutomatic ? c : c + 1;
            foreach (var group in clicks[c].Groups)
            {
                foreach (var (animation, offset) in group.Effects)
                {
                    var start = group.Start + offset;
                    result.Add(new Scheduled(index++, animation, number, start, start + animation.Duration));
                }
            }
        }

        return result;
    }

    /// <summary>How many presses the slide's animations take — the numbered markers in the editor.</summary>
    public static int ClickCount(IReadOnlyList<SlideAnimation> animations)
        => Schedule(animations).Select(x => x.Click).Where(x => x > 0).DefaultIfEmpty(0).Max();

    static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}

/// <summary>
/// Replaces a slide's animation list, putting the previous timing tree back verbatim on undo.
/// </summary>
/// <remarks>
/// Every animation edit — add, remove, reorder, retime, retrigger — is this one command with a new list,
/// the same way PowerPoint's animation pane is one list the user rearranges.
/// </remarks>
public sealed record SetSlideAnimationsCommand(int Slide, IReadOnlyList<SlideAnimation> Animations) : SlideCommand
{
    public override string Name => "Animation";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (context.PartAt(this.Slide)?.Slide is not { } slide)
            return new NoOpSlideCommand();

        var inverse = new RestoreSlideTimingCommand(this.Slide, SlideTimingXml.Find(slide)?.CloneNode(true));

        // Only shapes that are still on the slide; an animation on a deleted shape is a repair prompt.
        var present = slide.CommonSlideData?.ShapeTree?.Descendants<DocumentFormat.OpenXml.Presentation.NonVisualDrawingProperties>()
            .Select(x => x.Id?.Value ?? 0)
            .ToHashSet() ?? [];

        var withText = slide.CommonSlideData?.ShapeTree?.Descendants<DocumentFormat.OpenXml.Presentation.Shape>()
            .Where(x => x.TextBody is not null)
            .Select(x => x.NonVisualShapeProperties?.NonVisualDrawingProperties?.Id?.Value ?? 0)
            .ToHashSet() ?? [];

        SlideTimingXml.Write(slide, this.Animations.Where(x => present.Contains(x.ShapeId)).ToList(), withText);
        context.Reproject(this.Slide);
        return inverse;
    }
}

/// <summary>Puts a slide's <c>p:timing</c> back exactly as it was captured.</summary>
public sealed record RestoreSlideTimingCommand(int Slide, OpenXmlElement? Timing) : SlideCommand
{
    public override string Name => "Animation";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (context.PartAt(this.Slide)?.Slide is not { } slide)
            return new NoOpSlideCommand();

        var inverse = new RestoreSlideTimingCommand(this.Slide, SlideTimingXml.Find(slide)?.CloneNode(true));
        SlideTimingXml.Find(slide)?.Remove();

        if (this.Timing?.CloneNode(true) is { } timing)
        {
            OpenXmlElement? after = slide.ChildElements.LastOrDefault(x => x.LocalName is "cSld" or "clrMapOvr" or "transition" or "AlternateContent");
            if (after is not null)
                slide.InsertAfter(timing, after);
            else
                slide.PrependChild(timing);
        }

        context.Reproject(this.Slide);
        return inverse;
    }
}
