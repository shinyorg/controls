using Shiny.Controls.Keyboard;

namespace Shiny.Maui.Controls;

/// <summary>The default <see cref="IKeyboardShortcutService"/> — a facade over the app's one shortcut engine.</summary>
public class KeyboardShortcutService : IKeyboardShortcutService
{
    static KeyboardShortcutEngine Engine => KeyboardShortcutManager.Engine;

    public bool IsSupported => KeyboardShortcutManager.IsSupported;

    public KeyboardPlatform Platform => Engine.Platform;

    public TimeSpan ChordTimeout
    {
        get => Engine.ChordTimeout;
        set => Engine.ChordTimeout = value;
    }

    public bool IsChordPending => Engine.IsChordPending;

    public IReadOnlyList<KeyChord> PendingChords => Engine.PendingChords;

    public event EventHandler? ChordStateChanged
    {
        add => Engine.ChordStateChanged += value;
        remove => Engine.ChordStateChanged -= value;
    }

    public event EventHandler<KeyboardShortcutEventArgs>? ShortcutInvoked
    {
        add => Engine.ShortcutInvoked += value;
        remove => Engine.ShortcutInvoked -= value;
    }

    public IDisposable Register(string gesture, Action<KeyboardShortcutEventArgs> pressed, Action<KeyboardShortcutBinding>? configure = null, Window? window = null)
    {
        var binding = new KeyboardShortcutBinding(KeyGesture.Parse(gesture), pressed);
        configure?.Invoke(binding);
        return this.Register(binding, window);
    }

    public IDisposable Register(KeyboardShortcutBinding binding, Window? window = null)
    {
        if (window == null)
        {
            Engine.GlobalScope.Add(binding);
            KeyboardShortcutManager.EnsureOpenWindows();
            return new Registration(() => Engine.GlobalScope.Remove(binding));
        }

        var scope = Engine.CreateScope("Window", window);
        scope.Add(binding);
        scope.Activate();
        KeyboardShortcutManager.EnsureWindow(window);
        return new Registration(scope.Dispose);
    }

    public IReadOnlyList<ActiveShortcut> GetActiveShortcuts(Window? window = null)
        => Engine.GetActiveShortcuts(window ?? Application.Current?.Windows.FirstOrDefault());

    public string Format(string gesture) => this.Format(KeyGesture.Parse(gesture));

    public string Format(KeyGesture gesture) => gesture.ToDisplayString(this.Platform);

    sealed class Registration(Action dispose) : IDisposable
    {
        Action? dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref this.dispose, null)?.Invoke();
    }
}
