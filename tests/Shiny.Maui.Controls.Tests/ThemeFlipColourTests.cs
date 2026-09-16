using Microsoft.Maui.Controls;
using Shiny.Maui.Controls.Chat;
using Shiny.Maui.Controls.Chat.Internal;
using Shiny.Maui.Controls.DataGrid;
using Shiny.Maui.Controls.Themes;
using Shiny.Maui.Controls.Tree;
using Shiny.Maui.Controls.Tree.Internal;
using Shouldly;
using Xunit;
using Grid2 = Shiny.Maui.Controls.DataGrid.DataGrid;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// Colours that controls used to copy out of <c>Application.Current.Resources</c> once - when a row
/// was built or a bubble rendered - follow the theme live after a light/dark flip, and honour a
/// palette scoped over the control, while an explicit colour property still wins.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class ThemeFlipColourTests
{
    readonly Application app;
    readonly BasicTheme theme = new();

    public ThemeFlipColourTests()
    {
        TestDispatcherProvider.Install();
        this.app = new Application { UserAppTheme = AppTheme.Light };
        this.app.Resources.MergedDictionaries.Add(this.theme.Light);
    }

    Color Light(string key) => (Color)this.theme.Light[key];

    Color Dark(string key) => (Color)this.theme.Dark[key];

    /// <summary>The swap ShinyThemeManager makes on a light/dark flip. Nothing is rebuilt.</summary>
    void FlipToDark()
    {
        this.app.Resources.MergedDictionaries.Remove(this.theme.Light);
        this.app.Resources.MergedDictionaries.Add(this.theme.Dark);
    }

    void Host(View view) => new ContentPage { Content = view }.Parent = this.app;

    [Fact]
    public void ThePaletteActuallyDiffers()
    {
        // Every assertion below would pass vacuously on a pack whose light and dark keys matched.
        foreach (var key in new[] { ShinyThemeKeys.Color.Primary, ShinyThemeKeys.Color.OnSurface, ShinyThemeKeys.Color.Surface, ShinyThemeKeys.Color.OnSurfaceVariant })
            Light(key).ShouldNotBe(Dark(key), key);
    }


    // ---------------------------------------------------------------------------------------------
    // TreeView multi-select checkbox
    // ---------------------------------------------------------------------------------------------

    static TreeView BuildTree()
    {
        var tree = new TreeView
        {
            SelectionMode = TreeSelectionMode.Multiple,
            ChildrenSelector = _ => null
        };
        tree.ItemsSource = new List<string> { "one", "two" };
        return tree;
    }

    static Border CheckBox(TreeNodeView row)
        => ((IVisualTreeElement)row).GetVisualTreeDescendants().OfType<Border>()
            .Single(b => b.AutomationId == TreeNodeView.CheckBoxAutomationId);

    static Color Stroke(Border border) => ((SolidColorBrush)border.Stroke).Color;

    [Fact]
    public void TreeCheckBoxesFollowALightDarkFlip()
    {
        var tree = BuildTree();
        this.Host(tree);
        var rows = tree.rowLayout.Children.OfType<TreeNodeView>().ToList();
        tree.HandleRowTapped(rows[0].Node);

        var selected = CheckBox(rows[0]);
        var unselected = CheckBox(rows[1]);
        selected.BackgroundColor.ShouldBe(this.Light(ShinyThemeKeys.Color.Primary));
        Stroke(selected).ShouldBe(this.Light(ShinyThemeKeys.Color.Primary));
        Stroke(unselected).ShouldBe(this.Light(ShinyThemeKeys.Color.OnSurfaceVariant));
        ((Label)selected.Content!).TextColor.ShouldBe(this.Light(ShinyThemeKeys.Color.OnPrimary));

        this.FlipToDark();

        // No selection change: unfixed, the box kept the colours copied when it was last refreshed.
        selected.BackgroundColor.ShouldBe(this.Dark(ShinyThemeKeys.Color.Primary));
        Stroke(selected).ShouldBe(this.Dark(ShinyThemeKeys.Color.Primary));
        Stroke(unselected).ShouldBe(this.Dark(ShinyThemeKeys.Color.OnSurfaceVariant));
        ((Label)selected.Content!).TextColor.ShouldBe(this.Dark(ShinyThemeKeys.Color.OnPrimary));
        unselected.BackgroundColor.ShouldBe(Colors.Transparent);
    }

    [Fact]
    public void TreeCheckBoxesKeepAnExplicitColourAcrossAFlipAndReturnToTheThemeWhenDeselected()
    {
        var tree = BuildTree();
        tree.CheckBoxColor = Colors.Red;
        this.Host(tree);
        var row = tree.rowLayout.Children.OfType<TreeNodeView>().First();
        tree.HandleRowTapped(row.Node);

        this.FlipToDark();

        var box = CheckBox(row);
        box.BackgroundColor.ShouldBe(Colors.Red);
        Stroke(box).ShouldBe(Colors.Red);

        // Explicit colour -> token: the local value has to be cleared or the token never wins again.
        tree.HandleRowTapped(row.Node);
        Stroke(box).ShouldBe(this.Dark(ShinyThemeKeys.Color.OnSurfaceVariant));
        box.BackgroundColor.ShouldBe(Colors.Transparent);
    }

    [Fact]
    public void TreeCheckBoxesHonourAPaletteScopedOverTheTree()
    {
        var tree = BuildTree();
        tree.Resources.MergedDictionaries.Add(this.theme.Dark);
        this.Host(tree);
        var row = tree.rowLayout.Children.OfType<TreeNodeView>().First();
        tree.HandleRowTapped(row.Node);

        CheckBox(row).BackgroundColor.ShouldBe(this.Dark(ShinyThemeKeys.Color.Primary));
    }


    // ---------------------------------------------------------------------------------------------
    // DataGrid selection / stripe / frozen-pane backgrounds
    // ---------------------------------------------------------------------------------------------

    sealed class Person
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    static Grid2 BuildGrid(bool frozen = false)
    {
        var grid = new Grid2 { Striped = true, AutoWidthMeasurer = _ => 100 };
        var name = new DataGridColumn { Title = "Name", PropertyName = nameof(Person.Name), Width = new GridLength(100) };
        grid.Columns.Add(name);
        grid.Columns.Add(new DataGridColumn { Title = "Age", PropertyName = nameof(Person.Age), Width = new GridLength(80) });
        if (frozen)
        {
            grid.HorizontalScroll = true;
            name.Frozen = DataGridFrozen.Start;
        }
        grid.ItemsSource = new List<Person> { new() { Name = "a", Age = 1 }, new() { Name = "b", Age = 2 } };
        if (frozen)
        {
            // Re-resolves the pinned run, the same way the frozen-width tests do.
            grid.Columns.Add(new DataGridColumn { Title = "Again", PropertyName = nameof(Person.Age), Width = new GridLength(80) });
        }
        return grid;
    }

    static Grid RowGrid(Grid2 grid, object item)
        => ((Grid)grid.BuildDisplayItemView(item)).Children.OfType<Grid>().First();

    static Color Composite(Color tint, Color surface)
    {
        var a = tint.Alpha;
        return new Color(
            (float)(tint.Red * a + surface.Red * (1 - a)),
            (float)(tint.Green * a + surface.Green * (1 - a)),
            (float)(tint.Blue * a + surface.Blue * (1 - a)),
            1f);
    }

    [Fact]
    public void DataGridSelectionAndStripeFollowALightDarkFlip()
    {
        var grid = BuildGrid();
        this.Host(grid);
        var rows = grid.DisplayItems.OfType<DataGridRow>().ToList();
        rows[0].IsSelected = true;

        var selected = RowGrid(grid, rows[0]);
        var striped = RowGrid(grid, rows[1]);
        selected.BackgroundColor.ShouldBe(this.Light(ShinyThemeKeys.Color.Primary).WithAlpha(0.14f));
        striped.BackgroundColor.ShouldBe(this.Light(ShinyThemeKeys.Color.OnSurface).WithAlpha(0.04f));

        this.FlipToDark();

        // The row views are the ones already built - nothing was rebuilt to get here.
        selected.BackgroundColor.ShouldBe(this.Dark(ShinyThemeKeys.Color.Primary).WithAlpha(0.14f), "unfixed, the colours were copied once in the constructor");
        striped.BackgroundColor.ShouldBe(this.Dark(ShinyThemeKeys.Color.OnSurface).WithAlpha(0.04f));
        grid.DisplayItems.OfType<DataGridRow>().First().ShouldBeSameAs(rows[0]);
        rows[0].IsSelected.ShouldBeTrue();
    }

    [Fact]
    public void DataGridFrozenPanesRecompositeOntoTheNewSurface()
    {
        var grid = BuildGrid(frozen: true);
        this.Host(grid);
        var row = grid.DisplayItems.OfType<DataGridRow>().First();
        row.IsSelected = true;

        var view = grid.BuildDisplayItemView(row);
        Color[] Backgrounds() => ((IVisualTreeElement)view).GetVisualTreeDescendants().OfType<Grid>().Select(g => g.BackgroundColor).Where(c => c is not null).ToArray();

        var lightPane = Composite(this.Light(ShinyThemeKeys.Color.Primary).WithAlpha(0.14f), this.Light(ShinyThemeKeys.Color.Surface));
        var darkPane = Composite(this.Dark(ShinyThemeKeys.Color.Primary).WithAlpha(0.14f), this.Dark(ShinyThemeKeys.Color.Surface));
        Backgrounds().ShouldContain(lightPane);

        this.FlipToDark();

        Backgrounds().ShouldContain(darkPane);
        Backgrounds().ShouldNotContain(lightPane);
    }


    // ---------------------------------------------------------------------------------------------
    // ChatView bubbles
    // ---------------------------------------------------------------------------------------------

    static ChatMessage Message(string body) => new(
        "1", null, "them", body, null,
        MessageStatus.Sent, null, DateTimeOffset.Now, null, [], []);

    (ChatView Chat, ChatBubbleView Bubble) BuildBubble(string body, Action<ChatView>? configure = null)
    {
        var chat = new ChatView();
        configure?.Invoke(chat);
        var message = Message(body);
        chat.Items.Add(message);

        var bubble = new ChatBubbleView(chat, isMe: false);
        this.Host(new Grid { Children = { chat, bubble } });
        bubble.BindingContext = message;
        return (chat, bubble);
    }

    static Span[] TextSpans(ChatBubbleView bubble) => bubble.TextLabel.FormattedText!.Spans.Where(s => s.GestureRecognizers.Count == 0).ToArray();

    static Span[] LinkSpans(ChatBubbleView bubble) => bubble.TextLabel.FormattedText!.Spans.Where(s => s.GestureRecognizers.Count > 0).ToArray();

    [Fact]
    public void ChatBubbleTextAndLinksFollowALightDarkFlip()
    {
        var (_, bubble) = this.BuildBubble("hello **there** see https://example.com");

        TextSpans(bubble).ShouldNotBeEmpty();
        LinkSpans(bubble).ShouldHaveSingleItem();
        TextSpans(bubble).ShouldAllBe(s => s.TextColor == this.Light(ShinyThemeKeys.Color.OnSurface));
        LinkSpans(bubble).ShouldAllBe(s => s.TextColor == this.Light(ShinyThemeKeys.Color.Primary));

        this.FlipToDark();

        // Unfixed, dark-palette text stayed light-palette ink: dark on the now-dark bubble.
        TextSpans(bubble).ShouldAllBe(s => s.TextColor == this.Dark(ShinyThemeKeys.Color.OnSurface));
        LinkSpans(bubble).ShouldAllBe(s => s.TextColor == this.Dark(ShinyThemeKeys.Color.Primary));
    }

    [Fact]
    public void AnExplicitChatTextColourSurvivesAFlipWhileLinksFollowTheTheme()
    {
        var (_, bubble) = this.BuildBubble("hi https://example.com", chat => chat.OtherTextColor = Colors.Red);

        this.FlipToDark();

        TextSpans(bubble).ShouldAllBe(s => s.TextColor == Colors.Red);
        LinkSpans(bubble).ShouldAllBe(s => s.TextColor == this.Dark(ShinyThemeKeys.Color.Primary));
    }
}
