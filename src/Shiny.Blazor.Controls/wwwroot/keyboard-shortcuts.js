// Keyboard shortcuts for Shiny.Blazor.Controls.
//
// One capture-phase keydown/keyup listener on the window per service instance. Matching happens
// here, synchronously, because preventDefault has to be decided before the browser acts on the key
// and Blazor Server cannot reach .NET synchronously. Only a match (or a chord state change) crosses
// to .NET.
//
// The rules mirror Shiny.Controls.Keyboard's KeyChord.Matches and KeyboardShortcutEngine.Decide —
// layout-aware letters, positional digits, Shift-agnostic characters, the text-field rule, AltGr,
// chords with a timeout, swallowed repeats and paired releases. Change one, change the other.

const instances = new Map();

const CONTROL = 1, ALT = 2, SHIFT = 4, META = 8, PRIMARY = 16;

const MODIFIER_CODES = new Set([
    'ShiftLeft', 'ShiftRight', 'ControlLeft', 'ControlRight', 'AltLeft', 'AltRight',
    'MetaLeft', 'MetaRight', 'OSLeft', 'OSRight', 'Fn', 'FnLock', 'AltGraph', 'CapsLock'
]);

const NON_TEXT_INPUTS = new Set([
    'button', 'checkbox', 'radio', 'submit', 'reset', 'file', 'image', 'range', 'color', 'hidden'
]);

function detectPlatform() {
    const platform = (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || '';
    const ua = navigator.userAgent || '';
    if (/mac|iphone|ipad|ipod/i.test(platform) || /iPhone|iPad|Macintosh/.test(ua)) return 'apple';
    if (/android/i.test(platform) || /Android/.test(ua)) return 'android';
    if (/win/i.test(platform)) return 'windows';
    if (/linux|cros|x11/i.test(platform) || /Linux|CrOS/.test(ua)) return 'linux';
    return 'other';
}

export function attach(id, dotNet) {
    detach(id);

    const state = {
        dotNet,
        platform: detectPlatform(),
        timeout: 2000,
        scopes: new Map(),
        bindings: new Map(),
        byScope: new Map(),
        elements: new Map(),
        held: new Map(),
        pending: null
    };

    state.onDown = e => onKey(state, e, false);
    state.onUp = e => onKey(state, e, true);
    state.onBlur = () => releaseAll(state);

    window.addEventListener('keydown', state.onDown, true);
    window.addEventListener('keyup', state.onUp, true);
    window.addEventListener('blur', state.onBlur);

    instances.set(id, state);
    return state.platform;
}

export function update(id, json) {
    const state = instances.get(id);
    if (!state) return;

    const spec = JSON.parse(json);
    state.timeout = spec.chordTimeoutMs;
    state.scopes = new Map(spec.scopes.map(s => [s.id, s]));
    state.bindings = new Map(spec.bindings.map(b => [b.id, b]));
    state.byScope = new Map();
    for (const b of spec.bindings) {
        let list = state.byScope.get(b.scope);
        if (!list) state.byScope.set(b.scope, list = []);
        list.push(b);
    }

    // A binding that went away cannot be released or continued.
    for (const [key, held] of state.held) {
        if (!state.bindings.has(held.id)) state.held.delete(key);
    }
    if (state.pending) {
        state.pending.candidates = state.pending.candidates
            .map(c => state.bindings.get(c.id))
            .filter(c => c);
        if (state.pending.candidates.length === 0) clearPending(state, true);
    }
}

export function setElement(id, scopeId, element) {
    const state = instances.get(id);
    if (state) state.elements.set(scopeId, element);
}

export function detach(id) {
    const state = instances.get(id);
    if (!state) return;

    window.removeEventListener('keydown', state.onDown, true);
    window.removeEventListener('keyup', state.onUp, true);
    window.removeEventListener('blur', state.onBlur);
    instances.delete(id);
}

// ------------------------------------------------------------------------------------------

function onKey(state, e, isUp) {
    // An IME is composing: the keys belong to it.
    if (e.isComposing || e.keyCode === 229) return;

    const stroke = strokeOf(e, isUp);
    const result = decide(state, stroke);
    if (result && result.prevent) {
        e.preventDefault();
        e.stopPropagation();
    }
}

function strokeOf(e, isUp) {
    let mods = 0;
    if (e.ctrlKey) mods |= CONTROL;
    if (e.altKey) mods |= ALT;
    if (e.shiftKey) mods |= SHIFT;
    if (e.metaKey) mods |= META;

    // e.key is the typed character with Shift applied ("a", "A", "?", "ф") — or a name such as
    // "Escape"/"Dead" for keys that type nothing, which is not a character.
    const key = typeof e.key === 'string' && [...e.key].length === 1 ? e.key : null;
    const target = (e.composedPath && e.composedPath()[0]) || e.target;

    return {
        code: e.code || null,
        character: key,
        mods,
        repeat: !isUp && !!e.repeat,
        up: isUp,
        target,
        text: isTextInput(target),
        altGraph: !!(e.getModifierState && e.getModifierState('AltGraph'))
    };
}

function isTextInput(target) {
    let el = target && target.nodeType === 1 ? target : document.activeElement;
    if (!el) return false;
    if (el.isContentEditable) return true;

    const tag = el.tagName;
    if (tag === 'TEXTAREA' || tag === 'SELECT') return true;
    if (tag === 'INPUT') return !NON_TEXT_INPUTS.has((el.type || 'text').toLowerCase());
    return false;
}

function identity(stroke) {
    return stroke.code || (stroke.character || '').toUpperCase();
}

function resolve(mods, platform) {
    if ((mods & PRIMARY) === 0) return mods;
    return (mods & ~PRIMARY) | (platform === 'apple' ? META : CONTROL);
}

function letterOf(stroke) {
    if (stroke.character && /^[a-zA-Z]$/.test(stroke.character)) return stroke.character.toUpperCase();
    if (stroke.code && /^Key[A-Z]$/.test(stroke.code)) return stroke.code[3];
    return null;
}

function matches(chord, stroke, platform) {
    const required = resolve(chord.mods, platform);

    switch (chord.kind) {
        case 'char':
            return (stroke.mods & ~SHIFT) === (required & ~SHIFT) && stroke.character === chord.key;
        case 'letter':
            return stroke.mods === required && letterOf(stroke) === chord.key;
        case 'digit':
            if (stroke.mods !== required) return false;
            return stroke.code ? stroke.code === 'Digit' + chord.key : stroke.character === chord.key;
        default:
            if (stroke.mods !== required) return false;
            return stroke.code === chord.key || (chord.key === 'Enter' && stroke.code === 'NumpadEnter');
    }
}

function isCommandLike(chord, platform) {
    const resolved = resolve(chord.mods, platform);
    if ((resolved & (CONTROL | ALT | META)) !== 0) return true;
    return chord.kind === 'code' && (chord.key === 'Escape' || /^F([1-9]|1\d|2[0-4])$/.test(chord.key));
}

function allows(state, binding, first, stroke) {
    if (stroke.repeat && !binding.allowRepeat) return false;
    if (!stroke.text) return true;

    if (stroke.altGraph) {
        const resolved = resolve(first.mods, state.platform);
        if ((resolved & CONTROL) && (resolved & ALT)) return false;
    }

    switch (binding.textInput) {
        case 'always': return true;
        case 'never': return false;
        default: return isCommandLike(first, state.platform);
    }
}

// ------------------------------------------------------------------------------------------
// Scopes

function scopeLive(state, scope, target) {
    if (!scope || !scope.active) return false;
    if (!scope.element) return true;

    // An element scope only hears keys from inside it — focus-within, in effect.
    const el = state.elements.get(scope.id);
    return !!(el && target && el.contains(target));
}

function chainLive(state, scope, target) {
    let guard = 0;
    for (let s = scope; s && guard++ < 256; s = s.parent != null ? state.scopes.get(s.parent) : null) {
        if (!scopeLive(state, s, target)) return false;
    }
    return true;
}

function rootOf(state, scope) {
    let root = scope, guard = 0;
    while (root.parent != null && state.scopes.has(root.parent) && guard++ < 256) root = state.scopes.get(root.parent);
    return root;
}

function depthOf(state, scope) {
    let depth = 0, s = scope;
    while (s.parent != null && state.scopes.has(s.parent) && depth < 256) { s = state.scopes.get(s.parent); depth++; }
    return depth;
}

/** Live scopes, highest precedence first, ending at the first modal one, global (id 0) last. */
function eligibleScopes(state, target) {
    const live = [];
    for (const scope of state.scopes.values()) {
        if (scope.id === 0) continue;
        if (chainLive(state, scope, target)) live.push(scope);
    }

    live.sort((a, b) =>
        (rootOf(state, b).order - rootOf(state, a).order) ||
        (depthOf(state, b) - depthOf(state, a)) ||
        (b.order - a.order));

    const result = [];
    for (const scope of live) {
        result.push(scope);
        if (scope.modal) return result;
    }

    const global = state.scopes.get(0);
    if (global) result.push(global);
    return result;
}

// ------------------------------------------------------------------------------------------

function decide(state, stroke) {
    const key = identity(stroke);

    if (stroke.up) {
        const held = state.held.get(key);
        if (!held) return null;
        state.held.delete(key);
        invoke(state, held.id, stroke, true);
        return { prevent: held.prevent };
    }

    // Ctrl going down on its own presses nothing — and must not abandon "Ctrl+K, Ctrl+C" half-typed.
    if (MODIFIER_CODES.has(stroke.code)) return null;

    if (stroke.repeat && state.held.has(key)) {
        const held = state.held.get(key);
        const binding = state.bindings.get(held.id);
        if (binding && binding.allowRepeat && binding.enabled) invoke(state, binding.id, stroke, false);
        return { prevent: held.prevent };
    }

    const now = performance.now();

    if (state.pending && state.timeout > 0 && now - state.pending.at > state.timeout) {
        clearPending(state, true);
    }

    if (state.pending) {
        const index = state.pending.index;
        const next = state.pending.candidates.filter(b =>
            b.enabled && b.chords.length > index && matches(b.chords[index], stroke, state.platform));

        const complete = next.find(b => b.chords.length === index + 1);
        if (complete) {
            clearPending(state, true);
            return fire(state, complete, stroke, key);
        }

        if (next.length > 0) {
            state.pending = { candidates: next, index: index + 1, at: now };
            notifyChord(state, next[0].id, index + 1);
            return { prevent: true };
        }

        // Not a continuation: forget the sequence and treat this key as a fresh one rather than
        // eating it.
        clearPending(state, true);
    }

    for (const scope of eligibleScopes(state, stroke.target)) {
        const prefixes = [];
        let single = null;

        for (const binding of state.byScope.get(scope.id) || []) {
            if (!binding.enabled) continue;
            const first = binding.chords[0];
            if (!matches(first, stroke, state.platform) || !allows(state, binding, first, stroke)) continue;

            if (binding.chords.length > 1) {
                if (!stroke.repeat) prefixes.push(binding);
            } else if (!single) {
                single = binding;
            }
        }

        // The innermost scope with anything to say decides; a sequence beats a single chord there.
        if (prefixes.length > 0) {
            state.pending = { candidates: prefixes, index: 1, at: now };
            notifyChord(state, prefixes[0].id, 1);
            return { prevent: true };
        }

        if (single) return fire(state, single, stroke, key);
    }

    return null;
}

function fire(state, binding, stroke, key) {
    if (!stroke.repeat) state.held.set(key, { id: binding.id, prevent: binding.preventDefault });
    invoke(state, binding.id, stroke, false);
    return { prevent: binding.preventDefault };
}

function invoke(state, id, stroke, released) {
    state.dotNet
        .invokeMethodAsync('OnShortcut', id, released, stroke.repeat, stroke.code, stroke.character, stroke.mods)
        .catch(() => { /* the circuit is gone */ });
}

function notifyChord(state, id, index) {
    state.dotNet.invokeMethodAsync('OnChordState', id, index).catch(() => { });
}

function clearPending(state, notify) {
    if (!state.pending) return;
    state.pending = null;
    if (notify) notifyChord(state, 0, 0);
}

function releaseAll(state) {
    // The window lost focus: every key-up for what is held will go somewhere else.
    for (const held of state.held.values()) {
        invoke(state, held.id, { repeat: false, code: null, character: null, mods: 0 }, true);
    }
    state.held.clear();
    clearPending(state, true);
}
