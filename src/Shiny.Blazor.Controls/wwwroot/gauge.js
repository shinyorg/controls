// The needle for the Blazor Gauge. The dial itself is SVG rendered by the component; this moves the
// four things that change every frame - the needle's rotation, the value arc's dash and colour, and
// the readout's text - on requestAnimationFrame.
//
// The physics mirror NeedleMotion in Shiny.Controls.Gauge.Shared: a mass-1 spring whose stiffness
// and damping come from a period and a damping ratio, integrated with semi-implicit Euler in small
// substeps, bouncing off pegs at either end of the dial; or a fixed-length ease-out. Keep the
// constants in step with that file - a needle that settles differently on the two hosts is a bug
// neither host's tests would ever show.

const MAX_SUBSTEP = 1 / 240;
const PEG_RESTITUTION = 0.3;
const SETTLE_FRACTION = 0.0005;
const WAYPOINT_FRACTION = 0.01;
const MAX_FRAME = 0.25;

const instances = new Map();

function reducedMotion() {
    return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
}

function clamp(s, value) {
    if (Number.isNaN(value)) return s.cfg.minimum;
    return Math.min(s.cfg.maximum, Math.max(s.cfg.minimum, value));
}

function animates(s) {
    return s.cfg.mode !== 'none' && s.cfg.duration > 0 && !reducedMotion();
}

function finalTarget(s) {
    return s.waypoints.length > 0 ? s.waypoints[s.waypoints.length - 1] : s.target;
}

function query(s) {
    // Looked up again on every update: switching the needle style or toggling the readout replaces
    // the element, and a held reference would keep animating one that is no longer in the page.
    s.needle = s.root.querySelector('.shiny-gauge-needle');
    s.arc = s.root.querySelector('.shiny-gauge-value-arc');
    s.readout = s.root.querySelector('.shiny-gauge-readout');
}

function formatReadout(s, value) {
    const d = s.cfg.decimals;
    return value.toLocaleString(undefined, { minimumFractionDigits: d, maximumFractionDigits: d, useGrouping: false });
}

function arcColor(s, position) {
    const c = s.cfg;
    for (let i = c.rangeStarts.length - 1; i >= 0; i--) {
        if (position >= c.rangeStarts[i] && position <= c.rangeEnds[i]) return c.rangeColors[i];
    }
    return c.arcColor || '';
}

function apply(s) {
    const c = s.cfg;
    const range = c.maximum - c.minimum;
    const fraction = range > 0 ? (s.position - c.minimum) / range : 0;

    if (s.needle) {
        const angle = c.startAngle + fraction * c.sweepAngle;
        s.needle.setAttribute('transform', `rotate(${angle.toFixed(3)})`);
    }

    if (s.arc) {
        s.arc.setAttribute('stroke-dasharray', `${(fraction * 1000).toFixed(2)} 1000`);
        // A round cap on a zero-length dash still paints a dot at the start of the dial.
        s.arc.style.visibility = fraction > 0 ? '' : 'hidden';
        s.arc.style.stroke = arcColor(s, s.position);
    }

    if (s.readout) {
        // nodeValue, not textContent: textContent swaps the text node for a new one, and Blazor's
        // reference would then point at a node that is no longer in the document.
        const text = formatReadout(s, s.position);
        if (s.readout.firstChild) {
            if (s.readout.firstChild.nodeValue !== text) s.readout.firstChild.nodeValue = text;
        } else {
            s.readout.textContent = text;
        }
    }
}

function headFor(s, value) {
    s.target = value;
    s.easeFrom = s.position;
    s.easeElapsed = 0;
    s.moving = true;
    run(s);
}

function snap(s, value) {
    s.waypoints = [];
    s.position = value;
    s.target = value;
    s.velocity = 0;
    s.moving = false;
    apply(s);
}

function setTarget(s, value) {
    value = clamp(s, value);
    if (s.waypoints.length > 0) {
        // Mid-sweep, a new reading is where the sweep comes back to, not a reason to cut it short.
        s.waypoints[s.waypoints.length - 1] = value;
        return;
    }
    if (!animates(s)) {
        snap(s, value);
        return;
    }
    // Already heading there (or resting there): an unrelated update must not restart an ease.
    if (value === s.target && (s.moving || value === s.position)) return;
    headFor(s, value);
}

function startSweep(s, finalValue) {
    finalValue = clamp(s, finalValue);
    if (!animates(s)) {
        snap(s, finalValue);
        return;
    }
    s.waypoints = [finalValue];
    headFor(s, s.cfg.maximum);
}

function arrive(s) {
    if (s.waypoints.length > 0) {
        const next = s.waypoints.shift();
        if (s.cfg.mode === 'ease') s.position = s.target;
        headFor(s, next);
        return;
    }
    s.position = s.target;
    s.velocity = 0;
    s.moving = false;
    if (s.dotNetRef) {
        s.dotNetRef.invokeMethodAsync('NotifyNeedleSettled').catch(() => { });
    }
}

function stepEase(s, dt) {
    const previous = s.position;
    s.easeElapsed += dt;
    const t = Math.min(1, s.easeElapsed / s.cfg.duration);
    const eased = 1 - Math.pow(1 - t, 3);
    s.position = s.easeFrom + (s.target - s.easeFrom) * eased;
    s.velocity = (s.position - previous) / dt;
    if (t >= 1) arrive(s);
}

function stepSpring(s, dt) {
    const c = s.cfg;
    const range = c.maximum - c.minimum;
    const omega = 2 * Math.PI / c.duration;
    const stiffness = omega * omega;
    const damping = 4 * Math.PI * Math.max(0, c.damping) / c.duration;

    let remaining = dt;
    while (remaining > 0) {
        const h = Math.min(remaining, MAX_SUBSTEP);
        remaining -= h;

        const acceleration = stiffness * (s.target - s.position) - damping * s.velocity;
        s.velocity += acceleration * h;
        s.position += s.velocity * h;

        if (s.position > c.maximum) {
            s.position = c.maximum;
            if (s.velocity > 0) s.velocity = -s.velocity * PEG_RESTITUTION;
        } else if (s.position < c.minimum) {
            s.position = c.minimum;
            if (s.velocity < 0) s.velocity = -s.velocity * PEG_RESTITUTION;
        }
    }

    const distance = Math.abs(s.target - s.position);
    if (s.waypoints.length > 0) {
        if (distance <= range * WAYPOINT_FRACTION) arrive(s);
        return;
    }
    const settle = range * SETTLE_FRACTION;
    if (distance <= settle && Math.abs(s.velocity) <= settle * 10) arrive(s);
}

function run(s) {
    if (s.raf || s.disposed) return;
    s.last = 0;
    const frame = (time) => {
        s.raf = 0;
        if (s.disposed || !s.moving) return;

        const dt = s.last ? Math.min(MAX_FRAME, (time - s.last) / 1000) : 1 / 60;
        s.last = time;

        if (!animates(s)) {
            snap(s, finalTarget(s));
            return;
        }

        if (dt > 0) {
            if (s.cfg.mode === 'ease') stepEase(s, dt);
            else stepSpring(s, dt);
            apply(s);
        }

        if (s.moving) s.raf = requestAnimationFrame(frame);
    };
    s.raf = requestAnimationFrame(frame);
}

export function init(root, cfg, initialPosition, dotNetRef) {
    if (!root) return;
    const s = {
        root, cfg, dotNetRef: dotNetRef || null,
        position: initialPosition, velocity: 0, target: initialPosition,
        waypoints: [], easeFrom: initialPosition, easeElapsed: 0,
        moving: false, raf: 0, last: 0, disposed: false
    };
    s.position = clamp(s, s.position);
    s.target = s.position;
    instances.set(root, s);
    query(s);
    apply(s);

    if (cfg.sweepOnLoad) startSweep(s, cfg.value);
    else setTarget(s, cfg.value);
}

export function update(root, cfg) {
    const s = instances.get(root);
    if (!s) return;

    s.cfg = cfg;
    query(s);

    // New pegs pull everything inside them.
    s.position = clamp(s, s.position);
    s.target = clamp(s, s.target);
    s.waypoints = s.waypoints.map(w => clamp(s, w));

    if (!animates(s)) snap(s, finalTarget(s));
    setTarget(s, cfg.value);
    apply(s);
}

export function sweep(root) {
    const s = instances.get(root);
    if (s) startSweep(s, s.cfg.value);
}

export function dispose(root) {
    const s = instances.get(root);
    if (!s) return;
    s.disposed = true;
    if (s.raf) cancelAnimationFrame(s.raf);
    instances.delete(root);
}
