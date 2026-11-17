using System.Text.Json;

namespace Sample.Blazor.UITests;

/// <summary>
/// The "Jon Dick Nitpick checklist" in CLAUDE.md, measured on every gallery page:
/// 1. no scroll region inside another one on the same axis (double scrollbars), and no scroller that
///    only scrolls a few pixels; 2. no double borders — a bordered element whose edge sits against
///    its bordered container's edge, or two neighbours that both draw their shared divider.
/// Findings are also written to uitest-results/&lt;browser&gt;/jon-rules/&lt;width&gt;/&lt;route&gt;.json.
/// </summary>
public class JonRulesTests : BlazorTest
{
    public static IEnumerable<Func<string>> Routes()
        => BlazorRoutes.All.Select(r => (Func<string>)(() => r));

    [Test]
    [MethodDataSource(nameof(Routes))]
    public async Task AtDesktopWidth(string route)
    {
        await Open(route);
        await JonRules.AssertAsync(Page, route, "desktop");
    }
}

/// <summary>The same rules at phone width (360px).</summary>
public class JonRulesPhoneTests : BlazorTest
{
    protected override bool Phone => true;

    public static IEnumerable<Func<string>> Routes() => JonRulesTests.Routes();

    [Test]
    [MethodDataSource(nameof(Routes))]
    public async Task AtPhoneWidth(string route)
    {
        await Open(route);
        await JonRules.AssertAsync(Page, route, "phone");
    }
}

public static class JonRules
{
    public static async Task AssertAsync(IPage page, string route, string width)
    {
        // layout settles after OnAfterRenderAsync interop
        await page.WaitForTimeoutAsync(750);

        var findings = (await page.EvaluateAsync<JsonElement>(Script))
            .EnumerateArray().Select(e => e.GetString()!).Distinct().ToList();

        var dir = Path.Combine(BlazorTestSettings.ResultsDirectory, "jon-rules", width);
        Directory.CreateDirectory(dir);
        var name = route.Trim('/') is { Length: > 0 } r ? r : "home";
        File.WriteAllText(Path.Combine(dir, name + ".json"), JsonSerializer.Serialize(findings, new JsonSerializerOptions { WriteIndented = true }));

        if (findings.Count > 0)
            Assert.Fail($"'{route}' breaks the Jon rules ({width}):\n" + String.Join("\n", findings.Take(20)));
    }

    public const string Script =
        """
        () => {
            const out = [];
            const vw = document.documentElement.clientWidth;
            const describe = el => {
                if (el === document.scrollingElement) return 'document';
                let s = el.tagName.toLowerCase();
                const cls = [...el.classList].filter(c => !c.startsWith('b-')).slice(0, 2);
                if (cls.length) s += '.' + cls.join('.');
                const owner = el.parentElement?.closest('[class*="shiny-"]');
                if (owner && !cls.some(c => c.startsWith('shiny-'))) {
                    const oc = [...owner.classList].find(c => c.startsWith('shiny-'));
                    if (oc) s += ' (in .' + oc + ')';
                }
                return s;
            };
            const visible = el => {
                if (el.closest('[aria-hidden="true"], [inert], #shiny-splash, #blazor-error-ui, .source-code-panel, .shiny-marquee, [data-jon-ignore]')) return false;
                const cs = getComputedStyle(el);
                if (cs.visibility === 'hidden' || cs.display === 'none' || parseFloat(cs.opacity) === 0) return false;
                const r = el.getBoundingClientRect();
                return r.width > 0 && r.height > 0;
            };
            const isPage = el => el === document.scrollingElement || el.classList.contains('shiny-applayout-content');
            const all = [...document.querySelectorAll('body *')].filter(el => !(el instanceof SVGElement));

            // ---- rule 1: scrollers -------------------------------------------------------------
            const scrolls = (el, axis) => {
                if (el === document.scrollingElement)
                    return axis === 'y' ? el.scrollHeight > el.clientHeight + 1 : el.scrollWidth > el.clientWidth + 1;
                const cs = getComputedStyle(el);
                const o = axis === 'y' ? cs.overflowY : cs.overflowX;
                if (o !== 'auto' && o !== 'scroll') return false;
                return axis === 'y' ? el.scrollHeight > el.clientHeight + 1 : el.scrollWidth > el.clientWidth + 1;
            };
            for (const el of all) {
                if (el.matches('textarea, select, option, pre, code, pre *') || !visible(el)) continue;
                for (const axis of ['y', 'x']) {
                    if (!scrolls(el, axis)) continue;
                    const extra = axis === 'y' ? el.scrollHeight - el.clientHeight : el.scrollWidth - el.clientWidth;
                    // the page itself running a few px past the screen is just how long it is at this
                    // size; the rule is about a control region made too tall by its own chrome
                    if (extra <= 8 && !isPage(el))
                        out.push(`R1 pointless ${axis}-scroll of ${extra}px: ${describe(el)}`);
                    if (isPage(el)) continue;
                    // a gallery mini-app (.demo-frame) scrolls on purpose and must contain its overscroll
                    if (el.closest('.demo-frame, .demo-fill-body')) {
                        const ob = axis === 'y' ? getComputedStyle(el).overscrollBehaviorY : getComputedStyle(el).overscrollBehaviorX;
                        if (ob === 'contain' || ob === 'none') continue;
                    }
                    // a fixed layer (a sheet, a popover) scrolls against the viewport, whatever
                    // its DOM parents are doing
                    const fixedAt = a => getComputedStyle(a).position === 'fixed';
                    let outer = null, escaped = fixedAt(el);
                    for (let a = el.parentElement; a && !escaped; a = a.parentElement) {
                        if (scrolls(a, axis)) { outer = a; break; }
                        if (fixedAt(a)) escaped = true;
                    }
                    if (!outer && !escaped && scrolls(document.scrollingElement, axis)) outer = document.scrollingElement;
                    if (!outer) continue;
                    const where = isPage(outer) ? 'inside the page scroller' : `inside ${describe(outer)}`;
                    out.push(`R1 nested ${axis}-scroller: ${describe(el)} ${where}`);
                }
            }

            // ---- rule 2: borders ----------------------------------------------------------------
            const sides = ['Top', 'Right', 'Bottom', 'Left'];
            const border = (el, side) => {
                const cs = getComputedStyle(el);
                const w = parseFloat(cs[`border${side}Width`]);
                if (!(w > 0) || cs[`border${side}Style`] === 'none' || cs[`border${side}Style`] === 'hidden') return 0;
                const c = cs[`border${side}Color`];
                const m = c.match(/rgba?\(([^)]+)\)/);
                if (m) { const p = m[1].split(/[ ,\/]+/).filter(Boolean); if (p.length === 4 && parseFloat(p[3]) < 0.08) return 0; }
                if (c === 'transparent') return 0;
                return w;
            };
            const collapsedCell = el => el.matches('td, th') && getComputedStyle(el.closest('table')).borderCollapse === 'collapse';
            const overlap = (a1, a2, b1, b2) => Math.min(a2, b2) - Math.max(a1, b1);
            const bordered = all.filter(el => visible(el) && !collapsedCell(el) && sides.some(s => border(el, s)));
            const set = new Set(bordered);
            for (const el of bordered) {
                const r = el.getBoundingClientRect();
                for (const side of sides) {
                    if (!border(el, side)) continue;
                    // container edge: nearest bordered ancestor on the same side, within 8 levels
                    let a = el.parentElement;
                    for (let i = 0; a && i < 8; i++, a = a.parentElement) {
                        if (!set.has(a) || !border(a, side)) continue;
                        // content scrolling past that edge only lines up with it by chance
                        // - including when the container is itself the scroller (a list whose rows
                        // run on below its bottom edge)
                        let scrolled = false;
                        for (let b = el.parentElement; b; b = b.parentElement) {
                            if (scrolls(b, side === 'Top' || side === 'Bottom' ? 'y' : 'x')) { scrolled = true; break; }
                            if (b === a) break;
                        }
                        if (scrolled) break;
                        const ar = a.getBoundingClientRect();
                        const bw = border(a, side);
                        const gap =
                            side === 'Top' ? r.top - (ar.top + bw) :
                            side === 'Bottom' ? (ar.bottom - bw) - r.bottom :
                            side === 'Left' ? r.left - (ar.left + bw) : (ar.right - bw) - r.right;
                        const along = side === 'Top' || side === 'Bottom'
                            ? overlap(r.left, r.right, ar.left, ar.right) : overlap(r.top, r.bottom, ar.top, ar.bottom);
                        // an accent bar on a hairline (a selected tab's underline) is an indicator,
                        // not the same line drawn twice
                        const ew = border(el, side), heavier = Math.max(ew, bw) >= 2 * Math.min(ew, bw);
                        if (!heavier && gap >= -0.5 && gap <= 1.5 && along >= 24)
                            out.push(`R2 double ${side.toLowerCase()} border: ${describe(el)} against ${describe(a)}`);
                        break;
                    }
                }
                // neighbour divider: my right edge touching a sibling's left edge, both drawn
                const next = el.nextElementSibling;
                if (next && set.has(next)) {
                    const nr = next.getBoundingClientRect();
                    const pairs = [['Right', 'Left', nr.left - r.right, overlap(r.top, r.bottom, nr.top, nr.bottom)],
                                   ['Bottom', 'Top', nr.top - r.bottom, overlap(r.left, r.right, nr.left, nr.right)]];
                    for (const [mine, theirs, gap, along] of pairs)
                        if (border(el, mine) && border(next, theirs) && gap >= -0.5 && gap <= 1.5 && along >= 24)
                            out.push(`R2 doubled divider: ${describe(el)} ${mine.toLowerCase()} + ${describe(next)} ${theirs.toLowerCase()}`);
                }
            }
            return out;
        }
        """;
}
