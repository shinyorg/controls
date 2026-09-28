// Native text-input plumbing for the document editor.
//
// Blazor has no built-in `beforeinput` event, and registering a custom event type would need the
// consuming app to edit its index.html — so the listener is attached here instead and the library
// stays self-contained.
//
// `beforeinput` rather than key events on purpose: it is the only event that reports IME composition
// results, autocorrect, dictation and paste as ordinary text insertions. Handling keys works for a US
// keyboard and silently drops every other input method.
export function attach(element, dotNet) {
    if (!element)
        return null;

    const onBeforeInput = e => {
        // The contenteditable is only a keyboard target; the document model owns the text, so the
        // browser must never actually mutate this element.
        e.preventDefault();
        dotNet.invokeMethodAsync('HandleBeforeInput', e.inputType ?? '', e.data ?? null);
    };

    // Composition text arrives through beforeinput as insertCompositionText, but the element also has
    // to be cleared afterwards or the IME keeps appending to stale content.
    const onCompositionEnd = () => { element.textContent = ''; };

    // Paste arrives here with the clipboard's contents, which beforeinput does not carry for a
    // contenteditable - its data is null and the text is only on dataTransfer.
    const onPaste = e => {
        e.preventDefault();
        const text = e.clipboardData ? e.clipboardData.getData('text/plain') : '';
        dotNet.invokeMethodAsync('HandlePaste', text ?? '');
    };

    element.addEventListener('beforeinput', onBeforeInput);
    element.addEventListener('compositionend', onCompositionEnd);
    element.addEventListener('paste', onPaste);

    // Wrapped for .NET: a plain object cannot be marshalled back as an IJSObjectReference, and the
    // resulting deserialisation failure surfaces as "the listener silently never attached".
    return DotNet.createJSObjectReference({
        dispose: () => {
            element.removeEventListener('beforeinput', onBeforeInput);
            element.removeEventListener('compositionend', onCompositionEnd);
            element.removeEventListener('paste', onPaste);
        }
    });
}

/// Focuses the editor's hidden input. Done here rather than through ElementReference.FocusAsync so a
/// failure is visible in the console instead of vanishing into a catch.
export function focus(element) {
    element?.focus({ preventScroll: true });
}

export function detach(handle) {
    handle?.dispose?.();
    DotNet.disposeJSObjectReference?.(handle);
}

/// Puts plain text on the system clipboard. Needs a secure context; the caller treats a refusal as
/// "only the editor's own clipboard has it".
export async function writeClipboard(text) {
    if (navigator.clipboard?.writeText)
        await navigator.clipboard.writeText(text ?? '');
}

/// Reads the system clipboard's text for a Paste button, or null when the browser will not allow it.
export async function readClipboard() {
    try {
        return navigator.clipboard?.readText ? await navigator.clipboard.readText() : null;
    } catch {
        return null;
    }
}
