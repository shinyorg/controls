using Microsoft.Maui.Controls;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The floating label follows the text, not only focus. Text reaches the inner entry three ways and
/// each has to float the label: typing (no focus event on hosts that never report one, which is the
/// macOS AppKit head in shinyorg/controls#72), a programmatic Text, and a masked Text. Nothing here
/// focuses the entry, so all of it holds on a host where IsFocused never changes.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class TextEntryFloatingLabelTests
{
    static TextEntry Floating()
    {
        TestDispatcherProvider.Install();
        new Application();
        // The float animates the label, and MAUI animations need an IAnimationManager off a handler.
        return new FrozenAnimationHost().Attach(
            new TextEntry { Variant = TextEntryVariant.Floating, Placeholder = "Name" });
    }

    [Fact]
    public async Task Typing_FloatsTheLabel_WithoutFocus()
    {
        var te = Floating();
        te.IsPlaceholderUp.ShouldBeFalse();

        // What the platform handler does on a keystroke: writes the inner entry's Text directly.
        te.InnerEntry.Text = "A";

        te.IsPlaceholderUp.ShouldBeTrue();
        te.Text.ShouldBe("A");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ClearingTypedText_LowersTheLabel_WhenUnfocused()
    {
        var te = Floating();
        te.InnerEntry.Text = "A";

        te.InnerEntry.Text = string.Empty;

        te.IsPlaceholderUp.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ProgrammaticText_FloatsAndLowersTheLabel()
    {
        // This path writes the inner entry under suppression, so the typing handler never runs.
        var te = Floating();

        te.Text = "Allan";
        te.IsPlaceholderUp.ShouldBeTrue();

        te.Text = string.Empty;
        te.IsPlaceholderUp.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task MaskedText_FloatsTheLabel()
    {
        var te = Floating();
        te.Mask = "(###) ###-####";

        te.Text = "5551234567";

        te.IsPlaceholderUp.ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task TextSetBeforeFloatingVariant_StartsFloated()
    {
        TestDispatcherProvider.Install();
        new Application();
        var te = new FrozenAnimationHost().Attach(new TextEntry { Placeholder = "Name", Text = "Allan" });

        te.Variant = TextEntryVariant.Floating;

        te.IsPlaceholderUp.ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ClassicVariant_NeverFloats()
    {
        TestDispatcherProvider.Install();
        new Application();
        var te = new FrozenAnimationHost().Attach(new TextEntry { Placeholder = "Name" });

        te.InnerEntry.Text = "A";

        te.IsPlaceholderUp.ShouldBeFalse();
        await Task.CompletedTask;
    }
}
