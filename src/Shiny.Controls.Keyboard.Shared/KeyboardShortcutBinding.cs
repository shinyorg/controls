namespace Shiny.Controls.Keyboard;

/// <summary>
/// What a shortcut handler receives.
/// </summary>
public sealed class KeyboardShortcutEventArgs : EventArgs
{
    /// <summary>Create the arguments for <paramref name="binding"/> firing on <paramref name="stroke"/>.</summary>
    public KeyboardShortcutEventArgs(KeyboardShortcutBinding binding, KeyStroke stroke)
    {
        this.Binding = binding;
        this.Stroke = stroke;
    }

    /// <summary>The shortcut that fired.</summary>
    public KeyboardShortcutBinding Binding { get; }

    /// <summary>The gesture that was pressed.</summary>
    public KeyGesture Gesture => this.Binding.Gesture;

    /// <summary>The final stroke of the gesture, as the platform reported it.</summary>
    public KeyStroke Stroke { get; }

    /// <summary>True when this is an auto-repeat while the key is held. Only seen with <see cref="KeyboardShortcutBinding.AllowRepeat"/>.</summary>
    public bool IsRepeat => this.Stroke.IsRepeat;

    /// <summary>
    /// Whether the key is swallowed. True by default; set it to false in a <c>Pressed</c> handler to
    /// let the key carry on to the focused control as if no shortcut had matched. Ignored for a
    /// release.
    /// </summary>
    public bool Handled { get; set; } = true;
}

/// <summary>
/// One registered shortcut: a gesture and what to do when it is pressed.
/// </summary>
/// <remarks>
/// Every property is read at the moment a key arrives, so changing one — disabling the shortcut,
/// swapping its gesture — takes effect on the next key without re-registering anything.
/// </remarks>
public sealed class KeyboardShortcutBinding
{
    /// <summary>Create a shortcut for <paramref name="gesture"/>, optionally with its <see cref="Pressed"/> handler.</summary>
    public KeyboardShortcutBinding(KeyGesture gesture, Action<KeyboardShortcutEventArgs>? pressed = null)
    {
        this.Gesture = gesture;
        this.Pressed = pressed;
    }

    /// <summary>The keys to press.</summary>
    public KeyGesture Gesture { get; set; }

    /// <summary>Called when the gesture is pressed — and again for each auto-repeat when <see cref="AllowRepeat"/> is on.</summary>
    public Action<KeyboardShortcutEventArgs>? Pressed { get; set; }

    /// <summary>
    /// Called when the gesture's last key comes back up, whatever happened to the modifiers in the
    /// meantime — or when the window loses focus while it is held, so a push-to-talk never sticks on.
    /// </summary>
    public Action<KeyboardShortcutEventArgs>? Released { get; set; }

    /// <summary>
    /// Checked before firing. Returning false makes the shortcut behave as if it did not exist, so
    /// the key reaches the focused control — which is what a disabled command should do.
    /// </summary>
    public Func<bool>? CanExecute { get; set; }

    /// <summary>A disabled shortcut never matches. On by default.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Fire again for each auto-repeat while the key is held. Off by default: a repeated Save or
    /// Delete is never what anyone meant. Repeats of a shortcut that does not allow them are still
    /// swallowed, so holding <c>Ctrl+B</c> does not start typing b's.
    /// </summary>
    public bool AllowRepeat { get; set; }

    /// <summary>Whether this shortcut fires while a text field has focus. See <see cref="TextInputBehavior"/>.</summary>
    public TextInputBehavior TextInput { get; set; } = TextInputBehavior.Auto;

    /// <summary>What the shortcut does, for a shortcut cheat sheet. Optional.</summary>
    public string? Description { get; set; }

    /// <summary>A heading to group the shortcut under in a cheat sheet. Optional.</summary>
    public string? Category { get; set; }

    /// <summary>Whatever the host wants to keep with the binding — the element that declared it, for instance.</summary>
    public object? Tag { get; set; }

    internal bool IsUsable() => this.IsEnabled && (this.CanExecute?.Invoke() ?? true);
}

/// <summary>
/// A shortcut as it stands right now, for a cheat sheet or a tooltip.
/// </summary>
/// <param name="Binding">The shortcut.</param>
/// <param name="Scope">The scope that holds it.</param>
/// <param name="IsShadowed">
/// True when a scope with higher precedence has a shortcut with the same gesture, so this one cannot
/// currently be reached.
/// </param>
public sealed record ActiveShortcut(KeyboardShortcutBinding Binding, KeyboardShortcutScope Scope, bool IsShadowed);
