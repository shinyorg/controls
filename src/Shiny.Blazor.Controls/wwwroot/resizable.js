// Drag and keyboard controller for ResizablePanelGroup. One instance per group element.
//
// Everything it needs is read from the DOM when a drag starts - each panel's current flex-grow is its
// size in percent, its limits are data attributes - so there is nothing to keep in sync with .NET.
// It moves the panels live and reports the settled sizes back once (pointer up, a key press).
//
// resize() mirrors ResizableLayout.Resize (Shiny.Maui.Controls/Resizable/ResizableLayout.cs), which
// both hosts compile; keep the two in step.
const states = new WeakMap();
const EPSILON = 0.0001;

export function init(groupEl, dotnetRef) {
    if (!groupEl || states.has(groupEl)) return;
    const state = { groupEl, dotnet: dotnetRef, drag: null };
    states.set(groupEl, state);

    state.onPointerDown = e => onPointerDown(state, e);
    state.onPointerMove = e => onPointerMove(state, e);
    state.onPointerUp = e => endDrag(state, true);
    state.onPointerCancel = () => endDrag(state, false);
    state.onKeyDown = e => onKeyDown(state, e);

    groupEl.addEventListener('pointerdown', state.onPointerDown);
    groupEl.addEventListener('keydown', state.onKeyDown);
}

export function dispose(groupEl) {
    const state = states.get(groupEl);
    if (!state) return;
    endDrag(state, false);
    groupEl.removeEventListener('pointerdown', state.onPointerDown);
    groupEl.removeEventListener('keydown', state.onKeyDown);
    states.delete(groupEl);
}

// ---------------------------------------------------------------- DOM

// Only this group's own handles and panels - a nested group has its own controller.
function ownHandle(state, target) {
    const handle = target.closest('[data-resizable-handle]');
    return handle && handle.parentElement === state.groupEl ? handle : null;
}

function panelsOf(state) {
    return Array.from(state.groupEl.children).filter(el => el.hasAttribute('data-resizable-panel'));
}

function handleIndex(state, handle) {
    let seen = 0;
    for (const el of state.groupEl.children) {
        if (el.hasAttribute('data-resizable-panel')) seen++;
        else if (el === handle) return seen - 1;
    }
    return -1;
}

function horizontal(state) {
    return state.groupEl.getAttribute('data-direction') !== 'vertical';
}

function readLayout(state) {
    const panels = panelsOf(state);
    const num = (el, name, fallback) => {
        const v = parseFloat(el.getAttribute(name));
        return Number.isFinite(v) ? v : fallback;
    };
    const limits = panels.map(el => ({
        min: num(el, 'data-min', 0),
        max: num(el, 'data-max', 100),
        collapsible: el.hasAttribute('data-collapsible'),
        collapsed: num(el, 'data-collapsed-size', 0)
    }));
    const sizes = panels.map(el => parseFloat(getComputedStyle(el).flexGrow) || 0);
    const total = sizes.reduce((a, b) => a + b, 0);
    // Before .NET has worked out the sizes the panels are laid out by basis, not grow: read their
    // rendered lengths instead.
    if (total < 99 || total > 101) {
        const lengths = panels.map(el => axisLength(state, el));
        const sum = lengths.reduce((a, b) => a + b, 0) || 1;
        for (let i = 0; i < sizes.length; i++) sizes[i] = lengths[i] / sum * 100;
    }
    return { panels, limits, sizes };
}

function axisLength(state, el) {
    const r = el.getBoundingClientRect();
    return horizontal(state) ? r.width : r.height;
}

function apply(panels, sizes) {
    for (let i = 0; i < panels.length; i++) {
        panels[i].style.flex = `${sizes[i]} 1 0px`;
    }
}

function commit(state, sizes) {
    // the group may be mid-dispose; a late callback must not surface as an unhandled rejection
    state.dotnet.invokeMethodAsync('OnLayoutCommittedJs', sizes).catch(() => { });
}

// ---------------------------------------------------------------- pointer

function onPointerDown(state, e) {
    const handle = ownHandle(state, e.target);
    if (!handle || handle.hasAttribute('data-disabled')) return;
    if (e.pointerType === 'mouse' && e.button !== 0) return;

    const index = handleIndex(state, handle);
    const layout = readLayout(state);
    if (index < 0 || index >= layout.panels.length - 1) return;

    e.preventDefault();
    const available = layout.panels.reduce((sum, el) => sum + axisLength(state, el), 0);
    state.drag = {
        handle, index, ...layout, available,
        origin: horizontal(state) ? e.clientX : e.clientY,
        current: layout.sizes,
        pointerId: e.pointerId
    };
    try { handle.setPointerCapture(e.pointerId); } catch { }
    handle.addEventListener('pointermove', state.onPointerMove);
    handle.addEventListener('pointerup', state.onPointerUp);
    handle.addEventListener('pointercancel', state.onPointerCancel);
    handle.classList.add('is-dragging');
    document.documentElement.style.cursor = horizontal(state) ? 'col-resize' : 'row-resize';
    document.documentElement.style.userSelect = 'none';
    handle.focus({ preventScroll: true });
}

function onPointerMove(state, e) {
    const d = state.drag;
    if (!d || e.pointerId !== d.pointerId || d.available <= 0) return;
    const position = horizontal(state) ? e.clientX : e.clientY;
    const delta = (position - d.origin) / d.available * 100;
    d.current = resize(d.sizes, d.limits, d.index, delta);
    apply(d.panels, d.current);
}

function endDrag(state, keep) {
    const d = state.drag;
    if (!d) return;
    state.drag = null;
    d.handle.removeEventListener('pointermove', state.onPointerMove);
    d.handle.removeEventListener('pointerup', state.onPointerUp);
    d.handle.removeEventListener('pointercancel', state.onPointerCancel);
    try { d.handle.releasePointerCapture(d.pointerId); } catch { }
    d.handle.classList.remove('is-dragging');
    document.documentElement.style.cursor = '';
    document.documentElement.style.userSelect = '';

    if (!keep) {
        apply(d.panels, d.sizes);
        return;
    }
    if (d.current.some((s, i) => Math.abs(s - d.sizes[i]) > EPSILON)) commit(state, d.current);
}

// ---------------------------------------------------------------- keyboard

function onKeyDown(state, e) {
    const handle = ownHandle(state, e.target);
    if (!handle || handle.hasAttribute('data-disabled')) return;
    if (e.key === 'Escape' && state.drag) { endDrag(state, false); return; }

    const index = handleIndex(state, handle);
    const { panels, limits, sizes } = readLayout(state);
    if (index < 0 || index >= panels.length - 1) return;

    const step = parseFloat(state.groupEl.getAttribute('data-keyboard-step')) || 10;
    const h = horizontal(state);
    const first = limits[index];
    let next = null;

    switch (e.key) {
        case h ? 'ArrowRight' : 'ArrowDown': next = resize(sizes, limits, index, step); break;
        case h ? 'ArrowLeft' : 'ArrowUp': next = resize(sizes, limits, index, -step); break;
        case 'Home': next = resize(sizes, limits, index, -(sizes[index] - (first.collapsible ? first.collapsed : first.min))); break;
        case 'End': next = resize(sizes, limits, index, first.max - sizes[index]); break;
        case 'Enter': {
            // toggles the panel before the handle, or the one after when only that one collapses
            const target = first.collapsible ? index : limits[index + 1].collapsible ? index + 1 : -1;
            if (target < 0) return;
            next = toggle(state, sizes, limits, target);
            break;
        }
        default: return;
    }

    e.preventDefault();
    if (next && next.some((s, i) => Math.abs(s - sizes[i]) > EPSILON)) {
        apply(panels, next);
        commit(state, next);
    }
}

function toggle(state, sizes, limits, index) {
    const panel = panelsOf(state)[index];
    const l = limits[index];
    const isCollapsed = sizes[index] <= l.collapsed + EPSILON;
    if (!isCollapsed) {
        panel.setAttribute('data-expanded-size', String(sizes[index]));
        return moveEdge(sizes, limits, index, -(sizes[index] - l.collapsed));
    }
    const remembered = parseFloat(panel.getAttribute('data-expanded-size'));
    const target = Math.min(Math.max(Number.isFinite(remembered) ? remembered : l.min, l.min), l.max);
    return moveEdge(sizes, limits, index, target - sizes[index]);
}

function moveEdge(sizes, limits, index, change) {
    return index < sizes.length - 1
        ? resize(sizes, limits, index, change)
        : resize(sizes, limits, index - 1, -change);
}

// ---------------------------------------------------------------- arithmetic (mirrors ResizableLayout)

function isCollapsed(l, size) {
    return l.collapsible && size <= l.collapsed + EPSILON;
}

export function resize(start, limits, handleIndex, delta) {
    const sizes = start.slice();
    if (handleIndex < 0 || handleIndex >= sizes.length - 1 || Math.abs(delta) < EPSILON) return sizes;

    const growIndex = delta > 0 ? handleIndex : handleIndex + 1;
    const step = delta > 0 ? 1 : -1;
    const firstShrink = delta > 0 ? handleIndex + 1 : handleIndex;
    let wanted = Math.abs(delta);

    const grow = limits[growIndex];
    const growSize = sizes[growIndex];

    let minimumGrowth = 0;
    if (isCollapsed(grow, growSize)) {
        minimumGrowth = grow.min - growSize;
        if (wanted < minimumGrowth / 2) return sizes;
        wanted = Math.max(wanted, minimumGrowth);
    }

    wanted = Math.min(wanted, grow.max - growSize);
    if (wanted <= EPSILON || wanted < minimumGrowth - EPSILON) return sizes;

    // nearest panel first, passing over collapsed ones
    const shrinking = [];
    for (let i = firstShrink; i >= 0 && i < sizes.length; i += step) shrinking.push(i);
    let taken = 0;
    for (const i of shrinking.filter(i => !isCollapsed(limits[i], sizes[i]))) {
        if (taken >= wanted - EPSILON) break;
        const panel = limits[i];
        let need = wanted - taken;
        const size = sizes[i];

        const toMin = Math.max(0, size - panel.min);
        if (need <= toMin + EPSILON) {
            sizes[i] = size - need;
            taken += need;
            break;
        }

        sizes[i] = panel.min;
        taken += toMin;
        need -= toMin;

        if (panel.collapsible && panel.collapsed < panel.min) {
            const gap = panel.min - panel.collapsed;
            if (need >= gap / 2 && growSize + taken + gap <= grow.max + EPSILON) {
                sizes[i] = panel.collapsed;
                taken += gap;
            }
            break;
        }
    }

    if (taken < minimumGrowth - EPSILON) return start.slice();

    sizes[growIndex] = growSize + taken;
    return sizes;
}
