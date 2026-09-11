using Shiny.Maui.Controls;

namespace Sample.Features.Tabs;

public partial class TabsDemoPage : ShinyTabbedPage
{
    readonly TabsDemoViewModel viewModel = new();

    public TabsDemoPage()
    {
        this.InitializeComponent();
        SampleSourceCode.Attach(this);
        this.BindingContext = this.viewModel;

        this.SelectionChanged += (_, e) =>
            this.viewModel.Status = $"Selected {e.NewItem?.Title} (index {e.NewIndex}).";

        this.TabReselected += (_, e) =>
            this.viewModel.Status = $"Re-tapped {e.Item.Title} — scroll to top, pop to root, whatever the tab means by it.";

        // The centre button raises this before it presents anything, so a page can take the press
        // over entirely by cancelling.
        this.TabBar.CenterClicked += (_, _) =>
            this.viewModel.Status = "Centre button pressed.";

        this.TabBar.ActionInvoked += (_, e) =>
            this.viewModel.Status = $"Ran \"{e.Action.Text}\" from the centre menu.";
    }

    void OnBarDocked(object? sender, EventArgs e) => this.BarStyle = TabBarStyle.Docked;

    void OnBarFloating(object? sender, EventArgs e) => this.BarStyle = TabBarStyle.Floating;

    void OnBarSolid(object? sender, EventArgs e)
    {
        this.BarBackgroundOpacity = 1;
        this.ContentBehindTabBar = false;
    }

    void OnBarTranslucent(object? sender, EventArgs e) => this.BarBackgroundOpacity = 0.6;

    // Transparency on its own only reveals the page's own background. Running the content under the
    // bar is what puts something behind it worth seeing.
    void OnBarSheer(object? sender, EventArgs e)
    {
        this.BarBackgroundOpacity = 0.35;
        this.ContentBehindTabBar = true;
    }

    void OnMaterialSolid(object? sender, EventArgs e)
    {
        this.BarMaterial = TabBarMaterial.Solid;
        this.BarGlassTint = null;
    }

    // No ContentBehindTabBar here on purpose: a glass bar turns it on for itself, because glass with
    // nothing behind it is an expensive way to draw a grey rectangle.
    void OnMaterialGlass(object? sender, EventArgs e)
    {
        this.BarMaterial = TabBarMaterial.Glass;
        this.BarGlassTint = null;
    }

    void OnMaterialGlassClear(object? sender, EventArgs e)
    {
        this.BarMaterial = TabBarMaterial.GlassClear;
        this.BarGlassTint = null;
    }

    // A tint on glass is not a fill - the surface is still refracting what is behind it, so an
    // opaque colour gives tinted glass rather than a tinted rectangle.
    void OnMaterialGlassTinted(object? sender, EventArgs e)
    {
        this.BarMaterial = TabBarMaterial.Glass;
        this.BarGlassTint = Colors.MediumPurple;
    }

    void OnSlide(object? sender, EventArgs e) => this.Transition = StateTransition.Slide;

    void OnFade(object? sender, EventArgs e) => this.Transition = StateTransition.Fade;

    void OnScale(object? sender, EventArgs e) => this.Transition = StateTransition.Scale;

    void OnPill(object? sender, EventArgs e) => this.IndicatorStyle = TabIndicatorStyle.Pill;

    void OnLine(object? sender, EventArgs e) => this.IndicatorStyle = TabIndicatorStyle.Line;

    void OnDot(object? sender, EventArgs e) => this.IndicatorStyle = TabIndicatorStyle.Dot;

    void OnNoIndicator(object? sender, EventArgs e) => this.IndicatorStyle = TabIndicatorStyle.None;

    void OnIndicatorSlide(object? sender, EventArgs e)
    {
        this.TabBar.IndicatorTransition = TabIndicatorTransition.Slide;
        this.viewModel.Status = "The indicator travels from the old tab to the new one.";
    }

    void OnIndicatorStatic(object? sender, EventArgs e)
    {
        this.TabBar.IndicatorTransition = TabIndicatorTransition.None;
        this.viewModel.Status = "The indicator is drawn inside each cell — it appears rather than travels.";
    }

    void OnSlowIndicator(object? sender, EventArgs e)
    {
        // Same journey, four times as long — the easiest way to actually see the travel.
        this.TabBar.IndicatorTransition = TabIndicatorTransition.Slide;
        this.TabBar.AnimationDuration = this.TabBar.AnimationDuration == 800u ? 200u : 800u;
        this.viewModel.Status = $"Indicator travel over {this.TabBar.AnimationDuration}ms.";
    }

    void OnLabelsAlways(object? sender, EventArgs e) => this.LabelMode = TabLabelMode.Always;

    void OnLabelsSelected(object? sender, EventArgs e) => this.LabelMode = TabLabelMode.SelectedOnly;

    void OnLabelsNever(object? sender, EventArgs e) => this.LabelMode = TabLabelMode.Never;

    void OnAnimScale(object? sender, EventArgs e) => this.SetAnimation(TabSelectionAnimation.Scale);

    void OnAnimLift(object? sender, EventArgs e) => this.SetAnimation(TabSelectionAnimation.Lift);

    void OnAnimBounce(object? sender, EventArgs e) => this.SetAnimation(TabSelectionAnimation.Bounce);

    void SetAnimation(TabSelectionAnimation animation)
    {
        // Animator wins over SelectionAnimation, so it has to be cleared to get the built-ins back.
        this.TabBar.Animator = null;
        this.TabBar.SelectionAnimation = animation;
        this.viewModel.Status = $"Tab animation: {animation}.";
    }

    void OnAnimCustom(object? sender, EventArgs e)
    {
        this.TabBar.Animator = new SpinAnimator();
        this.viewModel.Status = "Tab animation: a custom ITabAnimator that spins the icon.";
    }

    /// <summary>Anything at all can be an animation — this one spins the incoming tab's icon.</summary>
    sealed class SpinAnimator : ITabAnimator
    {
        public async Task AnimateAsync(TabAnimationContext context)
        {
            if (context.Icon is not { } icon || context.Duration == 0)
                return;

            if (context.IsSelected)
                await icon.RotateToAsync(360, context.Duration, Easing.CubicOut);
            else
                icon.Rotation = 0;
        }
    }

    // The badge on a tab, rather than on its page: the Chat tab may never have been opened, so
    // there is no page to carry a count yet.
    void OnAddBadge(object? sender, EventArgs e)
    {
        var chat = this.Tabs[1];
        chat.Badge = Int32.TryParse(chat.Badge, out var count) ? (count + 1).ToString() : "1";
    }

    void OnClearBadge(object? sender, EventArgs e) => this.Tabs[1].Badge = null;
}
