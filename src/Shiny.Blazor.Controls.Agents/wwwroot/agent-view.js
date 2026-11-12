// AgentView's scroll and composer behaviour. Everything here runs without calling back into .NET, so
// following a token stream costs no round trips on Blazor Server.
//
// - Stick to bottom: while the reader is at the live edge, any growth of the conversation (a token,
//   an image loading, a surface expanding) re-pins the scroller to the bottom. Scrolling up detaches,
//   and the scroller gets data-detached, which the stylesheet uses to show the "jump to latest" pill.
// - Enter sends: the keydown is cancelled here (Blazor can't make preventDefault depend on the key),
//   Shift+Enter and IME composition keep their newline.
// - The composer grows with its text up to a few lines, then scrolls.

const states = new WeakMap();
const threshold = 48;

export function init(scrollEl, inputEl) {
    if (!scrollEl) return;

    const state = { follow: true, observer: null, resize: null };
    states.set(scrollEl, state);

    const atBottom = () => scrollEl.scrollHeight - scrollEl.scrollTop - scrollEl.clientHeight < threshold;
    const pin = () => {
        if (state.follow) {
            scrollEl.scrollTop = scrollEl.scrollHeight;
            state.lastTop = scrollEl.scrollTop;
        }
    };
    const mark = () => {
        if (state.follow) scrollEl.removeAttribute('data-detached');
        else scrollEl.setAttribute('data-detached', '');
    };

    // Only the reader scrolling up detaches. Content reflowing under a resize, or the browser clamping
    // scrollTop, must not - those scroll events don't move the view upwards on purpose.
    state.lastTop = scrollEl.scrollTop;
    state.onScroll = () => {
        const top = scrollEl.scrollTop;
        if (atBottom())
            state.follow = true;
        else if (top < state.lastTop - 2)
            state.follow = false;
        state.lastTop = top;
        mark();
    };
    scrollEl.addEventListener('scroll', state.onScroll, { passive: true });

    state.observer = new MutationObserver(pin);
    state.observer.observe(scrollEl, { childList: true, subtree: true, characterData: true });

    // content that changes height without a DOM mutation (images, fonts, a gauge drawing)
    // and the scroller itself shrinking (the composer growing, a window resize)
    if (window.ResizeObserver) {
        state.resize = new ResizeObserver(pin);
        if (scrollEl.firstElementChild)
            state.resize.observe(scrollEl.firstElementChild);
        state.resize.observe(scrollEl);
    }

    if (inputEl) {
        inputEl.addEventListener('keydown', e => {
            if (e.key === 'Enter' && !e.shiftKey && !e.isComposing)
                e.preventDefault();
        });
        const grow = () => autoGrow(inputEl, 6);
        inputEl.addEventListener('input', grow);
        // Blazor clears the value after a send without an input event; a send always scrolls to the
        // end (scrollToEnd below), which re-measures, and keyup catches anything else
        inputEl.addEventListener('keyup', grow);
        state.grow = grow;
    }
    pin();
}

export function scrollToEnd(scrollEl, animate) {
    const state = states.get(scrollEl);
    if (!scrollEl) return;
    if (state) {
        state.follow = true;
        scrollEl.removeAttribute('data-detached');
    }
    scrollEl.scrollTo({ top: scrollEl.scrollHeight, behavior: animate ? 'smooth' : 'instant' });
    if (state) state.lastTop = scrollEl.scrollTop;
    if (state?.grow) requestAnimationFrame(state.grow);
}

export function dispose(scrollEl) {
    const state = states.get(scrollEl);
    if (!state) return;
    state.observer?.disconnect();
    state.resize?.disconnect();
    scrollEl.removeEventListener('scroll', state.onScroll);
    states.delete(scrollEl);
}

function autoGrow(el, maxRows) {
    const cs = getComputedStyle(el);
    const line = parseFloat(cs.lineHeight) || parseFloat(cs.fontSize) * 1.4;
    const chrome = parseFloat(cs.paddingTop) + parseFloat(cs.paddingBottom)
        + parseFloat(cs.borderTopWidth) + parseFloat(cs.borderBottomWidth);
    const max = Math.round(line * maxRows + chrome);
    el.style.height = 'auto';
    const wanted = el.scrollHeight + parseFloat(cs.borderTopWidth) + parseFloat(cs.borderBottomWidth);
    el.style.height = Math.min(wanted, max) + 'px';
    el.style.overflowY = wanted > max ? 'auto' : 'hidden';
}
