// Pointer-driven card drag for KanbanView.
//
// Pointer events rather than HTML5 drag-and-drop: DnD never fires on touch, and a board is the one
// control people most expect to drag on a phone. Pointer events are uniform across mouse, touch and
// pen.
//
// The hit testing deliberately never crosses into .NET while the finger is down. Resolving a drop
// target means reading element rectangles, and routing that through interop puts a render pass and a
// round trip between the pointer moving and the insertion line following it - which is precisely the
// lag that reads as a broken board. .NET is asked once, at drag start, which lanes will accept this
// card; everything after that is measured here.

const LANE_SEPARATOR = '';
const DRAG_THRESHOLD = 5;
const AUTOSCROLL_ZONE = 56;
const AUTOSCROLL_STEP = 14;

const instances = new WeakMap();

export function attach(root, dotnet) {
    if (!root || instances.has(root)) {
        return;
    }

    const state = {
        root,
        dotnet,
        indicator: root.querySelector('[data-kanban-drop]'),
        pending: null,
        drag: null,
        listeners: [],
    };

    const on = (target, name, fn, options) => {
        target.addEventListener(name, fn, options);
        state.listeners.push([target, name, fn, options]);
    };

    on(root, 'pointerdown', e => onPointerDown(state, e));
    on(window, 'pointermove', e => onPointerMove(state, e));
    on(window, 'pointerup', e => onPointerUp(state, e));
    on(window, 'pointercancel', () => cancelDrag(state));

    instances.set(root, state);
}


export function detach(root) {
    const state = root && instances.get(root);
    if (!state) {
        return;
    }

    cancelDrag(state);
    state.listeners.forEach(([target, name, fn, options]) => target.removeEventListener(name, fn, options));
    instances.delete(root);
}


export function scrollToCard(root, cardId) {
    const card = root?.querySelector(`[data-kanban-card="${CSS.escape(cardId)}"]`);
    card?.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'smooth' });
}


// ---------------------------------------------------------------- press

function onPointerDown(state, e) {
    // Only the primary button, and never on something that is already interactive - a card carrying
    // its own button or link has to stay clickable.
    if (e.button !== 0 || e.target.closest('button, a, input, textarea, select')) {
        return;
    }

    const card = e.target.closest('[data-kanban-card]');
    if (!card || card.dataset.kanbanLocked === 'true') {
        return;
    }

    state.pending = {
        card,
        cardId: card.getAttribute('data-kanban-card'),
        startX: e.clientX,
        startY: e.clientY,
        pointerId: e.pointerId,
    };
}


// ---------------------------------------------------------------- move

async function onPointerMove(state, e) {
    if (state.pending && !state.drag) {
        const dx = e.clientX - state.pending.startX;
        const dy = e.clientY - state.pending.startY;

        // A threshold, not an immediate start: without it every click on a card is a one-pixel drag,
        // and the card never receives the click at all.
        if (Math.hypot(dx, dy) < DRAG_THRESHOLD) {
            return;
        }

        await beginDrag(state, e);
    }

    if (!state.drag) {
        return;
    }

    e.preventDefault();

    const d = state.drag;
    d.card.style.transform = `translate(${e.clientX - d.startX}px, ${e.clientY - d.startY}px)`;
    d.pointer = { x: e.clientX, y: e.clientY };

    resolveTarget(state, e.clientX, e.clientY);
    autoScroll(state, e.clientX, e.clientY);
}


async function beginDrag(state, e) {
    const p = state.pending;
    state.pending = null;

    let allowed = [];
    try {
        allowed = await state.dotnet.invokeMethodAsync('OnDragStartJs', p.cardId) ?? [];
    } catch {
        // The circuit went away mid-gesture. Nothing to drag into.
        return;
    }

    state.drag = {
        card: p.card,
        cardId: p.cardId,
        startX: p.startX,
        startY: p.startY,
        allowed: new Set(allowed),
        target: null,
        pointer: { x: e.clientX, y: e.clientY },
    };

    p.card.classList.add('is-dragging');
    state.root.classList.add('is-dragging');
    document.body.style.userSelect = 'none';
}


function resolveTarget(state, x, y) {
    const d = state.drag;
    d.target = null;

    const cell = cellAt(state, x, y);
    if (!cell) {
        hideIndicator(state);
        return;
    }

    const columnId = cell.getAttribute('data-kanban-column');
    const swimlaneId = cell.getAttribute('data-kanban-swimlane') ?? '';

    if (!d.allowed.has(`${columnId}${LANE_SEPARATOR}${swimlaneId}`)) {
        hideIndicator(state);
        return;
    }

    const cards = [...cell.querySelectorAll('[data-kanban-card]')];
    let index = cards.length;

    for (let i = 0; i < cards.length; i++) {
        const r = cards[i].getBoundingClientRect();

        // The dragged card has been translated out from under itself, so measuring where it is now
        // would put the line wherever the finger is. Its original slot is what counts, and PlanMove
        // on the .NET side is what takes it back out of the count.
        if (cards[i] === d.card) {
            continue;
        }

        if (y < r.top + r.height / 2) {
            index = i;
            break;
        }
    }

    d.target = { columnId, swimlaneId, index };
    showIndicator(state, cell, cards, index);
}


function cellAt(state, x, y) {
    const cells = [...state.root.querySelectorAll('[data-kanban-cell]')];

    let nearest = null;
    let best = Infinity;

    for (const cell of cells) {
        const r = cell.getBoundingClientRect();

        if (x >= r.left && x <= r.right && y >= r.top && y <= r.bottom) {
            return cell;
        }

        // Dragging into the gutter between two columns should still land somewhere: a drop that
        // silently does nothing because the finger was four pixels wide of a column is
        // indistinguishable from a board that is broken.
        const dx = Math.max(r.left - x, 0, x - r.right);
        const dy = Math.max(r.top - y, 0, y - r.bottom);
        const distance = Math.hypot(dx, dy);

        if (distance < best) {
            best = distance;
            nearest = cell;
        }
    }

    return best <= 120 ? nearest : null;
}


function showIndicator(state, cell, cards, index) {
    const indicator = state.indicator;
    if (!indicator) {
        return;
    }

    const rootRect = state.root.getBoundingClientRect();
    const cellRect = cell.getBoundingClientRect();
    const styles = getComputedStyle(cell);
    const padLeft = parseFloat(styles.paddingLeft) || 0;
    const padRight = parseFloat(styles.paddingRight) || 0;
    const padTop = parseFloat(styles.paddingTop) || 0;
    const gap = parseFloat(styles.rowGap) || 0;

    const others = cards.filter(c => c !== state.drag.card);
    let top;

    if (index < others.length) {
        top = others[index].getBoundingClientRect().top - gap / 2;
    } else if (others.length > 0) {
        top = others[others.length - 1].getBoundingClientRect().bottom + gap / 2;
    } else {
        top = cellRect.top + padTop;
    }

    indicator.style.left = `${cellRect.left - rootRect.left + state.root.scrollLeft + padLeft}px`;
    indicator.style.top = `${top - rootRect.top + state.root.scrollTop}px`;
    indicator.style.width = `${cellRect.width - padLeft - padRight}px`;
    indicator.hidden = false;
}


function hideIndicator(state) {
    if (state.indicator) {
        state.indicator.hidden = true;
    }
}


// ---------------------------------------------------------------- auto-scroll

function autoScroll(state, x, y) {
    const r = state.root.getBoundingClientRect();

    const dx = nudge(x - r.left, r.width);
    const dy = nudge(y - r.top, r.height);

    if (dx || dy) {
        state.root.scrollBy(dx, dy);
    }
}


function nudge(position, extent) {
    if (extent <= 0) {
        return 0;
    }
    if (position < AUTOSCROLL_ZONE) {
        return -AUTOSCROLL_STEP;
    }
    if (position > extent - AUTOSCROLL_ZONE) {
        return AUTOSCROLL_STEP;
    }
    return 0;
}


// ---------------------------------------------------------------- release

function onPointerUp(state) {
    state.pending = null;

    const d = state.drag;
    if (!d) {
        return;
    }

    const target = d.target;
    endDrag(state);

    if (target) {
        state.dotnet.invokeMethodAsync('OnDropJs', d.cardId, target.columnId, target.swimlaneId, target.index);
    }
}


function cancelDrag(state) {
    state.pending = null;
    if (state.drag) {
        endDrag(state);
    }
}


function endDrag(state) {
    const d = state.drag;
    state.drag = null;

    if (d) {
        d.card.style.transform = '';
        d.card.classList.remove('is-dragging');
    }

    state.root.classList.remove('is-dragging');
    document.body.style.userSelect = '';
    hideIndicator(state);
}
