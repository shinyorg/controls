using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// Everything the field decides — committing, de-duplicating, capping, splitting a paste — is a method
/// on the component rather than logic inside an event handler, which is what lets it be driven here
/// without a renderer.
/// </summary>
public class TagEntryTests
{
    /// <summary>Types <paramref name="text"/> the way a keyboard would: one input event per character.</summary>
    static async Task TypeAsync(TagEntry field, string text)
    {
        foreach (var ch in text)
            await field.ProcessInputAsync(field.PendingText + ch, commitTrailing: false);
    }

    /// <summary>
    /// Adopts the <c>Tags</c> parameter the way the first lifecycle pass does. Setting the parameter
    /// through <c>SetParametersAsync</c> is not an option here: that ends in a <c>StateHasChanged</c>,
    /// and a component with no render handle throws on one.
    /// </summary>
    static TagEntry Bound(TagEntry field)
    {
        field.TakeTagsParameter();
        return field;
    }

    /// <summary>Drops a whole string in at once, which is what the input event after a paste looks like.</summary>
    static Task PasteAsync(TagEntry field, string text)
        => field.ProcessInputAsync(field.PendingText + text, commitTrailing: true);


    // ---------------------------------------------------------------------------------------------
    // Committing
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task ADelimiterCommitsWhatIsInFrontOfIt()
    {
        var field = new TagEntry();

        await TypeAsync(field, "design,");

        field.Current.ShouldBe(["design"]);
        field.PendingText.ShouldBe(String.Empty);
    }


    [Fact]
    public async Task EnterCommitsTheTypedText()
    {
        var field = new TagEntry();

        await TypeAsync(field, "engineering");
        field.Current.ShouldBeEmpty();

        await field.CommitPendingAsync();
        field.Current.ShouldBe(["engineering"]);
    }


    [Fact]
    public async Task EveryConfiguredDelimiterCommits()
    {
        var field = new TagEntry { Delimiters = [",", ";"] };

        await TypeAsync(field, "a,b;c,");

        field.Current.ShouldBe(["a", "b", "c"]);
    }


    /// <summary>
    /// An empty delimiter list is how a tag gets to contain a comma, so it must not be read as "use the
    /// default" — the distinction between empty and unset is the whole feature.
    /// </summary>
    [Fact]
    public async Task NoDelimiters_MeansEnterOnly()
    {
        var field = new TagEntry { Delimiters = [] };

        await TypeAsync(field, "Smith, John");
        field.Current.ShouldBeEmpty();

        await field.CommitPendingAsync();
        field.Current.ShouldBe(["Smith, John"]);
    }


    [Fact]
    public async Task APasteCommitsItsTrailingFragmentAndTypingDoesNot()
    {
        var pasted = new TagEntry();
        await PasteAsync(pasted, "a, b, c");
        pasted.Current.ShouldBe(["a", "b", "c"]);
        pasted.PendingText.ShouldBe(String.Empty);

        var typed = new TagEntry();
        await TypeAsync(typed, "a, b, c");
        typed.Current.ShouldBe(["a", "b"]);
        typed.PendingText.ShouldBe("c");
    }


    [Fact]
    public async Task WhitespaceIsTrimmedByDefault()
    {
        var field = new TagEntry();

        await TypeAsync(field, "  spaced  ,");

        field.Current.ShouldBe(["spaced"]);
    }


    // ---------------------------------------------------------------------------------------------
    // Refusals
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task DuplicatesAreDroppedQuietly()
    {
        var field = new TagEntry();

        await TypeAsync(field, "echo,");
        await TypeAsync(field, "ECHO,");

        field.Current.ShouldBe(["echo"]);
    }


    [Fact]
    public async Task AllowDuplicates_LetsThemThrough()
    {
        var field = new TagEntry { AllowDuplicates = true };

        await TypeAsync(field, "echo,");
        await TypeAsync(field, "echo,");

        field.Current.ShouldBe(["echo", "echo"]);
    }


    [Fact]
    public async Task CaseSensitiveDuplicates_TreatsCaseAsADifference()
    {
        var field = new TagEntry { CaseSensitiveDuplicates = true };

        await TypeAsync(field, "echo,");
        await TypeAsync(field, "ECHO,");

        field.Current.ShouldBe(["echo", "ECHO"]);
    }


    /// <summary>
    /// At the cap the typed text has to stay put. Eating it would throw away something the user wrote,
    /// with no sign that it happened.
    /// </summary>
    [Fact]
    public async Task MaxTags_StopsCommittingAndLeavesTheTypedTextAlone()
    {
        var field = new TagEntry { MaxTags = 2 };

        await TypeAsync(field, "one,");
        await TypeAsync(field, "two,");
        await TypeAsync(field, "three");
        await field.CommitPendingAsync();

        field.Current.ShouldBe(["one", "two"]);
        field.PendingText.ShouldBe("three");
    }


    [Fact]
    public async Task TagValidator_CanRefuseAValue()
    {
        var field = new TagEntry { TagValidator = tag => tag.Length >= 3 };

        await TypeAsync(field, "ab,");
        field.Current.ShouldBeEmpty();

        await TypeAsync(field, "abc,");
        field.Current.ShouldBe(["abc"]);
    }


    [Fact]
    public async Task ReadOnly_BlocksAddingAndRemoving()
    {
        var field = Bound(new TagEntry { Tags = ["locked"], ReadOnly = true });

        (await field.AddTagAsync("another")).ShouldBeFalse();
        (await field.RemoveTagAsync("locked")).ShouldBeFalse();

        field.Current.ShouldBe(["locked"]);
    }


    [Fact]
    public async Task Disabled_BlocksAddingAndRemoving()
    {
        var field = Bound(new TagEntry { Tags = ["locked"], Disabled = true });

        (await field.AddTagAsync("another")).ShouldBeFalse();
        (await field.RemoveTagAsync("locked")).ShouldBeFalse();

        field.Current.ShouldBe(["locked"]);
    }


    // ---------------------------------------------------------------------------------------------
    // The list
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task ABoundListIsAdoptedAndReportedBack()
    {
        IReadOnlyList<string> bound = ["alpha"];

        var field = Bound(new TagEntry
        {
            Tags = bound,
            TagsChanged = Microsoft.AspNetCore.Components.EventCallback.Factory
                .Create<IReadOnlyList<string>>(new object(), x => bound = x)
        });

        field.Current.ShouldBe(["alpha"]);

        await field.AddTagAsync("beta");
        bound.ShouldBe(["alpha", "beta"]);
    }


    [Fact]
    public async Task RemovingATag_ReportsItOnce()
    {
        var removed = new List<string>();

        var field = Bound(new TagEntry
        {
            Tags = ["alpha", "beta"],
            TagRemoved = Microsoft.AspNetCore.Components.EventCallback.Factory
                .Create<string>(new object(), removed.Add)
        });

        (await field.RemoveTagAsync("alpha")).ShouldBeTrue();

        removed.ShouldBe(["alpha"]);
        field.Current.ShouldBe(["beta"]);
    }


    [Fact]
    public async Task ClearAsync_EmptiesTheField()
    {
        var field = Bound(new TagEntry { Tags = ["alpha", "beta"] });

        await field.ClearAsync();

        field.Current.ShouldBeEmpty();
    }
}
