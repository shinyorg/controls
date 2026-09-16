// Lifts a full-screen overlay into the browser's top layer, and lets it back down.
//
// An overlay is position:fixed, and fixed only means "fixed to the screen" while no ancestor has a
// transform, a filter or will-change: transform. SheetView moves its panel with a transform, so an
// overlay rendered inside a sheet - a media picker's chooser, a lightbox - was laid out against the
// panel instead: pinned to the panel's bottom edge, below the fold, and clipped by it. Tapping the
// button looked like it did nothing.
//
// The top layer ignores every ancestor. The element keeps its place in the DOM, so Blazor's diffing,
// event bubbling and inherited CSS variables are all unaffected. Each overlay carries popover="manual"
// (no light dismiss, no Escape - the component decides when it closes) and resets the popover UA
// styles in its own stylesheet. Where popovers are not supported, these do nothing and the overlay
// stays where it always was.

export function show(el) {
    if (!supported(el) || !el.isConnected)
        return;

    try {
        if (!el.matches(':popover-open'))
            el.showPopover();
    } catch (err) {
        console.warn('[shiny] could not raise overlay to the top layer', err);
    }
}

export function hide(el) {
    if (!supported(el))
        return;

    try {
        if (el.matches(':popover-open'))
            el.hidePopover();
    } catch {
        // already gone
    }
}

function supported(el) {
    return !!el && typeof el.showPopover === 'function';
}
