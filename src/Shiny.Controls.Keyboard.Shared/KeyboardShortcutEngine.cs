namespace Shiny.Controls.Keyboard;

/// <summary>
/// Decides which shortcut, if any, a key stroke presses — and presses it.
/// </summary>
/// <remarks>
/// <para>
/// Hosts feed it every key down and up from a window (<see cref="Process"/>) and do what it says
/// with the native event: a <c>true</c> return means "swallow it". Everything else lives here: scope
/// precedence and modal blocking, the text-field rule, AltGr, chord sequences and their timeout,
/// auto-repeat, and pairing each release with the shortcut whose key went down.
/// </para>
/// <para>
/// Handlers are invoked outside the engine's lock, so a handler may register or remove shortcuts,
/// open a dialog that activates a modal scope, and so on.
/// </para>
/// </remarks>
public sealed class KeyboardShortcutEngine
{
    static readonly object anyWindow = new();

    readonly TimeProvider time;
    readonly List<KeyboardShortcutScope> scopes = new();
    readonly Dictionary<(object Window, string Key), Held> held = new();

    long activation;
    List<Candidate>? pending;
    int pendingIndex;
    long pendingAt;
    object? pendingWindow;

    /// <summary>Create an engine for <paramref name="platform"/>.</summary>
    /// <param name="platform">Decides what <see cref="KeyModifiers.Primary"/> means.</param>
    /// <param name="timeProvider">The clock behind <see cref="ChordTimeout"/>; the system clock when null.</param>
    public KeyboardShortcutEngine(KeyboardPlatform platform, TimeProvider? timeProvider = null)
    {
        this.Platform = platform;
        this.time = timeProvider ?? TimeProvider.System;
        this.GlobalScope = new KeyboardShortcutScope(this, "Global");
        this.GlobalScope.ForceActive();
    }

    internal object Gate { get; } = new();

    /// <summary>Decides what <see cref="KeyModifiers.Primary"/> means.</summary>
    public KeyboardPlatform Platform { get; set; }

    /// <summary>
    /// How long the second chord of a sequence like <c>Ctrl+K, Ctrl+C</c> may take before the first
    /// is forgotten. Two seconds by default; <see cref="TimeSpan.Zero"/> waits indefinitely (until
    /// any key that does not continue the sequence).
    /// </summary>
    public TimeSpan ChordTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Always active, every window, lowest precedence — where app-wide shortcuts registered in code
    /// live. Still blocked by a modal scope.
    /// </summary>
    public KeyboardShortcutScope GlobalScope { get; }

    /// <summary>True while the first chord(s) of a sequence have been pressed and the engine waits for the rest.</summary>
    public bool IsChordPending
    {
        get
        {
            lock (this.Gate)
                return this.pending != null;
        }
    }

    /// <summary>The chords pressed so far in a pending sequence, for a "waiting for the second key" hint.</summary>
    public IReadOnlyList<KeyChord> PendingChords
    {
        get
        {
            lock (this.Gate)
            {
                if (this.pending is not { Count: > 0 } list)
                    return [];

                return list[0].Binding.Gesture.Chords.Take(this.pendingIndex).ToArray();
            }
        }
    }

    /// <summary>Raised when a sequence starts waiting for its next chord, and again when it completes, is abandoned or times out.</summary>
    public event EventHandler? ChordStateChanged;

    /// <summary>Raised after a shortcut's <c>Pressed</c> handler runs — for logging, analytics or a key-cast overlay.</summary>
    public event EventHandler<KeyboardShortcutEventArgs>? ShortcutInvoked;

    /// <summary>Create a scope. It starts inactive.</summary>
    public KeyboardShortcutScope CreateScope(string? name = null, object? window = null, KeyboardShortcutScope? parent = null, bool isModal = false)
    {
        var scope = new KeyboardShortcutScope(this, name)
        {
            Window = window,
            Parent = parent,
            IsModal = isModal
        };

        lock (this.Gate)
            this.scopes.Add(scope);

        return scope;
    }

    /// <summary>Deactivate a scope, release anything it holds and drop it.</summary>
    public void RemoveScope(KeyboardShortcutScope scope)
    {
        if (scope == this.GlobalScope)
            throw new InvalidOperationException("The global scope cannot be removed.");

        scope.Deactivate();
        scope.Clear();

        lock (this.Gate)
        {
            this.scopes.Remove(scope);
            foreach (var child in this.scopes)
            {
                if (child.Parent == scope)
                    child.Parent = scope.Parent;
            }
        }
    }

    /// <summary>
    /// Offer one key stroke from <paramref name="window"/>. Returns true when it pressed (or
    /// continued, or released) a shortcut and the native event should be swallowed.
    /// </summary>
    public bool Process(KeyStroke stroke, object? window = null)
    {
        Func<bool>? invoke = null;
        bool result;

        lock (this.Gate)
            result = this.Decide(stroke, window ?? anyWindow, ref invoke);

        // The decision is made under the lock; the handlers run outside it. A handler can still
        // turn a match down (Handled = false), which is why its answer is the one returned.
        return invoke == null ? result : invoke();
    }

    /// <summary>
    /// Release every key held in <paramref name="window"/> (every window when null) and abandon a
    /// pending sequence. Hosts call this when a window loses focus: the key-up for anything still
    /// held will be delivered to some other window, or to nobody.
    /// </summary>
    public void ReleaseAll(object? window = null)
    {
        List<Held> released;
        bool chordChanged;

        lock (this.Gate)
        {
            var keys = this.held.Keys.Where(x => window == null || ReferenceEquals(x.Window, window)).ToList();
            released = keys.Select(k => this.held[k]).ToList();
            foreach (var key in keys)
                this.held.Remove(key);

            chordChanged = this.pending != null && (window == null || ReferenceEquals(this.pendingWindow, window));
            if (chordChanged)
                this.ClearPendingUnsafe();
        }

        foreach (var item in released)
            InvokeReleased(item);

        if (chordChanged)
            this.ChordStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Every shortcut that can currently be reached from <paramref name="window"/>, highest
    /// precedence first — the list a "press ? for shortcuts" sheet shows.
    /// </summary>
    public IReadOnlyList<ActiveShortcut> GetActiveShortcuts(object? window = null)
    {
        lock (this.Gate)
        {
            var result = new List<ActiveShortcut>();
            var seen = new HashSet<KeyGesture>();

            foreach (var scope in this.EligibleScopes(window ?? anyWindow, includeAnyWindow: window == null))
            {
                foreach (var binding in scope.BindingsUnsafe)
                {
                    if (!binding.IsEnabled)
                        continue;

                    result.Add(new ActiveShortcut(binding, scope, !seen.Add(binding.Gesture)));
                }
            }

            return result;
        }
    }

    internal long NextActivation() => ++this.activation;

    /// <summary>A binding is going away — drop any key it is holding without calling it back.</summary>
    internal void Forget(KeyboardShortcutBinding binding)
    {
        lock (this.Gate)
        {
            foreach (var key in this.held.Where(x => x.Value.Binding == binding).Select(x => x.Key).ToList())
                this.held.Remove(key);

            if (this.pending != null)
            {
                this.pending.RemoveAll(x => x.Binding == binding);
                if (this.pending.Count == 0)
                    this.ClearPendingUnsafe();
            }
        }
    }

    // -------------------------------------------------------------------------------------

    bool Decide(KeyStroke stroke, object window, ref Func<bool>? invoke)
    {
        var identity = (window, stroke.Identity);

        if (stroke.IsKeyUp)
        {
            if (!this.held.Remove(identity, out var released))
                return false;

            invoke = () =>
            {
                InvokeReleased(released with { Stroke = stroke });
                return true;
            };
            return true;
        }

        // Ctrl going down on its own presses nothing, and must not abandon a sequence half-typed
        // either — "Ctrl+K, Ctrl+C" is pressed by letting go of nothing but K.
        if (KeyNames.IsModifierCode(stroke.Code))
            return false;

        if (stroke.IsRepeat && this.held.TryGetValue(identity, out var holding))
        {
            // The key that pressed a shortcut is still down. Repeat it if it asked to be repeated;
            // swallow the repeat either way, or holding Ctrl+B would start typing b's.
            if (holding.Binding.AllowRepeat && holding.Binding.IsUsable())
            {
                var fire = this.Fire(holding.Binding, stroke, window, record: false);
                invoke = () =>
                {
                    fire();
                    return true;
                };
            }

            return true;
        }

        var now = this.time.GetTimestamp();
        var chordChanged = false;

        if (this.pending != null)
        {
            var expired = this.ChordTimeout > TimeSpan.Zero
                && this.time.GetElapsedTime(this.pendingAt, now) > this.ChordTimeout;

            if (expired || !ReferenceEquals(this.pendingWindow, window))
            {
                this.ClearPendingUnsafe();
                chordChanged = true;
            }
        }

        if (this.pending != null)
        {
            var index = this.pendingIndex;
            var next = this.pending
                .Where(x => x.Binding.Gesture.Chords.Count > index
                         && x.Binding.Gesture.Chords[index].Matches(stroke, this.Platform)
                         && x.Binding.IsUsable())
                .ToList();

            var complete = next.FirstOrDefault(x => x.Binding.Gesture.Chords.Count == index + 1);
            if (complete != null)
            {
                this.ClearPendingUnsafe();
                invoke = this.Fire(complete.Binding, stroke, window, record: true, chordChanged: true);
                return true;
            }

            if (next.Count > 0)
            {
                this.pending = next;
                this.pendingIndex++;
                this.pendingAt = now;
                invoke = this.ChordChanged(true);
                return true;
            }

            // Not a continuation. Forget the sequence and treat the stroke as a fresh one, rather
            // than eating it — a user who pressed Ctrl+K by mistake should not lose their next key.
            this.ClearPendingUnsafe();
            chordChanged = true;
        }

        foreach (var scope in this.EligibleScopes(window, includeAnyWindow: true))
        {
            List<Candidate>? prefixes = null;
            KeyboardShortcutBinding? single = null;

            foreach (var binding in scope.BindingsUnsafe)
            {
                var first = binding.Gesture.Chords[0];
                if (!first.Matches(stroke, this.Platform) || !this.Allows(binding, first, stroke) || !binding.IsUsable())
                    continue;

                if (binding.Gesture.IsSequence)
                {
                    if (!stroke.IsRepeat)
                        (prefixes ??= new()).Add(new Candidate(binding, scope));
                }
                else
                {
                    single ??= binding;
                }
            }

            // The innermost scope with anything to say decides. A sequence beats a single chord in
            // the same scope: with both "Ctrl+K" and "Ctrl+K, Ctrl+C" registered, waiting is the
            // only way the second can ever be reached.
            if (prefixes != null)
            {
                this.pending = prefixes;
                this.pendingIndex = 1;
                this.pendingAt = now;
                this.pendingWindow = window;
                invoke = this.ChordChanged(true);
                return true;
            }

            if (single != null)
            {
                invoke = this.Fire(single, stroke, window, record: !stroke.IsRepeat, chordChanged);
                return true;
            }
        }

        if (chordChanged)
            invoke = this.ChordChanged(false);

        return false;
    }

    bool Allows(KeyboardShortcutBinding binding, KeyChord first, KeyStroke stroke)
    {
        if (stroke.IsRepeat && !binding.AllowRepeat)
            return false;

        if (!stroke.IsTextInputFocused)
            return true;

        // AltGr types characters. While the user is typing, a Ctrl+Alt shortcut must not steal it.
        if (stroke.IsAltGraph)
        {
            var resolved = first.Modifiers.Resolve(this.Platform);
            if (resolved.HasFlag(KeyModifiers.Control) && resolved.HasFlag(KeyModifiers.Alt))
                return false;
        }

        return binding.TextInput switch
        {
            TextInputBehavior.Always => true,
            TextInputBehavior.Never => false,
            _ => first.IsCommandLike(this.Platform)
        };
    }

    Func<bool> Fire(KeyboardShortcutBinding binding, KeyStroke stroke, object window, bool record, bool chordChanged = false)
    {
        var args = new KeyboardShortcutEventArgs(binding, stroke);

        // Recorded before the handler runs rather than after, because the key-up can only arrive
        // after this call returns — and a handler that opens a modal window may not return until
        // long after it has.
        var identity = (window, stroke.Identity);
        if (record)
            this.held[identity] = new Held(binding, stroke);

        return () =>
        {
            if (chordChanged)
                this.RaiseChordChanged();

            binding.Pressed?.Invoke(args);

            if (!args.Handled && record)
            {
                lock (this.Gate)
                {
                    if (this.held.TryGetValue(identity, out var current) && current.Binding == binding)
                        this.held.Remove(identity);
                }
            }

            this.ShortcutInvoked?.Invoke(this, args);
            return args.Handled;
        };
    }

    Func<bool> ChordChanged(bool handled) => () =>
    {
        this.RaiseChordChanged();
        return handled;
    };

    void RaiseChordChanged() => this.ChordStateChanged?.Invoke(this, EventArgs.Empty);

    static void InvokeReleased(Held item)
        => item.Binding.Released?.Invoke(new KeyboardShortcutEventArgs(item.Binding, item.Stroke));

    void ClearPendingUnsafe()
    {
        this.pending = null;
        this.pendingIndex = 0;
        this.pendingWindow = null;
    }

    /// <summary>Active scopes that can see <paramref name="window"/>, highest precedence first, ending at the first modal one.</summary>
    IEnumerable<KeyboardShortcutScope> EligibleScopes(object window, bool includeAnyWindow)
    {
        var ordered = this.scopes
            .Where(x => x.IsActive && (x.Window == null || ReferenceEquals(x.Window, window) || (includeAnyWindow && window == anyWindow)))
            .Where(x => AncestorsActive(x))
            .OrderByDescending(x => x.Root.Activation)
            .ThenByDescending(x => x.Depth)
            .ThenByDescending(x => x.Activation)
            .ToList();

        foreach (var scope in ordered)
        {
            yield return scope;
            if (scope.IsModal)
                yield break;
        }

        yield return this.GlobalScope;
    }

    /// <summary>A scope whose enclosing scope is switched off is off too — a view on a page that has disappeared.</summary>
    static bool AncestorsActive(KeyboardShortcutScope scope)
    {
        var guard = 0;
        for (var p = scope.Parent; p != null && guard++ < 256; p = p.Parent)
        {
            if (!p.IsActive)
                return false;
        }

        return true;
    }

    sealed record Candidate(KeyboardShortcutBinding Binding, KeyboardShortcutScope Scope);

    sealed record Held(KeyboardShortcutBinding Binding, KeyStroke Stroke);
}
