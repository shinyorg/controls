using System.Collections.Specialized;
using System.ComponentModel;
using Shiny.Controls.Keyboard;

namespace Shiny.Maui.Controls;

/// <summary>
/// The engine scope behind one element's <see cref="KeyboardShortcuts"/>: keeps its shortcuts in
/// sync with the collection, its bindings fed with the element's <c>BindingContext</c>, and the scope
/// switched on exactly while the element is showing.
/// </summary>
/// <remarks>
/// <para>
/// "Showing" deliberately avoids <c>Loaded</c>. A page underneath a pushed one stays loaded on iOS,
/// and the AppKit and GTK heads do not raise every lifecycle event the mobile ones do. A page is
/// showing between <c>Appearing</c> and <c>Disappearing</c>; any other element while it is in a
/// window, visible, and its page is showing.
/// </para>
/// <para>
/// That last clause is enforced by the engine rather than here: a view's scope takes its page's
/// scope as its parent — creating an empty one for the page if the page declares no shortcuts — and
/// a scope whose parent is inactive is inactive. It also makes the view's shortcuts outrank the
/// page's, which is the precedence anyone would expect.
/// </para>
/// </remarks>
sealed class KeyboardShortcutHost
{
    static readonly BindableProperty HostProperty = BindableProperty.CreateAttached(
        "ShinyKeyboardShortcutHost",
        typeof(KeyboardShortcutHost),
        typeof(KeyboardShortcutHost),
        null
    );

    readonly VisualElement element;
    KeyboardShortcutCollection? collection;
    bool appeared;
    bool sawLifecycle;

    KeyboardShortcutHost(VisualElement element)
    {
        this.element = element;
        this.Scope = KeyboardShortcutManager.Engine.CreateScope(element.GetType().Name);
        this.Scope.IsModal = KeyboardShortcuts.GetIsModal(element);

        element.PropertyChanged += this.OnElementPropertyChanged;
        element.BindingContextChanged += this.OnBindingContextChanged;

        if (element is Page page)
        {
            page.Appearing += this.OnAppearing;
            page.Disappearing += this.OnDisappearing;

            // A host created after the page appeared — a view added at runtime asks for its page's
            // scope — has missed Appearing and has to work out where things stand.
            this.appeared = IsShowing(page);
        }

        this.Evaluate();
    }

    public KeyboardShortcutScope Scope { get; }

    public static KeyboardShortcutHost Ensure(VisualElement element)
    {
        if (element.GetValue(HostProperty) is KeyboardShortcutHost existing)
            return existing;

        var host = new KeyboardShortcutHost(element);
        element.SetValue(HostProperty, host);
        return host;
    }

    internal static KeyboardShortcutHost? Get(BindableObject element) => element.GetValue(HostProperty) as KeyboardShortcutHost;

    public void SetModal(bool modal) => this.Scope.IsModal = modal;

    public void SetCollection(KeyboardShortcutCollection? value)
    {
        if (this.collection != null)
            this.collection.CollectionChanged -= this.OnCollectionChanged;

        this.collection = value;

        if (this.collection != null)
            this.collection.CollectionChanged += this.OnCollectionChanged;

        this.Sync();
    }

    void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => this.Sync();

    void Sync()
    {
        var wanted = this.collection?.ToList() ?? [];
        var current = this.Scope.Bindings;

        foreach (var binding in current)
        {
            if (binding.Tag is not KeyboardShortcut shortcut || !wanted.Contains(shortcut))
                this.Scope.Remove(binding);
        }

        // Re-added in collection order, which is the order the engine tries them in.
        foreach (var shortcut in wanted)
        {
            this.Scope.Remove(shortcut.Binding);
            this.Scope.Add(shortcut.Binding);
            BindableObject.SetInheritedBindingContext(shortcut, this.element.BindingContext);
        }
    }

    void OnBindingContextChanged(object? sender, EventArgs e)
    {
        // Items of an attached collection are not children of anything, so nothing hands them the
        // element's BindingContext — {Binding SaveCommand} would quietly resolve against null.
        if (this.collection == null)
            return;

        foreach (var shortcut in this.collection)
            BindableObject.SetInheritedBindingContext(shortcut, this.element.BindingContext);
    }

    void OnAppearing(object? sender, EventArgs e)
    {
        this.sawLifecycle = true;
        this.appeared = true;
        this.Evaluate();
    }

    void OnDisappearing(object? sender, EventArgs e)
    {
        this.sawLifecycle = true;
        this.appeared = false;
        this.Evaluate();
    }

    void OnElementPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(VisualElement.IsVisible) or nameof(VisualElement.Window) or nameof(Element.Parent)))
            return;

        // Shortcuts are usually declared in XAML, before the page has a window — so the host exists
        // before anything could have appeared. Until the first Appearing/Disappearing arrives, the
        // page's place in its window is the best evidence there is; the lifecycle events take over
        // the moment they start.
        if (this.element is Page page && !this.sawLifecycle && e.PropertyName == nameof(VisualElement.Window))
            this.appeared = IsShowing(page);

        this.Evaluate();
    }

    void Evaluate()
    {
        var window = this.element.Window;
        var active = this.element is Page
            ? this.appeared && window != null
            : window != null && this.element.IsVisible;

        if (!active)
        {
            this.Scope.Deactivate();
            return;
        }

        this.Scope.Window = window;
        this.Scope.Parent = this.FindParentScope();
        this.Scope.Activate();
        KeyboardShortcutManager.EnsureWindow(window);
    }

    KeyboardShortcutScope? FindParentScope()
    {
        for (var parent = this.element.Parent; parent != null; parent = parent.Parent)
        {
            if (Get(parent) is { } host)
                return host.Scope;

            // A view always hangs off its page's scope, so the page's Appearing/Disappearing switch
            // the view too. A page only links to an enclosing page that has shortcuts of its own.
            if (parent is Page page && this.element is not Page)
                return Ensure(page).Scope;
        }

        return null;
    }

    /// <summary>
    /// Best guess at whether <paramref name="page"/> is on screen right now, for a host created after
    /// the page's <c>Appearing</c>. Follows the window's current-page chain from the top modal down
    /// through Shell, navigation, tabbed and flyout pages.
    /// </summary>
    internal static bool IsShowing(Page page)
    {
        if (page.Window is not { } window || window.Page is not { } root)
            return false;

        var top = root.Navigation?.ModalStack is { Count: > 0 } modals ? modals[^1] : root;
        if (OnChain(top, page))
            return true;

        // A modal can sit over a page that is still the root's current page — that one is not showing.
        return false;
    }

    static bool OnChain(Page? current, Page target)
    {
        for (var i = 0; current != null && i < 32; i++)
        {
            if (current == target)
                return true;

            if (current is FlyoutPage { IsPresented: true } presented && OnChain(presented.Flyout, target))
                return true;

            current = current switch
            {
                Shell shell => shell.CurrentPage,
                NavigationPage navigation => navigation.CurrentPage,
                FlyoutPage flyout => flyout.Detail,
                TabbedPage tabbed => tabbed.CurrentPage,
                _ => null
            };
        }

        return false;
    }
}
