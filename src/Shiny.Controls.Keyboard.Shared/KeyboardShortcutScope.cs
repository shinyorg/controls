namespace Shiny.Controls.Keyboard;

/// <summary>
/// A group of shortcuts that are switched on and off together — usually everything one page, one
/// view or one dialog declares.
/// </summary>
/// <remarks>
/// <para>Precedence, when more than one active scope has a shortcut for the same keys:</para>
/// <list type="number">
/// <item><description>A scope beats every scope that encloses it (its <see cref="Parent"/> chain), so a view's <c>Ctrl+F</c> wins over its page's.</description></item>
/// <item><description>Between unrelated scopes, the tree that was activated most recently wins — the page that just appeared beats the one underneath it.</description></item>
/// <item><description>A <see cref="IsModal"/> scope ends the search: nothing beneath it is consulted, the engine's global scope included. That is what keeps an open dialog's keys from reaching the page behind it.</description></item>
/// </list>
/// </remarks>
public sealed class KeyboardShortcutScope : IDisposable
{
    readonly KeyboardShortcutEngine engine;
    readonly List<KeyboardShortcutBinding> bindings = new();

    internal KeyboardShortcutScope(KeyboardShortcutEngine engine, string? name)
    {
        this.engine = engine;
        this.Name = name;
    }

    /// <summary>A label for debugging and cheat sheets.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// The window this scope belongs to, compared by reference against the window a stroke came
    /// from. Null means every window.
    /// </summary>
    public object? Window { get; set; }

    /// <summary>The enclosing scope, if any. Decides precedence — see the class remarks.</summary>
    public KeyboardShortcutScope? Parent { get; set; }

    /// <summary>Block every scope beneath this one while it is active.</summary>
    public bool IsModal { get; set; }

    /// <summary>Whether the scope's shortcuts can currently fire.</summary>
    public bool IsActive { get; private set; }

    /// <summary>The shortcuts in this scope, in the order they were added — which is the order they are tried.</summary>
    public IReadOnlyList<KeyboardShortcutBinding> Bindings
    {
        get
        {
            lock (this.engine.Gate)
                return this.bindings.ToArray();
        }
    }

    internal long Activation { get; private set; }

    internal int Depth
    {
        get
        {
            var depth = 0;
            for (var p = this.Parent; p != null && depth < 256; p = p.Parent)
                depth++;

            return depth;
        }
    }

    internal KeyboardShortcutScope Root
    {
        get
        {
            var root = this;
            var guard = 0;
            while (root.Parent != null && guard++ < 256)
                root = root.Parent;

            return root;
        }
    }

    internal List<KeyboardShortcutBinding> BindingsUnsafe => this.bindings;

    /// <summary>Add a shortcut built elsewhere.</summary>
    public KeyboardShortcutBinding Add(KeyboardShortcutBinding binding)
    {
        lock (this.engine.Gate)
        {
            if (!this.bindings.Contains(binding))
                this.bindings.Add(binding);
        }

        return binding;
    }

    /// <summary>Add a shortcut.</summary>
    public KeyboardShortcutBinding Add(KeyGesture gesture, Action<KeyboardShortcutEventArgs> pressed)
        => this.Add(new KeyboardShortcutBinding(gesture, pressed));

    /// <summary>Add a shortcut from gesture syntax — <c>"Primary+S"</c>, <c>"Ctrl+K, Ctrl+C"</c>.</summary>
    public KeyboardShortcutBinding Add(string gesture, Action<KeyboardShortcutEventArgs> pressed)
        => this.Add(KeyGesture.Parse(gesture), pressed);

    /// <summary>Remove a shortcut. A key it is holding down is released first.</summary>
    public bool Remove(KeyboardShortcutBinding binding)
    {
        bool removed;
        lock (this.engine.Gate)
            removed = this.bindings.Remove(binding);

        if (removed)
            this.engine.Forget(binding);

        return removed;
    }

    /// <summary>Remove every shortcut.</summary>
    public void Clear()
    {
        KeyboardShortcutBinding[] all;
        lock (this.engine.Gate)
        {
            all = this.bindings.ToArray();
            this.bindings.Clear();
        }

        foreach (var binding in all)
            this.engine.Forget(binding);
    }

    /// <summary>
    /// Switch the scope on. Activating an inactive scope moves it to the front of its peers; calling
    /// this on one that is already active changes nothing.
    /// </summary>
    public void Activate()
    {
        lock (this.engine.Gate)
        {
            if (this.IsActive)
                return;

            this.IsActive = true;
            this.Activation = this.engine.NextActivation();
        }
    }

    /// <summary>Switch the scope off, releasing any key one of its shortcuts is holding.</summary>
    public void Deactivate()
    {
        lock (this.engine.Gate)
        {
            if (!this.IsActive)
                return;

            this.IsActive = false;
        }

        foreach (var binding in this.Bindings)
            this.engine.Forget(binding);
    }

    /// <summary>Deactivate, clear and detach from the engine.</summary>
    public void Dispose() => this.engine.RemoveScope(this);

    internal void ForceActive()
    {
        this.IsActive = true;
        this.Activation = 0;
    }

    public override string ToString() => $"Scope '{this.Name}' ({this.bindings.Count} shortcut(s), {(this.IsActive ? "active" : "inactive")}{(this.IsModal ? ", modal" : "")})";
}
