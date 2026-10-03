using Microsoft.Maui.Controls;
using Shiny.Controls.Keyboard;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The MAUI half of keyboard shortcuts: XAML-facing properties, binding context hand-off, and the
/// scope rules the host applies. Strokes are fed straight into the manager, standing in for a native
/// key source. The engine is static and shared, so every test uses a key no other test uses.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class KeyboardShortcutTests
{
    public KeyboardShortcutTests()
    {
        // Unconditional: a style added by an earlier test must not leak in through Application.Current.
        _ = new Application();
        KeyboardShortcutManager.Engine.Platform = KeyboardPlatform.Windows;
    }

    sealed class ViewModel
    {
        public int Saves;
        public Command Save => new(() => this.Saves++);
    }

    static KeyStroke Ctrl(string code) => new() { Code = code, Modifiers = KeyModifiers.Control };

    [Fact]
    public void Shortcuts_inherit_the_element_binding_context()
    {
        var vm = new ViewModel();
        var page = new ContentPage { BindingContext = vm };
        var shortcut = new KeyboardShortcut { Gesture = "Ctrl+F1" };
        shortcut.SetBinding(KeyboardShortcut.CommandProperty, nameof(ViewModel.Save));

        KeyboardShortcuts.GetShortcuts(page).Add(shortcut);

        shortcut.BindingContext.ShouldBe(vm);
        shortcut.Command.ShouldNotBeNull();

        var other = new ViewModel();
        page.BindingContext = other;
        shortcut.BindingContext.ShouldBe(other);
    }

    [Fact]
    public void The_xaml_source_generator_path_reaches_a_live_collection()
    {
        // SourceGen emits GetValue(ShortcutsProperty).Add(...) and never calls GetShortcuts. A lazily
        // created collection was null there, and the page threw in InitializeComponent.
        var hits = 0;
        var page = new ContentPage();
        var shortcut = new KeyboardShortcut { Gesture = "Ctrl+F11" };
        shortcut.Pressed += (_, _) => hits++;

        var collection = (KeyboardShortcutCollection)page.GetValue(KeyboardShortcuts.ShortcutsProperty);
        collection.ShouldNotBeNull();
        collection.Add(shortcut);

        var window = new Window(page);
        KeyboardShortcutManager.Process(Ctrl("F11"), window).ShouldBeTrue();
        hits.ShouldBe(1);
    }

    [Fact]
    public void Gesture_and_key_modifiers_both_describe_the_keys()
    {
        var byGesture = new KeyboardShortcut { Gesture = "Primary+Shift+S" };
        byGesture.DisplayText.ShouldBe("Ctrl+Shift+S");

        var byKey = new KeyboardShortcut { Key = "S", Modifiers = KeyModifiers.Primary | KeyModifiers.Shift };
        byKey.ParsedGesture.ShouldBe(byGesture.ParsedGesture);

        // The gesture wins when both are set.
        var both = new KeyboardShortcut { Key = "Q", Gesture = "F7" };
        both.ParsedGesture!.ToString().ShouldBe("F7");
    }

    [Fact]
    public void An_invalid_gesture_disables_the_shortcut()
    {
        var shortcut = new KeyboardShortcut { Gesture = "Hyper+Nope" };
        shortcut.ParsedGesture.ShouldBeNull();
        shortcut.DisplayText.ShouldBe(String.Empty);
        shortcut.Binding.IsEnabled.ShouldBeFalse();

        shortcut.Gesture = "Ctrl+F2";
        shortcut.Binding.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void A_page_in_a_window_fires_its_command()
    {
        var vm = new ViewModel();
        var page = new ContentPage { BindingContext = vm };
        var shortcut = new KeyboardShortcut { Gesture = "Ctrl+F3" };
        shortcut.SetBinding(KeyboardShortcut.CommandProperty, nameof(ViewModel.Save));
        KeyboardShortcuts.GetShortcuts(page).Add(shortcut);

        var window = new Window(page);

        KeyboardShortcutManager.Process(Ctrl("F3"), window).ShouldBeTrue();
        vm.Saves.ShouldBe(1);
    }

    [Fact]
    public void Nothing_fires_outside_a_window()
    {
        var hits = 0;
        var page = new ContentPage();
        var shortcut = new KeyboardShortcut { Gesture = "Ctrl+F4" };
        shortcut.Pressed += (_, _) => hits++;
        KeyboardShortcuts.GetShortcuts(page).Add(shortcut);

        KeyboardShortcutManager.Process(Ctrl("F4"), null).ShouldBeFalse();
        hits.ShouldBe(0);
    }

    [Fact]
    public void A_view_outranks_its_page_and_hides_with_IsVisible()
    {
        var log = new List<string>();
        var view = new ContentView();
        var page = new ContentPage { Content = view };

        var pageShortcut = new KeyboardShortcut { Gesture = "Ctrl+F5" };
        pageShortcut.Pressed += (_, _) => log.Add("page");
        KeyboardShortcuts.GetShortcuts(page).Add(pageShortcut);

        var viewShortcut = new KeyboardShortcut { Gesture = "Ctrl+F5" };
        viewShortcut.Pressed += (_, _) => log.Add("view");
        KeyboardShortcuts.GetShortcuts(view).Add(viewShortcut);

        var window = new Window(page);

        KeyboardShortcutManager.Process(Ctrl("F5"), window);
        view.IsVisible = false;
        KeyboardShortcutManager.Process(Ctrl("F5"), window);

        log.ShouldBe(["view", "page"]);
    }

    [Fact]
    public void A_modal_element_blocks_the_page_behind_it()
    {
        var hits = 0;
        var dialog = new ContentView { IsVisible = false };
        var page = new ContentPage { Content = new Grid { Children = { dialog } } };

        var save = new KeyboardShortcut { Gesture = "Ctrl+F6" };
        save.Pressed += (_, _) => hits++;
        KeyboardShortcuts.GetShortcuts(page).Add(save);
        KeyboardShortcuts.SetIsModal(dialog, true);

        var window = new Window(page);

        KeyboardShortcutManager.Process(Ctrl("F6"), window).ShouldBeTrue();
        dialog.IsVisible = true;
        KeyboardShortcutManager.Process(Ctrl("F6"), window).ShouldBeFalse();
        dialog.IsVisible = false;
        KeyboardShortcutManager.Process(Ctrl("F6"), window).ShouldBeTrue();

        hits.ShouldBe(2);
    }

    [Fact]
    public void A_command_that_cannot_execute_lets_the_key_through()
    {
        var page = new ContentPage();
        var enabled = false;
        var shortcut = new KeyboardShortcut
        {
            Gesture = "Ctrl+F7",
            Command = new Command(() => { }, () => enabled)
        };
        KeyboardShortcuts.GetShortcuts(page).Add(shortcut);
        var window = new Window(page);

        KeyboardShortcutManager.Process(Ctrl("F7"), window).ShouldBeFalse();
        enabled = true;
        KeyboardShortcutManager.Process(Ctrl("F7"), window).ShouldBeTrue();
    }

    [Fact]
    public void Removing_a_shortcut_from_the_collection_unregisters_it()
    {
        var page = new ContentPage();
        var shortcut = new KeyboardShortcut { Gesture = "Ctrl+F8" };
        KeyboardShortcuts.GetShortcuts(page).Add(shortcut);
        var window = new Window(page);

        KeyboardShortcutManager.Process(Ctrl("F8"), window).ShouldBeTrue();
        KeyboardShortcuts.GetShortcuts(page).Remove(shortcut);
        KeyboardShortcutManager.Process(Ctrl("F8"), window).ShouldBeFalse();
    }

    [Fact]
    public void Released_command_runs_on_key_up()
    {
        var log = new List<string>();
        var page = new ContentPage();
        var shortcut = new KeyboardShortcut
        {
            Gesture = "F9",
            Command = new Command(() => log.Add("down")),
            ReleasedCommand = new Command(() => log.Add("up"))
        };
        KeyboardShortcuts.GetShortcuts(page).Add(shortcut);
        var window = new Window(page);

        KeyboardShortcutManager.Process(new KeyStroke { Code = "F9" }, window);
        KeyboardShortcutManager.Process(new KeyStroke { Code = "F9", IsKeyUp = true }, window);

        log.ShouldBe(["down", "up"]);
    }

    [Fact]
    public void Service_registrations_are_app_wide_and_disposable()
    {
        var service = new KeyboardShortcutService();
        var hits = 0;
        var registration = service.Register("Ctrl+F10", _ => hits++, b => b.Description = "test");

        service.GetActiveShortcuts().ShouldContain(x => x.Binding.Description == "test");
        KeyboardShortcutManager.Process(Ctrl("F10"), null).ShouldBeTrue();

        registration.Dispose();
        KeyboardShortcutManager.Process(Ctrl("F10"), null).ShouldBeFalse();
        hits.ShouldBe(1);
    }

    [Fact]
    public void Service_formats_for_the_platform()
    {
        var service = new KeyboardShortcutService();
        KeyboardShortcutManager.Engine.Platform = KeyboardPlatform.Apple;
        try
        {
            service.Format("Primary+Shift+P").ShouldBe("⇧⌘P");
        }
        finally
        {
            KeyboardShortcutManager.Engine.Platform = KeyboardPlatform.Windows;
        }
    }
}
