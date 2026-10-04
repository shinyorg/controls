// Marquee for Shiny.Blazor.Controls.
//
// The movement is pure CSS. This only measures: how many copies it takes to keep the box filled for a
// whole loop, how long a loop takes when a speed is given instead of a duration, and how much padding
// keeps upright items from overlapping on an angled strip. Values the script owns are written onto the
// strip, which Blazor never restyles, so a re-render cannot wipe them.

const states = new WeakMap();

export function init(root, dotnet, json) {
    if (!root) return;
    const state = { dotnet, opts: JSON.parse(json), copies: 0, raf: 0, observer: null, pressed: null };

    if (typeof ResizeObserver === 'function') {
        state.observer = new ResizeObserver(() => schedule(root, state));
        state.observer.observe(root);
        const group = root.querySelector('.shiny-marquee__group');
        if (group) state.observer.observe(group);
    }

    states.set(root, state);
    wirePress(root, state);
    measure(root, state);
}

export function update(root, json) {
    const state = states.get(root);
    if (!state) return;
    state.opts = JSON.parse(json);
    wirePress(root, state);
    schedule(root, state);
}

export function dispose(root) {
    const state = states.get(root);
    if (!state) return;
    state.observer?.disconnect();
    cancelAnimationFrame(state.raf);
    unwirePress(root, state);
    states.delete(root);
}

function schedule(root, state) {
    cancelAnimationFrame(state.raf);
    state.raf = requestAnimationFrame(() => measure(root, state));
}

function measure(root, state) {
    const strip = root.querySelector('.shiny-marquee__strip');
    const group = root.querySelector('.shiny-marquee__group');
    if (!strip || !group) return;

    const { mode, angle, gap, repeat, speed, upright } = state.opts;
    const rad = angle * Math.PI / 180;

    // Pad upright items on an angled strip by half the tallest one's projection along it.
    if (mode === 'angled' && upright) {
        let tallest = 0;
        for (const child of group.children) tallest = Math.max(tallest, child.offsetHeight);
        strip.style.setProperty('--shiny-marquee-upright-pad', `${(tallest * Math.abs(Math.sin(rad))) / 2}px`);
    } else {
        strip.style.removeProperty('--shiny-marquee-upright-pad');
    }

    // Layout sizes, not getBoundingClientRect: the strip may be rotated, and transforms do not
    // change offsetWidth.
    let length, viewport, extra;
    if (mode === 'vertical') {
        length = group.offsetHeight;
        viewport = root.clientHeight;
        extra = 1;
    } else if (mode === 'horizontal') {
        length = group.offsetWidth;
        viewport = root.clientWidth;
        extra = 1;
    } else {
        // A centred strip shifted back by up to one period must still span the box along the axis.
        length = group.offsetWidth;
        viewport = Math.abs(root.clientWidth * Math.cos(rad)) + Math.abs(root.clientHeight * Math.sin(rad));
        extra = 2;
    }

    const period = length + gap;
    if (period <= gap) return;

    if (speed > 0) strip.style.setProperty('--shiny-marquee-speed-duration', `${period / speed}s`);
    else strip.style.removeProperty('--shiny-marquee-speed-duration');

    const needed = Math.max(repeat, Math.ceil(viewport / period) + extra);
    if (needed !== state.copies) {
        state.copies = needed;
        state.dotnet.invokeMethodAsync('SetCopies', needed).catch(() => { });
    }
}

// :active is unreliable for touch on iOS Safari, so pressing is tracked with pointer events and shown
// as a class on the strip.
function wirePress(root, state) {
    if (state.opts.pauseOnPress && !state.pressed) {
        const strip = () => root.querySelector('.shiny-marquee__strip');
        const down = () => strip()?.classList.add('is-pressed');
        const up = () => strip()?.classList.remove('is-pressed');
        state.pressed = { down, up };
        root.addEventListener('pointerdown', down);
        for (const type of ['pointerup', 'pointercancel', 'pointerleave']) root.addEventListener(type, up);
    } else if (!state.opts.pauseOnPress && state.pressed) {
        unwirePress(root, state);
    }
}

function unwirePress(root, state) {
    if (!state.pressed) return;
    root.removeEventListener('pointerdown', state.pressed.down);
    for (const type of ['pointerup', 'pointercancel', 'pointerleave']) root.removeEventListener(type, state.pressed.up);
    root.querySelector('.shiny-marquee__strip')?.classList.remove('is-pressed');
    state.pressed = null;
}
