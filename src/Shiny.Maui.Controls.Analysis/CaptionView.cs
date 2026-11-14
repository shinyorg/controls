namespace Shiny.Maui.Controls.Analysis;

/// <summary>
/// Shows captions from an <see cref="ICaptionSource"/> (a speech caption analyzer) as a subtitle bar. Lay it over a
/// <c>MediaElement</c> or a <c>CameraView</c> in the same grid cell, aligned to the bottom.
/// </summary>
/// <remarks>
/// <para>
/// For media whose captions were made ahead of playback, bind <see cref="Position"/> to the player's position and the
/// view shows exactly the cue being spoken. For live captions it shows the newest committed text followed by the
/// utterance still being recognized, and clears itself after <see cref="LiveHoldTime"/> without new speech.
/// </para>
/// <para>The bar draws its own background, so give it no <c>Border</c> of your own.</para>
/// </remarks>
/// <example>
/// <code language="xaml">
/// &lt;Grid&gt;
///     &lt;media:MediaElement x:Name="Player" Source="talk.mp4"&gt;
///         &lt;media:MediaElement.AudioAnalyzer&gt;
///             &lt;speech:SpeechCaptionAnalyzer x:Name="Captions" /&gt;
///         &lt;/media:MediaElement.AudioAnalyzer&gt;
///     &lt;/media:MediaElement&gt;
///     &lt;media:CaptionView Source="{x:Reference Captions}"
///                       Position="{Binding Position, Source={x:Reference Player}}"
///                       VerticalOptions="End" Margin="16,0,16,72" /&gt;
/// &lt;/Grid&gt;
/// </code>
/// </example>
public class CaptionView : ContentView
{
    readonly Border bar;
    readonly Label label;
    IDispatcherTimer? holdTimer;

    public CaptionView()
    {
        this.label = new Label
        {
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap,
            TextColor = Colors.White,
            FontSize = 18
        };
        this.bar = new Border
        {
            StrokeThickness = 0,
            Padding = new Thickness(12, 6),
            BackgroundColor = Color.FromRgba(0, 0, 0, 170),
            HorizontalOptions = LayoutOptions.Center,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            Content = this.label,
            IsVisible = false
        };
        this.Content = this.bar;
        this.InputTransparent = true;
    }

    /// <summary>The captions to show — usually a speech caption analyzer.</summary>
    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source), typeof(ICaptionSource), typeof(CaptionView), null, propertyChanged: OnSourceChanged);

    /// <summary>The media position; bind it to the player's <c>Position</c> for timed captions.</summary>
    public static readonly BindableProperty PositionProperty = BindableProperty.Create(
        nameof(Position), typeof(TimeSpan), typeof(CaptionView), TimeSpan.Zero, propertyChanged: (b, _, _) => ((CaptionView)b).Refresh());

    /// <summary>Timed, live, or decided by the source. Default <see cref="CaptionDisplayMode.Auto"/>.</summary>
    public static readonly BindableProperty ModeProperty = BindableProperty.Create(
        nameof(Mode), typeof(CaptionDisplayMode), typeof(CaptionView), CaptionDisplayMode.Auto, propertyChanged: (b, _, _) => ((CaptionView)b).Refresh());

    /// <summary>In live mode, how long a caption stays up with no new speech. Default 4 seconds.</summary>
    public static readonly BindableProperty LiveHoldTimeProperty = BindableProperty.Create(
        nameof(LiveHoldTime), typeof(TimeSpan), typeof(CaptionView), TimeSpan.FromSeconds(4));

    /// <summary>In live mode, the most characters shown; older text is cut at a word boundary. Default 140.</summary>
    public static readonly BindableProperty MaxLiveCharactersProperty = BindableProperty.Create(
        nameof(MaxLiveCharacters), typeof(int), typeof(CaptionView), 140, propertyChanged: (b, _, _) => ((CaptionView)b).Refresh());

    /// <summary>Caption text color. Default white.</summary>
    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor), typeof(Color), typeof(CaptionView), Colors.White, propertyChanged: (b, _, v) => ((CaptionView)b).label.TextColor = (Color)v);

    /// <summary>Caption font size. Default 18.</summary>
    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
        nameof(FontSize), typeof(double), typeof(CaptionView), 18d, propertyChanged: (b, _, v) => ((CaptionView)b).label.FontSize = (double)v);

    /// <summary>The bar behind the text. Default translucent black; transparent for bare text.</summary>
    public static readonly BindableProperty BarColorProperty = BindableProperty.Create(
        nameof(BarColor), typeof(Color), typeof(CaptionView), Color.FromRgba(0, 0, 0, 170), propertyChanged: (b, _, v) => ((CaptionView)b).bar.BackgroundColor = (Color)v);

    /// <inheritdoc cref="SourceProperty"/>
    public ICaptionSource? Source
    {
        get => (ICaptionSource?)this.GetValue(SourceProperty);
        set => this.SetValue(SourceProperty, value);
    }

    /// <inheritdoc cref="PositionProperty"/>
    public TimeSpan Position
    {
        get => (TimeSpan)this.GetValue(PositionProperty);
        set => this.SetValue(PositionProperty, value);
    }

    /// <inheritdoc cref="ModeProperty"/>
    public CaptionDisplayMode Mode
    {
        get => (CaptionDisplayMode)this.GetValue(ModeProperty);
        set => this.SetValue(ModeProperty, value);
    }

    /// <inheritdoc cref="LiveHoldTimeProperty"/>
    public TimeSpan LiveHoldTime
    {
        get => (TimeSpan)this.GetValue(LiveHoldTimeProperty);
        set => this.SetValue(LiveHoldTimeProperty, value);
    }

    /// <inheritdoc cref="MaxLiveCharactersProperty"/>
    public int MaxLiveCharacters
    {
        get => (int)this.GetValue(MaxLiveCharactersProperty);
        set => this.SetValue(MaxLiveCharactersProperty, value);
    }

    /// <inheritdoc cref="TextColorProperty"/>
    public Color TextColor
    {
        get => (Color)this.GetValue(TextColorProperty);
        set => this.SetValue(TextColorProperty, value);
    }

    /// <inheritdoc cref="FontSizeProperty"/>
    public double FontSize
    {
        get => (double)this.GetValue(FontSizeProperty);
        set => this.SetValue(FontSizeProperty, value);
    }

    /// <inheritdoc cref="BarColorProperty"/>
    public Color BarColor
    {
        get => (Color)this.GetValue(BarColorProperty);
        set => this.SetValue(BarColorProperty, value);
    }

    /// <summary>The text currently shown, or null when the bar is hidden.</summary>
    public string? CurrentText => this.bar.IsVisible ? this.label.Text : null;

    static void OnSourceChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var self = (CaptionView)bindable;
        if (oldValue is ICaptionSource old)
            old.CaptionsChanged -= self.OnCaptionsChanged;
        if (newValue is ICaptionSource next)
            next.CaptionsChanged += self.OnCaptionsChanged;
        self.Refresh();
    }

    void OnCaptionsChanged(object? sender, EventArgs e)
    {
        if (this.Dispatcher.IsDispatchRequired)
            this.Dispatcher.Dispatch(this.OnLiveUpdate);
        else
            this.OnLiveUpdate();
    }

    void OnLiveUpdate()
    {
        this.Refresh();
        if (!this.IsTimedMode())
            this.RestartHold();
    }

    bool IsTimedMode() => this.Mode switch
    {
        CaptionDisplayMode.Timed => true,
        CaptionDisplayMode.Live => false,
        _ => this.Source?.IsTimed == true
    };

    void Refresh()
    {
        var source = this.Source;
        string? text = null;

        if (source is not null)
        {
            text = this.IsTimedMode()
                ? source.Cues.CueAt(this.Position)?.Text
                : this.LiveText(source);
        }

        this.Show(text);
    }

    string? LiveText(ICaptionSource source)
    {
        var cues = source.Cues;
        var last = cues.Count > 0 ? cues[^1].Text : null;
        var partial = source.PartialText;
        var text = (last, partial) switch
        {
            (null, null) => null,
            ({ } l, null) => l,
            (null, { } p) => p,
            ({ } l, { } p) => l + " " + p
        };
        return text is null ? null : Tail(text.Trim(), this.MaxLiveCharacters);
    }

    // keep the newest words: live captions read from the end
    static string Tail(string text, int max)
    {
        if (max <= 0 || text.Length <= max)
            return text;

        var cut = text.IndexOf(' ', text.Length - max);
        return "…" + (cut < 0 ? text[^max..] : text[(cut + 1)..]);
    }

    void Show(string? text)
    {
        var visible = !String.IsNullOrWhiteSpace(text);
        this.label.Text = visible ? text : null;
        this.bar.IsVisible = visible;
    }

    void RestartHold()
    {
        this.holdTimer ??= this.CreateHoldTimer();
        if (this.holdTimer is null)
            return;

        this.holdTimer.Stop();
        this.holdTimer.Interval = this.LiveHoldTime;
        this.holdTimer.Start();
    }

    IDispatcherTimer? CreateHoldTimer()
    {
        var timer = this.Dispatcher?.CreateTimer();
        if (timer is null)
            return null;

        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            if (!this.IsTimedMode())
                this.Show(null);
        };
        return timer;
    }
}
