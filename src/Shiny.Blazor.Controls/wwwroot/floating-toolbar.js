// FloatingToolbar's browser side.
//
// Deliberately thin: placement, the top layer and the reposition-on-scroll watcher all come from
// tooltip.js, because a toolbar anchored to a control is the same positioning problem as a tooltip
// anchored to one and a second implementation would only drift from it.

import { place, open as openPopover, close as closePopover, observe, unobserve } from './tooltip.js';

export { place, openPopover as open, closePopover as close, observe, unobserve };

const bound = new Map();

/** Elements whose own pointer story must not be hijacked by a long-press trigger. */
const LONG_PRESS_MS_DEFAULT = 450;


function resolveAll(selector) {
    if (!selector)
        return [];

    try {
        return [...document.querySelectorAll(selector)];
    }
    catch {
        // A malformed selector is a caller error, not a reason to take the page down.
        return [];
    }
}


/**
 * Wires the trigger onto every element matching the selector and reports which one fired.
 *
 * The index is what makes one bar serve a whole list: .NET gets told which target it is acting on,
 * rather than having to guess from a single anchor.
 */
export function bind(id, selector, trigger, dotnet, longPressDelay) {
    unbind(id);

    const targets = resolveAll(selector);
    if (targets.length === 0)
        return 0;

    const state = { targets, handlers: [], timer: 0 };
    const delay = longPressDelay > 0 ? longPressDelay : LONG_PRESS_MS_DEFAULT;

    const call = (name, index) => {
        try {
            dotnet.invokeMethodAsync(name, index);
        }
        catch {
            // The circuit is gone; stop listening rather than throwing on every pointer move.
            unbind(id);
        }
    };

    targets.forEach((el, index) => {
        const on = (event, fn) => {
            el.addEventListener(event, fn);
            state.handlers.push([el, event, fn]);
        };

        switch (trigger) {
            case 'hover':
                on('pointerenter', () => call('OnTriggerShow', index));
                on('pointerleave', () => call('OnTriggerHide', index));
                break;

            case 'click':
                on('click', () => call('OnTriggerToggle', index));
                break;

            case 'focus':
                on('focusin', () => call('OnTriggerShow', index));
                on('focusout', () => call('OnTriggerHide', index));
                break;

            case 'hoverorfocus':
                on('pointerenter', () => call('OnTriggerShow', index));
                on('pointerleave', () => call('OnTriggerHide', index));
                on('focusin', () => call('OnTriggerShow', index));
                on('focusout', () => call('OnTriggerHide', index));
                break;

            case 'longpress':
                on('pointerdown', () => {
                    clearTimeout(state.timer);
                    state.timer = setTimeout(() => call('OnTriggerShow', index), delay);
                });
                on('pointerup', () => clearTimeout(state.timer));
                on('pointercancel', () => clearTimeout(state.timer));
                on('pointerleave', () => clearTimeout(state.timer));
                // Without this a long press also raises the browser's context menu on touch, which
                // lands on top of the bar that the press just opened.
                on('contextmenu', (ev) => ev.preventDefault());
                break;

            default:
                break;
        }
    });

    bound.set(id, state);
    return targets.length;
}


export function unbind(id) {
    const state = bound.get(id);
    if (!state)
        return;

    clearTimeout(state.timer);
    state.handlers.forEach(([el, event, fn]) => el.removeEventListener(event, fn));
    bound.delete(id);
}


/** The element the bar should anchor to, by index into the bound target list. */
export function targetAt(id, index) {
    const state = bound.get(id);
    return state && state.targets[index] ? state.targets[index] : null;
}


/**
 * Places the bar against one of the bound targets and reports the side actually used, which the
 * caller needs because a flip changes which edge the entry animation grows out of.
 */
export function placeAt(bar, id, index, preferred, gap, margin) {
    const target = targetAt(id, index);
    const result = place(bar, target, preferred, gap, margin, 0);

    return result ? { placement: result.placement, left: result.left, top: result.top } : null;
}


/**
 * How many items fit along the bar, measured rather than guessed.
 *
 * The bar is sized to its content, so the constraint is the viewport rather than a parent: an
 * anchored bar that runs off the screen edge cannot be scrolled to.
 */
export function fitCount(bar, orientation, margin) {
    if (!bar)
        return 0;

    const cells = [...bar.querySelectorAll('[data-toolbar-cell]')];
    if (cells.length === 0)
        return 0;

    const vertical = orientation === 'vertical';
    const room = (vertical ? window.innerHeight : window.innerWidth) - margin * 2;

    let used = 0;
    let count = 0;
    for (const cell of cells) {
        const rect = cell.getBoundingClientRect();
        used += vertical ? rect.height : rect.width;
        if (used > room)
            break;
        count++;
    }

    return count;
}
