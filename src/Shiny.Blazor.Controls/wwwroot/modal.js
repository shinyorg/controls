// ModalView's browser side: the parts of the modal contract that live in the document rather than in
// the component - focus, the Escape key, the page's scrollbar, and dragging/resizing the panel.
//
// One shared stack drives all of it. Modals stack (a confirm over an editor over a page), and every
// one of those behaviours has to answer "which modal is on top?" the same way: Escape closes only the
// top one, the trap only holds for the top one, and the scrollbar comes back only when the last one
// leaves.

const stack = [];
const BASE_Z = 1300;

const FOCUSABLE = [
    'a[href]',
    'area[href]',
    'button:not([disabled])',
    'input:not([disabled]):not([type="hidden"])',
    'select:not([disabled])',
    'textarea:not([disabled])',
    'iframe',
    'audio[controls]',
    'video[controls]',
    '[contenteditable]:not([contenteditable="false"])',
    '[tabindex]:not([tabindex="-1"])'
].join(',');

let keyHandler = null;

/**
 * Wires one modal up and puts it on top of the stack.
 *
 * @param {string} id instance id, and the handle for everything below
 * @param {HTMLElement} root the fixed layer holding backdrop and panel
 * @param {HTMLElement} panel the dialog itself
 * @param {object} dotnet the component, for the Escape callback
 * @param {object} options mirrors the component's parameters (camelCased by the serializer)
 */
export function attach(id, root, panel, dotnet, options) {
    detach(id);

    const entry = {
        id,
        root,
        panel,
        dotnet,
        options: options ?? {},
        previousFocus: document.activeElement instanceof HTMLElement ? document.activeElement : null,
        cleanups: []
    };

    stack.push(entry);
    restack();
    ensureKeyHandler();

    if (entry.options.lockScroll)
        lockScroll(true);

    if (entry.options.autoFocus)
        focusFirst(panel);

    if (entry.options.draggable || entry.options.resizable)
        wireGestures(entry);
}


/** Unwinds everything attach did, in reverse, and hands focus back. */
export function detach(id) {
    const index = stack.findIndex(e => e.id === id);
    if (index < 0)
        return;

    const [entry] = stack.splice(index, 1);
    entry.cleanups.forEach(fn => {
        try {
            fn();
        }
        catch {
            // A listener whose element is already gone. Nothing to remove, nothing to report.
        }
    });

    if (entry.options.lockScroll)
        lockScroll(false);

    // Focus first, then repaint the stack: moving focus while the element is still in the document is
    // what keeps the browser from dropping the caret onto <body>.
    if (entry.options.restoreFocus && entry.previousFocus && document.contains(entry.previousFocus))
        entry.previousFocus.focus({ preventScroll: true });

    restack();

    if (stack.length === 0)
        removeKeyHandler();
}


/** A short shove, for a dismissal the modal refused. */
export function nudge(id) {
    const entry = stack.find(e => e.id === id);
    if (!entry)
        return;

    const panel = entry.panel;
    panel.classList.remove('is-nudging');
    // Force a reflow, or re-adding the class in the same frame does not restart the animation.
    void panel.offsetWidth;
    panel.classList.add('is-nudging');
    panel.addEventListener('animationend', () => panel.classList.remove('is-nudging'), { once: true });
}


/** Drops any drag offset and any resized size, back to what the stylesheet says. */
export function resetGeometry(id) {
    const entry = stack.find(e => e.id === id);
    if (!entry)
        return;

    entry.panel.style.transform = '';
    entry.panel.style.width = '';
    entry.panel.style.height = '';
    entry.panel.style.maxWidth = '';
    entry.panel.style.maxHeight = '';

    // Back to the origin, not to nothing: the drag reads this on its next pointerdown, and a null
    // here spreads into NaN offsets that the browser then drops on the floor - a drag that silently
    // does nothing after the first maximise.
    entry.offset = { x: 0, y: 0 };
}


// -------------------------------------------------------------------------------------------------
// Stack
// -------------------------------------------------------------------------------------------------

function restack() {
    stack.forEach((entry, index) => {
        entry.root.style.zIndex = String(BASE_Z + index * 10);
    });
}


function ensureKeyHandler() {
    if (keyHandler)
        return;

    keyHandler = (e) => {
        const entry = stack[stack.length - 1];
        if (!entry)
            return;

        if (e.key === 'Escape' && entry.options.closeOnEscape) {
            e.preventDefault();
            e.stopPropagation();
            try {
                entry.dotnet.invokeMethodAsync('OnEscapeJs');
            }
            catch {
                // The component went away without detaching (a torn-down circuit). Drop it.
                detach(entry.id);
            }
            return;
        }

        if (e.key === 'Tab' && entry.options.trapFocus)
            trapTab(e, entry.panel);
    };

    // Capture, so the page's own key handling does not see Escape first and act on it while a modal
    // is up - and so the trap runs before anything inside the modal moves focus itself.
    window.addEventListener('keydown', keyHandler, true);
}


function removeKeyHandler() {
    if (!keyHandler)
        return;

    window.removeEventListener('keydown', keyHandler, true);
    keyHandler = null;
}


// -------------------------------------------------------------------------------------------------
// Focus
// -------------------------------------------------------------------------------------------------

function focusables(panel) {
    return Array.from(panel.querySelectorAll(FOCUSABLE))
        .filter(el => el.offsetParent !== null || el === document.activeElement);
}


function focusFirst(panel) {
    const preferred = panel.querySelector('[data-shiny-autofocus]');
    const target = preferred ?? focusables(panel)[0];

    if (target) {
        target.focus({ preventScroll: true });
        return;
    }

    // Nothing focusable inside: focus the panel itself so the trap and the screen reader still have
    // somewhere to be. tabindex is set here rather than in markup so it never shows up in Tab order.
    if (!panel.hasAttribute('tabindex'))
        panel.setAttribute('tabindex', '-1');

    panel.focus({ preventScroll: true });
}


function trapTab(e, panel) {
    const items = focusables(panel);
    if (items.length === 0) {
        e.preventDefault();
        panel.focus({ preventScroll: true });
        return;
    }

    const first = items[0];
    const last = items[items.length - 1];
    const active = document.activeElement;

    if (!panel.contains(active)) {
        e.preventDefault();
        (e.shiftKey ? last : first).focus();
        return;
    }

    if (e.shiftKey && active === first) {
        e.preventDefault();
        last.focus();
    }
    else if (!e.shiftKey && active === last) {
        e.preventDefault();
        first.focus();
    }
}


// -------------------------------------------------------------------------------------------------
// Page scroll
// -------------------------------------------------------------------------------------------------

let scrollLocks = 0;

/**
 * Stops the page scrolling behind the modal, and counts, because a second modal over the first must
 * not hand the scrollbar back when only it closes.
 *
 * Restores exactly what it replaced rather than blanking the style, which would wipe an app that sets
 * its own overflow on the body.
 */
function lockScroll(locked) {
    const body = document.body;

    if (locked) {
        if (scrollLocks++ === 0) {
            body.dataset.shinyModalOverflow = body.style.overflow;
            // Replacing the scrollbar with padding keeps the page from lurching sideways as it goes.
            const gap = window.innerWidth - document.documentElement.clientWidth;
            if (gap > 0) {
                body.dataset.shinyModalPad = body.style.paddingRight;
                body.style.paddingRight = `calc(${body.style.paddingRight || '0px'} + ${gap}px)`;
            }
            body.style.overflow = 'hidden';
        }
        return;
    }

    if (scrollLocks > 0 && --scrollLocks === 0) {
        body.style.overflow = body.dataset.shinyModalOverflow ?? '';
        delete body.dataset.shinyModalOverflow;

        if ('shinyModalPad' in body.dataset) {
            body.style.paddingRight = body.dataset.shinyModalPad ?? '';
            delete body.dataset.shinyModalPad;
        }
    }
}


// -------------------------------------------------------------------------------------------------
// Drag and resize
// -------------------------------------------------------------------------------------------------

const MIN_PANEL_WIDTH = 260;
const MIN_PANEL_HEIGHT = 160;


/**
 * The box the panel is allowed to fill: the viewport, less whatever gutter the layer keeps around it.
 * Read off the root rather than assumed, because that gutter narrows at the phone breakpoint and goes
 * to zero once the panel is maximised.
 */
function availableBox(entry) {
    const style = getComputedStyle(entry.root);
    const horizontal = (parseFloat(style.paddingLeft) || 0) + (parseFloat(style.paddingRight) || 0);
    const vertical = (parseFloat(style.paddingTop) || 0) + (parseFloat(style.paddingBottom) || 0);

    return {
        width: Math.max(MIN_PANEL_WIDTH, window.innerWidth - horizontal),
        height: Math.max(MIN_PANEL_HEIGHT, window.innerHeight - vertical)
    };
}


/**
 * Pins a drag offset so the panel stays on screen.
 *
 * The whole panel, not merely a grabbable strip of it: the footer usually holds the only buttons that
 * answer the modal, and a window that can be shoved until those buttons are off screen is a window
 * you cannot answer. A panel taller than the viewport is the one case where the two limits invert -
 * there the same clamp becomes a range between its own edges, so an oversized window can still be
 * dragged up and down to read the rest of it.
 */
function clampOffset(entry, next) {
    // getBoundingClientRect already carries the current transform, so back it out to get the box the
    // panel would occupy at rest.
    const rect = entry.panel.getBoundingClientRect();
    const left = rect.left - entry.offset.x;
    const right = rect.right - entry.offset.x;
    const top = rect.top - entry.offset.y;
    const bottom = rect.bottom - entry.offset.y;

    const between = (value, a, b) => Math.min(Math.max(a, b), Math.max(Math.min(a, b), value));

    return {
        x: between(next.x, -left, window.innerWidth - right),
        y: between(next.y, -top, window.innerHeight - bottom)
    };
}


/** Writes the offset out, without introducing a transform the panel did not already carry. */
function applyOffset(entry) {
    // An inline transform overrides the stylesheet's, which is what runs the exit animation - so a
    // panel sitting at the origin is left alone rather than pinned to translate(0, 0).
    if (entry.offset.x === 0 && entry.offset.y === 0 && !entry.panel.style.transform)
        return;

    entry.panel.style.transform = `translate(${entry.offset.x}px, ${entry.offset.y}px)`;
}


/**
 * Dragging and resizing, both delegated from the panel.
 *
 * Delegation rather than a listener on the header and one on the grip, because Blazor rebuilds those
 * elements - the grip is removed outright while the panel is maximised - and a listener bound to the
 * element that was there at attach time is on a node the document no longer contains. The panel is
 * the one element that lives as long as the modal does.
 */
function wireGestures(entry) {
    const { panel, options } = entry;
    entry.offset = entry.offset ?? { x: 0, y: 0 };

    // Shrinking the browser window would otherwise leave a dragged or resized panel hanging off the
    // edge, with no gesture having taken place to correct it.
    const onViewportChange = () => {
        entry.offset = clampOffset(entry, entry.offset);
        applyOffset(entry);
    };

    window.addEventListener('resize', onViewportChange);
    entry.cleanups.push(() => window.removeEventListener('resize', onViewportChange));

    const onPointerDown = (e) => {
        // Left button (or touch/pen) only.
        if (e.button !== 0)
            return;

        if (options.resizable && !panel.classList.contains('is-maximized') && e.target.closest('[data-shiny-modal-resize]')) {
            startResize(entry, e);
            return;
        }

        if (!options.draggable || panel.classList.contains('is-maximized'))
            return;

        // The header is the drag surface, minus its own controls - grabbing near a button should
        // press the button, not start a drag.
        if (e.target.closest('.shiny-modal-header') && !e.target.closest('[data-shiny-modal-nodrag]'))
            startDrag(entry, e);
    };

    panel.addEventListener('pointerdown', onPointerDown);
    entry.cleanups.push(() => {
        panel.removeEventListener('pointerdown', onPointerDown);
        panel.style.transform = '';
        panel.style.transition = '';
        panel.style.width = '';
        panel.style.height = '';
        panel.style.maxWidth = '';
        panel.style.maxHeight = '';
    });
}


function startDrag(entry, e) {
    const { panel } = entry;
    const startX = e.clientX;
    const startY = e.clientY;
    const origin = { ...entry.offset };

    panel.setPointerCapture(e.pointerId);
    panel.classList.add('is-dragging');
    // The panel transitions transform for its entry animation; leaving that on would make the panel
    // lag the pointer by the animation duration.
    panel.style.transition = 'none';

    const onMove = (move) => {
        entry.offset = clampOffset(entry, {
            x: origin.x + (move.clientX - startX),
            y: origin.y + (move.clientY - startY)
        });
        panel.style.transform = `translate(${entry.offset.x}px, ${entry.offset.y}px)`;
    };

    const onUp = () => {
        panel.releasePointerCapture(e.pointerId);
        panel.removeEventListener('pointermove', onMove);
        panel.removeEventListener('pointerup', onUp);
        panel.removeEventListener('pointercancel', onUp);
        panel.classList.remove('is-dragging');
        panel.style.transition = '';
    };

    panel.addEventListener('pointermove', onMove);
    panel.addEventListener('pointerup', onUp);
    panel.addEventListener('pointercancel', onUp);
}


function startResize(entry, e) {
    const { panel } = entry;
    e.preventDefault();

    const rect = panel.getBoundingClientRect();
    const startX = e.clientX;
    const startY = e.clientY;

    panel.setPointerCapture(e.pointerId);
    panel.style.transition = 'none';

    const onMove = (move) => {
        // maxWidth/maxHeight from the stylesheet would cap what the user just asked for, so the
        // explicit size wins for as long as it is set - but only out to the layer's own box. Past
        // that the panel grows through the viewport and takes the header and footer with it.
        const box = availableBox(entry);

        panel.style.maxWidth = 'none';
        panel.style.maxHeight = 'none';
        panel.style.width = Math.max(MIN_PANEL_WIDTH, Math.min(box.width, rect.width + (move.clientX - startX))) + 'px';
        panel.style.height = Math.max(MIN_PANEL_HEIGHT, Math.min(box.height, rect.height + (move.clientY - startY))) + 'px';

        // A panel already dragged down grows downwards, so the size that just fit the viewport can
        // still push the footer off the bottom of it.
        entry.offset = clampOffset(entry, entry.offset);
        applyOffset(entry);
    };

    const onUp = () => {
        panel.releasePointerCapture(e.pointerId);
        panel.removeEventListener('pointermove', onMove);
        panel.removeEventListener('pointerup', onUp);
        panel.removeEventListener('pointercancel', onUp);
        panel.style.transition = '';
    };

    panel.addEventListener('pointermove', onMove);
    panel.addEventListener('pointerup', onUp);
    panel.addEventListener('pointercancel', onUp);
}
