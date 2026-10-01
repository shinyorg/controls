// Dock interaction controller: splitter + rail resize, tab drag (reorder / re-dock / tear-off),
// floating window move, resize and drag-to-dock. One delegated instance per DockHost root element.
//
// Dragging works the way Visual Studio's does: the moment a tab leaves its strip it becomes a
// translucent window following the pointer, a docking compass appears over the pane underneath
// and four guides sit on the host's outer edges. Only a drop ON a guide docks — anywhere else the
// panel floats where it was let go, and a preview shows exactly where a guide would put it.
//
// All drag visuals (drag window, compass, guides, preview) are created here with inline styles:
// Blazor scoped CSS cannot reach JS-created elements, and they live on <body> so no container's
// overflow or transform can clip them.
const states = new WeakMap();

const DRAG_THRESHOLD = 5;
const GUIDE = 34;            // guide button size
const GUIDE_GAP = 4;
const DRAG_OPACITY = '0.72'; // drag window / dragged floating window
const EDGE_RAIL = 240;       // preview size for an outer-edge (rail) drop

export function init(hostEl, dotnetRef, locked) {
    if (!hostEl || states.has(hostEl)) return;
    const state = {
        hostEl,
        dotnet: dotnetRef,
        locked: !!locked,
        pointerDown: null,
        drag: null,
    };
    states.set(hostEl, state);

    state.onPointerDown = e => onPointerDown(state, e);
    state.onPointerMove = e => onPointerMove(state, e);
    state.onPointerUp = e => onPointerUp(state, e);
    state.onPointerCancel = () => cancelDrag(state);
    state.onKeyDown = e => { if (e.key === 'Escape') cancelDrag(state); };

    hostEl.addEventListener('pointerdown', state.onPointerDown);
    window.addEventListener('pointermove', state.onPointerMove);
    window.addEventListener('pointerup', state.onPointerUp);
    window.addEventListener('pointercancel', state.onPointerCancel);
    window.addEventListener('keydown', state.onKeyDown);
}

export function setLocked(hostEl, locked) {
    const state = states.get(hostEl);
    if (state) state.locked = !!locked;
}

export function dispose(hostEl) {
    const state = states.get(hostEl);
    if (!state) return;
    cancelDrag(state);
    hostEl.removeEventListener('pointerdown', state.onPointerDown);
    window.removeEventListener('pointermove', state.onPointerMove);
    window.removeEventListener('pointerup', state.onPointerUp);
    window.removeEventListener('pointercancel', state.onPointerCancel);
    window.removeEventListener('keydown', state.onKeyDown);
    states.delete(hostEl);
}

function hostRect(state) {
    return state.hostEl.getBoundingClientRect();
}

function within(r, x, y) {
    return x >= r.left && x <= r.right && y >= r.top && y <= r.bottom;
}

function call(state, method, ...args) {
    // the host may be mid-dispose; a late callback must not surface as an unhandled rejection
    state.dotnet.invokeMethodAsync(method, ...args).catch(() => { });
}

// ---------------------------------------------------------------- theme
// Inline-styled visuals read the host's resolved tokens once per drag, so they follow the theme
// (and dark mode) without a stylesheet.
function theme(state) {
    const cs = getComputedStyle(state.hostEl);
    const v = (name, fallback) => cs.getPropertyValue(name).trim() || fallback;
    return {
        accent: v('--shiny-dock-accent', v('--shiny-color-primary', '#0055d9')),
        onAccent: v('--shiny-color-on-primary', '#ffffff'),
        surface: v('--shiny-dock-group-bg', v('--shiny-color-surface', '#f6fafe')),
        surfaceHigh: v('--shiny-color-surface-container-high', v('--shiny-color-surface-container', '#e8eef6')),
        onSurface: v('--shiny-color-on-surface', '#161c23'),
        outline: v('--shiny-dock-border', v('--shiny-color-outline-variant', '#bbc7df')),
        font: cs.fontFamily,
    };
}

// ---------------------------------------------------------------- pointer down
function onPointerDown(state, e) {
    const floatEl = e.target.closest('[data-dock-float]');
    if (floatEl) focusFloat(state, floatEl);

    if (state.locked || e.button !== 0) return;

    const splitter = e.target.closest('[data-dock-splitter]');
    if (splitter) {
        startSplitterDrag(state, e, splitter);
        return;
    }

    const railResizer = e.target.closest('[data-dock-rail-resizer]');
    if (railResizer) {
        startRailResize(state, e, railResizer);
        return;
    }

    const floatResizer = e.target.closest('[data-dock-float-rsz]');
    if (floatResizer && floatEl) {
        startFloatResize(state, e, floatEl, floatResizer.getAttribute('data-dock-float-rsz'));
        return;
    }

    const floatHeader = e.target.closest('[data-dock-float-header]');
    if (floatHeader && floatEl && !e.target.closest('[data-dock-float-btn]')) {
        // deferred like a tab: a double-click on the title bar docks the window back
        e.preventDefault();
        state.pointerDown = { kind: 'float', el: floatEl, x: e.clientX, y: e.clientY };
        return;
    }

    const tab = e.target.closest('[data-dock-tab]');
    if (tab && !e.target.closest('.shiny-dock-tab__close')) {
        // defer: becomes a drag only after the threshold, so plain clicks still activate.
        // preventDefault stops Safari starting a native text selection on mousedown —
        // it would persist through the whole drag (click events still fire). Touch is
        // left alone so the tab strip stays scrollable.
        if (e.pointerType === 'mouse')
            e.preventDefault();
        state.pointerDown = { kind: 'tab', el: tab, x: e.clientX, y: e.clientY };
    }
}

function focusFloat(state, floatEl) {
    // raise immediately for feel; .NET owns the z-order so the next render agrees
    let top = 0;
    state.hostEl.querySelectorAll('[data-dock-float]').forEach(f => {
        top = Math.max(top, parseInt(f.style.zIndex || '0', 10));
    });
    if (parseInt(floatEl.style.zIndex || '0', 10) < top || top === 0)
        floatEl.style.zIndex = String(top + 1);
    call(state, 'OnFloatingFocusedJs', parseInt(floatEl.getAttribute('data-dock-float'), 10));
}

function suppressSelection() {
    document.getSelection()?.removeAllRanges();
    document.body.style.userSelect = 'none';
    document.body.style.webkitUserSelect = 'none';
}

function restoreSelection() {
    document.body.style.userSelect = '';
    document.body.style.webkitUserSelect = '';
}

// ---------------------------------------------------------------- splitters
function startSplitterDrag(state, e, splitterEl) {
    const container = splitterEl.parentElement;          // .shiny-dock-split
    const first = splitterEl.previousElementSibling;     // .shiny-dock-split__child
    const second = splitterEl.nextElementSibling;
    if (!first || !second) return;

    const horizontal = container.classList.contains('shiny-dock-split--h');
    state.drag = {
        kind: 'splitter',
        splitId: splitterEl.getAttribute('data-dock-splitter'),
        splitterEl, container, first, second, horizontal,
        ratio: null,
    };
    splitterEl.classList.add('is-dragging');
    document.body.style.cursor = horizontal ? 'col-resize' : 'row-resize';
    suppressSelection();
    e.preventDefault();
}

function moveSplitter(state, e) {
    const d = state.drag;
    const rect = d.container.getBoundingClientRect();
    let ratio = d.horizontal
        ? (e.clientX - rect.left) / rect.width
        : (e.clientY - rect.top) / rect.height;
    ratio = Math.min(0.92, Math.max(0.08, ratio));
    d.ratio = ratio;
    // live visual update without .NET round-trips; ratio committed on pointer-up
    d.first.style.flex = `${ratio} 1 0%`;
    d.second.style.flex = `${1 - ratio} 1 0%`;
}

// ---------------------------------------------------------------- rail resize
function startRailResize(state, e, resizerEl) {
    const area = resizerEl.getAttribute('data-dock-rail-resizer'); // left|right|top|bottom
    const rail = state.hostEl.querySelector(`.shiny-dock-rail--${area}`);
    if (!rail) return;

    const vertical = area === 'left' || area === 'right';
    state.drag = { kind: 'rail', area, rail, resizerEl, vertical, size: null };
    resizerEl.classList.add('is-dragging');
    document.body.style.cursor = vertical ? 'col-resize' : 'row-resize';
    suppressSelection();
    e.preventDefault();
}

function moveRailResize(state, e) {
    const d = state.drag;
    const r = d.rail.getBoundingClientRect();
    let size;
    switch (d.area) {
        case 'left': size = e.clientX - r.left; break;
        case 'right': size = r.right - e.clientX; break;
        case 'top': size = e.clientY - r.top; break;
        default: size = r.bottom - e.clientY; break;
    }
    size = Math.min(1200, Math.max(80, size));
    d.size = size;
    // live visual update; committed to .NET on pointer-up
    if (d.vertical) d.rail.style.width = `${size}px`;
    else d.rail.style.height = `${size}px`;
}

// ---------------------------------------------------------------- floating resize
const MIN_FLOAT_W = 200;
const MIN_FLOAT_H = 140;

function startFloatResize(state, e, floatEl, dir) {
    const h = hostRect(state);
    const r = floatEl.getBoundingClientRect();
    const start = { x: r.left - h.left, y: r.top - h.top, width: r.width, height: r.height };
    state.drag = {
        kind: 'float-resize',
        el: floatEl,
        index: parseInt(floatEl.getAttribute('data-dock-float'), 10),
        dir, sx: e.clientX, sy: e.clientY, start, rect: { ...start },
    };
    document.body.style.cursor = getComputedStyle(e.target).cursor;
    suppressSelection();
    e.preventDefault();
}

function moveFloatResize(state, e) {
    const d = state.drag;
    const dx = e.clientX - d.sx, dy = e.clientY - d.sy;
    const s = d.start;
    let { x, y, width, height } = s;
    if (d.dir.includes('e')) width = Math.max(MIN_FLOAT_W, s.width + dx);
    if (d.dir.includes('s')) height = Math.max(MIN_FLOAT_H, s.height + dy);
    if (d.dir.includes('w')) {
        width = Math.max(MIN_FLOAT_W, s.width - dx);
        x = s.x + s.width - width;
    }
    if (d.dir.includes('n')) {
        height = Math.max(MIN_FLOAT_H, s.height - dy);
        y = s.y + s.height - height;
    }
    // never let the title bar leave the host — it is the only way to grab the window again
    if (y < 0) { height += y; y = 0; }
    d.rect = { x, y, width, height };
    Object.assign(d.el.style, { left: `${x}px`, top: `${y}px`, width: `${width}px`, height: `${height}px` });
}

// ---------------------------------------------------------------- floating window drag
function beginFloatDrag(state, floatEl, px, py) {
    const h = hostRect(state);
    const r = floatEl.getBoundingClientRect();
    state.drag = {
        kind: 'float',
        el: floatEl,
        index: parseInt(floatEl.getAttribute('data-dock-float'), 10),
        offsetX: px - r.left,
        offsetY: py - r.top,
        origin: { x: r.left - h.left, y: r.top - h.top },
        x: r.left - h.left, y: r.top - h.top,
        target: null,
        ui: createDragUi(state),
    };
    // the real window travels with the pointer, see-through so the guides under it read
    floatEl.style.transition = 'opacity 120ms ease';
    floatEl.style.opacity = DRAG_OPACITY;
    floatEl.style.pointerEvents = 'none';
    floatEl.classList.add('is-dragging');
    state.pointerDown = null;
    suppressSelection();
}

function moveFloat(state, e) {
    const d = state.drag;
    const h = hostRect(state);
    d.x = Math.max(-d.el.offsetWidth + 80, Math.min(h.width - 60, e.clientX - h.left - d.offsetX));
    d.y = Math.max(0, Math.min(h.height - 30, e.clientY - h.top - d.offsetY));
    d.el.style.left = `${d.x}px`;
    d.el.style.top = `${d.y}px`;
    d.target = updateTarget(state, e);
}

function endFloatVisuals(d) {
    d.el.style.opacity = '';
    d.el.style.pointerEvents = '';
    d.el.classList.remove('is-dragging');
    destroyDragUi(d.ui);
}

// ---------------------------------------------------------------- tab drag
function beginTabDrag(state) {
    const p = state.pointerDown;
    const tabEl = p.el;
    const groupEl = tabEl.closest('[data-dock-group]');

    // the only tab of a floating window IS the window: drag the window itself
    const floatEl = tabEl.closest('[data-dock-float]');
    if (floatEl && floatEl.querySelectorAll('[data-dock-tab]').length === 1) {
        beginFloatDrag(state, floatEl, p.x, p.y);
        return;
    }

    const t = theme(state);
    const gr = groupEl ? groupEl.getBoundingClientRect() : tabEl.getBoundingClientRect();
    const width = Math.round(Math.min(520, Math.max(260, gr.width)));
    const height = Math.round(Math.min(380, Math.max(180, gr.height)));
    const tabRect = tabEl.getBoundingClientRect();
    const offsetX = Math.min(width - 48, Math.max(16, p.x - tabRect.left + 12));
    const offsetY = 15;

    const win = buildDragWindow(t, tabEl, groupEl, width, height);
    document.body.appendChild(win);
    placeAt(win, p.x - offsetX, p.y - offsetY);
    // fade in — unless a move has already set the opacity for a guide underneath
    requestAnimationFrame(() => { if (win.style.opacity === '0') win.style.opacity = DRAG_OPACITY; });

    tabEl.classList.add('is-drag-source');
    state.drag = {
        kind: 'tab',
        srcInstanceId: tabEl.getAttribute('data-dock-tab'),
        srcGroupId: groupEl?.getAttribute('data-dock-group'),
        srcSolo: groupEl ? groupEl.querySelectorAll('[data-dock-tab]').length === 1 : false,
        tabEl, win, width, height, offsetX, offsetY,
        target: null,
        ui: createDragUi(state),
    };
    state.pointerDown = null;
    suppressSelection();
    call(state, 'OnDragStartedJs', state.drag.srcInstanceId);
}

function placeAt(el, x, y) {
    el.style.transform = `translate(${Math.round(x)}px, ${Math.round(y)}px)`;
}

// A floating-window look-alike carrying a live snapshot of the panel, so what you drag is the
// window you are about to get — not a label.
function buildDragWindow(t, tabEl, groupEl, width, height) {
    const title = tabEl.querySelector('.shiny-dock-tab__title')?.textContent?.trim() || 'Panel';
    const icon = tabEl.querySelector('.shiny-dock-tab__icon')?.textContent?.trim();

    const win = document.createElement('div');
    win.setAttribute('aria-hidden', 'true');
    Object.assign(win.style, {
        position: 'fixed', left: '0px', top: '0px', zIndex: '10000', pointerEvents: 'none',
        width: `${width}px`, height: `${height}px`,
        display: 'flex', flexDirection: 'column', overflow: 'hidden', boxSizing: 'border-box',
        background: t.surface, color: t.onSurface, fontFamily: t.font,
        border: `1px solid ${t.accent}`, borderRadius: '8px',
        boxShadow: '0 18px 48px rgba(0,0,0,.32), 0 4px 12px rgba(0,0,0,.18)',
        opacity: '0', transition: 'opacity 140ms ease',
    });

    const header = document.createElement('div');
    Object.assign(header.style, {
        display: 'flex', alignItems: 'center', gap: '6px', flex: '0 0 auto',
        padding: '7px 10px', fontSize: '12px', fontWeight: '600',
        background: t.accent, color: t.onAccent, whiteSpace: 'nowrap', overflow: 'hidden',
    });
    if (icon) {
        const i = document.createElement('span');
        i.textContent = icon;
        header.appendChild(i);
    }
    const label = document.createElement('span');
    label.textContent = title;
    Object.assign(label.style, { overflow: 'hidden', textOverflow: 'ellipsis' });
    header.appendChild(label);
    win.appendChild(header);

    const body = document.createElement('div');
    Object.assign(body.style, { flex: '1', minHeight: '0', overflow: 'hidden', position: 'relative' });
    const isActive = tabEl.classList.contains('shiny-dock-tab--active');
    const panel = isActive ? groupEl?.querySelector('.shiny-dock-panel--active') : null;
    if (panel) {
        const clone = panel.cloneNode(true);
        clone.removeAttribute('id');
        clone.querySelectorAll('[id]').forEach(n => n.removeAttribute('id'));
        clone.querySelectorAll('[autofocus]').forEach(n => n.removeAttribute('autofocus'));
        Object.assign(clone.style, { display: 'block', height: '100%', overflow: 'hidden', pointerEvents: 'none' });
        body.appendChild(clone);
    } else {
        Object.assign(body.style, {
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            fontSize: '12px', fontStyle: 'italic', opacity: '0.6',
        });
        body.textContent = title;
    }
    win.appendChild(body);
    return win;
}

function moveTabDrag(state, e) {
    const d = state.drag;
    placeAt(d.win, e.clientX - d.offsetX, e.clientY - d.offsetY);
    d.target = updateTarget(state, e);
    // over a guide the window fades further so the preview underneath is unmistakable
    d.win.style.opacity = d.target ? '0.45' : DRAG_OPACITY;
}

function endTabVisuals(d) {
    d.win.remove();
    d.tabEl.classList.remove('is-drag-source');
    destroyDragUi(d.ui);
}

// ---------------------------------------------------------------- docking guides
const ZONE_FILL = {
    // the highlighted part of the little window each guide draws
    Center: 'M5 7h14v12H5z',
    Left: 'M5 7h7v12H5z',
    Right: 'M12 7h7v12h-7z',
    Top: 'M5 7h14v6H5z',
    Bottom: 'M5 13h14v6H5z',
};
const EDGE_FILL = {
    Left: 'M5 7h4v12H5z',
    Right: 'M15 7h4v12h-4z',
    Top: 'M5 7h14v3H5z',
    Bottom: 'M5 16h14v3H5z',
};

function guideIcon(path) {
    return `<svg viewBox="0 0 24 24" width="22" height="22" aria-hidden="true">
<rect x="4.25" y="4.25" width="15.5" height="15.5" rx="1.5" fill="none" stroke="currentColor" stroke-width="1.5"/>
<path d="M4.25 7h15.5" stroke="currentColor" stroke-width="1.5"/>
<path d="${path}" fill="currentColor" fill-opacity=".55"/></svg>`;
}

function makeGuide(t, zone, path) {
    const el = document.createElement('div');
    el.innerHTML = guideIcon(path);
    el.dataset.zone = zone;
    Object.assign(el.style, {
        position: 'fixed', width: `${GUIDE}px`, height: `${GUIDE}px`, boxSizing: 'border-box',
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        background: t.surface, color: t.accent,
        border: `1px solid ${t.outline}`, borderRadius: '7px',
        boxShadow: '0 2px 8px rgba(0,0,0,.22)',
        transition: 'background 80ms ease, color 80ms ease, transform 80ms ease',
    });
    return el;
}

function setGuideHot(t, el, hot) {
    if (el._hot === hot) return;
    el._hot = hot;
    el.style.background = hot ? t.accent : t.surface;
    el.style.color = hot ? t.onAccent : t.accent;
    el.style.transform = hot ? 'scale(1.08)' : '';
}

function createDragUi(state) {
    const t = theme(state);
    const root = document.createElement('div');
    root.setAttribute('aria-hidden', 'true');
    // guides float above the drag window, as in Visual Studio — the window is what you aim with, not at
    Object.assign(root.style, { position: 'fixed', inset: '0', zIndex: '10001', pointerEvents: 'none' });

    const preview = document.createElement('div');
    Object.assign(preview.style, {
        position: 'fixed', display: 'none', boxSizing: 'border-box', borderRadius: '6px',
        background: `color-mix(in srgb, ${t.accent} 24%, transparent)`,
        border: `2px solid ${t.accent}`,
        transition: 'left 90ms ease, top 90ms ease, width 90ms ease, height 90ms ease',
    });
    root.appendChild(preview);

    // a slim caret marking where a tab-strip drop inserts
    const caret = document.createElement('div');
    Object.assign(caret.style, {
        position: 'fixed', display: 'none', width: '3px', borderRadius: '2px', background: t.accent,
        boxShadow: `0 0 0 1px ${t.surface}`,
    });
    root.appendChild(caret);

    // outer guides: one per host edge → dock to that side of the whole layout
    const h = hostRect(state);
    const edges = [];
    const inset = 10;
    for (const zone of ['Left', 'Right', 'Top', 'Bottom']) {
        const g = makeGuide(t, zone, EDGE_FILL[zone]);
        let x, y;
        switch (zone) {
            case 'Left': x = h.left + inset; y = h.top + h.height / 2 - GUIDE / 2; break;
            case 'Right': x = h.right - inset - GUIDE; y = h.top + h.height / 2 - GUIDE / 2; break;
            case 'Top': x = h.left + h.width / 2 - GUIDE / 2; y = h.top + inset; break;
            default: x = h.left + h.width / 2 - GUIDE / 2; y = h.bottom - inset - GUIDE; break;
        }
        Object.assign(g.style, { left: `${x}px`, top: `${y}px` });
        root.appendChild(g);
        edges.push(g);
    }

    // the compass: a cross of five guides centred on the pane under the pointer
    const compass = document.createElement('div');
    const size = GUIDE * 3 + GUIDE_GAP * 2;
    const pad = 7;
    Object.assign(compass.style, {
        position: 'fixed', display: 'none', width: `${size + pad * 2}px`, height: `${size + pad * 2}px`,
        boxSizing: 'border-box', padding: `${pad}px`,
        background: `color-mix(in srgb, ${t.surfaceHigh} 78%, transparent)`,
        backdropFilter: 'blur(6px)', webkitBackdropFilter: 'blur(6px)',
        borderRadius: '16px',
        opacity: '0', transition: 'opacity 100ms ease',
        clipPath: 'polygon(33% 0, 67% 0, 67% 33%, 100% 33%, 100% 67%, 67% 67%, 67% 100%, 33% 100%, 33% 67%, 0 67%, 0 33%, 33% 33%)',
    });
    const cells = {};
    const pos = { Top: [1, 0], Left: [0, 1], Center: [1, 1], Right: [2, 1], Bottom: [1, 2] };
    for (const zone of Object.keys(pos)) {
        const g = makeGuide(t, zone, ZONE_FILL[zone]);
        g.style.position = 'absolute';
        g.style.left = `${pad + pos[zone][0] * (GUIDE + GUIDE_GAP)}px`;
        g.style.top = `${pad + pos[zone][1] * (GUIDE + GUIDE_GAP)}px`;
        compass.appendChild(g);
        cells[zone] = g;
    }
    root.appendChild(compass);

    document.body.appendChild(root);
    state.hostEl.classList.add('shiny-dock-host--dragging');
    return { root, preview, caret, edges, compass, cells, t, host: h, compassSize: size + pad * 2, compassFor: null, compassCenterOnly: false };
}

function destroyDragUi(ui) {
    if (!ui) return;
    ui.root.remove();
    document.querySelectorAll('.shiny-dock-host--dragging').forEach(h => h.classList.remove('shiny-dock-host--dragging'));
}

function showCompass(ui, targetEl, rect, centerOnly) {
    if (ui.compassFor !== targetEl || ui.compassCenterOnly !== centerOnly) {
        ui.compassFor = targetEl;
        ui.compassCenterOnly = centerOnly;
        const w = ui.compassSize;
        ui.compass.style.left = `${Math.round(rect.left + rect.width / 2 - w / 2)}px`;
        ui.compass.style.top = `${Math.round(rect.top + rect.height / 2 - w / 2)}px`;
        for (const [zone, cell] of Object.entries(ui.cells))
            cell.style.visibility = centerOnly && zone !== 'Center' ? 'hidden' : 'visible';
        // a lone centre guide sits on its own, without the cross behind it
        ui.compass.style.background = centerOnly ? 'transparent' : `color-mix(in srgb, ${ui.t.surfaceHigh} 78%, transparent)`;
    }
    ui.compass.style.display = 'block';
    requestAnimationFrame(() => { ui.compass.style.opacity = '1'; });
}

function hideCompass(ui) {
    ui.compassFor = null;
    ui.compass.style.display = 'none';
    ui.compass.style.opacity = '0';
}

function hitGuide(el, x, y) {
    return el.style.visibility !== 'hidden' && within(el.getBoundingClientRect(), x, y);
}

function showPreview(ui, r) {
    if (!r) { ui.preview.style.display = 'none'; return; }
    Object.assign(ui.preview.style, {
        display: 'block',
        left: `${r.left}px`, top: `${r.top}px`, width: `${r.width}px`, height: `${r.height}px`,
    });
}

function half(r, zone) {
    switch (zone) {
        case 'Left': return { left: r.left, top: r.top, width: r.width / 2, height: r.height };
        case 'Right': return { left: r.left + r.width / 2, top: r.top, width: r.width / 2, height: r.height };
        case 'Top': return { left: r.left, top: r.top, width: r.width, height: r.height / 2 };
        case 'Bottom': return { left: r.left, top: r.top + r.height / 2, width: r.width, height: r.height / 2 };
        default: return { left: r.left, top: r.top, width: r.width, height: r.height };
    }
}

function edgePreview(h, zone) {
    const w = Math.min(EDGE_RAIL, h.width / 3), ht = Math.min(EDGE_RAIL, h.height / 3);
    switch (zone) {
        case 'Left': return { left: h.left, top: h.top, width: w, height: h.height };
        case 'Right': return { left: h.right - w, top: h.top, width: w, height: h.height };
        case 'Top': return { left: h.left, top: h.top, width: h.width, height: ht };
        default: return { left: h.left, top: h.bottom - ht, width: h.width, height: ht };
    }
}

// Resolves what a drop here would do, and draws it. Returns null when the drop would float.
function updateTarget(state, e) {
    const d = state.drag;
    const ui = d.ui;
    const x = e.clientX, y = e.clientY;
    let target = null;
    ui.caret.style.display = 'none';

    // 1. what is under the pointer decides where the compass sits (dragged visuals are click-through)
    const under = document.elementFromPoint(x, y);
    const inHost = under && state.hostEl.contains(under);
    const strip = inHost ? under.closest('[data-dock-tabstrip]') : null;
    const groupEl = inHost ? under.closest('[data-dock-group]') : null;
    const emptyEl = inHost ? under.closest('[data-dock-empty]') : null;

    if (groupEl) {
        const content = groupEl.querySelector(':scope > [data-dock-group-content]');
        const cr = content && content.offsetHeight > 0 ? content.getBoundingClientRect() : groupEl.getBoundingClientRect();
        // a tab alone in its group can't split or merge with itself
        const own = d.kind === 'tab' && d.srcSolo && groupEl.getAttribute('data-dock-group') === d.srcGroupId;
        if (own) hideCompass(ui);
        else showCompass(ui, groupEl, cr, false);
    } else if (emptyEl) {
        showCompass(ui, emptyEl, emptyEl.getBoundingClientRect(), true);
    } else if (!(ui.compassFor && within(ui.compass.getBoundingClientRect(), x, y))) {
        hideCompass(ui);
    }

    // 2. outer edge guides
    for (const g of ui.edges) {
        const hot = !target && hitGuide(g, x, y);
        setGuideHot(ui.t, g, hot);
        if (hot)
            target = { groupId: null, zone: g.dataset.zone, index: -1, rect: edgePreview(ui.host, g.dataset.zone) };
    }

    // 3. compass guides
    for (const [zone, cell] of Object.entries(ui.cells)) {
        const hot = !target && ui.compassFor && hitGuide(cell, x, y);
        setGuideHot(ui.t, cell, !!hot);
        if (hot) {
            const forEl = ui.compassFor;
            const isEmpty = forEl.hasAttribute('data-dock-empty');
            target = {
                groupId: isEmpty ? null : forEl.getAttribute('data-dock-group'),
                zone, index: -1,
                rect: half(forEl.getBoundingClientRect(), zone),
            };
        }
    }

    // 4. a tab strip: insert at the gap under the pointer
    if (!target && strip) {
        const own = d.kind === 'tab' && d.srcSolo && strip.getAttribute('data-dock-tabstrip') === d.srcGroupId;
        if (!own) {
            const tabs = [...strip.querySelectorAll('[data-dock-tab]')];
            let index = tabs.length;
            for (let i = 0; i < tabs.length; i++) {
                const tr = tabs[i].getBoundingClientRect();
                if (x < tr.left + tr.width / 2) { index = i; break; }
            }
            const sr = strip.getBoundingClientRect();
            const ref = tabs[index] ?? tabs[tabs.length - 1];
            const rr = ref?.getBoundingClientRect();
            const cx = !rr ? sr.left + 6 : tabs[index] ? rr.left - 1 : rr.right + 1;
            Object.assign(ui.caret.style, {
                display: 'block', left: `${cx - 1}px`, top: `${sr.top + 3}px`, height: `${sr.height - 6}px`,
            });
            target = {
                groupId: strip.getAttribute('data-dock-tabstrip'),
                zone: 'TabStrip', index,
                rect: (groupEl ?? strip).getBoundingClientRect(),
            };
        }
    }

    showPreview(ui, target?.rect);
    return target;
}

// ---------------------------------------------------------------- move / up / cancel
function onPointerMove(state, e) {
    if (state.pointerDown && !state.drag) {
        const p = state.pointerDown;
        if (Math.abs(e.clientX - p.x) + Math.abs(e.clientY - p.y) > DRAG_THRESHOLD) {
            if (p.kind === 'float') beginFloatDrag(state, p.el, p.x, p.y);
            else beginTabDrag(state);
        }
    }
    if (!state.drag) return;
    switch (state.drag.kind) {
        case 'splitter': moveSplitter(state, e); break;
        case 'rail': moveRailResize(state, e); break;
        case 'float-resize': moveFloatResize(state, e); break;
        case 'float': moveFloat(state, e); break;
        case 'tab': moveTabDrag(state, e); break;
    }
}

function onPointerUp(state, e) {
    state.pointerDown = null;
    const d = state.drag;
    if (!d) return;
    state.drag = null;
    document.body.style.cursor = '';
    restoreSelection();

    switch (d.kind) {
        case 'splitter':
            d.splitterEl.classList.remove('is-dragging');
            if (d.ratio != null)
                call(state, 'OnSplitterRatioChangedJs', d.splitId, d.ratio);
            break;
        case 'rail':
            d.resizerEl.classList.remove('is-dragging');
            if (d.size != null)
                call(state, 'OnRailResizedJs', d.area, d.size);
            break;
        case 'float-resize': {
            const r = d.rect;
            call(state, 'OnFloatingBoundsChangedJs', d.index, r.x, r.y, r.width, r.height);
            break;
        }
        case 'float': {
            endFloatVisuals(d);
            const t = d.target;
            if (t)
                call(state, 'OnFloatingDroppedJs', d.index, t.groupId, t.zone, t.index);
            else
                call(state, 'OnFloatingMovedJs', d.index, d.x, d.y);
            break;
        }
        case 'tab': {
            const h = hostRect(state);
            const wr = d.win.getBoundingClientRect();
            endTabVisuals(d);
            const t = d.target;
            if (t) {
                call(state, 'OnTabDroppedJs', d.srcInstanceId, t.groupId, t.zone, t.index, 0, 0, 0, 0);
            } else {
                // no guide: float right where the drag window was let go, at its size
                const x = Math.max(0, Math.min(h.width - 80, wr.left - h.left));
                const y = Math.max(0, Math.min(h.height - 30, wr.top - h.top));
                call(state, 'OnTabDroppedJs', d.srcInstanceId, null, 'TearOff', -1, x, y, d.width, d.height);
            }
            break;
        }
    }
}

function cancelDrag(state) {
    const d = state.drag;
    if (!d) { state.pointerDown = null; return; }
    state.drag = null;
    document.body.style.cursor = '';
    restoreSelection();
    switch (d.kind) {
        case 'tab':
            endTabVisuals(d);
            call(state, 'OnDragCancelledJs', d.srcInstanceId);
            break;
        case 'float':
            endFloatVisuals(d);
            d.el.style.left = `${d.origin.x}px`;
            d.el.style.top = `${d.origin.y}px`;
            break;
        case 'float-resize': {
            const s = d.start;
            Object.assign(d.el.style, { left: `${s.x}px`, top: `${s.y}px`, width: `${s.width}px`, height: `${s.height}px` });
            break;
        }
        case 'splitter':
            d.splitterEl.classList.remove('is-dragging');
            break;
        case 'rail':
            d.resizerEl.classList.remove('is-dragging');
            break;
    }
}
