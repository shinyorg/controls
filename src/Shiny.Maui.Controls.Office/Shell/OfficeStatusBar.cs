using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Controls.Office.Icons;
using Shiny.Controls.Office.Shell;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// The Office status bar: segments on the left ("Page 1 of 3", "197 words"), and Focus, the three view
/// modes and the zoom control on the right.
/// </summary>
/// <remarks>
/// The editor feeds <see cref="Items"/> — one <see cref="OfficeStatusItem"/> per segment, whose
/// <c>Text</c> it updates as the caret moves — and binds <see cref="Zoom"/> two-way to its own zoom. The
/// zoom slider is the non-linear one from <see cref="OfficeZoomModel"/>, with 100% at its centre.
/// </remarks>
public class OfficeStatusBar : ContentView
{
    readonly ObservableCollection<OfficeStatusItem> items = new();
    readonly HorizontalStackLayout left;
    readonly HorizontalStackLayout viewModes;
    readonly Border focus;
    readonly Border zoomOut;
    readonly Border zoomIn;
    readonly Grid sliderHost;
    readonly Microsoft.Maui.Controls.Slider slider;
    readonly BoxView hundredTick;
    readonly Label percent;
    readonly Border percentButton;
    readonly HorizontalStackLayout zoomGroup;
    readonly Border fitButton;
    readonly Dictionary<OfficeStatusItem, View> itemViews = new();
    readonly List<(OfficeViewMode Mode, Border Button)> modeButtons = [];
    bool syncingSlider;
    OfficeApp? inheritedApp;

    public static readonly BindableProperty AppProperty = BindableProperty.Create(
        nameof(App), typeof(OfficeApp?), typeof(OfficeStatusBar), null,
        propertyChanged: (b, _, _) => ((OfficeStatusBar)b).BuildViewModes());

    public static readonly BindableProperty ViewModesProperty = BindableProperty.Create(
        nameof(ViewModes), typeof(IReadOnlyList<OfficeViewMode>), typeof(OfficeStatusBar), null,
        propertyChanged: (b, _, _) => ((OfficeStatusBar)b).BuildViewModes());

    public static readonly BindableProperty SelectedViewModeProperty = BindableProperty.Create(
        nameof(SelectedViewMode), typeof(string), typeof(OfficeStatusBar), null, BindingMode.TwoWay,
        propertyChanged: (b, _, _) => ((OfficeStatusBar)b).PaintViewModes());

    public static readonly BindableProperty ZoomProperty = BindableProperty.Create(
        nameof(Zoom), typeof(double), typeof(OfficeStatusBar), 1d, BindingMode.TwoWay,
        coerceValue: (b, v) => ((OfficeStatusBar)b).ZoomModel.Clamp((double)v),
        propertyChanged: (b, _, _) => ((OfficeStatusBar)b).OnZoomChanged());

    public static readonly BindableProperty ZoomModelProperty = BindableProperty.Create(
        nameof(ZoomModel), typeof(OfficeZoomModel), typeof(OfficeStatusBar), OfficeZoomModel.Default,
        propertyChanged: (b, _, _) => ((OfficeStatusBar)b).OnZoomChanged());

    public static readonly BindableProperty ShowZoomProperty = BindableProperty.Create(
        nameof(ShowZoom), typeof(bool), typeof(OfficeStatusBar), true,
        propertyChanged: (b, _, _) => ((OfficeStatusBar)b).ApplyVisibility());

    public static readonly BindableProperty ShowZoomSliderProperty = BindableProperty.Create(
        nameof(ShowZoomSlider), typeof(bool), typeof(OfficeStatusBar), true,
        propertyChanged: (b, _, _) => ((OfficeStatusBar)b).ApplyVisibility());

    public static readonly BindableProperty ShowFocusProperty = BindableProperty.Create(
        nameof(ShowFocus), typeof(bool), typeof(OfficeStatusBar), true,
        propertyChanged: (b, _, _) => ((OfficeStatusBar)b).ApplyVisibility());

    public static readonly BindableProperty ShowViewModesProperty = BindableProperty.Create(
        nameof(ShowViewModes), typeof(bool), typeof(OfficeStatusBar), true,
        propertyChanged: (b, _, _) => ((OfficeStatusBar)b).ApplyVisibility());

    public static readonly BindableProperty IsCompactProperty = BindableProperty.Create(
        nameof(IsCompact), typeof(bool), typeof(OfficeStatusBar), false,
        propertyChanged: (b, _, _) => ((OfficeStatusBar)b).ApplyVisibility());

    public static readonly BindableProperty ShowFitToWindowProperty = BindableProperty.Create(
        nameof(ShowFitToWindow), typeof(bool), typeof(OfficeStatusBar), false,
        propertyChanged: (b, _, _) => ((OfficeStatusBar)b).ApplyVisibility());

    public static readonly BindableProperty IsFittedProperty = BindableProperty.Create(
        nameof(IsFitted), typeof(bool), typeof(OfficeStatusBar), false,
        propertyChanged: (b, _, _) => ((OfficeStatusBar)b).PaintFit());

    public static readonly BindableProperty PageWidthProperty = BindableProperty.Create(nameof(PageWidth), typeof(double), typeof(OfficeStatusBar), 0d);
    public static readonly BindableProperty PageHeightProperty = BindableProperty.Create(nameof(PageHeight), typeof(double), typeof(OfficeStatusBar), 0d);
    public static readonly BindableProperty TextWidthProperty = BindableProperty.Create(nameof(TextWidth), typeof(double), typeof(OfficeStatusBar), 0d);
    public static readonly BindableProperty ViewportWidthProperty = BindableProperty.Create(nameof(ViewportWidth), typeof(double), typeof(OfficeStatusBar), 0d);
    public static readonly BindableProperty ViewportHeightProperty = BindableProperty.Create(nameof(ViewportHeight), typeof(double), typeof(OfficeStatusBar), 0d);


    public OfficeStatusBar()
    {
        this.HeightRequest = 28;
        this.SetDynamicResource(BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainer);

        this.left = new HorizontalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center, Padding = new Thickness(8, 0) };

        this.focus = ShellChrome.TextButton("Focus", () => this.FocusRequested?.Invoke(this, EventArgs.Empty), OfficeShellIcon.Focus, null, out _);
        this.focus.Padding = new Thickness(6, 2);

        this.viewModes = new HorizontalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center };

        this.zoomOut = ShellChrome.IconButton(OfficeShellIcon.Minus, "Zoom out", this.ZoomOut, size: 12);
        this.zoomIn = ShellChrome.IconButton(OfficeShellIcon.Plus, "Zoom in", this.ZoomIn, size: 12);
        this.zoomOut.Padding = this.zoomIn.Padding = new Thickness(4);

        this.slider = new Microsoft.Maui.Controls.Slider { Minimum = 0, Maximum = 1, WidthRequest = 110, VerticalOptions = LayoutOptions.Center };
        this.slider.SetDynamicResource(Microsoft.Maui.Controls.Slider.MinimumTrackColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        this.slider.SetDynamicResource(Microsoft.Maui.Controls.Slider.MaximumTrackColorProperty, ShinyThemeKeys.Color.OutlineVariant);
        this.slider.SetDynamicResource(Microsoft.Maui.Controls.Slider.ThumbColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        this.slider.ValueChanged += (_, e) =>
        {
            if (!this.syncingSlider)
                this.SetZoomFromSlider(e.NewValue);
        };

        // The notch at 100% — a hint for where the slider snaps, Office's own mark.
        this.hundredTick = new BoxView
        {
            WidthRequest = 1,
            HeightRequest = 8,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };
        this.hundredTick.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        this.sliderHost = new Grid { WidthRequest = 110, VerticalOptions = LayoutOptions.Center };
        this.sliderHost.Children.Add(this.hundredTick);
        this.sliderHost.Children.Add(this.slider);

        this.percent = ShellChrome.Text("100%", 12, ShinyThemeKeys.Color.OnSurfaceVariant);
        this.percent.MinimumWidthRequest = 40;
        this.percent.HorizontalTextAlignment = TextAlignment.End;
        this.percentButton = new Border
        {
            Content = this.percent,
            Padding = new Thickness(6, 2),
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = 4 }
        };
        ShellChrome.Hint(this.percentButton, "Zoom level");
        ShellChrome.OnTap(this.percentButton, this.OpenZoomDialog);

        this.zoomGroup = new HorizontalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center };
        this.zoomGroup.Children.Add(this.zoomOut);
        this.zoomGroup.Children.Add(this.sliderHost);
        this.zoomGroup.Children.Add(this.zoomIn);
        this.zoomGroup.Children.Add(this.percentButton);

        // PowerPoint's "Fit slide to current window". Built with the bar and shown by IsVisible.
        this.fitButton = ShellChrome.IconButton(OfficeShellIcon.FitToWindow, "Fit slide to current window", this.FitToWindow, size: 14);
        this.fitButton.Padding = new Thickness(5, 3);
        this.fitButton.AutomationId = "OfficeStatusFitToWindow";
        this.zoomGroup.Children.Add(this.fitButton);

        var right = new HorizontalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center, Padding = new Thickness(0, 0, 8, 0) };
        right.Children.Add(this.focus);
        right.Children.Add(this.viewModes);
        right.Children.Add(this.zoomGroup);

        var grid = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        // Clipped: the segments are a stack, which lays out at its full width whatever its column is
        // given, so on a phone "English (United States)" ran on underneath the zoom buttons.
        var leftClip = new Grid { IsClippedToBounds = true, VerticalOptions = LayoutOptions.Fill };
        leftClip.Add(this.left);
        grid.Add(leftClip, 0);
        grid.Add(right, 1);

        var root = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        root.Add(ShellChrome.Rule(), 0, 0);
        root.Add(grid, 0, 1);
        this.Content = root;

        this.items.CollectionChanged += this.OnItemsChanged;
        this.BuildViewModes();
        this.OnZoomChanged();
        this.ApplyVisibility();
    }


    /// <summary>Which app's view modes to offer. Null inherits from an enclosing <see cref="OfficeShell"/>, else Word.</summary>
    public OfficeApp? App
    {
        get => (OfficeApp?)this.GetValue(AppProperty);
        set => this.SetValue(AppProperty, value);
    }

    /// <summary>The left-hand segments.</summary>
    public IList<OfficeStatusItem> Items => this.items;

    /// <summary>The view mode buttons. Null uses the app's three.</summary>
    public IReadOnlyList<OfficeViewMode>? ViewModes
    {
        get => (IReadOnlyList<OfficeViewMode>?)this.GetValue(ViewModesProperty);
        set => this.SetValue(ViewModesProperty, value);
    }

    /// <summary>The pressed view mode's <see cref="OfficeViewMode.Id"/>. Two-way.</summary>
    public string? SelectedViewMode
    {
        get => (string?)this.GetValue(SelectedViewModeProperty);
        set => this.SetValue(SelectedViewModeProperty, value);
    }

    /// <summary>The zoom factor (1 = 100%). Two-way; held inside <see cref="ZoomModel"/>'s range.</summary>
    public double Zoom
    {
        get => (double)this.GetValue(ZoomProperty);
        set => this.SetValue(ZoomProperty, value);
    }

    public OfficeZoomModel ZoomModel
    {
        get => (OfficeZoomModel)this.GetValue(ZoomModelProperty);
        set => this.SetValue(ZoomModelProperty, value);
    }

    public bool ShowZoom
    {
        get => (bool)this.GetValue(ShowZoomProperty);
        set => this.SetValue(ShowZoomProperty, value);
    }

    public bool ShowZoomSlider
    {
        get => (bool)this.GetValue(ShowZoomSliderProperty);
        set => this.SetValue(ShowZoomSliderProperty, value);
    }

    public bool ShowFocus
    {
        get => (bool)this.GetValue(ShowFocusProperty);
        set => this.SetValue(ShowFocusProperty, value);
    }

    public bool ShowViewModes
    {
        get => (bool)this.GetValue(ShowViewModesProperty);
        set => this.SetValue(ShowViewModesProperty, value);
    }

    /// <summary>PowerPoint's "Fit slide to current window" button after the percentage. Off by default.</summary>
    public bool ShowFitToWindow
    {
        get => (bool)this.GetValue(ShowFitToWindowProperty);
        set => this.SetValue(ShowFitToWindowProperty, value);
    }

    /// <summary>Draws the fit button pressed: the zoom is following the window rather than a set percentage.</summary>
    public bool IsFitted
    {
        get => (bool)this.GetValue(IsFittedProperty);
        set => this.SetValue(IsFittedProperty, value);
    }

    /// <summary>Phone width: the slider and view modes give way. Set by <see cref="OfficeShell"/>.</summary>
    public bool IsCompact
    {
        get => (bool)this.GetValue(IsCompactProperty);
        set => this.SetValue(IsCompactProperty, value);
    }

    /// <summary>The page's width at 100%, for the zoom dialog's fit presets. Same unit as the viewport.</summary>
    public double PageWidth { get => (double)this.GetValue(PageWidthProperty); set => this.SetValue(PageWidthProperty, value); }
    public double PageHeight { get => (double)this.GetValue(PageHeightProperty); set => this.SetValue(PageHeightProperty, value); }
    public double TextWidth { get => (double)this.GetValue(TextWidthProperty); set => this.SetValue(TextWidthProperty, value); }
    public double ViewportWidth { get => (double)this.GetValue(ViewportWidthProperty); set => this.SetValue(ViewportWidthProperty, value); }
    public double ViewportHeight { get => (double)this.GetValue(ViewportHeightProperty); set => this.SetValue(ViewportHeightProperty, value); }


    /// <summary>A clickable segment was pressed — "Page 1 of 3" opens Go To in Word.</summary>
    public event EventHandler<OfficeStatusItem>? ItemClicked;

    /// <summary>The Focus button was pressed. <see cref="OfficeShell"/> answers it by toggling focus mode.</summary>
    public event EventHandler? FocusRequested;

    public event EventHandler<OfficeViewMode>? ViewModeChanged;

    /// <summary>The fit button was pressed. The host works out the zoom and feeds it back through <see cref="Zoom"/>.</summary>
    public event EventHandler? FitToWindowRequested;

    /// <summary>The percentage was pressed outside an <see cref="OfficeShell"/>, which would otherwise show its zoom dialog.</summary>
    public event EventHandler? ZoomDialogRequested;


    internal OfficeApp EffectiveApp => this.App ?? this.inheritedApp ?? OfficeApp.Word;

    internal void InheritApp(OfficeApp app)
    {
        this.inheritedApp = app;
        this.BuildViewModes();
    }


    /// <summary>Presses the fit button. Test seam.</summary>
    public void FitToWindow() => this.FitToWindowRequested?.Invoke(this, EventArgs.Empty);

    public void ZoomIn() => this.Zoom = this.ZoomModel.StepZoom(this.Zoom, 1);

    public void ZoomOut() => this.Zoom = this.ZoomModel.StepZoom(this.Zoom, -1);

    /// <summary>Sets the zoom from a slider position (0–1), snapping to 100% when close.</summary>
    public void SetZoomFromSlider(double position) => this.Zoom = this.ZoomModel.FromSlider(position);


    /// <summary>Opens the Zoom dialog — the enclosing shell's, or raises <see cref="ZoomDialogRequested"/>.</summary>
    public void OpenZoomDialog()
    {
        if (ShellChrome.Ancestor<OfficeShell>(this) is { } shell)
            shell.ShowZoomDialog(this);
        else
            this.ZoomDialogRequested?.Invoke(this, EventArgs.Empty);
    }


    public void SelectViewMode(string id)
    {
        var mode = this.EffectiveViewModes.FirstOrDefault(x => x.Id == id);
        if (mode is null)
            return;

        this.SelectedViewMode = id;
        this.ViewModeChanged?.Invoke(this, mode);
    }


    /// <summary>Presses a segment. Test seam.</summary>
    public void ClickItem(OfficeStatusItem item)
    {
        if (item.IsClickable)
            this.ItemClicked?.Invoke(this, item);
    }


    IReadOnlyList<OfficeViewMode> EffectiveViewModes => this.ViewModes ?? OfficeViewModes.For(this.EffectiveApp);


    void OnZoomChanged()
    {
        if (this.percent is null)
            return;

        this.percent.Text = OfficeZoomModel.Format(this.Zoom);

        this.syncingSlider = true;
        this.slider.Value = this.ZoomModel.ToSlider(this.Zoom);
        this.syncingSlider = false;

        // Place the notch where 100% lands on the track; the track insets about 8px each side.
        var at = this.ZoomModel.ToSlider(1);
        this.hundredTick.HorizontalOptions = LayoutOptions.Start;
        this.hundredTick.Margin = new Thickness(8 + ((this.sliderHost.WidthRequest - 16) * at), 0, 0, 0);
    }


    void BuildViewModes()
    {
        if (this.viewModes is null)
            return;

        // Rebuilt only when the app or the mode list changes, which a host does before first layout.
        this.viewModes.Children.Clear();
        this.modeButtons.Clear();

        foreach (var mode in this.EffectiveViewModes)
        {
            var m = mode;
            var button = ShellChrome.IconButton(mode.Icon, mode.Text, () => this.SelectViewMode(m.Id), size: 14);
            button.Padding = new Thickness(5, 3);
            this.modeButtons.Add((mode, button));
            this.viewModes.Children.Add(button);
        }

        if (this.SelectedViewMode is null && this.EffectiveViewModes.Count > 0)
            this.SelectedViewMode = OfficeViewModes.DefaultId(this.EffectiveApp);

        this.PaintViewModes();
    }


    void PaintViewModes()
    {
        foreach (var (mode, button) in this.modeButtons)
        {
            if (mode.Id == this.SelectedViewMode)
                button.SetDynamicResource(BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHighest);
            else
            {
                button.RemoveDynamicResource(BackgroundColorProperty);
                button.BackgroundColor = Colors.Transparent;
            }
        }
    }


    void PaintFit()
    {
        if (this.fitButton is null)
            return;

        if (this.IsFitted)
            this.fitButton.SetDynamicResource(BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHighest);
        else
        {
            this.fitButton.RemoveDynamicResource(BackgroundColorProperty);
            this.fitButton.BackgroundColor = Colors.Transparent;
        }
    }


    void ApplyVisibility()
    {
        if (this.focus is null)
            return;

        this.focus.IsVisible = this.ShowFocus && !this.IsCompact;
        this.viewModes.IsVisible = this.ShowViewModes && !this.IsCompact;
        this.zoomGroup.IsVisible = this.ShowZoom;
        this.sliderHost.IsVisible = this.ShowZoomSlider && !this.IsCompact;
        this.fitButton.IsVisible = this.ShowFitToWindow;
        this.PaintFit();
    }


    void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Segments normally arrive once, before layout; a rebuild after that is a rare structural edit
        // (the AppKit head may not paint segments added late — keep the list stable and change Text).
        foreach (var (item, _) in this.itemViews)
            item.PropertyChanged -= this.OnItemChanged;

        this.itemViews.Clear();
        this.left.Children.Clear();

        foreach (var item in this.items)
        {
            var view = this.BuildItem(item);
            this.itemViews[item] = view;
            item.PropertyChanged += this.OnItemChanged;
            this.left.Children.Add(view);
        }
    }


    View BuildItem(OfficeStatusItem item)
    {
        var label = ShellChrome.Text(item.Text, 12, ShinyThemeKeys.Color.OnSurfaceVariant);
        var border = new Border
        {
            Content = label,
            Padding = new Thickness(8, 2),
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            IsVisible = item.IsVisible,
            StrokeShape = new RoundRectangle { CornerRadius = 4 }
        };
        ShellChrome.Hint(border, item.Tooltip ?? item.Text);
        ShellChrome.OnTap(border, () => this.ClickItem(item));
        return border;
    }


    void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not OfficeStatusItem item || !this.itemViews.TryGetValue(item, out var view) || view is not Border border)
            return;

        border.IsVisible = item.IsVisible;
        if (border.Content is Label label)
            label.Text = item.Text;

        ShellChrome.Hint(border, item.Tooltip ?? item.Text);
    }
}
