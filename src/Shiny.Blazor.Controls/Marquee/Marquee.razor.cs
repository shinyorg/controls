using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

/// <summary>
/// Scrolls its content endlessly - horizontally, vertically, or along any angle - in the style of
/// Magic UI's marquee.
/// </summary>
/// <example>
/// <code>
/// &lt;Marquee PauseOnHover="true" FadeEdges="true"&gt;
///     @foreach (var logo in logos) { &lt;img src="@logo" height="32" /&gt; }
/// &lt;/Marquee&gt;
/// </code>
/// </example>
/// <remarks>
/// <para>
/// The movement is a CSS animation; the only script measures, so it can add copies when a few are not
/// enough to fill the box, turn <see cref="Speed"/> into a duration, and pad upright items at an angle.
/// </para>
/// <para>
/// At 0° and 180° the marquee is as tall as its content. Vertical and angled marquees take their size
/// from the page - give them a height (an angled one falls back to 160px).
/// </para>
/// </remarks>
public partial class Marquee
{
    ElementReference root;
    IJSObjectReference? module;
    DotNetObjectReference<Marquee>? selfRef;
    bool disposed;
    int copies = 4;
    string? lastOptions;

    /// <summary>The content to repeat. Rendered once per copy, so keep it free of state that must exist once.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// The direction of travel, in degrees. 0 scrolls leftward, 90 upward, 180 rightward, 270 downward;
    /// anything in between travels along that angle (-15 drifts left and slightly down).
    /// </summary>
    [Parameter] public double Angle { get; set; }

    /// <summary>Runs the other way along the same axis.</summary>
    [Parameter] public bool Reverse { get; set; }

    /// <summary>How long one pass of the content takes. Ignored when <see cref="Speed"/> is set.</summary>
    [Parameter] public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(40);

    /// <summary>Travel speed in CSS pixels per second. When positive it replaces <see cref="Duration"/>.</summary>
    [Parameter] public double Speed { get; set; }

    /// <summary>Space between items, and between copies, in CSS pixels.</summary>
    [Parameter] public double Gap { get; set; } = 16;

    /// <summary>The fewest copies to render. More are added when it takes more to fill the box.</summary>
    [Parameter] public int Repeat { get; set; } = 4;

    /// <summary>Stops while the pointer is over it.</summary>
    [Parameter] public bool PauseOnHover { get; set; }

    /// <summary>Stops while a finger or button is held down on it.</summary>
    [Parameter] public bool PauseOnPress { get; set; }

    /// <summary>Whether it moves at all. False holds it where it is.</summary>
    [Parameter] public bool IsRunning { get; set; } = true;

    /// <summary>Fades the content out at both ends of the axis (a CSS mask, so it works over any background).</summary>
    [Parameter] public bool FadeEdges { get; set; }

    /// <summary>How long each fade is, in CSS pixels.</summary>
    [Parameter] public double FadeLength { get; set; } = 48;

    /// <summary>At an angle, whether items stay level (the default) or tilt with the direction of travel.</summary>
    [Parameter] public bool KeepContentUpright { get; set; } = true;

    /// <summary>Holds still under <c>prefers-reduced-motion: reduce</c>. On by default.</summary>
    [Parameter] public bool RespectReducedMotion { get; set; } = true;

    [Parameter] public string? Class { get; set; }

    [Parameter] public string? Style { get; set; }


    internal enum Mode { Horizontal, Vertical, Angled }

    /// <summary>The angle folded into [0, 360).</summary>
    internal double NormalizedAngle
    {
        get
        {
            var a = this.Angle % 360;
            return a < 0 ? a + 360 : a;
        }
    }

    internal Mode LayoutMode
    {
        get
        {
            var a = this.NormalizedAngle;
            if (IsNear(a, 0) || IsNear(a, 180) || IsNear(a, 360))
                return Mode.Horizontal;
            if (IsNear(a, 90) || IsNear(a, 270))
                return Mode.Vertical;
            return Mode.Angled;
        }
    }

    /// <summary>
    /// Whether the animation runs backwards. 180° and 270° are the straight modes run in reverse,
    /// so they flip it - and <see cref="Reverse"/> flips it back.
    /// </summary>
    internal bool RunsBackwards
    {
        get
        {
            var a = this.NormalizedAngle;
            var flipped = this.LayoutMode != Mode.Angled && (IsNear(a, 180) || IsNear(a, 270));
            return this.Reverse ^ flipped;
        }
    }

    internal int CopyCount => this.copies;

    string ModeClass => this.LayoutMode switch
    {
        Mode.Vertical => "shiny-marquee--vertical",
        Mode.Angled => this.KeepContentUpright ? "shiny-marquee--angled shiny-marquee--upright" : "shiny-marquee--angled",
        _ => "shiny-marquee--horizontal"
    };

    string StateClasses
    {
        get
        {
            var classes = new List<string>(5);
            if (this.RunsBackwards) classes.Add("shiny-marquee--reverse");
            if (!this.IsRunning) classes.Add("shiny-marquee--paused");
            if (this.PauseOnHover) classes.Add("shiny-marquee--pause-hover");
            if (this.FadeEdges) classes.Add("shiny-marquee--fade");
            if (this.RespectReducedMotion) classes.Add("shiny-marquee--respect-motion");
            return string.Join(' ', classes);
        }
    }

    internal string RootStyle
    {
        get
        {
            var c = CultureInfo.InvariantCulture;
            var angle = this.NormalizedAngle;
            var style =
                $"--shiny-marquee-duration:{Math.Max(0.01, this.Duration.TotalSeconds).ToString("0.###", c)}s;" +
                $"--shiny-marquee-gap:{this.Gap.ToString("0.##", c)}px;" +
                $"--shiny-marquee-angle:{angle.ToString("0.##", c)}deg;" +
                $"--shiny-marquee-fade:{this.FadeLength.ToString("0.##", c)}px;" +
                $"--shiny-marquee-fade-angle:{(90 + angle).ToString("0.##", c)}deg;";

            return string.IsNullOrWhiteSpace(this.Style) ? style : style + this.Style;
        }
    }


    protected override void OnParametersSet()
    {
        // Never fewer than asked for; the script only ever raises the count.
        if (this.copies < Math.Max(1, this.Repeat) || this.lastOptions is null)
            this.copies = Math.Max(1, this.Repeat);
    }


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            var options = this.OptionsJson();

            if (firstRender)
            {
                var loaded = await this.JS.InvokeAsync<IJSObjectReference>("import", "./_content/Shiny.Blazor.Controls/marquee.js");
                if (this.disposed) { await loaded.ReleaseLateAsync(); return; }

                this.module = loaded;
                this.selfRef = DotNetObjectReference.Create(this);
                this.lastOptions = options;
                await this.module.InvokeVoidAsync("init", this.root, this.selfRef, options);
                return;
            }

            if (this.module is not null && options != this.lastOptions)
            {
                this.lastOptions = options;
                await this.module.InvokeVoidAsync("update", this.root, options);
            }
        }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
        catch (TaskCanceledException) { }
        catch (JSException) { }
    }


    /// <summary>The script's measurement: how many copies it takes to fill the box.</summary>
    [JSInvokable]
    public void SetCopies(int count)
    {
        count = Math.Max(Math.Max(1, this.Repeat), Math.Min(count, 200));
        if (count == this.copies)
            return;

        this.copies = count;
        this.StateHasChanged();
    }


    // A string, not an object: no DTO for the trimmer to strip in a published WASM build.
    string OptionsJson()
    {
        var c = CultureInfo.InvariantCulture;
        var mode = this.LayoutMode switch
        {
            Mode.Vertical => "vertical",
            Mode.Angled => "angled",
            _ => "horizontal"
        };

        return "{" +
            $"\"mode\":\"{mode}\"," +
            $"\"angle\":{this.NormalizedAngle.ToString(c)}," +
            $"\"gap\":{this.Gap.ToString(c)}," +
            $"\"repeat\":{Math.Max(1, this.Repeat).ToString(c)}," +
            $"\"speed\":{Math.Max(0, this.Speed).ToString(c)}," +
            $"\"upright\":{(this.KeepContentUpright ? "true" : "false")}," +
            $"\"pauseOnPress\":{(this.PauseOnPress ? "true" : "false")}" +
            "}";
    }


    static bool IsNear(double a, double b) => Math.Abs(a - b) < 0.0001;


    public async ValueTask DisposeAsync()
    {
        this.disposed = true;

        try
        {
            if (this.module is not null)
            {
                await this.module.InvokeVoidAsync("dispose", this.root);
                await this.module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
        catch (TaskCanceledException) { }
        catch (JSException) { }

        this.selfRef?.Dispose();
        GC.SuppressFinalize(this);
    }
}
