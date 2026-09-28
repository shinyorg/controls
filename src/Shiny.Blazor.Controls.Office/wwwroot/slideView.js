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

/// Opens an external link from a show, in a new tab so the show keeps its place.
export function openLink(url) {
    if (url)
        window.open(url, '_blank', 'noopener');
}

/// Plays an embedded clip over the show: a full-window <video> (or <audio>) on a dark scrim, closed by
/// a click outside it, Escape, or the clip ending. The bytes arrive as a stream reference and become a
/// blob URL, which is revoked when the player goes.
export async function playMedia(host, streamRef, contentType, isVideo) {
    if (!host || !streamRef)
        return;

    const buffer = await streamRef.arrayBuffer();
    const url = URL.createObjectURL(new Blob([buffer], { type: contentType || (isVideo ? 'video/mp4' : 'audio/mpeg') }));

    const scrim = document.createElement('div');
    scrim.className = 'shiny-slides-media';
    scrim.style.cssText = 'position:absolute;inset:0;z-index:5;display:flex;align-items:center;justify-content:center;background:rgba(0,0,0,.85)';

    const player = document.createElement(isVideo ? 'video' : 'audio');
    player.src = url;
    player.controls = true;
    player.autoplay = true;
    player.style.cssText = isVideo ? 'max-width:92%;max-height:92%' : 'width:min(480px,90%)';

    const onKey = e => {
        if (e.key === 'Escape') {
            e.stopPropagation();
            e.preventDefault();
            close();
        }
    };

    const close = () => {
        player.pause();
        scrim.remove();
        URL.revokeObjectURL(url);
        document.removeEventListener('keydown', onKey, true);
    };

    // The show advances on a click; a click on the player must not reach it.
    scrim.addEventListener('click', e => { e.stopPropagation(); if (e.target === scrim) close(); });
    scrim.addEventListener('pointerdown', e => e.stopPropagation());
    player.addEventListener('ended', close);
    document.addEventListener('keydown', onKey, true);

    scrim.appendChild(player);
    host.appendChild(scrim);
}
