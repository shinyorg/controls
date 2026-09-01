// Presenting mode for SlideView: the browser Fullscreen API, plus the idle timer that fades the
// presenter chrome and the pointer out of a running show.
//
// The Fullscreen API is a bonus, not the mechanism. Requesting it can be refused (an iframe without
// `allowfullscreen`, iOS Safari, a user gesture the browser did not like) and the deck still has to
// fill the screen, so the component's CSS class does the covering and this only tries to remove the
// browser chrome on top of that.

const registry = new WeakMap();
const IdleAfterMs = 3000;

export function attach(element, dotNet) {
    if (!element || registry.has(element))
        return;

    // Fullscreen is reported on the document, not the element, and fires for exits we never asked for
    // — Escape, F11, a tab switch. Comparing against our own element is what makes those exits land
    // back in .NET as IsPresenting = false instead of leaving the component lying about its state.
    const onFullscreen = () => dotNet.invokeMethodAsync(
        'OnPresentingChangedJs', document.fullscreenElement === element);

    let timer = 0;
    const wake = () => {
        element.classList.remove('is-idle');
        clearTimeout(timer);
        timer = setTimeout(() => element.classList.add('is-idle'), IdleAfterMs);
    };

    document.addEventListener('fullscreenchange', onFullscreen);
    element.addEventListener('pointermove', wake);
    element.addEventListener('pointerdown', wake);
    element.addEventListener('keydown', wake);

    registry.set(element, { onFullscreen, wake, clear: () => clearTimeout(timer) });
    wake();
}

export function detach(element) {
    const entry = element && registry.get(element);
    if (!entry)
        return;

    document.removeEventListener('fullscreenchange', entry.onFullscreen);
    element.removeEventListener('pointermove', entry.wake);
    element.removeEventListener('pointerdown', entry.wake);
    element.removeEventListener('keydown', entry.wake);
    entry.clear();
    registry.delete(element);
}

/// Enter fullscreen and take focus, so the arrow keys reach the deck. Returns whether the browser
/// actually went fullscreen; false is not a failure to present, only a failure to hide the chrome.
export async function present(element) {
    if (!element)
        return false;

    // Focus first: a rejected fullscreen request must still leave the keyboard pointed at the deck.
    element.focus?.();

    if (typeof element.requestFullscreen !== 'function')
        return false;

    try {
        await element.requestFullscreen({ navigationUI: 'hide' });
        return true;
    } catch {
        return false;
    }
}

export async function exit(element) {
    if (document.fullscreenElement && (!element || document.fullscreenElement === element))
        await document.exitFullscreen();
}

/// Called after the presenting class lands, so the chrome starts visible and the idle countdown
/// starts from the beginning of the show rather than from whenever the pointer last moved.
export function wake(element) {
    const entry = element && registry.get(element);
    entry?.wake();
}
