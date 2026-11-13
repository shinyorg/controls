using System.Collections.ObjectModel;
using Shiny.Maui.Controls.FloatingPanel;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.SignaturePad;

public partial class SignaturePad : ContentView
{
    /// <summary>
    /// Appended to the pad's own <see cref="Element.AutomationId"/> to name the sheet's content. The sheet
    /// is a <see cref="FloatingPanel.FloatingPanel"/>, so it is not under the pad in the tree.
    /// </summary>
    public const string PopupAutomationIdSuffix = "_Popup";

    /// <summary>Names the drawing surface.</summary>
    public const string CanvasAutomationIdSuffix = "_Canvas";

    /// <summary>Names the sign (accept) button.</summary>
    public const string SignAutomationIdSuffix = "_Sign";

    /// <summary>Names the cancel button.</summary>
    public const string CancelAutomationIdSuffix = "_Cancel";

    /// <summary>Names the clear button.</summary>
    public const string ClearAutomationIdSuffix = "_Clear";

    readonly SignatureView signatureView;
    readonly Button signButton;
    readonly Button cancelButton;
    readonly FloatingPanel.FloatingPanel floatingPanel;
    bool isSyncing;

    public SignaturePad()
    {
        // Hide the wrapper when the panel is closed so it doesn't block
        // touches to the page content underneath.
        IsVisible = false;

        // The panel is the frame here, so the surface draws no border of its own.
        signatureView = new SignatureView
        {
            ShowBorder = false,
            CornerRadius = 0,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        signatureView.StrokeCompleted += (_, _) => signButton!.IsEnabled = signatureView.HasSignature;
        signatureView.Cleared += (_, _) => signButton!.IsEnabled = false;

        signButton = new Button
        {
            Text = "Sign",
            IsEnabled = false,
            CornerRadius = 8,
            HorizontalOptions = LayoutOptions.Fill
        }.Neutralize();
        signButton.SetDynamicResource(Button.TextColorProperty, ShinyThemeKeys.Color.OnPrimary);
        this.ApplySignButtonColor(this.SignButtonColor);
        signButton.Clicked += OnSignClicked;

        cancelButton = new Button
        {
            Text = "Cancel",
            CornerRadius = 8,
            HorizontalOptions = LayoutOptions.Fill
        }.Neutralize();
        cancelButton.SetDynamicResource(Button.TextColorProperty, ShinyThemeKeys.Color.OnSecondaryContainer);
        this.ApplyCancelButtonColor(this.CancelButtonColor);
        cancelButton.Clicked += OnCancelClicked;

        // Button bar
        var buttonBar = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10,
            Padding = new Thickness(16, 8, 16, 16)
        };
        buttonBar.Children.Add(cancelButton);
        Grid.SetColumn(cancelButton, 0);
        buttonBar.Children.Add(signButton);
        Grid.SetColumn(signButton, 1);

        var titleLabel = new Label
        {
            Text = "Draw your signature below",
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 12, 0, 4)
        }.WithFontSize(ShinyThemeKeys.Type.BodyLargeSize);

        var contentGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            },
            Padding = new Thickness(16, 0)
        };
        contentGrid.Children.Add(titleLabel);
        Grid.SetRow(titleLabel, 0);
        contentGrid.Children.Add(signatureView);
        Grid.SetRow(signatureView, 1);
        contentGrid.Children.Add(buttonBar);
        Grid.SetRow(buttonBar, 2);

        AutomationIds.Link(this, contentGrid, PopupAutomationIdSuffix);
        AutomationIds.Link(this, signatureView.Surface, CanvasAutomationIdSuffix);
        AutomationIds.Link(this, signButton, SignAutomationIdSuffix);
        AutomationIds.Link(this, cancelButton, CancelAutomationIdSuffix);
        AutomationIds.Link(this, signatureView.ClearButton, ClearAutomationIdSuffix);

        floatingPanel = new FloatingPanel.FloatingPanel
        {
            IsLocked = true,
            HasBackdrop = true,
            CloseOnBackdropTap = false,
            ShowHandle = false,
            IsContentScrollEnabled = false,
            PanelCornerRadius = 16,
            Position = FloatingPanelPosition.Bottom,
            PanelContent = contentGrid,
            Detents = new ObservableCollection<DetentValue> { DetentValue.Half }
        };

        floatingPanel.Opened += (_, _) =>
        {
            // Prevent system navigation gestures (iOS interactive "swipe back"
            // pop; Android edge-swipe back and TabbedPage swipe-between-tabs)
            // from hijacking strokes that start near the screen edges.
            SetBackGestureEnabled(false);

            if (isSyncing) return;
            isSyncing = true;
            SetValue(IsOpenProperty, true);
            isSyncing = false;
        };

        floatingPanel.Closed += (_, _) =>
        {
            SetBackGestureEnabled(true);

            if (isSyncing) return;
            isSyncing = true;
            SetValue(IsOpenProperty, false);
            isSyncing = false;
            IsVisible = false;
            ResetCanvas();
        };

        Content = floatingPanel;

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(SignaturePad));
    }

    void OnSignClicked(object? sender, EventArgs e)
    {
        var stream = signatureView.ExportToPng(ExportWidth, ExportHeight);
        var args = new SignatureImageEventArgs(stream);

        Signed?.Invoke(this, args);
        if (SignCommand?.CanExecute(args) == true)
            SignCommand.Execute(args);

        ResetCanvas();
        IsOpen = false;
        IsVisible = false;
    }

    void OnCancelClicked(object? sender, EventArgs e)
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
        if (CancelCommand?.CanExecute(null) == true)
            CancelCommand.Execute(null);

        ResetCanvas();
        IsOpen = false;
        IsVisible = false;
    }

    void ResetCanvas()
    {
        signatureView.Clear();
        signButton.IsEnabled = false;
    }

    /// <summary>The surface inside the sheet - the same <see cref="SignatureView"/> you can place on a page yourself.</summary>
    internal SignatureView SignatureView => signatureView;

    // Implemented per-platform to suppress system navigation gestures that
    // would otherwise steal edge-started strokes while the pad is open:
    //   iOS     - the navigation controller's interactive "swipe back" pop.
    //   Android - the API 29+ system back edge-swipe (via gesture exclusion
    //             rects) and a hosting TabbedPage's ViewPager2 swipe.
    // No-op on platforms without such gestures (Windows).
    partial void SetBackGestureEnabled(bool enabled);

    void ApplySignButtonColor(Color? explicitColor)
        => ThemeProbe.Tint(signButton, Button.BackgroundColorProperty, explicitColor, ShinyThemeKeys.Color.Primary);

    void ApplyCancelButtonColor(Color? explicitColor)
        => ThemeProbe.Tint(cancelButton, Button.BackgroundColorProperty, explicitColor, ShinyThemeKeys.Color.SecondaryContainer);
}
