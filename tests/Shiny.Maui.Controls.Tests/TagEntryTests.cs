using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// Cover for a field whose whole behaviour is in what happens to the text between keystrokes: when it
/// becomes a tag, when it is refused, and the one platform trick that makes Backspace-on-empty
/// detectable at all.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class TagEntryTests
{
    public TagEntryTests()
    {
        TestDispatcherProvider.Install();
        TestDispatcherProvider.Instance.Timers.Clear();

        _ = new Application();
        Application.Current!.Resources.MergedDictionaries.Add(new Themes.BasicLightTheme());
    }


    /// <summary>
    /// A field with the keyboard in it. The platform normally says so through the inner entry's focus
    /// events, which a headless test cannot raise — and the sentinel that makes Backspace detectable is
    /// only in the box while the field is being typed into.
    /// </summary>
    static TagEntry Focused(TagEntry field)
    {
        field.SetFocused(true);
        return field;
    }

    /// <summary>Types <paramref name="text"/> the way a keyboard would — one character at a time.</summary>
    static void Type(TagEntry field, string text)
    {
        foreach (var ch in text)
            field.Editor.Text = (field.Editor.Text ?? String.Empty) + ch;
    }

    /// <summary>Drops a whole string in at once, which is what a paste looks like to an Entry.</summary>
    static void Paste(TagEntry field, string text)
        => field.Editor.Text = (field.Editor.Text ?? String.Empty) + text;

    static void PressEnter(TagEntry field) => field.CommitPending();


    // ---------------------------------------------------------------------------------------------
    // Committing
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ADelimiterCommitsWhatIsInFrontOfIt()
    {
        var field = new TagEntry();

        Type(field, "design,");

        field.Tags!.ShouldBe(["design"]);
        field.PendingText.ShouldBe(String.Empty);
    }


    [Fact]
    public void EnterCommitsTheTypedText()
    {
        var field = new TagEntry();

        Type(field, "engineering");
        field.Tags!.ShouldBeEmpty();

        PressEnter(field);
        field.Tags!.ShouldBe(["engineering"]);
    }


    [Fact]
    public void EveryConfiguredDelimiterCommits()
    {
        var field = new TagEntry { Delimiters = new ObservableCollection<string> { ",", ";" } };

        Type(field, "a,b;c,");

        field.Tags!.ShouldBe(["a", "b", "c"]);
    }


    /// <summary>
    /// An empty delimiter list is how a tag gets to contain a comma, so it must not be read as "use the
    /// default" - the distinction between empty and unset is the whole feature.
    /// </summary>
    [Fact]
    public void NoDelimiters_MeansEnterOnly()
    {
        var field = new TagEntry { Delimiters = new ObservableCollection<string>() };

        Type(field, "Smith, John");
        field.Tags!.ShouldBeEmpty();

        PressEnter(field);
        field.Tags!.ShouldBe(["Smith, John"]);
    }


    /// <summary>
    /// A paste arrives whole, and "a, b, c" is three tags. Typing the same characters leaves "c" in the
    /// editor, because the user is still writing it.
    /// </summary>
    [Fact]
    public void APasteCommitsItsTrailingFragmentAndTypingDoesNot()
    {
        var pasted = new TagEntry();
        Paste(pasted, "a, b, c");
        pasted.Tags!.ShouldBe(["a", "b", "c"]);
        pasted.PendingText.ShouldBe(String.Empty);

        var typed = new TagEntry();
        Type(typed, "a, b, c");
        typed.Tags!.ShouldBe(["a", "b"]);
        typed.PendingText.ShouldBe("c");
    }


    [Fact]
    public void WhitespaceIsTrimmedByDefault()
    {
        var field = new TagEntry();

        Type(field, "  spaced  ,");

        field.Tags!.ShouldBe(["spaced"]);
    }


    // ---------------------------------------------------------------------------------------------
    // Refusals
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void DuplicatesAreDroppedQuietly()
    {
        var field = new TagEntry();

        Type(field, "echo,");
        Type(field, "ECHO,");

        field.Tags!.ShouldBe(["echo"]);
    }


    [Fact]
    public void AllowDuplicates_LetsThemThrough()
    {
        var field = new TagEntry { AllowDuplicates = true };

        Type(field, "echo,");
        Type(field, "echo,");

        field.Tags!.ShouldBe(["echo", "echo"]);
    }


    [Fact]
    public void CaseSensitiveDuplicates_TreatsCaseAsADifference()
    {
        var field = new TagEntry { CaseSensitiveDuplicates = true };

        Type(field, "echo,");
        Type(field, "ECHO,");

        field.Tags!.ShouldBe(["echo", "ECHO"]);
    }


    /// <summary>
    /// At the cap the typed text has to stay put. Eating it would throw away something the user wrote,
    /// with no sign that it happened.
    /// </summary>
    [Fact]
    public void MaxTags_StopsCommittingAndLeavesTheTypedTextAlone()
    {
        var field = new TagEntry { MaxTags = 2 };

        Type(field, "one,");
        Type(field, "two,");
        Type(field, "three");
        PressEnter(field);

        field.Tags!.ShouldBe(["one", "two"]);
        field.PendingText.ShouldBe("three");
    }


    [Fact]
    public void TagAdding_CanRefuseAValue()
    {
        var field = new TagEntry();
        field.TagAdding += (_, e) => e.Cancel = e.Tag.Length < 3;

        Type(field, "ab,");
        field.Tags!.ShouldBeEmpty();

        Type(field, "abc,");
        field.Tags!.ShouldBe(["abc"]);
    }


    [Fact]
    public void ReadOnly_BlocksAddingAndRemoving()
    {
        var field = new TagEntry { Tags = new ObservableCollection<string> { "locked" }, IsReadOnly = true };

        Type(field, "new,");
        field.AddTag("another").ShouldBeFalse();
        field.RemoveTag("locked").ShouldBeFalse();

        field.Tags!.ShouldBe(["locked"]);
    }


    // ---------------------------------------------------------------------------------------------
    // Backspace
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// MAUI has no portable key-down on an Entry, so the field keeps a zero-width space in front of what
    /// is typed and reads its deletion as the Backspace. This is that mechanism, from the outside.
    /// </summary>
    [Fact]
    public void BackspaceOnEmptyText_RemovesTheNewestTag()
    {
        var field = Focused(new TagEntry());

        Type(field, "one,two,");
        field.Tags!.ShouldBe(["one", "two"]);

        // The editor holds the sentinel and nothing else; deleting it is Backspace against empty text.
        field.Editor.Text.ShouldBe(TagEntry.Sentinel);
        field.Editor.Text = String.Empty;

        field.Tags!.ShouldBe(["one"]);
    }


    [Fact]
    public void BackspaceWithTextTyped_LeavesTheTagsAlone()
    {
        var field = Focused(new TagEntry());

        Type(field, "one,");
        Type(field, "part");

        // Deleting a character of the typed text, not the sentinel.
        field.Editor.Text = TagEntry.Sentinel + "par";

        field.Tags!.ShouldBe(["one"]);
        field.PendingText.ShouldBe("par");
    }


    [Fact]
    public void BackspaceRemovesTagOff_MeansNoSentinelAndNoRemoval()
    {
        var field = Focused(new TagEntry { BackspaceRemovesTag = false });

        Type(field, "one,");
        field.Editor.Text.ShouldBe(String.Empty);

        field.Editor.Text = String.Empty;
        field.Tags!.ShouldBe(["one"]);
    }


    [Fact]
    public void TheSentinelNeverReachesACommittedTag()
    {
        var field = Focused(new TagEntry());

        Type(field, "clean,");

        field.Tags![0].ShouldBe("clean");
        field.Tags![0].ShouldNotContain(TagEntry.Sentinel);
    }


    /// <summary>
    /// The sentinel costs the placeholder — a prompt does not show while any text is present, invisible
    /// or not — so it is only in the box when it can actually do something: while the field is being
    /// typed into, and while there is a tag for Backspace to remove.
    /// </summary>
    [Fact]
    public void TheSentinelIsOnlyPresentWhenBackspaceCouldDoSomething()
    {
        var field = new TagEntry();

        // Not focused: nothing in the box, so the placeholder shows.
        field.AddTag("one");
        field.Editor.Text.ShouldBe(String.Empty);

        // Focused with a tag behind the caret: the sentinel is in.
        field.SetFocused(true);
        field.Editor.Text.ShouldBe(TagEntry.Sentinel);

        // The last tag goes and it comes straight back out.
        field.RemoveTag("one");
        field.Editor.Text.ShouldBe(String.Empty);

        // And leaving the field takes it out too.
        field.AddTag("two");
        field.Editor.Text.ShouldBe(TagEntry.Sentinel);
        field.SetFocused(false);
        field.Editor.Text.ShouldBe(String.Empty);
    }


    // ---------------------------------------------------------------------------------------------
    // The list
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ChipsFollowTheBoundCollection()
    {
        var tags = new ObservableCollection<string> { "alpha", "beta" };
        var field = new TagEntry { Tags = tags };

        field.ChipTags.ShouldBe(["alpha", "beta"]);

        tags.Add("gamma");
        field.ChipTags.ShouldBe(["alpha", "beta", "gamma"]);

        tags.Clear();
        field.ChipTags.ShouldBeEmpty();
    }


    [Fact]
    public void RemovingATag_ReportsItOnce()
    {
        var field = new TagEntry { Tags = new ObservableCollection<string> { "alpha", "beta" } };

        var removed = new List<string>();
        field.TagRemoved += (_, e) => removed.Add(e.Tag);

        field.RemoveTag("alpha").ShouldBeTrue();

        removed.ShouldBe(["alpha"]);
        field.Tags!.ShouldBe(["beta"]);
    }


    [Fact]
    public void AddingATag_ReportsItsIndex()
    {
        var field = new TagEntry();
        TagEventArgs? seen = null;
        field.TagAdded += (_, e) => seen = e;

        field.AddTag("first").ShouldBeTrue();
        field.AddTag("second").ShouldBeTrue();

        seen!.Tag.ShouldBe("second");
        seen.Index.ShouldBe(1);
    }


    /// <summary>
    /// A plain <c>List</c> raises nothing, so the chips have to be rebuilt by the control itself - a
    /// view model that hands over a plain list is not a view model whose field silently stops working.
    /// </summary>
    [Fact]
    public void APlainListStillRendersChips()
    {
        var field = new TagEntry { Tags = new List<string> { "plain" } };

        field.ChipTags.ShouldBe(["plain"]);

        field.AddTag("added");
        field.ChipTags.ShouldBe(["plain", "added"]);
    }
}
