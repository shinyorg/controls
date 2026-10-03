using System.Text.Json;
using Microsoft.JSInterop;
using Shiny.Controls.Keyboard;
using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// The .NET half of Blazor keyboard shortcuts: what the browser is told to match, and what happens
/// when it reports a match. The matching itself runs in keyboard-shortcuts.js.
/// </summary>
public class KeyboardShortcutServiceTests
{
    sealed class NoJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new InvalidOperationException("JS is not available in this test.");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => throw new InvalidOperationException("JS is not available in this test.");
    }

    static KeyboardShortcutService Create() => new(new NoJs());

    static JsonElement Spec(KeyboardShortcutService service)
        => JsonDocument.Parse(service.BuildSpec()).RootElement;

    [Fact]
    public void Registrations_reach_the_spec_with_their_matching_rules()
    {
        var service = Create();
        service.Register("Primary+Shift+P", _ => { }, b =>
        {
            b.AllowRepeat = true;
            b.TextInput = TextInputBehavior.Never;
        });

        var spec = Spec(service);
        var binding = spec.GetProperty("bindings").EnumerateArray().Single();

        binding.GetProperty("scope").GetInt32().ShouldBe(0);
        binding.GetProperty("allowRepeat").GetBoolean().ShouldBeTrue();
        binding.GetProperty("textInput").GetString().ShouldBe("never");
        binding.GetProperty("preventDefault").GetBoolean().ShouldBeTrue();

        var chord = binding.GetProperty("chords").EnumerateArray().Single();
        chord.GetProperty("mods").GetInt32().ShouldBe((int)(KeyModifiers.Primary | KeyModifiers.Shift));
        chord.GetProperty("key").GetString().ShouldBe("P");
        chord.GetProperty("kind").GetString().ShouldBe("letter");
    }

    [Fact]
    public void Sequences_and_character_keys_keep_their_kind()
    {
        var service = Create();
        service.Register("Ctrl+K, Ctrl+Slash", _ => { });
        service.Register("?", _ => { });

        var bindings = Spec(service).GetProperty("bindings").EnumerateArray().ToList();
        bindings[0].GetProperty("chords").EnumerateArray().Select(c => c.GetProperty("kind").GetString()).ShouldBe(["letter", "code"]);
        bindings[1].GetProperty("chords")[0].GetProperty("kind").GetString().ShouldBe("char");
    }

    [Fact]
    public void Disposing_a_registration_removes_it()
    {
        var service = Create();
        var registration = service.Register("Ctrl+S", _ => { });
        registration.Dispose();

        Spec(service).GetProperty("bindings").GetArrayLength().ShouldBe(0);
        service.GetActiveShortcuts().ShouldBeEmpty();
    }

    [Fact]
    public void Scopes_carry_parent_modality_and_activation_order()
    {
        var service = Create();
        var page = service.CreateScope("page", null, isModal: false, isElement: false);
        var dialog = service.CreateScope("dialog", page, isModal: true, isElement: true);
        service.UpdateScope(page, isModal: false, isActive: true);
        service.UpdateScope(dialog, isModal: true, isActive: true);

        var scopes = Spec(service).GetProperty("scopes").EnumerateArray().ToList();
        var pageSpec = scopes.Single(s => s.GetProperty("id").GetInt32() != 0 && s.GetProperty("parent").ValueKind == JsonValueKind.Null);
        var dialogSpec = scopes.Single(s => s.GetProperty("modal").GetBoolean());

        dialogSpec.GetProperty("parent").GetInt32().ShouldBe(pageSpec.GetProperty("id").GetInt32());
        dialogSpec.GetProperty("element").GetBoolean().ShouldBeTrue();
        dialogSpec.GetProperty("order").GetInt64().ShouldBeGreaterThan(pageSpec.GetProperty("order").GetInt64());
    }

    [Fact]
    public void A_reported_match_runs_the_handler()
    {
        var service = Create();
        KeyboardShortcutEventArgs? received = null;
        var invoked = 0;
        service.ShortcutInvoked += (_, _) => invoked++;
        service.Register("Ctrl+S", e => received = e);

        var id = Spec(service).GetProperty("bindings")[0].GetProperty("id").GetInt32();
        service.OnShortcut(id, released: false, repeat: false, code: "KeyS", key: "s", modifiers: (int)KeyModifiers.Control);

        received.ShouldNotBeNull();
        received!.Gesture.ToString().ShouldBe("Ctrl+S");
        received.Stroke.Code.ShouldBe("KeyS");
        invoked.ShouldBe(1);
    }

    [Fact]
    public void Releases_go_to_the_released_handler()
    {
        var service = Create();
        var log = new List<string>();
        service.Register("Space", _ => log.Add("down"), b => b.Released = _ => log.Add("up"));

        var id = Spec(service).GetProperty("bindings")[0].GetProperty("id").GetInt32();
        service.OnShortcut(id, false, false, "Space", " ", 0);
        service.OnShortcut(id, true, false, "Space", " ", 0);

        log.ShouldBe(["down", "up"]);
    }

    [Fact]
    public void Can_execute_false_skips_the_handler()
    {
        var service = Create();
        var hits = 0;
        service.Register("Ctrl+S", _ => hits++, b => b.CanExecute = () => false);

        var id = Spec(service).GetProperty("bindings")[0].GetProperty("id").GetInt32();
        service.OnShortcut(id, false, false, "KeyS", "s", (int)KeyModifiers.Control);
        hits.ShouldBe(0);
    }

    [Fact]
    public void Chord_state_reports_what_has_been_pressed()
    {
        var service = Create();
        var changes = 0;
        service.ChordStateChanged += (_, _) => changes++;
        service.Register("Ctrl+K, Ctrl+C", _ => { });

        var id = Spec(service).GetProperty("bindings")[0].GetProperty("id").GetInt32();
        service.OnChordState(id, 1);
        service.IsChordPending.ShouldBeTrue();
        service.PendingChords.Single().ToString().ShouldBe("Ctrl+K");

        service.OnChordState(0, 0);
        service.IsChordPending.ShouldBeFalse();
        changes.ShouldBe(2);
    }

    [Fact]
    public void Unknown_ids_are_ignored()
    {
        var service = Create();
        Should.NotThrow(() => service.OnShortcut(9999, false, false, null, null, 0));
    }

    [Fact]
    public void Formats_with_the_reported_platform()
    {
        var service = Create();
        service.Format("Primary+S").ShouldBe("Ctrl+S"); // "other" until the browser reports in
    }
}
