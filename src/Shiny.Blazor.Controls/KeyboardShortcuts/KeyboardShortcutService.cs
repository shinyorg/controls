using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Shiny.Controls.Keyboard;

namespace Shiny.Blazor.Controls;

/// <summary>The default <see cref="IKeyboardShortcutService"/>.</summary>
/// <remarks>
/// <para>
/// The scopes and bindings live here, in a <see cref="KeyboardShortcutEngine"/> used as a registry
/// — which is what answers <see cref="GetActiveShortcuts"/> and formats gestures with the same code
/// MAUI uses. What the browser needs to match keys synchronously is pushed to
/// <c>keyboard-shortcuts.js</c> as one JSON document: a string rather than a graph of DTOs, because
/// array DTOs over interop lose their element type to trimming in a published WebAssembly app.
/// </para>
/// <para>
/// Pushes are coalesced: a page with thirty <see cref="KeyboardShortcut"/> children registers them
/// in one render and sends one update.
/// </para>
/// </remarks>
public sealed class KeyboardShortcutService : IKeyboardShortcutService
{
    readonly IJSRuntime js;
    readonly ILogger<KeyboardShortcutService>? logger;
    readonly string instanceId = Guid.NewGuid().ToString("N");
    readonly KeyboardShortcutEngine registry = new(KeyboardPlatform.Other);
    readonly Dictionary<KeyboardShortcutScope, ScopeState> scopes = new();
    readonly Dictionary<int, KeyboardShortcutBinding> bindingsById = new();
    readonly Dictionary<KeyboardShortcutBinding, int> idsByBinding = new();
    readonly HashSet<KeyboardShortcutBinding> allowDefault = new();

    IJSObjectReference? module;
    DotNetObjectReference<KeyboardShortcutService>? self;
    Task? starting;
    int generation;
    int nextId;
    long nextOrder;
    bool pushScheduled;
    KeyboardShortcutBinding? pendingBinding;
    int pendingIndex;

    public KeyboardShortcutService(IJSRuntime js, ILogger<KeyboardShortcutService>? logger = null)
    {
        this.js = js;
        this.logger = logger;
        this.scopes[this.registry.GlobalScope] = new ScopeState(0, 0, false, false);
    }

    public KeyboardPlatform Platform => this.registry.Platform;

    public event EventHandler? PlatformChanged;

    public bool IsRunning => this.module != null;

    public TimeSpan ChordTimeout
    {
        get => this.registry.ChordTimeout;
        set
        {
            this.registry.ChordTimeout = value;
            this.Changed();
        }
    }

    public bool IsChordPending => this.pendingBinding != null;

    public IReadOnlyList<KeyChord> PendingChords
        => this.pendingBinding?.Gesture.Chords.Take(this.pendingIndex).ToArray() ?? [];

    public event EventHandler? ChordStateChanged;

    public event EventHandler<KeyboardShortcutEventArgs>? ShortcutInvoked;

    public Task StartAsync()
    {
        if (this.module != null)
            return Task.CompletedTask;

        return this.starting ??= this.StartCoreAsync();
    }

    async Task StartCoreAsync()
    {
        var generation = this.generation;
        try
        {
            var loaded = await this.js
                .InvokeAsync<IJSObjectReference>("import", "./_content/Shiny.Blazor.Controls/keyboard-shortcuts.js")
                .ConfigureAwait(false);

            if (generation != this.generation)
            {
                await loaded.ReleaseLateAsync().ConfigureAwait(false);
                return;
            }

            var self = DotNetObjectReference.Create(this);
            this.self = self;
            this.module = loaded;

            var platform = await loaded.InvokeAsync<string>("attach", this.instanceId, self).ConfigureAwait(false);
            this.registry.Platform = ParsePlatform(platform);

            foreach (var (_, state) in this.scopes)
            {
                if (state.Element is { } element)
                    await loaded.InvokeVoidAsync("setElement", this.instanceId, state.Id, element).ConfigureAwait(false);
            }

            await this.PushAsync().ConfigureAwait(false);
            this.PlatformChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or InvalidOperationException or TaskCanceledException)
        {
            // Prerendering (no JS yet) or a circuit that went away mid-start. A later StartAsync —
            // the next component's first render — tries again.
            this.logger?.LogDebug(ex, "Keyboard shortcuts could not start yet.");
        }
        finally
        {
            this.starting = null;
        }
    }

    static KeyboardPlatform ParsePlatform(string? platform) => platform switch
    {
        "apple" => KeyboardPlatform.Apple,
        "windows" => KeyboardPlatform.Windows,
        "linux" => KeyboardPlatform.Linux,
        "android" => KeyboardPlatform.Android,
        _ => KeyboardPlatform.Other
    };

    public IDisposable Register(string gesture, Action<KeyboardShortcutEventArgs> pressed, Action<KeyboardShortcutBinding>? configure = null)
    {
        var binding = new KeyboardShortcutBinding(KeyGesture.Parse(gesture), pressed);
        configure?.Invoke(binding);
        return this.Register(binding);
    }

    public IDisposable Register(KeyboardShortcutBinding binding)
    {
        this.AddBinding(this.registry.GlobalScope, binding);
        _ = this.StartAsync();
        return new Registration(() => this.RemoveBinding(this.registry.GlobalScope, binding));
    }

    public IReadOnlyList<ActiveShortcut> GetActiveShortcuts() => this.registry.GetActiveShortcuts();

    public string Format(string gesture) => this.Format(KeyGesture.Parse(gesture));

    public string Format(KeyGesture gesture) => gesture.ToDisplayString(this.Platform);

    // -------------------------------------------------------------------------------------
    // Used by the components

    internal KeyboardShortcutScope GlobalScope => this.registry.GlobalScope;

    internal KeyboardShortcutScope CreateScope(string? name, KeyboardShortcutScope? parent, bool isModal, bool isElement)
    {
        var scope = this.registry.CreateScope(name, parent: parent, isModal: isModal);
        this.scopes[scope] = new ScopeState(++this.nextId, 0, isModal, isElement);
        return scope;
    }

    internal void UpdateScope(KeyboardShortcutScope scope, bool isModal, bool isActive)
    {
        if (!this.scopes.TryGetValue(scope, out var state))
            return;

        scope.IsModal = isModal;
        state.IsModal = isModal;

        if (isActive && !scope.IsActive)
        {
            scope.Activate();
            state.Order = ++this.nextOrder;
        }
        else if (!isActive && scope.IsActive)
        {
            scope.Deactivate();
        }

        this.Changed();
    }

    internal async Task SetScopeElementAsync(KeyboardShortcutScope scope, ElementReference element)
    {
        if (!this.scopes.TryGetValue(scope, out var state))
            return;

        state.Element = element;
        if (this.module is { } module)
        {
            try
            {
                await module.InvokeVoidAsync("setElement", this.instanceId, state.Id, element).ConfigureAwait(false);
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }

    internal void RemoveScope(KeyboardShortcutScope scope)
    {
        if (!this.scopes.Remove(scope))
            return;

        foreach (var binding in scope.Bindings)
            this.Forget(binding);

        this.registry.RemoveScope(scope);
        this.Changed();
    }

    internal void AddBinding(KeyboardShortcutScope scope, KeyboardShortcutBinding binding, bool preventDefault = true)
    {
        scope.Add(binding);
        if (!this.idsByBinding.ContainsKey(binding))
        {
            var id = ++this.nextId;
            this.idsByBinding[binding] = id;
            this.bindingsById[id] = binding;
        }

        this.SetPreventDefault(binding, preventDefault);
        this.Changed();
    }

    internal void SetPreventDefault(KeyboardShortcutBinding binding, bool preventDefault)
    {
        if (preventDefault)
            this.allowDefault.Remove(binding);
        else
            this.allowDefault.Add(binding);
    }

    internal void RemoveBinding(KeyboardShortcutScope scope, KeyboardShortcutBinding binding)
    {
        scope.Remove(binding);
        this.Forget(binding);
        this.Changed();
    }

    void Forget(KeyboardShortcutBinding binding)
    {
        if (this.idsByBinding.Remove(binding, out var id))
            this.bindingsById.Remove(id);

        this.allowDefault.Remove(binding);
    }

    /// <summary>Something the browser matches against changed. Coalesced into one push.</summary>
    internal void Changed()
    {
        if (this.pushScheduled || this.module == null)
            return;

        this.pushScheduled = true;
        _ = this.PushSoonAsync();
    }

    async Task PushSoonAsync()
    {
        // Yield so every registration made in the same render lands in the same push.
        await Task.Yield();
        this.pushScheduled = false;
        await this.PushAsync().ConfigureAwait(false);
    }

    async Task PushAsync()
    {
        if (this.module is not { } module)
            return;

        try
        {
            await module.InvokeVoidAsync("update", this.instanceId, this.BuildSpec()).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or ObjectDisposedException or TaskCanceledException)
        {
        }
    }

    /// <summary>The whole matching table as JSON — internal so the tests can check what the browser is told.</summary>
    internal string BuildSpec()
    {
        var spec = new ShortcutSpec
        {
            Platform = this.Platform switch
            {
                KeyboardPlatform.Apple => "apple",
                KeyboardPlatform.Windows => "windows",
                KeyboardPlatform.Linux => "linux",
                KeyboardPlatform.Android => "android",
                _ => "other"
            },
            ChordTimeoutMs = (int)Math.Min(int.MaxValue, this.registry.ChordTimeout.TotalMilliseconds)
        };

        foreach (var (scope, state) in this.scopes)
        {
            spec.Scopes.Add(new ScopeSpec
            {
                Id = state.Id,
                Parent = scope.Parent != null && this.scopes.TryGetValue(scope.Parent, out var parent) ? parent.Id : null,
                Order = state.Order,
                Modal = state.IsModal,
                Element = state.IsElement,
                Active = scope.IsActive
            });

            foreach (var binding in scope.Bindings)
            {
                if (!this.idsByBinding.TryGetValue(binding, out var id))
                    continue;

                spec.Bindings.Add(new BindingSpec
                {
                    Id = id,
                    Scope = state.Id,
                    Enabled = binding.IsEnabled,
                    AllowRepeat = binding.AllowRepeat,
                    TextInput = binding.TextInput switch
                    {
                        TextInputBehavior.Always => "always",
                        TextInputBehavior.Never => "never",
                        _ => "auto"
                    },
                    PreventDefault = !this.allowDefault.Contains(binding),
                    Chords = binding.Gesture.Chords.Select(c => new ChordSpec
                    {
                        Mods = (int)c.Modifiers,
                        Key = c.Key,
                        Kind = c.Kind switch
                        {
                            KeyKind.Letter => "letter",
                            KeyKind.Digit => "digit",
                            KeyKind.Character => "char",
                            _ => "code"
                        }
                    }).ToList()
                });
            }
        }

        return JsonSerializer.Serialize(spec, KeyboardShortcutJsonContext.Default.ShortcutSpec);
    }

    // -------------------------------------------------------------------------------------
    // Called from keyboard-shortcuts.js

    [JSInvokable]
    public void OnShortcut(int id, bool released, bool repeat, string? code, string? key, int modifiers)
    {
        if (!this.bindingsById.TryGetValue(id, out var binding))
            return;

        var stroke = new KeyStroke
        {
            Code = code,
            Character = key,
            Modifiers = (KeyModifiers)modifiers,
            IsRepeat = repeat,
            IsKeyUp = released
        };

        var args = new KeyboardShortcutEventArgs(binding, stroke);
        try
        {
            if (released)
            {
                binding.Released?.Invoke(args);
                return;
            }

            // The browser has already decided (and prevented the default); all CanExecute can do
            // now is decline to run the handler.
            if (binding.CanExecute?.Invoke() == false)
                return;

            binding.Pressed?.Invoke(args);
            this.ShortcutInvoked?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            this.logger?.LogError(ex, "The keyboard shortcut {Gesture} threw.", binding.Gesture);
        }
    }

    [JSInvokable]
    public void OnChordState(int id, int index)
    {
        this.pendingBinding = id > 0 && this.bindingsById.TryGetValue(id, out var binding) ? binding : null;
        this.pendingIndex = this.pendingBinding == null ? 0 : index;
        this.ChordStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        this.generation++;
        var current = this.module;
        this.module = null;

        if (current != null)
        {
            try
            {
                await current.InvokeVoidAsync("detach", this.instanceId).ConfigureAwait(false);
                await current.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is JSDisconnectedException or ObjectDisposedException or TaskCanceledException)
            {
            }
        }

        this.self?.Dispose();
        this.self = null;
    }

    sealed class ScopeState(int id, long order, bool isModal, bool isElement)
    {
        public int Id { get; } = id;
        public long Order { get; set; } = order;
        public bool IsModal { get; set; } = isModal;
        public bool IsElement { get; } = isElement;
        public ElementReference? Element { get; set; }
    }

    sealed class Registration(Action dispose) : IDisposable
    {
        Action? dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref this.dispose, null)?.Invoke();
    }
}

// -----------------------------------------------------------------------------------------
// The table keyboard-shortcuts.js matches against. Serialized by source generation, so nothing
// here depends on reflection surviving the trimmer.

sealed class ShortcutSpec
{
    public string Platform { get; set; } = "other";
    public int ChordTimeoutMs { get; set; }
    public List<ScopeSpec> Scopes { get; set; } = new();
    public List<BindingSpec> Bindings { get; set; } = new();
}

sealed class ScopeSpec
{
    public int Id { get; set; }
    public int? Parent { get; set; }
    public long Order { get; set; }
    public bool Modal { get; set; }
    public bool Element { get; set; }
    public bool Active { get; set; }
}

sealed class BindingSpec
{
    public int Id { get; set; }
    public int Scope { get; set; }
    public bool Enabled { get; set; }
    public bool AllowRepeat { get; set; }
    public string TextInput { get; set; } = "auto";
    public bool PreventDefault { get; set; } = true;
    public List<ChordSpec> Chords { get; set; } = new();
}

sealed class ChordSpec
{
    public int Mods { get; set; }
    public string Key { get; set; } = String.Empty;
    public string Kind { get; set; } = "code";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ShortcutSpec))]
partial class KeyboardShortcutJsonContext : JsonSerializerContext;
