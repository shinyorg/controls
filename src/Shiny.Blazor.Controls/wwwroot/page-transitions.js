// Page transitions for PageTransitionHost, on the browser's View Transitions API.
//
// The host calls begin() from the router's location-changing hook: the browser captures the page as
// it is, and begin() resolves as soon as that picture exists, so the navigation can go ahead and the
// router render the incoming page underneath it. Once that render is done the host calls finish()
// with the resolved transition, which picks the animations and lets the browser run them.
//
// The ::view-transition pseudo-elements cannot be reached by Blazor's scoped CSS (it rewrites the
// rightmost selector with an attribute they never carry), so the stylesheet is injected from here.

const STYLE_ID = 'shiny-page-transitions-style';
const HERO_ATTR = 'data-shiny-hero';
const SAFETY_MS = 2500;

let pending = null;
let lastPointer = null;
const origins = new Map(); // url -> {x, y} in viewport coordinates, for the circular reveal's way back

injectStyles();

document.addEventListener('pointerdown', e => {
    lastPointer = { x: e.clientX, y: e.clientY };
}, { capture: true, passive: true });


function supported() {
    return typeof document.startViewTransition === 'function' &&
        !window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}


/** Captures the current page. Resolves true once the picture exists, false when nothing will animate. */
export function begin() {
    if (!supported())
        return Promise.resolve(false);

    // A navigation that starts before the last one finished: let the old one go at once.
    if (pending)
        release(pending, false);

    const used = nameHeroes();
    const pointer = lastPointer;

    return new Promise(captured => {
        let releaseUpdate;
        const update = new Promise(r => releaseUpdate = r);

        let transition;
        try {
            transition = document.startViewTransition(() => {
                captured(true);
                return update;
            });
        } catch {
            clearHeroes();
            captured(false);
            return;
        }

        const state = {
            transition,
            releaseUpdate,
            oldHeroes: used,
            pointer,
            done: false,
            // If the navigation is cancelled (a NavigationLock, say) finish() never comes; the page
            // must not stay frozen behind the captured picture.
            timer: setTimeout(() => release(state, false), SAFETY_MS)
        };
        pending = state;

        transition.ready.catch(() => { });
        transition.finished.finally(() => {
            if (pending === state)
                pending = null;
            cleanup();
        });
    });
}


/** Runs the transition for the page now rendered. */
export function finish(type, isPop, x, y, durationMs, sourceUrl, targetUrl) {
    const state = pending;
    if (!state || state.done)
        return;

    const newHeroes = nameHeroes();
    const sharesHero = [...newHeroes].some(n => state.oldHeroes.has(n));

    // Nothing set anywhere: a shared hero still flies, over a crossfade.
    if (type === 'default' && sharesHero)
        type = 'fade';

    if (type === 'default' || type === 'none' || !ANIMATIONS[type]) {
        release(state, false);
        return;
    }

    const root = document.documentElement;
    const host = document.querySelector('.shiny-page-transition');
    const [oldAnim, newAnim] = pick(type, isPop);

    root.style.setProperty('--shiny-vt-duration', `${durationMs}ms`);
    root.style.setProperty('--shiny-vt-x', String(x));
    root.style.setProperty('--shiny-vt-y', String(y));
    root.style.setProperty('--shiny-vt-old', oldAnim ? `shiny-vt-${oldAnim}` : 'none');
    root.style.setProperty('--shiny-vt-new', newAnim ? `shiny-vt-${newAnim}` : 'none');

    if (type === 'sharedaxisx' || type === 'sharedaxisy') {
        const vertical = y !== 0;
        const sign = isPop ? -1 : 1;
        const ax = type === 'sharedaxisx' ? (vertical ? 30 : x * 30) : 0;
        const ay = type === 'sharedaxisy' ? (vertical ? y * 30 : 30) : 0;
        root.style.setProperty('--shiny-vt-ax', `${ax * sign}px`);
        root.style.setProperty('--shiny-vt-ay', `${ay * sign}px`);
    }

    if (type === 'sharedaxisz') {
        root.style.setProperty('--shiny-vt-zin', isPop ? '1.1' : '0.8');
        root.style.setProperty('--shiny-vt-zout', isPop ? '0.8' : '1.1');
    }

    if (type === 'flip') {
        const horizontal = y === 0;
        const turn = 90 * (horizontal ? x : -y) * (isPop ? -1 : 1);
        root.style.setProperty('--shiny-vt-fx', horizontal ? '0' : '1');
        root.style.setProperty('--shiny-vt-fy', horizontal ? '1' : '0');
        root.style.setProperty('--shiny-vt-turn', `${turn}deg`);
    }

    if (type === 'circularreveal' && host) {
        // Grows out of the click that started the navigation and remembers it, so going back
        // shrinks into the same point. Coordinates are relative to the host, whose box the
        // snapshot fills.
        let origin = isPop ? origins.get(sourceUrl) : state.pointer;
        if (!isPop && origin)
            origins.set(targetUrl, origin);

        const rect = host.getBoundingClientRect();
        const cx = origin ? origin.x - rect.left : rect.width / 2;
        const cy = origin ? origin.y - rect.top : rect.height / 2;
        const r = Math.hypot(Math.max(cx, rect.width - cx), Math.max(cy, rect.height - cy));
        root.style.setProperty('--shiny-vt-cx', `${cx}px`);
        root.style.setProperty('--shiny-vt-cy', `${cy}px`);
        root.style.setProperty('--shiny-vt-r', `${r}px`);
    }

    // The page that moves goes on top: the incoming one on a push, the outgoing one on the way back -
    // except a reveal, which uncovers the page underneath.
    const oldOnTop = isPop ? type !== 'reveal' : type === 'reveal';
    root.toggleAttribute('data-shiny-vt-oldtop', oldOnTop);
    root.setAttribute('data-shiny-vt', type);

    release(state, true);
}


function release(state, animate) {
    if (state.done)
        return;

    state.done = true;
    clearTimeout(state.timer);
    if (!animate) {
        try { state.transition.skipTransition(); } catch { }
    }
    state.releaseUpdate();
    if (pending === state)
        pending = null;
}


function cleanup() {
    const root = document.documentElement;
    root.removeAttribute('data-shiny-vt');
    root.removeAttribute('data-shiny-vt-oldtop');
    for (const name of ['duration', 'x', 'y', 'old', 'new', 'ax', 'ay', 'zin', 'zout', 'fx', 'fy', 'turn', 'cx', 'cy', 'r'])
        root.style.removeProperty(`--shiny-vt-${name}`);
    clearHeroes();
}


/**
 * Gives each hero on the page its view-transition-name. Names must be unique in a snapshot - a
 * duplicate makes the browser abandon the whole transition - so only the first element with a given
 * hero name takes part.
 */
function nameHeroes() {
    const used = new Set();
    for (const el of document.querySelectorAll(`[${HERO_ATTR}]`)) {
        const name = 'shiny-hero-' + String(el.getAttribute(HERO_ATTR)).replace(/[^a-zA-Z0-9_-]/g, '-');
        if (used.has(name) || !isShowing(el)) {
            el.style.viewTransitionName = '';
            continue;
        }
        el.style.viewTransitionName = name;
        used.add(name);
    }
    return used;
}


function clearHeroes() {
    for (const el of document.querySelectorAll(`[${HERO_ATTR}]`))
        el.style.viewTransitionName = '';
}


function isShowing(el) {
    const rect = el.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0 &&
        rect.bottom > 0 && rect.right > 0 &&
        rect.top < window.innerHeight && rect.left < window.innerWidth;
}


// [old, new] keyframe names per transition, for a push and for the way back.
const ANIMATIONS = {
    fade: { push: [null, 'fade-in'], pop: ['fade-out', null] },
    fadethrough: { push: ['fade-out-early', 'fade-in-late-grow'], pop: ['fade-out-early', 'fade-in-late-grow'] },
    slide: { push: ['to-behind', 'from-edge'], pop: ['to-edge', 'from-behind'] },
    push: { push: ['to-opposite', 'from-edge'], pop: ['to-edge', 'from-opposite'] },
    cover: { push: [null, 'from-edge'], pop: ['to-edge', null] },
    reveal: { push: ['to-opposite', null], pop: [null, 'from-opposite'] },
    sharedaxisx: { push: ['axis-out', 'axis-in'], pop: ['axis-out', 'axis-in'] },
    sharedaxisy: { push: ['axis-out', 'axis-in'], pop: ['axis-out', 'axis-in'] },
    sharedaxisz: { push: ['z-out', 'z-in'], pop: ['z-out', 'z-in'] },
    zoom: { push: [null, 'zoom-in'], pop: ['zoom-out', null] },
    circularreveal: { push: [null, 'circle-grow'], pop: ['circle-shrink', null] },
    flip: { push: ['flip-out', 'flip-in'], pop: ['flip-out', 'flip-in'] }
};

function pick(type, isPop) {
    const entry = ANIMATIONS[type];
    return isPop ? entry.pop : entry.push;
}


function injectStyles() {
    if (document.getElementById(STYLE_ID))
        return;

    const style = document.createElement('style');
    style.id = STYLE_ID;
    style.textContent = `
/* The chrome around the host does not animate; only the page inside it does. */
:root[data-shiny-vt]::view-transition-old(root),
:root[data-shiny-vt]::view-transition-new(root) {
    animation: none;
}

:root[data-shiny-vt]::view-transition-group(*) {
    animation-duration: var(--shiny-vt-duration, 320ms);
    animation-timing-function: cubic-bezier(0.4, 0, 0.2, 1);
}

:root[data-shiny-vt]::view-transition-old(shiny-page),
:root[data-shiny-vt]::view-transition-new(shiny-page) {
    animation-name: none;
    animation-duration: var(--shiny-vt-duration, 320ms);
    animation-timing-function: cubic-bezier(0.4, 0, 0.2, 1);
    animation-fill-mode: both;
    /* the browser's default crossfade adds the two pictures; a page sliding over another must not glow */
    mix-blend-mode: normal;
}

:root[data-shiny-vt]::view-transition-old(shiny-page) { animation-name: var(--shiny-vt-old, none); }
:root[data-shiny-vt]::view-transition-new(shiny-page) { animation-name: var(--shiny-vt-new, none); }
:root[data-shiny-vt][data-shiny-vt-oldtop]::view-transition-old(shiny-page) { z-index: 1; }

@keyframes shiny-vt-fade-in { from { opacity: 0; } }
@keyframes shiny-vt-fade-out { to { opacity: 0; } }
@keyframes shiny-vt-fade-out-early { 0% { opacity: 1; } 35%, 100% { opacity: 0; } }
@keyframes shiny-vt-fade-in-late-grow { 0%, 35% { opacity: 0; transform: scale(0.92); } 100% { opacity: 1; transform: none; } }

@keyframes shiny-vt-from-edge { from { transform: translate(calc(var(--shiny-vt-x) * 100%), calc(var(--shiny-vt-y) * 100%)); } }
@keyframes shiny-vt-to-edge { to { transform: translate(calc(var(--shiny-vt-x) * 100%), calc(var(--shiny-vt-y) * 100%)); } }
@keyframes shiny-vt-from-behind { from { transform: translate(calc(var(--shiny-vt-x) * -30%), calc(var(--shiny-vt-y) * -30%)); } }
@keyframes shiny-vt-to-behind { to { transform: translate(calc(var(--shiny-vt-x) * -30%), calc(var(--shiny-vt-y) * -30%)); } }
@keyframes shiny-vt-from-opposite { from { transform: translate(calc(var(--shiny-vt-x) * -100%), calc(var(--shiny-vt-y) * -100%)); } }
@keyframes shiny-vt-to-opposite { to { transform: translate(calc(var(--shiny-vt-x) * -100%), calc(var(--shiny-vt-y) * -100%)); } }

@keyframes shiny-vt-axis-in {
    0% { opacity: 0; transform: translate(var(--shiny-vt-ax, 0), var(--shiny-vt-ay, 0)); }
    30% { opacity: 0; }
    100% { opacity: 1; transform: none; }
}
@keyframes shiny-vt-axis-out {
    0% { opacity: 1; }
    30% { opacity: 0; }
    100% { opacity: 0; transform: translate(calc(var(--shiny-vt-ax, 0) * -1), calc(var(--shiny-vt-ay, 0) * -1)); }
}

@keyframes shiny-vt-z-in {
    0% { opacity: 0; transform: scale(var(--shiny-vt-zin, 0.8)); }
    30% { opacity: 0; }
    100% { opacity: 1; transform: none; }
}
@keyframes shiny-vt-z-out {
    0% { opacity: 1; }
    30% { opacity: 0; }
    100% { opacity: 0; transform: scale(var(--shiny-vt-zout, 1.1)); }
}

@keyframes shiny-vt-zoom-in { from { opacity: 0; transform: scale(0.85); } }
@keyframes shiny-vt-zoom-out { to { opacity: 0; transform: scale(0.85); } }

@keyframes shiny-vt-circle-grow {
    from { clip-path: circle(0 at var(--shiny-vt-cx, 50%) var(--shiny-vt-cy, 50%)); }
    to { clip-path: circle(var(--shiny-vt-r, 150%) at var(--shiny-vt-cx, 50%) var(--shiny-vt-cy, 50%)); }
}
@keyframes shiny-vt-circle-shrink {
    from { clip-path: circle(var(--shiny-vt-r, 150%) at var(--shiny-vt-cx, 50%) var(--shiny-vt-cy, 50%)); }
    to { clip-path: circle(0 at var(--shiny-vt-cx, 50%) var(--shiny-vt-cy, 50%)); }
}

@keyframes shiny-vt-flip-out {
    0% { opacity: 1; transform: perspective(1400px) rotate3d(var(--shiny-vt-fx, 0), var(--shiny-vt-fy, 1), 0, 0deg); }
    49.9% { opacity: 1; }
    50% { opacity: 0; transform: perspective(1400px) rotate3d(var(--shiny-vt-fx, 0), var(--shiny-vt-fy, 1), 0, var(--shiny-vt-turn, 90deg)); }
    100% { opacity: 0; transform: perspective(1400px) rotate3d(var(--shiny-vt-fx, 0), var(--shiny-vt-fy, 1), 0, var(--shiny-vt-turn, 90deg)); }
}
@keyframes shiny-vt-flip-in {
    0% { opacity: 0; transform: perspective(1400px) rotate3d(var(--shiny-vt-fx, 0), var(--shiny-vt-fy, 1), 0, calc(var(--shiny-vt-turn, 90deg) * -1)); }
    50% { opacity: 0; transform: perspective(1400px) rotate3d(var(--shiny-vt-fx, 0), var(--shiny-vt-fy, 1), 0, calc(var(--shiny-vt-turn, 90deg) * -1)); }
    50.1% { opacity: 1; }
    100% { opacity: 1; transform: perspective(1400px) rotate3d(var(--shiny-vt-fx, 0), var(--shiny-vt-fy, 1), 0, 0deg); }
}
`;
    document.head.appendChild(style);
}
