const instances = new Map();

function rowOf(e) {
    return e.target instanceof Element
        ? e.target.closest('.shiny-treeview-row[data-treenode-id]')
        : null;
}

function clearIndicators(root) {
    root.querySelectorAll('.drop-above, .drop-below, .drop-into')
        .forEach(el => el.classList.remove('drop-above', 'drop-below', 'drop-into'));
}

function zoneFor(row, clientY) {
    const rect = row.getBoundingClientRect();
    const rel = (clientY - rect.top) / Math.max(rect.height, 1);
    if (row.dataset.treenodeFolder === 'true' && rel >= 0.25 && rel <= 0.75)
        return 'into';
    return rel < 0.5 ? 'above' : 'below';
}

export function init(root, dotNetRef) {
    const state = { sourceId: null, listeners: [] };
    const on = (name, fn) => {
        root.addEventListener(name, fn);
        state.listeners.push([name, fn]);
    };

    on('dragstart', e => {
        const row = rowOf(e);
        if (!row || row.getAttribute('draggable') !== 'true') return;
        state.sourceId = row.dataset.treenodeId;
        // Safari and Firefox abort the drag unless data is set in dragstart;
        // Blazor's synthetic drag events can't do this, hence this module.
        e.dataTransfer.setData('text/plain', state.sourceId);
        e.dataTransfer.effectAllowed = 'move';
    });

    on('dragover', e => {
        const row = rowOf(e);
        if (!row || state.sourceId == null) return;
        e.preventDefault();
        e.dataTransfer.dropEffect = 'move';
        clearIndicators(root);
        if (row.dataset.treenodeId !== state.sourceId)
            row.classList.add('drop-' + zoneFor(row, e.clientY));
    });

    on('dragleave', e => {
        // only clear when truly leaving the tree, not when crossing child elements
        if (!(e.relatedTarget instanceof Element) || !root.contains(e.relatedTarget))
            clearIndicators(root);
    });

    on('drop', e => {
        const row = rowOf(e);
        clearIndicators(root);
        const sourceId = state.sourceId ?? e.dataTransfer.getData('text/plain');
        state.sourceId = null;
        if (!row || !sourceId || row.dataset.treenodeId === sourceId) return;
        e.preventDefault();
        try {
            dotNetRef.invokeMethodAsync('OnJsDrop', sourceId, row.dataset.treenodeId, zoneFor(row, e.clientY))
                .catch(() => {});
        } catch { }
    });

    on('dragend', () => {
        state.sourceId = null;
        clearIndicators(root);
    });

    instances.set(root, state);
}

export function dispose(root) {
    const state = instances.get(root);
    if (!state) return;
    for (const [name, fn] of state.listeners)
        root.removeEventListener(name, fn);
    instances.delete(root);
}

// Virtualized trees: keep the keyboard-focused row (by index in the flat list) inside the
// scroller, so <Virtualize> renders it.
export function scrollRowIntoView(root, index, rowHeight) {
    if (!root) return;
    const top = index * rowHeight;
    const bottom = top + rowHeight;
    if (top < root.scrollTop)
        root.scrollTop = top;
    else if (bottom > root.scrollTop + root.clientHeight)
        root.scrollTop = bottom - root.clientHeight;
}
