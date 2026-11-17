using System.Reflection;
using Shiny.Controls.Media;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Media.Tests;

public class VideoTextLayoutTests
{
    [Fact]
    public void Only_explicit_breaks_split_lines()
    {
        VideoTextLayout.SplitLines("BIG WAVES\r\nSURF\rNOW").ShouldBe(["BIG WAVES", "SURF", "NOW"]);
        VideoTextLayout.SplitLines(null).ShouldBeEmpty();
        VideoTextLayout.SplitLines("").ShouldBeEmpty();
    }

    [Fact]
    public void Auto_fit_is_bound_by_the_tighter_axis()
    {
        // 500x120 at 100px into a 1000x100 box: width allows 180px, height only 75px
        VideoTextLayout.FitFontSize(500, 120, 100, 1000, 100).ShouldBe(75, 0.0001);

        // same text into a tall box: width wins
        VideoTextLayout.FitFontSize(500, 120, 100, 500, 1000).ShouldBe(90, 0.0001);
    }

    [Fact]
    public void Nothing_to_measure_fits_to_zero()
    {
        VideoTextLayout.FitFontSize(0, 120, 100, 1000, 100).ShouldBe(0);
        VideoTextLayout.FitFontSize(500, 120, 100, 0, 100).ShouldBe(0);
    }

    [Fact]
    public void A_fixed_size_wins_over_auto_fit()
        => VideoTextLayout.ResolveFontSize(48, 500, 120, 100, 1000, 100).ShouldBe(48);

    [Fact]
    public void Cover_scale_crops_the_overflowing_axis()
    {
        // 16:9 video in a square view: height fills, width overflows by 16/9
        var (sx, sy) = VideoTextLayout.CoverScale(1920, 1080, 400, 400);
        sx.ShouldBe(16d / 9d, 0.0001);
        sy.ShouldBe(1, 0.0001);

        VideoTextLayout.CoverScale(0, 0, 400, 400).ShouldBe((1d, 1d));
    }

    [Fact]
    public void Auto_fit_svg_sizes_its_viewbox_from_the_measured_widths()
    {
        var svg = VideoTextLayout.BuildSvgMask(["OCEAN"], 0, "sans-serif", "bold", null, [450]);

        // 450 / 0.9 = 500 wide, 120 / 0.9 = 133.333 tall
        svg.ShouldContain("viewBox='0 0 500 133.333'");
        svg.ShouldContain("preserveAspectRatio='xMidYMid meet'");
        svg.ShouldContain("font-size='100'");
        svg.ShouldContain(">OCEAN</text>");
    }

    [Fact]
    public void Fixed_size_svg_has_no_viewbox()
    {
        var svg = VideoTextLayout.BuildSvgMask(["OCEAN"], 64, null, null, null);
        svg.ShouldNotContain("viewBox");
        svg.ShouldContain("font-size='64'");
        svg.ShouldContain("x='50%' y='50%'");
    }

    [Fact]
    public void Lines_are_centred_as_a_block()
    {
        var svg = VideoTextLayout.BuildSvgMask(["A", "B"], 100, null, null, null);
        svg.ShouldContain("dy='-60'>A<");
        svg.ShouldContain("dy='60'>B<");
    }

    [Fact]
    public void Text_and_attributes_are_xml_escaped()
    {
        var svg = VideoTextLayout.BuildSvgMask(["<R&D>"], 40, "Bob's Font", null, null);
        svg.ShouldContain("&lt;R&amp;D&gt;");
        svg.ShouldContain("font-family='Bob&apos;s Font'");
    }

    [Fact]
    public void Data_url_is_fully_percent_encoded()
    {
        var url = VideoTextLayout.ToDataUrl("<svg a='1'>#</svg>");
        url.ShouldStartWith("data:image/svg+xml;charset=utf-8,");
        url.ShouldNotContain("'");
        url.ShouldNotContain("#");
        url.ShouldNotContain("\"");
    }

    [Fact]
    public void A_horizontal_marquee_fits_to_the_height_only()
    {
        // 500x120 at 100px into 400x100: still auto-fit would be width-bound (72px); scrolling only the height (75px)
        VideoTextLayout.FitFontSize(500, 120, 100, 400, 100, MarqueeDirection.Left).ShouldBe(75, 0.0001);
        VideoTextLayout.FitFontSize(500, 120, 100, 400, 100, MarqueeDirection.None).ShouldBe(72, 0.0001);
    }

    [Fact]
    public void A_vertical_marquee_fits_to_the_width_only()
        => VideoTextLayout.FitFontSize(500, 120, 100, 400, 50, MarqueeDirection.Up).ShouldBe(72, 0.0001);

    [Theory]
    [InlineData(MarqueeDirection.Left, 250, 0, -250, 0)]
    [InlineData(MarqueeDirection.Right, -250, 0, 250, 0)]
    [InlineData(MarqueeDirection.Up, 0, 80, 0, -80)]
    [InlineData(MarqueeDirection.Down, 0, -80, 0, 80)]
    public void A_marquee_pass_runs_from_off_one_edge_to_off_the_other(
        MarqueeDirection direction, double startX, double startY, double endX, double endY)
    {
        // a 300x100 view, a 200x60 text block: each pass travels view + text along its axis
        VideoTextLayout.MarqueeOffset(direction, 0, 300, 100, 200, 60).ShouldBe((startX, startY));
        VideoTextLayout.MarqueeOffset(direction, 1, 300, 100, 200, 60).ShouldBe((endX, endY));
        VideoTextLayout.MarqueeOffset(direction, 0.5, 300, 100, 200, 60).ShouldBe((0d, 0d));
    }

    [Fact]
    public void No_marquee_stays_centred()
        => VideoTextLayout.MarqueeOffset(MarqueeDirection.None, 0.3, 300, 100, 200, 60).ShouldBe((0d, 0d));

    [Fact]
    public void A_marquee_svg_scrolls_in_pixels_once_the_size_is_known()
    {
        // 100px tall, one line: 90 / 1.2 = 75px font; 450 at 100px -> 337.5 wide; run = 600 + 337.5
        var svg = VideoTextLayout.BuildSvgMask(["OCEAN"], 0, null, null, null, [450], MarqueeDirection.Left, 6, 600, 100);
        svg.ShouldNotContain("viewBox");
        svg.ShouldContain("font-size='75'");
        svg.ShouldContain("from{transform:translate(468.75px,0px)}to{transform:translate(-468.75px,0px)}");
        svg.ShouldContain("animation:shiny-vt-marquee 6s linear infinite");
        svg.ShouldContain("<g id='shiny-vt-text'");
    }

    [Fact]
    public void A_marquee_svg_is_still_until_the_size_is_known()
    {
        var svg = VideoTextLayout.BuildSvgMask(["OCEAN"], 0, null, null, null, [450], MarqueeDirection.Left, 6);
        svg.ShouldContain("viewBox");
        svg.ShouldNotContain("animation");
    }

    [Fact]
    public void Overlay_svg_knocks_the_text_out_of_a_coloured_sheet()
    {
        var svg = VideoTextLayout.BuildSvgOverlay(["OCEAN"], 64, "Inter", null, null, "#112233", 0.75, "m1");
        svg.ShouldContain("<mask id='m1'");
        svg.ShouldContain("<rect width='100%' height='100%' fill='white'/>");
        svg.ShouldContain("<g fill='black'");
        svg.ShouldContain("fill='#112233' opacity='0.75' mask='url(#m1)'");
    }

    [Fact]
    public void Overlay_opacity_is_clamped()
        => VideoTextLayout.BuildSvgOverlay(["A"], 64, null, null, null, "black", 3, "m").ShouldContain("opacity='1'");
}


public class VideoTextTests : MediaTestBase
{
    [Fact]
    public void Defaults_are_silent_bold_and_auto_fit()
    {
        var text = new VideoText();
        text.IsMuted.ShouldBeTrue();
        text.FontAttributes.ShouldBe(FontAttributes.Bold);
        text.FontSize.ShouldBe(0);
        text.OverlayColor.ShouldBe(Colors.Black);
        text.OverlayOpacity.ShouldBe(1);
        text.Marquee.ShouldBe(MarqueeDirection.None);
        text.MarqueeDuration.ShouldBe(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public void Overlay_opacity_is_clamped_to_0_1()
    {
        new VideoText { OverlayOpacity = 2 }.OverlayOpacity.ShouldBe(1);
        new VideoText { OverlayOpacity = -1 }.OverlayOpacity.ShouldBe(0);
    }

    [Fact]
    public void The_fallback_label_sits_on_the_overlay()
    {
        var text = new VideoText { Text = "OCEAN", OverlayColor = Colors.Navy, OverlayOpacity = 0.5 };
        var label = text.Content.ShouldBeOfType<Label>();
        label.BackgroundColor.Alpha.ShouldBe(0.5f, 0.001f);
        label.BackgroundColor.Blue.ShouldBe(Colors.Navy.Blue, 0.001f);

        text.OverlayColor = null;
        label.BackgroundColor.ShouldBeNull();
    }

    [Fact]
    public void Negative_font_size_coerces_to_auto_fit()
        => new VideoText { FontSize = -10 }.FontSize.ShouldBe(0);

    [Fact]
    public void Without_a_mask_the_text_is_shown_as_a_label()
    {
        // the plain net11.0 target (Linux head, and this test host) has no masked output
        VideoText.IsMaskSupported.ShouldBeFalse();

        var text = new VideoText { Text = "OCEAN", FallbackTextColor = Colors.Teal, FontSize = 40 };
        var label = text.Content.ShouldBeOfType<Label>();
        label.Text.ShouldBe("OCEAN");
        label.TextColor.ShouldBe(Colors.Teal);
        label.FontSize.ShouldBe(40);
    }


    // ── fallback marquee lifetime ────────────────────────────────────────────────────────────────
    //
    // It used to be a repeating MAUI Animation that nothing ever aborted, so MAUI's static animation table
    // kept the label (and the page) alive and ticking after the page was gone.

    static VideoText MarqueeText(out Label label)
    {
        var text = new VideoText { Text = "OCEAN", FontSize = 40, Marquee = MarqueeDirection.Left };
        label = text.Content.ShouldBeOfType<Label>();
        ((IView)label).Arrange(new Rect(0, 0, 300, 100));
        return text;
    }

    // Loaded/Unloaded never fire headlessly (there is no window), so they are driven through MAUI's own
    // internal senders. Unloaded is ignored unless Loaded fired first.
    static void Send(VisualElement element, string method)
        => typeof(VisualElement)
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes)!
            .Invoke(element, null);

    [Fact]
    public void Fallback_marquee_runs_on_a_frame_timer()
    {
        var text = MarqueeText(out var label);

        text.IsFallbackMarqueeRunning.ShouldBeTrue();
        TimerWithInterval(TimeSpan.FromMilliseconds(16)).ShouldNotBeNull().IsRunning.ShouldBeTrue();
        label.TranslationX.ShouldBeGreaterThan(0); // a leftward pass starts off the right edge
    }

    [Fact]
    public void Unloading_stops_the_fallback_marquee()
    {
        var text = MarqueeText(out var label);
        var timer = TimerWithInterval(TimeSpan.FromMilliseconds(16)).ShouldNotBeNull();

        Send(text, "SendLoaded");
        Send(text, "SendUnloaded");

        text.IsFallbackMarqueeRunning.ShouldBeFalse();
        timer.IsRunning.ShouldBeFalse();
        label.TranslationX.ShouldBe(0);
    }

    [Fact]
    public void Loading_again_restarts_the_fallback_marquee()
    {
        var text = MarqueeText(out _);
        Send(text, "SendLoaded");
        Send(text, "SendUnloaded");

        Send(text, "SendLoaded");

        text.IsFallbackMarqueeRunning.ShouldBeTrue();
    }

    [Fact]
    public void Disconnecting_the_handler_stops_the_fallback_marquee()
    {
        var text = MarqueeText(out _);
        text.Handler = new StubViewHandler();

        text.Handler = null;

        text.IsFallbackMarqueeRunning.ShouldBeFalse();
    }

    [Fact]
    public void An_unloaded_marquee_does_not_start_on_a_resize()
    {
        var text = MarqueeText(out var label);
        Send(text, "SendLoaded");
        Send(text, "SendUnloaded");

        ((IView)label).Arrange(new Rect(0, 0, 400, 100));

        text.IsFallbackMarqueeRunning.ShouldBeFalse();
    }
}
