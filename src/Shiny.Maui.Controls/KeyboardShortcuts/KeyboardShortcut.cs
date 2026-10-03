using System.Windows.Input;
using Shiny.Controls.Keyboard;

namespace Shiny.Maui.Controls;

/// <summary>
/// One keyboard shortcut, declared in XAML under <see cref="KeyboardShortcuts"/>.
/// </summary>
/// <example>
/// <code>
/// &lt;ContentPage xmlns:shiny="http://shiny.net/maui/controls"&gt;
///     &lt;shiny:KeyboardShortcuts.Shortcuts&gt;
///         &lt;shiny:KeyboardShortcut Gesture="Primary+S" Command="{Binding SaveCommand}" Description="Save" /&gt;
///         &lt;shiny:KeyboardShortcut Key="F5" Command="{Binding RefreshCommand}" /&gt;
///         &lt;shiny:KeyboardShortcut Gesture="Ctrl+K, Ctrl+C" Command="{Binding CommentCommand}" /&gt;
///     &lt;/shiny:KeyboardShortcuts.Shortcuts&gt;
/// &lt;/ContentPage&gt;
/// </code>
/// </example>
/// <remarks>
/// <para>
/// Give either <see cref="Gesture"/> (<c>"Primary+Shift+A"</c>) or <see cref="Key"/> with
/// <see cref="Modifiers"/>; the gesture wins when both are set. Prefer
/// <see cref="KeyModifiers.Primary"/> over <c>Control</c> in cross-platform code — it is ⌘ on a Mac.
/// </para>
/// <para>
/// Native key sources: Windows, Android (hardware keyboards) and iOS/iPadOS (hardware keyboards,
/// observed through GameController, so the key is not swallowed) come with this package; macOS
/// AppKit, Linux GTK4 and Mac Catalyst need <c>Shiny.Maui.Controls.Desktop</c> with
/// <c>UseDesktopKeyboardShortcuts()</c>.
/// </para>
/// </remarks>
public class KeyboardShortcut : BindableObject
{
    public KeyboardShortcut()
    {
        this.Binding = new KeyboardShortcutBinding(KeyGesture.From(KeyModifiers.None, "Escape"))
        {
            Pressed = this.OnPressed,
            Released = this.OnReleased,
            CanExecute = this.CanExecute,
            IsEnabled = false,
            Tag = this
        };
    }

    /// <summary>The engine registration this shortcut drives. Every property here writes through to it.</summary>
    internal KeyboardShortcutBinding Binding { get; }

    /// <summary>Raised when the gesture is pressed, before <see cref="Command"/> runs. Set <see cref="KeyboardShortcutEventArgs.Handled"/> to false to let the key through.</summary>
    public event EventHandler<KeyboardShortcutEventArgs>? Pressed;

    /// <summary>Raised when the gesture's last key comes back up, or the window loses focus while it is held.</summary>
    public event EventHandler<KeyboardShortcutEventArgs>? Released;

    public static readonly BindableProperty GestureProperty = BindableProperty.Create(
        nameof(Gesture),
        typeof(string),
        typeof(KeyboardShortcut),
        null,
        propertyChanged: (b, _, _) => ((KeyboardShortcut)b).UpdateGesture()
    );

    /// <summary>
    /// The keys, in gesture syntax: <c>"Primary+S"</c>, <c>"Ctrl+Shift+A"</c>, <c>"F5"</c>, <c>"?"</c>,
    /// <c>"Ctrl+K, Ctrl+C"</c>. See <see cref="KeyGesture"/> for the full grammar.
    /// </summary>
    public string? Gesture
    {
        get => (string?)this.GetValue(GestureProperty);
        set => this.SetValue(GestureProperty, value);
    }

    public static readonly BindableProperty KeyProperty = BindableProperty.Create(
        nameof(Key),
        typeof(string),
        typeof(KeyboardShortcut),
        null,
        propertyChanged: (b, _, _) => ((KeyboardShortcut)b).UpdateGesture()
    );

    /// <summary>The key, used with <see cref="Modifiers"/> when <see cref="Gesture"/> is not set: <c>"A"</c>, <c>"F5"</c>, <c>"Escape"</c>.</summary>
    public string? Key
    {
        get => (string?)this.GetValue(KeyProperty);
        set => this.SetValue(KeyProperty, value);
    }

    public static readonly BindableProperty ModifiersProperty = BindableProperty.Create(
        nameof(Modifiers),
        typeof(KeyModifiers),
        typeof(KeyboardShortcut),
        KeyModifiers.None,
        propertyChanged: (b, _, _) => ((KeyboardShortcut)b).UpdateGesture()
    );

    /// <summary>The modifiers for <see cref="Key"/> — <c>Modifiers="Primary,Shift"</c>. Ignored when <see cref="Gesture"/> is set.</summary>
    public KeyModifiers Modifiers
    {
        get => (KeyModifiers)this.GetValue(ModifiersProperty);
        set => this.SetValue(ModifiersProperty, value);
    }

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command),
        typeof(ICommand),
        typeof(KeyboardShortcut)
    );

    /// <summary>
    /// Executed when the gesture is pressed. While <c>CanExecute</c> is false the shortcut stands
    /// aside entirely and the key reaches the focused control.
    /// </summary>
    public ICommand? Command
    {
        get => (ICommand?)this.GetValue(CommandProperty);
        set => this.SetValue(CommandProperty, value);
    }

    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(
        nameof(CommandParameter),
        typeof(object),
        typeof(KeyboardShortcut)
    );

    /// <summary>Passed to <see cref="Command"/> and <see cref="ReleasedCommand"/>.</summary>
    public object? CommandParameter
    {
        get => this.GetValue(CommandParameterProperty);
        set => this.SetValue(CommandParameterProperty, value);
    }

    public static readonly BindableProperty ReleasedCommandProperty = BindableProperty.Create(
        nameof(ReleasedCommand),
        typeof(ICommand),
        typeof(KeyboardShortcut)
    );

    /// <summary>Executed when the key comes back up — the other half of a push-to-talk.</summary>
    public ICommand? ReleasedCommand
    {
        get => (ICommand?)this.GetValue(ReleasedCommandProperty);
        set => this.SetValue(ReleasedCommandProperty, value);
    }

    public static readonly BindableProperty IsEnabledProperty = BindableProperty.Create(
        nameof(IsEnabled),
        typeof(bool),
        typeof(KeyboardShortcut),
        true,
        propertyChanged: (b, _, _) => ((KeyboardShortcut)b).UpdateEnabled()
    );

    /// <summary>A disabled shortcut never matches and the key passes through. On by default.</summary>
    public bool IsEnabled
    {
        get => (bool)this.GetValue(IsEnabledProperty);
        set => this.SetValue(IsEnabledProperty, value);
    }

    public static readonly BindableProperty AllowRepeatProperty = BindableProperty.Create(
        nameof(AllowRepeat),
        typeof(bool),
        typeof(KeyboardShortcut),
        false,
        propertyChanged: (b, _, n) => ((KeyboardShortcut)b).Binding.AllowRepeat = (bool)n
    );

    /// <summary>
    /// Fire again for each auto-repeat while the key is held — for nudging a selection with the
    /// arrows. Off by default. iOS reports no repeats at all.
    /// </summary>
    public bool AllowRepeat
    {
        get => (bool)this.GetValue(AllowRepeatProperty);
        set => this.SetValue(AllowRepeatProperty, value);
    }

    public static readonly BindableProperty TextInputProperty = BindableProperty.Create(
        nameof(TextInput),
        typeof(TextInputBehavior),
        typeof(KeyboardShortcut),
        TextInputBehavior.Auto,
        propertyChanged: (b, _, n) => ((KeyboardShortcut)b).Binding.TextInput = (TextInputBehavior)n
    );

    /// <summary>
    /// Whether the shortcut fires while a text field has focus. <see cref="TextInputBehavior.Auto"/>
    /// (the default) lets <c>Ctrl+B</c> through and keeps plain keys like <c>J</c> or <c>?</c> with the field.
    /// </summary>
    public TextInputBehavior TextInput
    {
        get => (TextInputBehavior)this.GetValue(TextInputProperty);
        set => this.SetValue(TextInputProperty, value);
    }

    public static readonly BindableProperty DescriptionProperty = BindableProperty.Create(
        nameof(Description),
        typeof(string),
        typeof(KeyboardShortcut),
        null,
        propertyChanged: (b, _, n) => ((KeyboardShortcut)b).Binding.Description = (string?)n
    );

    /// <summary>What the shortcut does, for a cheat sheet (<see cref="IKeyboardShortcutService.GetActiveShortcuts"/>).</summary>
    public string? Description
    {
        get => (string?)this.GetValue(DescriptionProperty);
        set => this.SetValue(DescriptionProperty, value);
    }

    public static readonly BindableProperty CategoryProperty = BindableProperty.Create(
        nameof(Category),
        typeof(string),
        typeof(KeyboardShortcut),
        null,
        propertyChanged: (b, _, n) => ((KeyboardShortcut)b).Binding.Category = (string?)n
    );

    /// <summary>A heading to group the shortcut under in a cheat sheet.</summary>
    public string? Category
    {
        get => (string?)this.GetValue(CategoryProperty);
        set => this.SetValue(CategoryProperty, value);
    }

    static readonly BindablePropertyKey DisplayTextPropertyKey = BindableProperty.CreateReadOnly(
        nameof(DisplayText),
        typeof(string),
        typeof(KeyboardShortcut),
        String.Empty
    );

    public static readonly BindableProperty DisplayTextProperty = DisplayTextPropertyKey.BindableProperty;

    /// <summary>
    /// The gesture written the way this platform writes it — <c>Ctrl+Shift+S</c> on Windows,
    /// <c>⇧⌘S</c> on a Mac. Bind a tooltip or a menu hint to it. Empty while the gesture is invalid.
    /// </summary>
    public string DisplayText => (string)this.GetValue(DisplayTextProperty);

    /// <summary>The parsed gesture, or null while <see cref="Gesture"/>/<see cref="Key"/> do not describe one.</summary>
    public KeyGesture? ParsedGesture { get; private set; }

    void UpdateGesture()
    {
        KeyGesture? parsed = null;

        if (!String.IsNullOrWhiteSpace(this.Gesture))
        {
            if (!KeyGesture.TryParse(this.Gesture, out parsed))
                System.Diagnostics.Debug.WriteLine($"[Shiny.KeyboardShortcuts] '{this.Gesture}' is not a keyboard gesture — the shortcut is disabled.");
        }
        else if (!String.IsNullOrWhiteSpace(this.Key))
        {
            if (KeyNames.TryNormalize(this.Key, out var key, out var kind))
                parsed = KeyGesture.From(new KeyChord(this.Modifiers, key, kind));
            else
                System.Diagnostics.Debug.WriteLine($"[Shiny.KeyboardShortcuts] '{this.Key}' is not a key name — the shortcut is disabled.");
        }

        this.ParsedGesture = parsed;
        if (parsed != null)
            this.Binding.Gesture = parsed;

        this.SetValue(DisplayTextPropertyKey, parsed?.ToDisplayString(KeyboardShortcutManager.Engine.Platform) ?? String.Empty);
        this.UpdateEnabled();
    }

    void UpdateEnabled() => this.Binding.IsEnabled = this.IsEnabled && this.ParsedGesture != null;

    bool CanExecute() => this.Command?.CanExecute(this.CommandParameter) ?? true;

    void OnPressed(KeyboardShortcutEventArgs args)
    {
        this.Pressed?.Invoke(this, args);
        if (!args.Handled)
            return;

        if (this.Command is { } command && command.CanExecute(this.CommandParameter))
            command.Execute(this.CommandParameter);
    }

    void OnReleased(KeyboardShortcutEventArgs args)
    {
        this.Released?.Invoke(this, args);

        if (this.ReleasedCommand is { } command && command.CanExecute(this.CommandParameter))
            command.Execute(this.CommandParameter);
    }

    public override string ToString() => this.ParsedGesture?.ToString() ?? this.Gesture ?? this.Key ?? "(no gesture)";
}
