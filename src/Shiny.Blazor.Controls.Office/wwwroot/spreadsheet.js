// Small browser helpers for SpreadsheetView and FormulaBar. Everything here is keyed off plain
// elements and primitives, so nothing crosses interop as an object the trimmer could strip.

// Keys the grid owns while it has focus. Anything the controller answers to is stopped here, in the
// same event, rather than through Blazor's render-time preventDefault flag - which is decided before
// the key arrives and so always applied to the key after the one it was meant for.
const passThrough = new Set(['F11', 'F12']);
const browserChords = new Set(['w', 't', 'n', 'q', '+', '-', '=', 'Tab']);

export function attachGrid(host) {
    if (!host || host.__shinySheet) {
        return;
    }

    const onKey = e => {
        if (e.target !== host) {
            return;
        }

        if (passThrough.has(e.key)) {
            return;
        }

        const ctrl = e.ctrlKey || e.metaKey;
        if (ctrl && !e.shiftKey && browserChords.has(e.key)) {
            return;
        }

        // Alt chords belong to the browser and the OS, except Excel's AutoSum and list dropdown.
        if (e.altKey && e.key !== '=' && e.key !== 'ArrowDown') {
            return;
        }

        e.preventDefault();
    };

    host.addEventListener('keydown', onKey);
    host.__shinySheet = onKey;
}

export function detachGrid(host) {
    if (host && host.__shinySheet) {
        host.removeEventListener('keydown', host.__shinySheet);
        delete host.__shinySheet;
    }
}

// While an autocomplete list is open, the keys that drive it must not also move the caret or the
// focus. The attribute is written by the component on every render, so this follows its state.
export function attachAssist(input) {
    if (!input || input.__shinyAssist) {
        return;
    }

    const onKey = e => {
        if (input.dataset.assistOpen === 'true' &&
            (e.key === 'ArrowUp' || e.key === 'ArrowDown' || e.key === 'Tab' || e.key === 'Enter' || e.key === 'Escape')) {
            e.preventDefault();
        }
    };

    input.addEventListener('keydown', onKey);
    input.__shinyAssist = onKey;
}

export function caret(input) {
    if (!input || typeof input.selectionStart !== 'number') {
        return 0;
    }

    return input.selectionStart;
}

export function setValue(input, value, position) {
    if (!input) {
        return;
    }

    input.value = value;
    input.focus();

    const at = Math.max(0, Math.min(position, value.length));
    input.setSelectionRange(at, at);
}

export function openUrl(url) {
    window.open(url, '_blank', 'noopener');
}

export function focus(element) {
    if (element) {
        element.focus();
    }
}

// A file the backstage produced (Save, Save As, Export) handed to the browser as a download. The bytes
// arrive as a Uint8Array; primitives only, so nothing here depends on a .NET type surviving trimming.
export function downloadFile(fileName, mimeType, bytes) {
    const url = URL.createObjectURL(new Blob([bytes], { type: mimeType }));
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    a.style.display = 'none';
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 30000);
}

// Print: the sheet rendered to a PDF and opened in a tab, where the browser's own viewer prints it.
export function openFile(fileName, mimeType, bytes) {
    const url = URL.createObjectURL(new Blob([bytes], { type: mimeType }));
    const opened = window.open(url, '_blank');
    if (!opened) {
        // Popup blocked: a download still gets the pages to the user.
        downloadFile(fileName, mimeType, bytes);
    }
    setTimeout(() => URL.revokeObjectURL(url), 120000);
}
