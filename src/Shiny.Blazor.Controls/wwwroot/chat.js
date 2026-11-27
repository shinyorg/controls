const states = new WeakMap();

export function init(messagesEl, dotnetRef) {
    const state = {
        messagesEl,
        dotnet: dotnetRef,
        autoScroll: true,
        topLatched: false,
        mutations: null
    };
    states.set(messagesEl, state);

    messagesEl.addEventListener('scroll', () => {
        const { scrollTop, scrollHeight, clientHeight } = messagesEl;
        const wasFollowing = state.autoScroll;
        state.autoScroll = scrollHeight - scrollTop - clientHeight < 50;

        // Back at the newest message: whatever queued behind the "new messages" pill is now read.
        if (state.autoScroll && !wasFollowing && state.dotnet)
            state.dotnet.invokeMethodAsync('OnScrolledToBottom');

        // Fire a single load-older callback while near the top; re-arm once scrolled away.
        if (scrollTop < 60) {
            if (!state.topLatched) {
                state.topLatched = true;
                if (state.dotnet)
                    state.dotnet.invokeMethodAsync('OnScrolledToTop');
            }
        }
        else {
            state.topLatched = false;
        }
    });

    // A one-shot scrollToEnd after a render only works if the content had already reached its final
    // height, which it has not while images and webfonts are still resolving - the list ends up a few
    // hundred pixels short of the bottom. Re-pin whenever the content actually changes size, but only
    // while the reader is following the live edge (autoScroll), so paging back through history and
    // scrollToMessage are left alone.
    state.mutations = new MutationObserver(() => {
        if (state.autoScroll)
            messagesEl.scrollTop = messagesEl.scrollHeight;
    });
    state.mutations.observe(messagesEl, { childList: true, subtree: true, characterData: true });

    // Images report their height only on load, and that fires too late for the observer above to see
    // as a mutation. Capture phase, because load does not bubble.
    messagesEl.addEventListener('load', e => {
        if (state.autoScroll && e.target && e.target.tagName === 'IMG')
            messagesEl.scrollTop = messagesEl.scrollHeight;
    }, true);
}

// Enter-to-send has to cancel the keydown, or the browser still inserts the newline after the message
// has gone and the composer is left holding it. Blazor's @onkeydown:preventDefault cannot depend on
// which key it was, so the decision is made here. Shift+Enter and an IME composition keep the newline.
export function sendOnEnter(textareaEl, enabled) {
    if (!textareaEl) return;

    textareaEl.dataset.shinySendOnEnter = enabled ? 'true' : 'false';
    if (textareaEl.dataset.shinySendOnEnterHooked) return;

    textareaEl.dataset.shinySendOnEnterHooked = 'true';
    textareaEl.addEventListener('keydown', e => {
        if (textareaEl.dataset.shinySendOnEnter === 'true'
            && e.key === 'Enter' && !e.shiftKey && !e.isComposing)
            e.preventDefault();
    });
}

// Grow the composer with its content up to maxRows, then let it scroll. The cap is computed from the
// live font metrics rather than hardcoded so a restyled entry still gets maxRows worth of text.
export function autoGrow(textareaEl, maxRows) {
    if (!textareaEl) return;

    const cs = getComputedStyle(textareaEl);
    const lineHeight = parseFloat(cs.lineHeight) || parseFloat(cs.fontSize) * 1.4;
    const chrome = parseFloat(cs.paddingTop) + parseFloat(cs.paddingBottom)
        + parseFloat(cs.borderTopWidth) + parseFloat(cs.borderBottomWidth);

    const max = Math.round(lineHeight * Math.max(1, maxRows || 1) + chrome);
    const border = parseFloat(cs.borderTopWidth) + parseFloat(cs.borderBottomWidth);

    textareaEl.style.maxHeight = max + 'px';
    textareaEl.style.height = 'auto';

    // scrollHeight excludes borders; box-sizing is border-box (see the stylesheet), so add them back.
    const wanted = textareaEl.scrollHeight + border;
    textareaEl.style.height = Math.min(wanted, max) + 'px';
    textareaEl.style.overflowY = wanted > max ? 'auto' : 'hidden';
}

export function scrollToEnd(messagesEl, animate) {
    const state = states.get(messagesEl);
    if (!state) return;

    messagesEl.scrollTo({
        top: messagesEl.scrollHeight,
        behavior: animate ? 'smooth' : 'instant'
    });
    state.autoScroll = true;
}

export function scrollToMessage(messagesEl, messageIndex) {
    const state = states.get(messagesEl);
    if (!state) return;

    const wraps = messagesEl.querySelectorAll('.shiny-chat-bubble-wrap');
    if (wraps[messageIndex]) {
        wraps[messageIndex].scrollIntoView({ behavior: 'smooth', block: 'start' });
        state.autoScroll = false;
    }
}

export function maintainScrollPosition(messagesEl, previousScrollHeight) {
    const state = states.get(messagesEl);
    if (!state) return;

    const newScrollHeight = messagesEl.scrollHeight;
    messagesEl.scrollTop = newScrollHeight - previousScrollHeight;
}

export function getScrollHeight(messagesEl) {
    return messagesEl ? messagesEl.scrollHeight : 0;
}

export function isNearBottom(messagesEl) {
    if (!messagesEl) return true;
    const { scrollTop, scrollHeight, clientHeight } = messagesEl;
    return scrollHeight - scrollTop - clientHeight < 50;
}

// Wrap the current textarea selection with markdown delimiters (or insert a placeholder
// when there is no selection). Returns the new full text so .NET can update its bound field.
export function wrapSelection(textareaEl, before, after, placeholder) {
    if (!textareaEl) return '';
    const value = textareaEl.value ?? '';
    const start = textareaEl.selectionStart ?? value.length;
    const end = textareaEl.selectionEnd ?? value.length;
    const selected = value.substring(start, end) || placeholder || '';
    const next = value.substring(0, start) + before + selected + after + value.substring(end);

    textareaEl.value = next;
    const caret = start + before.length;
    textareaEl.focus();
    textareaEl.setSelectionRange(caret, caret + selected.length);
    return next;
}

// Prompt for a URL (and optional text) and insert a markdown link. Returns the new full text.
export function insertLink(textareaEl) {
    if (!textareaEl) return '';
    const value = textareaEl.value ?? '';
    const start = textareaEl.selectionStart ?? value.length;
    const end = textareaEl.selectionEnd ?? value.length;
    const selected = value.substring(start, end);

    const url = window.prompt('Link URL', 'https://');
    if (!url) return value;
    const text = selected || window.prompt('Link text', '') || url;

    const link = '[' + text + '](' + url + ')';
    const next = value.substring(0, start) + link + value.substring(end);
    textareaEl.value = next;
    const caret = start + link.length;
    textareaEl.focus();
    textareaEl.setSelectionRange(caret, caret);
    return next;
}

export function copyText(text) {
    if (navigator.clipboard && navigator.clipboard.writeText)
        return navigator.clipboard.writeText(text ?? '');
    return Promise.resolve();
}

export function dispose(messagesEl) {
    const state = states.get(messagesEl);
    if (state && state.mutations)
        state.mutations.disconnect();

    states.delete(messagesEl);
}
