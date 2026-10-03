using Microsoft.AspNetCore.Components;
using Shiny.Controls.Keyboard;

namespace Shiny.Blazor.Controls;

/// <summary>
/// One keyboard shortcut. Inside a <see cref="KeyboardShortcuts"/> group it follows the group's
/// scope; on its own it is page-wide at the lowest precedence. Renders nothing.
/// </summary>
/// <example>
/// <code>
/// &lt;KeyboardShortcut Gesture="Primary+Shift+P" OnPressed="OpenPalette" /&gt;
/// &lt;KeyboardShortcut Key="A" Modifiers="KeyModifiers.Control | KeyModifiers.Shift" OnPressed="SelectAll" /&gt;
/// &lt;KeyboardShortcut Key="Space" OnPressed="StartTalking" OnReleased="StopTalking" TextInput="TextInputBehavior.Never" /&gt;
/// </code>
/// </example>
/// <remarks>
/// Give either <see cref="Gesture"/> or <see cref="Key"/> with <see cref="Modifiers"/>; the gesture
/// wins when both are set. Prefer <see cref="KeyModifiers.Primary"/> to <c>Control</c> — it is ⌘ on
/// a Mac.
/// </remarks>
public class KeyboardShortcut : ComponentBase, IDisposable
{
    KeyboardShortcutService? service;
    KeyboardShortcutScope? target;
    KeyboardShortcutBinding? binding;
    string? signature;

    [Inject] IKeyboardShortcutService Service { get; set; } = null!;

    /// <summary>The group this shortcut belongs to. Public because a private cascading parameter is silently never set.</summary>
    [CascadingParameter] public KeyboardShortcuts? Group { get; set; }

    /// <summary>The keys in gesture syntax: <c>"Primary+S"</c>, <c>"Ctrl+Shift+A"</c>, <c>"F5"</c>, <c>"?"</c>, <c>"Ctrl+K, Ctrl+C"</c>.</summary>
    [Parameter] public string? Gesture { get; set; }

    /// <summary>The key, used with <see cref="Modifiers"/> when <see cref="Gesture"/> is not set.</summary>
    [Parameter] public string? Key { get; set; }

    /// <summary>The modifiers for <see cref="Key"/>. Ignored when <see cref="Gesture"/> is set.</summary>
    [Parameter] public KeyModifiers Modifiers { get; set; }

    /// <summary>Raised when the gesture is pressed — and for each auto-repeat when <see cref="AllowRepeat"/> is on.</summary>
    [Parameter] public EventCallback<KeyboardShortcutEventArgs> OnPressed { get; set; }

    /// <summary>Raised when the gesture's last key comes back up, or the window loses focus while it is held.</summary>
    [Parameter] public EventCallback<KeyboardShortcutEventArgs> OnReleased { get; set; }

    /// <summary>Fire again for each auto-repeat while held. Off by default; repeats are swallowed either way.</summary>
    [Parameter] public bool AllowRepeat { get; set; }

    /// <summary>Whether the shortcut fires while a text field has focus. <see cref="TextInputBehavior.Auto"/> lets <c>Ctrl+B</c> through and leaves plain keys to the field.</summary>
    [Parameter] public TextInputBehavior TextInput { get; set; } = TextInputBehavior.Auto;

    /// <summary>A disabled shortcut never matches and the key reaches the page. On by default.</summary>
    [Parameter] public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Stop the browser (and the focused element) acting on the key. On by default — turn it off to
    /// observe a key without taking it away. Some keys, such as Ctrl+W and Ctrl+T, belong to the
    /// browser and cannot be claimed by any page.
    /// </summary>
    [Parameter] public bool PreventDefault { get; set; } = true;

    /// <summary>What the shortcut does, for a cheat sheet.</summary>
    [Parameter] public string? Description { get; set; }

    /// <summary>A heading to group the shortcut under in a cheat sheet.</summary>
    [Parameter] public string? Category { get; set; }

    /// <summary>The parsed gesture, or null while <see cref="Gesture"/>/<see cref="Key"/> do not describe one.</summary>
    public KeyGesture? ParsedGesture { get; private set; }

    protected override void OnInitialized()
    {
        this.service = this.Service as KeyboardShortcutService
            ?? throw new InvalidOperationException(
                $"<KeyboardShortcut> needs the built-in {nameof(KeyboardShortcutService)}; register it with AddShinyKeyboardShortcuts() or AddShinyControls().");

        this.target = this.Group?.EngineScope ?? this.service.GlobalScope;
        this.binding = new KeyboardShortcutBinding(KeyGesture.From(KeyModifiers.None, "Escape"))
        {
            Pressed = args => _ = this.RaiseAsync(this.OnPressed, args),
            Released = args => _ = this.RaiseAsync(this.OnReleased, args),
            IsEnabled = false,
            Tag = this
        };
    }

    protected override void OnParametersSet()
    {
        this.ParsedGesture = Parse(this.Gesture, this.Key, this.Modifiers);

        var binding = this.binding!;
        if (this.ParsedGesture != null)
            binding.Gesture = this.ParsedGesture;

        binding.IsEnabled = this.IsEnabled && this.ParsedGesture != null;
        binding.AllowRepeat = this.AllowRepeat;
        binding.TextInput = this.TextInput;
        binding.Description = this.Description;
        binding.Category = this.Category;

        // A parent re-render re-sets every parameter. Only tell the browser when something it
        // matches on actually changed — under Blazor Server each update is a round trip.
        var signature = $"{binding.Gesture}|{binding.IsEnabled}|{binding.AllowRepeat}|{binding.TextInput}|{this.PreventDefault}";
        if (this.signature == null)
        {
            this.service!.AddBinding(this.target!, binding, this.PreventDefault);
        }
        else if (signature != this.signature)
        {
            this.service!.SetPreventDefault(binding, this.PreventDefault);
            this.service.Changed();
        }

        this.signature = signature;
    }

    protected override Task OnAfterRenderAsync(bool firstRender)
        => firstRender ? this.service!.StartAsync() : Task.CompletedTask;

    static KeyGesture? Parse(string? gesture, string? key, KeyModifiers modifiers)
    {
        if (!String.IsNullOrWhiteSpace(gesture))
            return KeyGesture.TryParse(gesture, out var parsed) ? parsed : null;

        if (!String.IsNullOrWhiteSpace(key) && KeyNames.TryNormalize(key, out var name, out var kind))
            return KeyGesture.From(new KeyChord(modifiers, name, kind));

        return null;
    }

    async Task RaiseAsync(EventCallback<KeyboardShortcutEventArgs> callback, KeyboardShortcutEventArgs args)
    {
        try
        {
            await callback.InvokeAsync(args);
        }
        catch (Exception ex)
        {
            // Routes to the nearest ErrorBoundary rather than vanishing into a discarded task.
            await this.DispatchExceptionAsync(ex);
        }
    }

    public void Dispose()
    {
        if (this.service != null && this.target != null && this.binding != null)
            this.service.RemoveBinding(this.target, this.binding);
    }
}
