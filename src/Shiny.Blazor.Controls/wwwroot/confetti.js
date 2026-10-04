// Confetti for Shiny.Blazor.Controls.
//
// The physics are canvas-confetti's (the library Magic UI's confetti wraps), frame for frame, and the
// presets mirror the MAUI host's ConfettiPresets so a burst looks the same on both. One fixed,
// click-through canvas per document is created on the first burst and removed once the air is clear.

const DEFAULT_COLORS = ['#26ccff', '#a25afd', '#ff5e7e', '#88ff5a', '#fcff42', '#ffa62d', '#ff36ff'];
const DEFAULTS = {
    particleCount: 50, angle: 90, spread: 45, startVelocity: 45, decay: 0.9, gravity: 1, drift: 0,
    flat: false, ticks: 200, originX: 0.5, originY: 0.5, colors: DEFAULT_COLORS,
    shapes: ['square', 'circle'], emoji: [], scalar: 1, disableForReducedMotion: false
};
const FRAME_MS = 1000 / 60;
// A stall (a hidden tab, a GC) would otherwise fast-forward a whole burst out of existence in one paint.
const MAX_STEPS_PER_FRAME = 4;

let canvas = null;
let ctx = null;
let particles = [];
let pending = [];
let runs = new Set();
let raf = 0;
let lastTime = 0;
let clock = 0;
let accumulator = 0;
const attached = new Map();

function options(input) {
    const o = Object.assign({}, DEFAULTS, input || {});
    o.shapes = (o.shapes && o.shapes.length ? o.shapes : ['square']).map(s => String(s).toLowerCase());
    o.colors = o.colors && o.colors.length ? o.colors : DEFAULT_COLORS;
    o.emoji = o.emoji || [];
    return o;
}

const between = (min, max) => min + Math.random() * (max - min);
const reducedMotion = () => typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;

function presetShots(name, x, y) {
    x = x ?? 0.5;
    y = y ?? 0.6;
    switch (String(name).toLowerCase()) {
        case 'random':
            return [{ delay: 0, options: { angle: between(55, 125), spread: between(50, 70), particleCount: Math.floor(between(50, 100)), originX: x, originY: y } }];

        case 'fireworks': {
            const shots = [];
            for (let at = 0; at < 5000; at += 250) {
                const count = Math.floor(50 * ((5000 - at) / 5000));
                if (count <= 0) break;
                for (const [min, max] of [[0.1, 0.3], [0.7, 0.9]]) {
                    shots.push({ delay: at, options: { particleCount: count, startVelocity: 30, spread: 360, ticks: 60, originX: between(min, max), originY: Math.random() - 0.2 } });
                }
            }
            return shots;
        }

        case 'sidecannons': {
            const colors = ['#a786ff', '#fd8bbc', '#eca184', '#f8deb1'];
            const shots = [];
            for (let at = 0; at < 3000; at += 50) {
                shots.push({ delay: at, options: { particleCount: 4, angle: 60, spread: 55, startVelocity: 60, originX: 0, originY: 0.5, colors } });
                shots.push({ delay: at, options: { particleCount: 4, angle: 120, spread: 55, startVelocity: 60, originX: 1, originY: 0.5, colors } });
            }
            return shots;
        }

        case 'stars': {
            const colors = ['#ffe400', '#ffbd00', '#e89400', '#ffca6c', '#fdffb8'];
            const base = { spread: 360, ticks: 50, gravity: 0, decay: 0.94, startVelocity: 30, originX: x, originY: y, colors };
            const shots = [];
            for (const at of [0, 100, 200]) {
                shots.push({ delay: at, options: { ...base, particleCount: 40, scalar: 1.2, shapes: ['star'] } });
                shots.push({ delay: at, options: { ...base, particleCount: 10, scalar: 0.75, shapes: ['circle'] } });
            }
            return shots;
        }

        default:
            return [{ delay: 0, options: { particleCount: 100, spread: 70, originX: x, originY: y } }];
    }
}

function ensureCanvas() {
    if (canvas && canvas.isConnected) return;
    canvas = document.createElement('canvas');
    canvas.className = 'shiny-confetti-canvas';
    canvas.setAttribute('aria-hidden', 'true');
    Object.assign(canvas.style, {
        position: 'fixed', inset: '0', width: '100vw', height: '100vh',
        pointerEvents: 'none', zIndex: '2147483000'
    });
    document.body.appendChild(canvas);
    ctx = canvas.getContext('2d');
    resize();
    window.addEventListener('resize', resize);
}

function resize() {
    if (!canvas) return;
    const dpr = window.devicePixelRatio || 1;
    canvas.width = Math.round(window.innerWidth * dpr);
    canvas.height = Math.round(window.innerHeight * dpr);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
}

function teardown() {
    cancelAnimationFrame(raf);
    raf = 0;
    window.removeEventListener('resize', resize);
    canvas?.remove();
    canvas = null;
    ctx = null;
}

function emit(o, run) {
    const x = o.originX * window.innerWidth;
    const y = o.originY * window.innerHeight;
    const radAngle = o.angle * (Math.PI / 180);
    const radSpread = o.spread * (Math.PI / 180);
    const count = Math.max(0, Math.floor(o.particleCount));

    for (let i = 0; i < count; i++) {
        particles.push({
            x, y,
            wobble: Math.random() * 10,
            wobbleSpeed: Math.min(0.11, Math.random() * 0.1 + 0.05),
            velocity: o.startVelocity * 0.5 + Math.random() * o.startVelocity,
            angle2D: -radAngle + (0.5 * radSpread - Math.random() * radSpread),
            tiltAngle: (Math.random() * 0.5 + 0.25) * Math.PI,
            color: o.colors[Math.floor(Math.random() * o.colors.length)],
            shape: o.shapes[Math.floor(Math.random() * o.shapes.length)],
            text: o.emoji.length ? o.emoji[Math.floor(Math.random() * o.emoji.length)] : null,
            tick: 0,
            totalTicks: Math.max(1, o.ticks),
            decay: o.decay, drift: o.drift, random: Math.random() + 2,
            tiltSin: 0, tiltCos: 0, wobbleX: 0, wobbleY: 0,
            gravity: o.gravity * 3, scalar: o.scalar, flat: o.flat, run
        });
    }
    run.live += count;
}

function step() {
    for (let i = particles.length - 1; i >= 0; i--) {
        const p = particles[i];
        p.x += Math.cos(p.angle2D) * p.velocity + p.drift;
        p.y += Math.sin(p.angle2D) * p.velocity + p.gravity;
        p.velocity *= p.decay;

        if (p.flat) {
            p.wobble = 0;
            p.wobbleX = p.x + 10 * p.scalar;
            p.wobbleY = p.y + 10 * p.scalar;
            p.tiltSin = 0; p.tiltCos = 0; p.random = 1;
        } else {
            p.wobble += p.wobbleSpeed;
            p.wobbleX = p.x + 10 * p.scalar * Math.cos(p.wobble);
            p.wobbleY = p.y + 10 * p.scalar * Math.sin(p.wobble);
            p.tiltAngle += 0.1;
            p.tiltSin = Math.sin(p.tiltAngle);
            p.tiltCos = Math.cos(p.tiltAngle);
            p.random = Math.random() + 2;
        }

        p.tick++;
        if (p.tick >= p.totalTicks) {
            particles.splice(i, 1);
            p.run.live--;
            settle(p.run);
        }
    }
}

function draw() {
    ctx.clearRect(0, 0, window.innerWidth, window.innerHeight);
    for (const p of particles) {
        ctx.save();
        ctx.globalAlpha = Math.max(0, 1 - p.tick / p.totalTicks);
        const x1 = p.x + p.random * p.tiltCos;
        const y1 = p.y + p.random * p.tiltSin;
        const x2 = p.wobbleX + p.random * p.tiltCos;
        const y2 = p.wobbleY + p.random * p.tiltSin;

        if (p.text) {
            ctx.translate(p.x, p.y);
            ctx.rotate(Math.PI / 10 * p.wobble);
            ctx.font = `${16 * p.scalar}px sans-serif`;
            ctx.textAlign = 'center';
            ctx.fillText(p.text, 0, 0);
        } else {
            ctx.fillStyle = p.color;
            ctx.beginPath();
            if (p.shape === 'circle') {
                ctx.ellipse(p.x, p.y, Math.max(0.5, Math.abs(x2 - x1) * 0.6), Math.max(0.5, Math.abs(y2 - y1) * 0.6), Math.PI / 10 * p.wobble, 0, 2 * Math.PI);
            } else if (p.shape === 'star') {
                let rot = Math.PI / 2 * 3;
                const inner = 4 * p.scalar, outer = 8 * p.scalar, stepAngle = Math.PI / 5;
                for (let i = 0; i < 5; i++) {
                    ctx.lineTo(p.x + Math.cos(rot) * outer, p.y + Math.sin(rot) * outer);
                    rot += stepAngle;
                    ctx.lineTo(p.x + Math.cos(rot) * inner, p.y + Math.sin(rot) * inner);
                    rot += stepAngle;
                }
            } else {
                ctx.moveTo(Math.floor(p.x), Math.floor(p.y));
                ctx.lineTo(Math.floor(p.wobbleX), Math.floor(y1));
                ctx.lineTo(Math.floor(x2), Math.floor(y2));
                ctx.lineTo(Math.floor(x1), Math.floor(p.wobbleY));
            }
            ctx.closePath();
            ctx.fill();
        }
        ctx.restore();
    }
}

function launchDue() {
    for (let i = 0; i < pending.length; i++) {
        const item = pending[i];
        if (item.due > clock) continue;
        pending.splice(i--, 1);
        item.run.pendingShots--;
        emit(item.options, item.run);
        settle(item.run);
    }
}

function frame(now) {
    const delta = lastTime ? Math.min(250, now - lastTime) : 0;
    lastTime = now;
    clock += delta;
    accumulator += delta;
    launchDue();

    let steps = 0;
    while (accumulator >= FRAME_MS && steps < MAX_STEPS_PER_FRAME) {
        step();
        accumulator -= FRAME_MS;
        steps++;
    }
    if (steps === MAX_STEPS_PER_FRAME) accumulator = 0;

    draw();

    if (particles.length === 0 && pending.length === 0) {
        teardown();
        return;
    }
    raf = requestAnimationFrame(frame);
}

function settle(run) {
    if (run.pendingShots <= 0 && run.live <= 0 && runs.delete(run)) run.resolve();
}

function run(shots) {
    if (reducedMotion()) shots = shots.filter(s => !s.options.disableForReducedMotion);
    if (!shots.length) return Promise.resolve();

    ensureCanvas();
    return new Promise(resolve => {
        const r = { pendingShots: shots.length, live: 0, resolve };
        runs.add(r);
        for (const s of shots) pending.push({ due: clock + s.delay, options: s.options, run: r });
        launchDue();
        if (!raf) {
            lastTime = 0;
            raf = requestAnimationFrame(frame);
        }
    });
}

function shotsFor(preset, json, x, y) {
    if (json) {
        const o = options(JSON.parse(json));
        if (x != null) o.originX = x;
        if (y != null) o.originY = y;
        return [{ delay: 0, options: o }];
    }
    return presetShots(preset, x, y).map(s => ({ delay: s.delay, options: options(s.options) }));
}

function centerOf(element) {
    // display:contents wrappers have no box of their own; measure what they wrap.
    let target = element;
    if (target && getComputedStyle(target).display === 'contents') target = target.firstElementChild || target;
    const rect = target?.getBoundingClientRect?.();
    if (!rect || (!rect.width && !rect.height)) return [null, null];
    return [(rect.left + rect.width / 2) / window.innerWidth, (rect.top + rect.height / 2) / window.innerHeight];
}

export function fire(json) {
    return run([{ delay: 0, options: options(json ? JSON.parse(json) : null) }]);
}

export function firePreset(preset, x, y) {
    return run(shotsFor(preset, null, x, y));
}

export function fireFrom(element, preset, json) {
    const [x, y] = centerOf(element);
    return run(shotsFor(preset, json, x, y));
}

export function clear() {
    particles = [];
    pending = [];
    for (const r of runs) r.resolve();
    runs.clear();
    teardown();
}

// The wrapper component. Listening here rather than round-tripping the click through .NET means the
// burst starts on the same frame as the click, which on Blazor Server is the difference between
// confetti and confetti that arrives a network hop late.
export function attach(id, element, trigger, preset, json) {
    detach(id);
    const state = { element, trigger, preset, json, handler: null };

    state.handler = e => {
        // A keyboard-activated click has no pointer position (detail is 0): burst from the control instead.
        let x = null, y = null;
        if (e.detail !== 0 && (e.clientX || e.clientY)) {
            x = e.clientX / window.innerWidth;
            y = e.clientY / window.innerHeight;
        } else {
            [x, y] = centerOf(e.target instanceof Element ? e.target : element);
        }
        run(shotsFor(state.preset, state.json, x, y));
    };

    const type = trigger === 'DoubleTap' ? 'dblclick' : 'click';
    element.addEventListener(type, state.handler);
    state.type = type;
    attached.set(id, state);
}

export function update(id, trigger, preset, json) {
    const state = attached.get(id);
    if (!state) return;
    if (state.trigger !== trigger) {
        attach(id, state.element, trigger, preset, json);
        return;
    }
    state.preset = preset;
    state.json = json;
}

export function detach(id) {
    const state = attached.get(id);
    if (!state) return;
    state.element.removeEventListener(state.type, state.handler);
    attached.delete(id);
}
