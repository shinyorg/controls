// Range pickers: centres the selected time slot of each list inside its own scroller, so opening
// a time picker at 17:00 does not start the user at midnight. Scrolls the list, never the page.
// The lists are position:relative, so an item's offsetTop is already measured from its list.
export function scrollSelectedIntoView(container) {
    if (!container) return;
    for (const list of container.querySelectorAll('[data-rp-scroll]')) {
        const item = list.querySelector('[aria-selected="true"]') || list.querySelector('[data-rp-anchor]');
        if (!item) continue;
        list.scrollTop = Math.max(0, item.offsetTop - (list.clientHeight / 2) + (item.offsetHeight / 2));
    }
}
