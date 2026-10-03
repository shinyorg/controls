namespace Shiny.Controls.Keyboard.Tests;

public class KeyboardShortcutEngineTests
{
    sealed class ManualTime : TimeProvider
    {
        long now;
        public void Advance(TimeSpan by) => this.now += (long)(by.TotalSeconds * this.TimestampFrequency);
        public override long GetTimestamp() => this.now;
        public override long TimestampFrequency => 1_000_000;
    }

    static KeyStroke Down(string code, KeyModifiers mods = KeyModifiers.None, string? ch = null, bool repeat = false, bool text = false, bool altGr = false)
        => new()
        {
            Code = code,
            Character = ch ?? NativeKeyCodes.UsCharacter(code, mods.HasFlag(KeyModifiers.Shift)),
            Modifiers = mods,
            IsRepeat = repeat,
            IsTextInputFocused = text,
            IsAltGraph = altGr
        };

    static KeyStroke Up(string code, KeyModifiers mods = KeyModifiers.None) => Down(code, mods) with { IsKeyUp = true };

    [Fact]
    public void Fires_and_swallows_a_matching_chord()
    {
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Windows);
        var hits = 0;
        engine.GlobalScope.Add("Ctrl+S", _ => hits++);

        engine.Process(Down("KeyS", KeyModifiers.Control)).ShouldBeTrue();
        hits.ShouldBe(1);
    }

    [Fact]
    public void Modifiers_match_exactly()
    {
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Windows);
        var hits = 0;
        engine.GlobalScope.Add("Ctrl+B", _ => hits++);

        engine.Process(Down("KeyB", KeyModifiers.Control | KeyModifiers.Shift)).ShouldBeFalse();
        engine.Process(Down("KeyB")).ShouldBeFalse();
        hits.ShouldBe(0);
    }

    [Fact]
    public void Primary_is_command_on_apple_and_control_elsewhere()
    {
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Apple);
        var hits = 0;
        engine.GlobalScope.Add("Primary+S", _ => hits++);

        engine.Process(Down("KeyS", KeyModifiers.Control)).ShouldBeFalse();
        engine.Process(Down("KeyS", KeyModifiers.Meta)).ShouldBeTrue();

        engine.Platform = KeyboardPlatform.Windows;
        engine.Process(Down("KeyS", KeyModifiers.Control)).ShouldBeTrue();
        hits.ShouldBe(2);
    }

    [Fact]
    public void Letters_follow_the_layout_not_the_position()
    {
        // AZERTY: the key printed Z sits where QWERTY has W.
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Windows);
        var hits = 0;
        engine.GlobalScope.Add("Ctrl+Z", _ => hits++);

        engine.Process(Down("KeyW", KeyModifiers.Control, ch: "z")).ShouldBeTrue();
        engine.Process(Down("KeyZ", KeyModifiers.Control, ch: "w")).ShouldBeFalse();
        hits.ShouldBe(1);
    }

    [Fact]
    public void Non_latin_layouts_fall_back_to_the_physical_letter()
    {
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Windows);
        var hits = 0;
        engine.GlobalScope.Add("Ctrl+A", _ => hits++);

        // Russian ЙЦУКЕН: the A position types "ф".
        engine.Process(Down("KeyA", KeyModifiers.Control, ch: "ф")).ShouldBeTrue();
        hits.ShouldBe(1);
    }

    [Fact]
    public void Digits_match_by_position_so_azerty_can_reach_them()
    {
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Windows);
        var hits = 0;
        engine.GlobalScope.Add("Ctrl+1", _ => hits++);

        // AZERTY's top-row 1 types "&" unshifted.
        engine.Process(Down("Digit1", KeyModifiers.Control, ch: "&")).ShouldBeTrue();
        hits.ShouldBe(1);
    }

    [Fact]
    public void Character_gestures_ignore_shift()
    {
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Windows);
        var hits = 0;
        engine.GlobalScope.Add("?", _ => hits++);

        engine.Process(Down("Slash", KeyModifiers.Shift)).ShouldBeTrue();
        engine.Process(Down("Slash")).ShouldBeFalse(); // "/" is not "?"
        engine.Process(Down("Slash", KeyModifiers.Shift | KeyModifiers.Control)).ShouldBeFalse();
        hits.ShouldBe(1);
    }

    [Fact]
    public void Enter_matches_the_keypad_enter_too()
    {
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Windows);
        var hits = 0;
        engine.GlobalScope.Add("Ctrl+Enter", _ => hits++);

        engine.Process(Down("NumpadEnter", KeyModifiers.Control)).ShouldBeTrue();
        hits.ShouldBe(1);
    }

    [Fact]
    public void Plain_keys_stay_with_a_focused_text_field_by_default()
    {
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Windows);
        var hits = new List<string>();
        engine.GlobalScope.Add("J", _ => hits.Add("J"));
        engine.GlobalScope.Add("?", _ => hits.Add("?"));
        engine.GlobalScope.Add("Ctrl+B", _ => hits.Add("Ctrl+B"));
        engine.GlobalScope.Add("Esc", _ => hits.Add("Esc"));
        engine.GlobalScope.Add("F2", _ => hits.Add("F2"));

        engine.Process(Down("KeyJ", text: true)).ShouldBeFalse();
        engine.Process(Down("Slash", KeyModifiers.Shift, text: true)).ShouldBeFalse();
        engine.Process(Down("KeyB", KeyModifiers.Control, text: true)).ShouldBeTrue();
        engine.Process(Down("Escape", text: true)).ShouldBeTrue();
        engine.Process(Down("F2", text: true)).ShouldBeTrue();

        hits.ShouldBe(["Ctrl+B", "Esc", "F2"]);
    }

    [Fact]
    public void TextInput_always_and_never_override_the_default()
    {
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Windows);
        var hits = new List<string>();
        engine.GlobalScope.Add("J", _ => hits.Add("J")).TextInput = TextInputBehavior.Always;
        engine.GlobalScope.Add("Ctrl+B", _ => hits.Add("Ctrl+B")).TextInput = TextInputBehavior.Never;

        engine.Process(Down("KeyJ", text: true)).ShouldBeTrue();
        engine.Process(Down("KeyB", KeyModifiers.Control, text: true)).ShouldBeFalse();
        hits.ShouldBe(["J"]);
    }

    [Fact]
    public void AltGr_never_presses_a_ctrl_alt_shortcut_while_typing()
    {
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Windows);
        var hits = 0;
        engine.GlobalScope.Add("Ctrl+Alt+E", _ => hits++);

        engine.Process(Down("KeyE", KeyModifiers.Control | KeyModifiers.Alt, ch: "€", text: true, altGr: true)).ShouldBeFalse();
        engine.Process(Down("KeyE", KeyModifiers.Control | KeyModifiers.Alt, ch: "e")).ShouldBeTrue();
        hits.ShouldBe(1);
    }

    [Fact]
    public void Disabled_or_cannot_execute_lets_the_key_through()
    {
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Windows);
        var binding = engine.GlobalScope.Add("Ctrl+S", _ => { });
        binding.IsEnabled = false;
        engine.Process(Down("KeyS", KeyModifiers.Control)).ShouldBeFalse();

        binding.IsEnabled = true;
        binding.CanExecute = () => false;
        engine.Process(Down("KeyS", KeyModifiers.Control)).ShouldBeFalse();
    }

    [Fact]
    public void A_handler_can_decline_the_key()
    {
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Windows);
        engine.GlobalScope.Add("Ctrl+S", e => e.Handled = false);

        engine.Process(Down("KeyS", KeyModifiers.Control)).ShouldBeFalse();
        // Declined, so it is not held either — no release for a key that was never claimed.
        engine.Process(Up("KeyS", KeyModifiers.Control)).ShouldBeFalse();
    }

    [Fact]
    public void Repeats_are_swallowed_but_only_fire_when_allowed()
    {
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Windows);
        var save = 0;
        var nudge = 0;
        engine.GlobalScope.Add("Ctrl+S", _ => save++);
        engine.GlobalScope.Add("Right", _ => nudge++).AllowRepeat = true;

        engine.Process(Down("KeyS", KeyModifiers.Control)).ShouldBeTrue();
        engine.Process(Down("KeyS", KeyModifiers.Control, repeat: true)).ShouldBeTrue();
        engine.Process(Down("KeyS", KeyModifiers.Control, repeat: true)).ShouldBeTrue();
        save.ShouldBe(1);

        engine.Process(Down("ArrowRight")).ShouldBeTrue();
        engine.Process(Down("ArrowRight", repeat: true)).ShouldBeTrue();
        engine.Process(Down("ArrowRight", repeat: true)).ShouldBeTrue();
        nudge.ShouldBe(3);
    }

    [Fact]
    public void Released_pairs_with_the_key_even_after_the_modifier_lets_go_first()
    {
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Windows);
        var log = new List<string>();
        var binding = engine.GlobalScope.Add("Ctrl+Space", _ => log.Add("down"));
        binding.Released = _ => log.Add("up");

        engine.Process(Down("Space", KeyModifiers.Control)).ShouldBeTrue();
        engine.Process(Up("ControlLeft")).ShouldBeFalse();
        engine.Process(Up("Space")).ShouldBeTrue();

        log.ShouldBe(["down", "up"]);
    }

    [Fact]
    public void Losing_focus_releases_what_is_held()
    {
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Windows);
        var released = 0;
        var window = new object();
        var binding = engine.GlobalScope.Add("Space", _ => { });
        binding.Released = _ => released++;

        engine.Process(Down("Space"), window).ShouldBeTrue();
        engine.ReleaseAll(window);
        released.ShouldBe(1);

        // The real key-up arrives later, to nobody.
        engine.Process(Up("Space"), window).ShouldBeFalse();
        released.ShouldBe(1);
    }

    [Fact]
    public void Sequences_wait_for_the_next_chord()
    {
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Windows);
        var hits = new List<string>();
        var changes = 0;
        engine.ChordStateChanged += (_, _) => changes++;
        engine.GlobalScope.Add("Ctrl+K, Ctrl+C", _ => hits.Add("comment"));
        engine.GlobalScope.Add("Ctrl+K, Ctrl+U", _ => hits.Add("uncomment"));

        engine.Process(Down("KeyK", KeyModifiers.Control)).ShouldBeTrue();
        engine.IsChordPending.ShouldBeTrue();
        engine.PendingChords.Single().Key.ShouldBe("K");

        // Holding Ctrl between the two is fine — the modifier's own key events change nothing.
        engine.Process(Down("ControlLeft", KeyModifiers.Control)).ShouldBeFalse();
        engine.IsChordPending.ShouldBeTrue();

        engine.Process(Down("KeyU", KeyModifiers.Control)).ShouldBeTrue();
        engine.IsChordPending.ShouldBeFalse();
        hits.ShouldBe(["uncomment"]);
        changes.ShouldBe(2);
    }

    [Fact]
    public void A_wrong_second_key_abandons_the_sequence_and_is_processed_fresh()
    {
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Windows);
        var hits = new List<string>();
        engine.GlobalScope.Add("Ctrl+K, Ctrl+C", _ => hits.Add("comment"));
        engine.GlobalScope.Add("Ctrl+S", _ => hits.Add("save"));

        engine.Process(Down("KeyK", KeyModifiers.Control)).ShouldBeTrue();
        engine.Process(Down("KeyS", KeyModifiers.Control)).ShouldBeTrue();
        engine.IsChordPending.ShouldBeFalse();
        hits.ShouldBe(["save"]);
    }

    [Fact]
    public void Sequences_time_out()
    {
        var time = new ManualTime();
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Windows, time) { ChordTimeout = TimeSpan.FromSeconds(1) };
        var hits = 0;
        engine.GlobalScope.Add("G, G", _ => hits++);

        engine.Process(Down("KeyG")).ShouldBeTrue();
        time.Advance(TimeSpan.FromSeconds(2));
        engine.Process(Down("KeyG")).ShouldBeTrue(); // starts a new sequence instead of finishing the old one
        hits.ShouldBe(0);

        engine.Process(Down("KeyG")).ShouldBeTrue();
        hits.ShouldBe(1);
    }

    [Fact]
    public void Inner_scopes_beat_their_parents_regardless_of_activation_order()
    {
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Windows);
        var hits = new List<string>();

        var page = engine.CreateScope("page");
        var view = engine.CreateScope("view", parent: page);
        page.Add("Ctrl+F", _ => hits.Add("page"));
        view.Add("Ctrl+F", _ => hits.Add("view"));

        // Children load before their page appears.
        view.Activate();
        page.Activate();

        engine.Process(Down("KeyF", KeyModifiers.Control)).ShouldBeTrue();
        hits.ShouldBe(["view"]);
    }

    [Fact]
    public void A_child_of_an_inactive_scope_is_inactive()
    {
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Windows);
        var page = engine.CreateScope("page");
        var view = engine.CreateScope("view", parent: page);
        var hits = 0;
        view.Add("Ctrl+F", _ => hits++);
        view.Activate();

        engine.Process(Down("KeyF", KeyModifiers.Control)).ShouldBeFalse();
        page.Activate();
        engine.Process(Down("KeyF", KeyModifiers.Control)).ShouldBeTrue();
        hits.ShouldBe(1);
    }

    [Fact]
    public void The_latest_tree_wins_between_unrelated_scopes()
    {
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Windows);
        var hits = new List<string>();
        var first = engine.CreateScope("first");
        var second = engine.CreateScope("second");
        first.Add("Ctrl+N", _ => hits.Add("first"));
        second.Add("Ctrl+N", _ => hits.Add("second"));

        first.Activate();
        second.Activate();
        engine.Process(Down("KeyN", KeyModifiers.Control));

        second.Deactivate();
        engine.Process(Down("KeyN", KeyModifiers.Control));

        hits.ShouldBe(["second", "first"]);
    }

    [Fact]
    public void Modal_scopes_block_everything_beneath_including_global()
    {
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Windows);
        var hits = new List<string>();
        engine.GlobalScope.Add("Ctrl+N", _ => hits.Add("global"));
        var page = engine.CreateScope("page");
        page.Add("Ctrl+S", _ => hits.Add("page"));
        page.Activate();

        var dialog = engine.CreateScope("dialog", parent: page, isModal: true);
        dialog.Add("Esc", _ => hits.Add("dialog"));
        dialog.Activate();

        engine.Process(Down("KeyN", KeyModifiers.Control)).ShouldBeFalse();
        engine.Process(Down("KeyS", KeyModifiers.Control)).ShouldBeFalse();
        engine.Process(Down("Escape")).ShouldBeTrue();

        dialog.Deactivate();
        engine.Process(Down("KeyS", KeyModifiers.Control)).ShouldBeTrue();
        hits.ShouldBe(["dialog", "page"]);
    }

    [Fact]
    public void Scopes_only_hear_their_own_window()
    {
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Windows);
        var a = new object();
        var b = new object();
        var hits = 0;
        var scope = engine.CreateScope("a", window: a);
        scope.Add("Ctrl+S", _ => hits++);
        scope.Activate();

        engine.Process(Down("KeyS", KeyModifiers.Control), b).ShouldBeFalse();
        engine.Process(Down("KeyS", KeyModifiers.Control), a).ShouldBeTrue();
        hits.ShouldBe(1);
    }

    [Fact]
    public void A_handler_may_change_registrations()
    {
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Windows);
        KeyboardShortcutBinding? once = null;
        var hits = 0;
        once = engine.GlobalScope.Add("Ctrl+O", _ =>
        {
            hits++;
            engine.GlobalScope.Remove(once!);
        });

        engine.Process(Down("KeyO", KeyModifiers.Control)).ShouldBeTrue();
        engine.Process(Down("KeyO", KeyModifiers.Control)).ShouldBeFalse();
        hits.ShouldBe(1);
    }

    [Fact]
    public void Active_shortcuts_list_in_precedence_order_and_flag_shadowing()
    {
        var engine = new KeyboardShortcutEngine(KeyboardPlatform.Windows);
        engine.GlobalScope.Add("Ctrl+F", _ => { }).Description = "global find";
        var page = engine.CreateScope("page");
        page.Add("Ctrl+F", _ => { }).Description = "page find";
        page.Activate();

        var list = engine.GetActiveShortcuts();
        list.Select(x => x.Binding.Description).ShouldBe(["page find", "global find"]);
        list.Select(x => x.IsShadowed).ShouldBe([false, true]);
    }
}
