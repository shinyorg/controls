using System.Collections.ObjectModel;
using Shiny.Controls.Keyboard;

namespace Shiny.Maui.Controls;

/// <summary>The shortcuts declared on one element. See <see cref="KeyboardShortcuts"/>.</summary>
public class KeyboardShortcutCollection : ObservableCollection<KeyboardShortcut>
{
}

/// <summary>
/// Attaches keyboard shortcuts to a page or any other element.
/// </summary>
/// <example>
/// <code>
/// &lt;ContentPage xmlns:shiny="http://shiny.net/maui/controls"&gt;
///     &lt;shiny:KeyboardShortcuts.Shortcuts&gt;
///         &lt;shiny:KeyboardShortcut Gesture="Primary+S" Command="{Binding SaveCommand}" /&gt;
///         &lt;shiny:KeyboardShortcut Gesture="?" Command="{Binding ShowShortcutsCommand}" /&gt;
///     &lt;/shiny:KeyboardShortcuts.Shortcuts&gt;
/// &lt;/ContentPage&gt;
/// </code>
/// </example>
/// <remarks>
/// <para>When the shortcuts are live:</para>
/// <list type="bullet">
/// <item><description>On a <see cref="Page"/> — while the page is showing: from <c>Appearing</c> to <c>Disappearing</c>. A page pushed on top silences the one beneath it.</description></item>
/// <item><description>On any other element — while it is loaded and visible <em>and</em> the page it sits on is showing.</description></item>
/// </list>
/// <para>
/// When two live elements bind the same keys, the inner one wins (a view over its page), and
/// between unrelated elements the most recently shown wins. <see cref="IsModalProperty"/> on an
/// element — a dialog's content, say — blocks every shortcut outside it while it is live, the
/// app-wide ones registered through <see cref="IKeyboardShortcutService"/> included.
/// </para>
/// <para>
/// Each shortcut's bindings resolve against the element's <c>BindingContext</c>, so
/// <c>{Binding SaveCommand}</c> reaches the page's view model as it would anywhere else on the page.
/// </para>
/// </remarks>
public static class KeyboardShortcuts
{
    public static readonly BindableProperty ShortcutsProperty = BindableProperty.CreateAttached(
        "Shortcuts",
        typeof(KeyboardShortcutCollection),
        typeof(KeyboardShortcuts),
        null,
        propertyChanged: OnShortcutsChanged,
        defaultValueCreator: CreateDefault
    );

    /// <summary>The element's shortcuts — what XAML's collection syntax adds to.</summary>
    public static KeyboardShortcutCollection GetShortcuts(BindableObject view)
        => (KeyboardShortcutCollection)view.GetValue(ShortcutsProperty);

    public static void SetShortcuts(BindableObject view, KeyboardShortcutCollection? value)
        => view.SetValue(ShortcutsProperty, value);

    public static readonly BindableProperty IsModalProperty = BindableProperty.CreateAttached(
        "IsModal",
        typeof(bool),
        typeof(KeyboardShortcuts),
        false,
        propertyChanged: (b, _, n) =>
        {
            if (b is VisualElement element)
                KeyboardShortcutHost.Ensure(element).SetModal((bool)n);
        }
    );

    /// <summary>
    /// While this element's shortcuts are live, nothing outside it can fire — the page beneath a
    /// dialog, the app-wide shortcuts. Setting it creates the scope even with no shortcuts, so a
    /// dialog can block the page's keys without declaring any of its own.
    /// </summary>
    public static bool GetIsModal(BindableObject view) => (bool)view.GetValue(IsModalProperty);

    public static void SetIsModal(BindableObject view, bool value) => view.SetValue(IsModalProperty, value);

    /// <remarks>
    /// The default has to come from here rather than from <see cref="GetShortcuts"/> creating one on
    /// first read: the XAML source generator adds to an attached collection with
    /// <c>GetValue(ShortcutsProperty).Add(...)</c> and never calls the getter, so a lazily created
    /// collection is a null reference at page construction. A created default never raises
    /// <c>propertyChanged</c> either, so the wiring that callback would have done happens here.
    /// </remarks>
    static object CreateDefault(BindableObject bindable)
    {
        var collection = new KeyboardShortcutCollection();
        if (bindable is VisualElement element)
            KeyboardShortcutHost.Ensure(element).SetCollection(collection);

        return collection;
    }

    static void OnShortcutsChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not VisualElement element)
        {
            if (newValue != null)
                System.Diagnostics.Debug.WriteLine($"[Shiny.KeyboardShortcuts] Shortcuts on a {bindable.GetType().Name} are ignored — attach them to a page or a visual element.");
            return;
        }

        KeyboardShortcutHost.Ensure(element).SetCollection(newValue as KeyboardShortcutCollection);
    }
}
