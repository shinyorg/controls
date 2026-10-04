using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Shiny.Controls.RangePickers;
using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// The range pickers rendered for real: the selection rules are the shared engine's and tested there,
/// so these cover the wiring - which callbacks fire when, that a draft is a draft until Apply, and that
/// a host which does not bind cannot wipe a half-picked range by re-rendering.
/// </summary>
public class RangePickerTests
{
    static readonly DateOnly Today = new(2026, 3, 18);
    static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    sealed class NoJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => default;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => default;
    }

    /// <summary>Where the host leaves the component it rendered, and how to make the host render again.</summary>
    sealed class Holder
    {
        public object? Instance;
        public Action? Rerender;
    }

    sealed class CaptureHost : ComponentBase
    {
        [Parameter] public Type Type { get; set; } = default!;
        [Parameter] public Dictionary<string, object?> Values { get; set; } = new();
        [Parameter] public Holder Holder { get; set; } = default!;

        protected override void OnInitialized() => this.Holder.Rerender = this.StateHasChanged;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent(0, this.Type);
            var seq = 1;
            foreach (var (key, value) in this.Values)
                b.AddAttribute(seq++, key, value);
            b.AddComponentReferenceCapture(seq, r => this.Holder.Instance = r);
            b.CloseComponent();
        }
    }

    sealed class Harness<T> : IAsyncDisposable where T : IComponent
    {
        public required HtmlRenderer Renderer { get; init; }
        public required HtmlRootComponent Root { get; init; }
        public required Holder Holder { get; init; }

        public T Component => (T)this.Holder.Instance!;

        public string Html => this.Renderer.Dispatcher.InvokeAsync(() => this.Root.ToHtmlString()).GetAwaiter().GetResult();

        public Task Run(Func<T, Task> action) => this.Renderer.Dispatcher.InvokeAsync(() => action(this.Component));

        public Task Run(Action<T> action) => this.Renderer.Dispatcher.InvokeAsync(() => action(this.Component));

        public Task Rerender() => this.Renderer.Dispatcher.InvokeAsync(() => this.Holder.Rerender!());

        public ValueTask DisposeAsync() => this.Renderer.DisposeAsync();
    }

    static async Task<Harness<T>> RenderAsync<T>(Dictionary<string, object?> values) where T : IComponent
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IJSRuntime>(new NoJs());

        var renderer = new HtmlRenderer(services.BuildServiceProvider(), NullLoggerFactory.Instance);
        var holder = new Holder();
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<CaptureHost>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(CaptureHost.Type)] = typeof(T),
            [nameof(CaptureHost.Values)] = values,
            [nameof(CaptureHost.Holder)] = holder
        })));

        return new Harness<T> { Renderer = renderer, Root = root, Holder = holder };
    }

    static EventCallback<TValue> Callback<TValue>(Action<TValue> action) => EventCallback.Factory.Create(new object(), action);

    static int Count(string html, string pattern) => Regex.Matches(html, pattern).Count;


    // ---------------------------------------------------------------------------------------------
    // DateRangeCalendar
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task OneMonthIsSixWholeWeeksOfDays()
    {
        await using var h = await RenderAsync<DateRangeCalendar>(new() { ["Today"] = Today, ["Culture"] = En });

        Count(h.Html, "data-date=").ShouldBe(42);
        h.Html.ShouldContain("March 2026");
    }

    [Fact]
    public async Task SideBySideMonthsLeaveTheirNeighboursDaysEmpty()
    {
        await using var h = await RenderAsync<DateRangeCalendar>(new() { ["Today"] = Today, ["Culture"] = En, ["Months"] = 2 });

        Count(h.Html, "data-date=").ShouldBe(31 + 30);
    }

    [Fact]
    public async Task TwoClicksRaiseTheRange()
    {
        var ranges = new List<DateRange?>();
        await using var h = await RenderAsync<DateRangeCalendar>(new()
        {
            ["Today"] = Today,
            ["RangeChanged"] = Callback<DateRange?>(ranges.Add)
        });

        await h.Run(c => c.TapAsync(new DateOnly(2026, 3, 3)));
        ranges.ShouldBeEmpty();

        await h.Run(c => c.TapAsync(new DateOnly(2026, 3, 9)));
        ranges.ShouldHaveSingleItem().ShouldBe(new DateRange(new(2026, 3, 3), new(2026, 3, 9)));

        h.Html.ShouldContain("is-start");
        h.Html.ShouldContain("is-end");
        Count(h.Html, "is-in-range").ShouldBe(5);
    }

    [Fact]
    public async Task ClickingADisabledDayDoesNothing()
    {
        var starts = new List<DateOnly?>();
        await using var h = await RenderAsync<DateRangeCalendar>(new()
        {
            ["Today"] = Today,
            ["MinDate"] = Today,
            ["StartDateChanged"] = Callback<DateOnly?>(starts.Add)
        });

        await h.Run(c => c.TapAsync(Today.AddDays(-1)));

        starts.ShouldBeEmpty();
        h.Html.ShouldContain("is-disabled");
    }

    [Fact]
    public async Task AnUnboundHostReRenderingKeepsTheHalfPickedRange()
    {
        await using var h = await RenderAsync<DateRangeCalendar>(new() { ["Today"] = Today });

        await h.Run(c => c.TapAsync(new DateOnly(2026, 3, 3)));
        await h.Rerender();

        h.Component.Selector.Start.ShouldBe(new DateOnly(2026, 3, 3));
    }

    [Fact]
    public async Task APresetSelectsItsRangeAndLightsUp()
    {
        var ranges = new List<DateRange?>();
        await using var h = await RenderAsync<DateRangeCalendar>(new()
        {
            ["Today"] = Today,
            ["Presets"] = (IReadOnlyList<DateRangePreset>)[DateRangePresets.Last7Days, DateRangePresets.ThisMonth],
            ["RangeChanged"] = Callback<DateRange?>(ranges.Add)
        });

        await h.Run(c => c.ApplyPresetAsync(DateRangePresets.Last7Days));

        ranges.ShouldHaveSingleItem().ShouldBe(new DateRange(new(2026, 3, 12), Today));
        Count(h.Html, "shiny-rangecal-preset is-active").ShouldBe(1);
    }


    // ---------------------------------------------------------------------------------------------
    // DateRangePicker
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task ApplyCommitsTheDraft()
    {
        DateOnly? start = null, end = null;
        DateRange? range = null;
        await using var h = await RenderAsync<DateRangePicker>(new()
        {
            ["Today"] = Today,
            ["Culture"] = En,
            ["StartDateChanged"] = Callback<DateOnly?>(v => start = v),
            ["EndDateChanged"] = Callback<DateOnly?>(v => end = v),
            ["RangeChanged"] = Callback<DateRange?>(v => range = v)
        });

        await h.Run(p => p.OpenAsync());
        h.Html.ShouldContain("shiny-rangefield-popover");

        await h.Run(p =>
        {
            p.OnDraftStart(new DateOnly(2026, 3, 3));
            p.OnDraftEnd(new DateOnly(2026, 3, 9));
        });
        start.ShouldBeNull();   // a draft, until Apply

        await h.Run(p => p.ApplyAsync());

        start.ShouldBe(new DateOnly(2026, 3, 3));
        end.ShouldBe(new DateOnly(2026, 3, 9));
        range.ShouldBe(new DateRange(new(2026, 3, 3), new(2026, 3, 9)));
        h.Component.IsOpen.ShouldBeFalse();
        System.Net.WebUtility.HtmlDecode(h.Html).ShouldContain("Mar 3 – 9, 2026");
    }

    [Fact]
    public async Task CancelThrowsTheDraftAway()
    {
        var raised = 0;
        await using var h = await RenderAsync<DateRangePicker>(new()
        {
            ["Today"] = Today,
            ["StartDate"] = new DateOnly(2026, 3, 1),
            ["EndDate"] = new DateOnly(2026, 3, 2),
            ["RangeChanged"] = Callback<DateRange?>(_ => raised++)
        });

        await h.Run(p => p.OpenAsync());
        await h.Run(p => p.OnDraftStart(new DateOnly(2026, 3, 20)));
        await h.Run(p => p.CancelAsync());

        raised.ShouldBe(0);
        h.Component.DraftStart.ShouldBe(new DateOnly(2026, 3, 1));
        h.Html.ShouldNotContain("shiny-rangefield-popover");
    }

    [Fact]
    public async Task ClearCommitsNothing()
    {
        DateRange? range = new DateRange(Today, Today);
        await using var h = await RenderAsync<DateRangePicker>(new()
        {
            ["StartDate"] = Today,
            ["EndDate"] = Today,
            ["RangeChanged"] = Callback<DateRange?>(v => range = v)
        });

        await h.Run(p => p.OpenAsync());
        await h.Run(p => p.ClearAsync());

        range.ShouldBeNull();
        h.Component.StartDate.ShouldBeNull();
    }


    // ---------------------------------------------------------------------------------------------
    // TimeRangePicker
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task EndSlotsFollowTheStart()
    {
        await using var h = await RenderAsync<TimeRangePicker>(new() { ["Interval"] = TimeSpan.FromMinutes(30) });

        await h.Run(p => p.SelectStart(new TimeOnly(9, 0)));

        h.Component.DraftEnd.ShouldBe(new TimeOnly(10, 0));   // an hour by default
        h.Component.EndSlots.ShouldAllBe(s => s.Time > new TimeOnly(9, 0) || s.Time == TimeOnly.MinValue);
    }

    [Fact]
    public async Task MovingTheStartKeepsTheLength()
    {
        await using var h = await RenderAsync<TimeRangePicker>(new() { ["Interval"] = TimeSpan.FromMinutes(30) });

        await h.Run(p => p.SelectStart(new TimeOnly(9, 0)));
        await h.Run(p => p.SelectEnd(new TimeOnly(11, 30)));
        await h.Run(p => p.SelectStart(new TimeOnly(13, 0)));

        h.Component.DraftEnd.ShouldBe(new TimeOnly(15, 30));
    }

    [Fact]
    public async Task ApplyCommitsATimeRange()
    {
        TimeRange? range = null;
        await using var h = await RenderAsync<TimeRangePicker>(new()
        {
            ["RangeChanged"] = Callback<TimeRange?>(v => range = v),
            ["AllowOvernight"] = true
        });

        await h.Run(p => p.OpenAsync());
        await h.Run(p => p.SelectStart(new TimeOnly(22, 0)));
        await h.Run(p => p.SelectEnd(new TimeOnly(6, 0)));
        await h.Run(p => p.ApplyAsync());

        range.ShouldBe(new TimeRange(new(22, 0), new(6, 0)));
    }


    // ---------------------------------------------------------------------------------------------
    // DateTimeRangePicker
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task OneDayIsASameDayRange()
    {
        await using var h = await RenderAsync<DateTimeRangePicker>(new() { ["Today"] = Today });

        await h.Run(p => p.OpenAsync());
        await h.Run(p => p.OnDraftStartDate(Today));

        h.Component.DraftRange.ShouldBe(new DateTimeRange(Today.ToDateTime(new TimeOnly(9, 0)), Today.ToDateTime(new TimeOnly(17, 0))));
    }

    [Fact]
    public async Task AStartPastTheEndPushesTheEndOn()
    {
        await using var h = await RenderAsync<DateTimeRangePicker>(new() { ["Today"] = Today });

        await h.Run(p => p.OpenAsync());
        await h.Run(p => p.OnDraftStartDate(Today));
        await h.Run(p => p.SetStartTime(new TimeOnly(18, 0)));

        h.Component.DraftRange!.Value.End.ShouldBe(Today.ToDateTime(new TimeOnly(18, 15)));
    }

    [Fact]
    public async Task ARangeOutsideTheDurationLimitsCannotBeApplied()
    {
        DateTimeRange? range = null;
        await using var h = await RenderAsync<DateTimeRangePicker>(new()
        {
            ["Today"] = Today,
            ["MaxDuration"] = TimeSpan.FromHours(4),
            ["RangeChanged"] = Callback<DateTimeRange?>(v => range = v)
        });

        await h.Run(p => p.OpenAsync());
        await h.Run(p => p.OnDraftStartDate(Today));   // 09:00 - 17:00 is 8 hours

        h.Component.DraftRange.ShouldBeNull();
        await h.Run(p => p.ApplyAsync());
        range.ShouldBeNull();

        await h.Run(p => p.SetEndTime(new TimeOnly(12, 0)));
        await h.Run(p => p.ApplyAsync());
        range.ShouldBe(new DateTimeRange(Today.ToDateTime(new TimeOnly(9, 0)), Today.ToDateTime(new TimeOnly(12, 0))));
    }
}
