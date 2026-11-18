// The Web Animations API side of Shiny.Blazor.Controls.Keyframe.
//
// Everything about *what* animates is decided in .NET (KeyframeCompiler): the keyframes, the merged
// transform parts, the baked iteration delay, the easing as CSS. This module only runs the result:
//
//  1. Groups - one or more element.animate() effects played, paused, seeked and reversed as one, with
//     a finished callback to .NET. A Keyframes is a group of one item; a Choreography is many.
//  2. Live readings - the computed value of a property right now, so a key with no value starts
//     from wherever the element is (mid-flight included) rather than snapping.
//  3. Reduced motion - prefers-reduced-motion, read through matchMedia and kept live.
//  4. Item transitions - enter, exit and FLIP moves for TransitionGroup.

const reduceQuery = typeof matchMedia === 'function' ? matchMedia('(prefers-reduced-motion: reduce)') : null;
const liveGroups = new Set();

reduceQuery?.addEventListener?.('change', () => {
    for (const group of liveGroups)
        group.motionChanged();
});

export function isReducedMotion() {
    return !!reduceQuery?.matches;
}

function shouldSkip(mode) {
    if (mode === 'instant')
        return true;
    if (mode === 'play')
        return false;
    return isReducedMotion();
}

function resolveTarget(t, elements) {
    let el = t.e >= 0 ? elements[t.e] : null;
    if (!el && t.id)
        el = document.getElementById(t.id);
    if (el && t.c >= 0)
        el = el.children[t.c] ?? null;
    return el ?? null;
}

function toOptions(timing) {
    return {
        duration: Math.max(timing.duration, 0),
        delay: timing.delay,
        endDelay: timing.endDelay,
        iterations: timing.iterations < 0 ? Infinity : timing.iterations,
        iterationStart: timing.iterationStart,
        direction: timing.direction,
        fill: timing.fill,
        easing: timing.easing
    };
}


// ------------------------------------------------------------------------------------------ groups

export function createGroup(dotnet, elements, json) {
    const group = new Group(dotnet, elements, JSON.parse(json));
    liveGroups.add(group);
    return group;
}

class Group {
    constructor(dotnet, elements, program) {
        this.dotnet = dotnet;
        this.elements = elements ?? [];
        this.program = program;
        this.animations = [];
        this.generation = 0;
        this.rate = program.rate ?? 1;
        this.playing = false;
    }

    /** Replaces what the group will play next; nothing running is touched until play(). */
    load(elements, json) {
        this.elements = elements ?? [];
        this.program = JSON.parse(json);
    }

    build() {
        this.cancelAll();
        const placed = this.program.items
            .map(item => ({ item, el: resolveTarget(item.t, this.elements) }))
            .filter(p => p.el && typeof p.el.animate === 'function');

        for (const { item, el } of placed) {
            const options = toOptions(item.timing);
            for (const frames of item.effects) {
                try {
                    const animation = el.animate(frames, options);
                    animation.pause();
                    animation.playbackRate = this.rate;
                    this.animations.push(animation);
                } catch (e) {
                    // A keyframe the browser cannot parse throws here; one bad track must not take
                    // the rest of the animation down with it.
                    console.warn('Shiny Keyframes: an effect could not be created', e, frames);
                }
            }
        }
    }

    ensureBuilt() {
        if (this.animations.length === 0)
            this.build();
    }

    cancelAll() {
        for (const a of this.animations) {
            try { a.cancel(); } catch { }
        }
        this.animations = [];
    }

    /** Starts from the beginning. Returns the generation the finished callback will carry. */
    play() {
        const generation = ++this.generation;
        this.build();
        this.playing = true;

        if (shouldSkip(this.program.reduced))
            this.skip();
        else
            for (const a of this.animations) a.play();

        this.watch(generation);
        return generation;
    }

    watch(generation) {
        const animations = this.animations;
        if (animations.length === 0) {
            queueMicrotask(() => this.notifyFinished(generation));
            return;
        }

        Promise.all(animations.map(a => a.finished))
            .then(() => this.notifyFinished(generation))
            .catch(() => { /* cancelled: .NET asked for that itself */ });
    }

    notifyFinished(generation) {
        if (generation !== this.generation)
            return;
        this.playing = false;
        this.dotnet?.invokeMethodAsync('OnGroupFinished', generation).catch(() => { });
    }

    /**
     * Reduced motion: a finite animation jumps to its end state; a loop holds its first frame, since
     * there is no end to jump to and a frozen mid-loop pose reads as broken.
     */
    skip() {
        for (const a of this.animations) {
            const timing = a.effect?.getTiming?.() ?? {};
            if (timing.iterations === Infinity) {
                a.pause();
                a.currentTime = Math.max(timing.delay ?? 0, 0);
            } else {
                try { a.finish(); } catch { }
            }
        }
    }

    motionChanged() {
        if (this.playing && this.program.reduced === 'auto' && isReducedMotion())
            this.skip();
    }

    pause() {
        for (const a of this.animations) a.pause();
        this.playing = false;
    }

    resume() {
        if (this.animations.length === 0)
            return this.play();

        for (const a of this.animations) a.play();
        this.playing = true;
        const generation = ++this.generation;
        this.watch(generation);
        return generation;
    }

    /** restore: back to how the element looked before; otherwise hold the values where they are. */
    stop(restore) {
        this.generation++;
        this.playing = false;
        if (!restore) {
            for (const a of this.animations) {
                try { a.commitStyles(); } catch { }
            }
        }
        this.cancelAll();
    }

    finish() {
        this.ensureBuilt();
        for (const a of this.animations) {
            try {
                a.finish();
            } catch {
                // A loop has no end; park it at the end of its first pass instead.
                const t = a.effect.getComputedTiming();
                a.pause();
                a.currentTime = Math.max(t.delay ?? 0, 0) + (t.duration ?? 0);
            }
        }
    }

    seek(ms) {
        const fresh = this.animations.length === 0;
        this.ensureBuilt();
        for (const a of this.animations) {
            if (fresh)
                a.pause();
            a.currentTime = ms;
        }
    }

    seekProgress(progress) {
        this.seek(Math.max(0, Math.min(1, progress)) * (this.program.totalMs ?? 0));
    }

    setRate(rate) {
        this.rate = rate;
        for (const a of this.animations) {
            a.playbackRate = rate;
            // A finished animation sits at its end; running it backwards needs it playing again.
            if (this.playing && a.playState === 'finished')
                a.play();
        }
    }

    position() {
        const a = this.animations[0];
        return a && typeof a.currentTime === 'number' ? a.currentTime : 0;
    }

    dispose() {
        this.generation++;
        this.cancelAll();
        liveGroups.delete(this);
        this.dotnet = null;
        this.elements = [];
    }
}


// ------------------------------------------------------------------------------------------ live readings

function angle(text) {
    const m = /(-?[\d.e+-]+)(deg|rad|grad|turn)?\s*$/.exec(String(text).trim());
    if (!m)
        return 0;
    const v = parseFloat(m[1]);
    switch (m[2]) {
        case 'rad': return v * 180 / Math.PI;
        case 'grad': return v * 0.9;
        case 'turn': return v * 360;
        default: return v;
    }
}

function pair(text, fallback) {
    if (!text || text === 'none')
        return [fallback, fallback];
    const parts = String(text).trim().split(/\s+/);
    const x = parseFloat(parts[0]);
    const y = parts.length > 1 ? parseFloat(parts[1]) : (fallback === 1 ? x : 0);
    return [isNaN(x) ? fallback : x, isNaN(y) ? fallback : y];
}

function rotation3d(el, style) {
    const t = style.transform;
    if (!t || t === 'none')
        return [0, 0];
    try {
        const m = new DOMMatrixReadOnly(t);
        // M = Rx(a) * Ry(b): b from row 0, a from column 1 (perspective only touches row 3).
        const b = Math.atan2(m.m31, m.m11) * 180 / Math.PI;
        const a = Math.atan2(m.m23, m.m22) * 180 / Math.PI;
        return [a, b];
    } catch {
        return [0, 0];
    }
}

function readOne(el, style, name) {
    switch (name.toLowerCase()) {
        case 'opacity': return style.opacity;
        case 'scale': return String(pair(style.scale, 1)[0]);
        case 'scalex': return String(pair(style.scale, 1)[0]);
        case 'scaley': return String(pair(style.scale, 1)[1]);
        case 'translationx': return String(pair(style.translate, 0)[0]);
        case 'translationy': return String(pair(style.translate, 0)[1]);
        case 'rotation':
        case 'spin': return style.rotate && style.rotate !== 'none' ? String(angle(style.rotate)) : '0';
        case 'rotationx': return String(rotation3d(el, style)[0]);
        case 'rotationy': return String(rotation3d(el, style)[1]);
        case 'anchorx': {
            const [x] = pair(style.transformOrigin, 0);
            return String(el.offsetWidth ? x / el.offsetWidth : 0.5);
        }
        case 'anchory': {
            const [, y] = pair(style.transformOrigin, 0);
            return String(el.offsetHeight ? y / el.offsetHeight : 0.5);
        }
        case 'backgroundcolor':
        case 'background': return style.backgroundColor;
        case 'textcolor': return style.color;
        case 'widthrequest': return style.width;
        case 'heightrequest': return style.height;
        case 'margin': return `${style.marginTop} ${style.marginRight} ${style.marginBottom} ${style.marginLeft}`;
        case 'padding': return `${style.paddingTop} ${style.paddingRight} ${style.paddingBottom} ${style.paddingLeft}`;
        default: {
            const css = name.startsWith('--') || name.includes('-')
                ? name
                : name.replace(/[A-Z]/g, (c, i) => (i ? '-' : '') + c.toLowerCase());
            return style.getPropertyValue(css);
        }
    }
}

/** Live values for one target, as strings in the order asked. */
export function readLive(elements, elementIndex, id, childIndex, names) {
    const el = resolveTarget({ e: elementIndex, id, c: childIndex }, elements ?? []);
    if (!el)
        return names.map(() => null);
    const style = getComputedStyle(el);
    return names.map(n => readOne(el, style, n));
}

/** Live values for every child element of a root: one row per child. */
export function readLiveChildren(root, names) {
    if (!root)
        return [];
    return Array.from(root.children).map(child => {
        const style = getComputedStyle(child);
        return names.map(n => readOne(child, style, n));
    });
}

export function childCount(root) {
    return root ? root.children.length : 0;
}


// ------------------------------------------------------------------------------------------ item transitions

const ITEM_KEY = 'data-shiny-tg-key';

export function attachTransitionGroup(container, dotnet) {
    return new TransitionGroupHandle(container, dotnet);
}

class TransitionGroupHandle {
    constructor(container, dotnet) {
        this.container = container;
        this.dotnet = dotnet;
        this.rects = new Map();
        this.moves = new Map();
        this.leaving = new Map();
    }

    items() {
        return Array.from(this.container.querySelectorAll(`:scope > [${ITEM_KEY}]`));
    }

    relativeRect(el) {
        const c = this.container.getBoundingClientRect();
        const r = el.getBoundingClientRect();
        return { x: r.left - c.left - this.container.clientLeft, y: r.top - c.top - this.container.clientTop, w: r.width, h: r.height };
    }

    /** Called just before .NET renders a change: where everything is on screen right now. */
    snapshot() {
        this.rects.clear();
        for (const el of this.items())
            this.rects.set(el.getAttribute(ITEM_KEY), this.relativeRect(el));
    }

    /**
     * Called after the render. entering/leaving are item keys; enter and leave are compiled programs
     * (frames per effect plus timing) to run on each.
     */
    update(entering, leaving, enterJson, leaveJson, moveMs, moveEasing, staggerMs, reduced) {
        const skip = shouldSkip(reduced);
        const enter = enterJson ? JSON.parse(enterJson) : null;
        const leave = leaveJson ? JSON.parse(leaveJson) : null;
        const byKey = new Map(this.items().map(el => [el.getAttribute(ITEM_KEY), el]));

        // An item put back while it was still animating out: stop the exit and return it to the flow.
        for (const key of entering.filter(k => this.leaving.has(k))) {
            for (const a of this.leaving.get(key)) a.cancel();
            this.leaving.delete(key);
            const el = byKey.get(key);
            if (el)
                for (const p of ['position', 'left', 'top', 'width', 'height', 'margin', 'pointerEvents']) el.style[p] = '';
        }

        // Leaving items come out of the flow first, pinned where they were, so the rest can move into
        // the space at the same time as they fade.
        let leaveIndex = 0;
        const fresh = leaving.filter(key => !this.leaving.has(key));
        for (const key of fresh.filter(k => !byKey.has(k)))
            this.left(key);

        for (const key of fresh.filter(k => byKey.has(k) && (skip || !leave)))
            this.left(key);

        for (const key of fresh.filter(k => byKey.has(k) && !skip && leave)) {
            const el = byKey.get(key);
            const rect = this.rects.get(key) ?? this.relativeRect(el);
            Object.assign(el.style, {
                position: 'absolute', left: `${rect.x}px`, top: `${rect.y}px`,
                width: `${rect.w}px`, height: `${rect.h}px`, margin: '0', pointerEvents: 'none'
            });
            const animations = this.run(el, leave, leaveIndex++ * staggerMs);
            this.leaving.set(key, animations);
            Promise.all(animations.map(a => a.finished))
                .then(() => this.left(key))
                .catch(() => { });
            if (animations.length === 0)
                this.left(key);
        }

        // FLIP: everything that stayed and moved slides from where it was on screen to where it is.
        if (moveMs > 0 && !skip) {
            const staying = [...byKey]
                .filter(([key]) => this.rects.has(key) && !this.leaving.has(key) && !entering.includes(key));

            // Measure without the moves still in flight, then start each from where it was on screen.
            for (const [key] of staying) {
                this.moves.get(key)?.cancel();
                this.moves.delete(key);
            }

            const moved = staying
                .map(([key, el]) => {
                    const before = this.rects.get(key);
                    const after = this.relativeRect(el);
                    return { key, el, dx: before.x - after.x, dy: before.y - after.y };
                })
                .filter(m => Math.abs(m.dx) >= 0.5 || Math.abs(m.dy) >= 0.5);

            for (const { key, el, dx, dy } of moved) {
                const move = el.animate(
                    [{ translate: `${dx}px ${dy}px` }, { translate: '0px 0px' }],
                    { duration: moveMs, easing: moveEasing, composite: 'add' });
                this.moves.set(key, move);
                move.finished.then(() => this.moves.delete(key)).catch(() => { });
            }
        }

        let enterIndex = 0;
        for (const el of entering.map(key => byKey.get(key)).filter(el => el)) {
            el.removeAttribute('data-shiny-tg-entering');
            if (!skip && enter) {
                // An enter ends on the item's own look; once done it lets go, so finished effects never pile up.
                const animations = this.run(el, enter, enterIndex++ * staggerMs);
                Promise.all(animations.map(a => a.finished))
                    .then(() => { for (const a of animations) a.cancel(); })
                    .catch(() => { });
            }
        }
    }

    run(el, program, extraDelay) {
        const options = toOptions(program.timing);
        options.delay = (options.delay ?? 0) + extraDelay;
        const result = [];
        for (const frames of program.effects) {
            try {
                result.push(el.animate(frames, options));
            } catch (e) {
                console.warn('Shiny TransitionGroup: an effect could not be created', e, frames);
            }
        }
        return result;
    }

    left(key) {
        this.leaving.delete(key);
        this.dotnet?.invokeMethodAsync('OnItemLeft', key).catch(() => { });
    }

    dispose() {
        for (const m of this.moves.values()) m.cancel();
        for (const list of this.leaving.values()) for (const a of list) a.cancel();
        this.moves.clear();
        this.leaving.clear();
        this.dotnet = null;
    }
}
