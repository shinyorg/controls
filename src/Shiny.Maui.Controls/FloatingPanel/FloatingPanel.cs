using System.Collections.ObjectModel;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.FloatingPanel;

[ContentProperty(nameof(PanelContent))]
public partial class FloatingPanel : ContentView
{
    const double DefaultAnimationDuration = 250;
    const double DragHandleHeight = 30;

    readonly Border sheetContainer;
    readonly Grid sheetInnerGrid;
    readonly Grid dragHandleContainer;
    readonly BoxView dragHandle;
    readonly ScrollView scrollView;
    readonly ContentView contentHost;
    readonly ContentView headerHost;
    readonly BoxView safeAreaFill;
    readonly PanGestureRecognizer panGesture;

    double panStartHeight;
    bool isAnimating;
    int currentDetentIndex;
    int detentIndexBeforeKeyboard = -1;
    bool isKeyboardVisible;
    DetentValue? fitContentDetent;

    bool IsBottom => Position is FloatingPanelPosition.Bottom or FloatingPanelPosition.BottomTabs;

    double GetAvailableHeight()
    {
        var host = GetOverlayHost();
        if (host is not null && host.Height > 0)
            return host.Height;
        if (Window?.Page is Page page && page.Height > 0)
            return page.Height;
        return 800;
    }

    double GetBottomSafeAreaInset()
    {
#if IOS || MACCATALYST
        // Try the handler's native view first — most reliable after layout
        if (Handler?.PlatformView is UIKit.UIView nativeView && nativeView.Window is UIKit.UIWindow nativeWindow)
            return nativeWindow.SafeAreaInsets.Bottom;

        // Fallback via UIApplication
        var window = UIKit.UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIKit.UIWindowScene>()
            .SelectMany(s => s.Windows)
            .FirstOrDefault();
        return window?.SafeAreaInsets.Bottom ?? 0;
#else
        return 0;
#endif
    }

    public FloatingPanel()
    {
        IsVisible = false;

        // Drag handle
        dragHandle = new BoxView
        {
            HeightRequest = 4,
            WidthRequest = 40,
            CornerRadius = 2,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 10, 0, 10)
        };

        dragHandleContainer = new Grid
        {
            HeightRequest = DragHandleHeight,
            BackgroundColor = Colors.Transparent
        };
        dragHandleContainer.Children.Add(dragHandle);

        // Pan gesture on handle only
        panGesture = new PanGestureRecognizer();
        panGesture.PanUpdated += OnPanUpdated;
        dragHandleContainer.GestureRecognizers.Add(panGesture);

        // Content host inside scroll view
        contentHost = new ContentView();
        scrollView = new ScrollView
        {
            Content = contentHost
        };

        // Header host
        headerHost = new ContentView();
        var headerTap = new TapGestureRecognizer();
        headerTap.Tapped += OnHeaderTapped;
        headerHost.GestureRecognizers.Add(headerTap);

        // Safe area fill — sits below the Border, colored to match the header
        safeAreaFill = new BoxView
        {
            HeightRequest = 0,
            IsVisible = false,
            Color = Colors.Transparent
        };

        // Inner grid — layout set by UpdateLayoutForPosition
        sheetInnerGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            }
        };
        sheetInnerGrid.Children.Add(dragHandleContainer);
        sheetInnerGrid.Children.Add(scrollView);
        sheetInnerGrid.Children.Add(headerHost);

        // Sheet container with rounded corners
        sheetContainer = new Border
        {
            // All four corners take the theme's radius rather than the top two: the bottom edge sits
            // under safeAreaFill at every detent, so the lower corners are never on screen anyway, and
            // a single CornerRadius resource can be resolved as one value.
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle()
                .WithCornerRadius(ShinyThemeKeys.Shape.CornerLargeRadius),
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            Content = sheetInnerGrid
        };
        Tint(dragHandle, BoxView.ColorProperty, this.HandleColor, ShinyThemeKeys.Color.OutlineVariant);
        Tint(sheetContainer, VisualElement.BackgroundColorProperty, this.PanelBackgroundColor, ShinyThemeKeys.Color.SurfaceContainerLow);

        // Outer grid: Border on top, safeAreaFill below
        var outerGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            }
        };
        outerGrid.Children.Add(sheetContainer);
        Grid.SetRow(sheetContainer, 0);
        outerGrid.Children.Add(safeAreaFill);
        Grid.SetRow(safeAreaFill, 1);

        Content = outerGrid;

        // Default layout
        UpdateLayoutForPosition();

        // Default detents
        Detents = new ObservableCollection<DetentValue>
        {
            DetentValue.Quarter,
            DetentValue.Half,
            DetentValue.Full
        };

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(FloatingPanel));
    }

    View ContentRowView => IsContentScrollEnabled ? scrollView : contentHost;

    /// <summary>
    /// Shows or hides the panel body — whichever view is in the content row.
    /// </summary>
    /// <remarks>
    /// ⚠️ Both are set, never just the ScrollView. With <see cref="IsContentScrollEnabled"/> off the row
    /// holds <c>contentHost</c> directly and the ScrollView is not in the tree at all, so hiding only the
    /// ScrollView left the whole body on screen under a closed panel's peeking header — covering the page
    /// and swallowing every touch meant for it. Setting both also means toggling
    /// <see cref="IsContentScrollEnabled"/> while closed swaps in a view that is already hidden.
    /// </remarks>
    void SetContentVisible(bool visible)
    {
        scrollView.IsVisible = visible;
        contentHost.IsVisible = visible;
    }

    /// <summary>
    /// Stops whatever height animation is running so a new one can start from where it left off.
    /// </summary>
    /// <remarks>
    /// Open and close used to return early while another animation ran, so an <see cref="IsOpen"/> flipped
    /// mid-animation (a quick tap on the header then the backdrop, or a command closing a panel that was
    /// still opening) was simply dropped: <see cref="IsOpen"/> said closed while the panel sat open over
    /// the page. The finished callbacks check <see cref="IsOpen"/> for the same reason — an aborted
    /// animation must not apply its end state over the one that replaced it.
    /// </remarks>
    void StopAnimations()
    {
        this.AbortAnimation("OpenPanel");
        this.AbortAnimation("ClosePanel");
        this.AbortAnimation("SnapPanel");
        isAnimating = false;
    }

    void UpdateScrollEnabled(bool enabled)
    {
        var oldView = enabled ? (View)contentHost : scrollView;
        var newView = enabled ? (View)scrollView : contentHost;

        if (!sheetInnerGrid.Children.Contains(oldView))
            return;

        var row = Grid.GetRow(oldView);
        sheetInnerGrid.Children.Remove(oldView);

        if (enabled)
            scrollView.Content = contentHost;
        else
            scrollView.Content = null;

        sheetInnerGrid.Children.Add(newView);
        Grid.SetRow(newView, row);
    }

    bool safeAreaApplied;

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler is null || safeAreaApplied)
            return;

        // Defer so UIKit has computed safe area insets
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100), () =>
        {
            if (safeAreaApplied) return;

            var inset = GetBottomSafeAreaInset();
            if (inset <= 0) return;

            safeAreaApplied = true;
            if (IsOpen)
                ApplyBottomSafeAreaExtension();
            else
                UpdateClosedState();
        });
    }

    void UpdateLayoutForPosition()
    {
        var contentView = ContentRowView;
        if (IsBottom)
        {
            // Bottom: handle, header, content
            VerticalOptions = LayoutOptions.End;
            Grid.SetRow(dragHandleContainer, 0);
            Grid.SetRow(headerHost, 1);
            Grid.SetRow(contentView, 2);

            sheetInnerGrid.RowDefinitions[0] = new RowDefinition(GridLength.Auto);
            sheetInnerGrid.RowDefinitions[1] = new RowDefinition(GridLength.Auto);
            sheetInnerGrid.RowDefinitions[2] = new RowDefinition(GridLength.Star);
        }
        else
        {
            // Top: content, header, handle
            VerticalOptions = LayoutOptions.Start;
            Grid.SetRow(contentView, 0);
            Grid.SetRow(headerHost, 1);
            Grid.SetRow(dragHandleContainer, 2);

            sheetInnerGrid.RowDefinitions[0] = new RowDefinition(GridLength.Star);
            sheetInnerGrid.RowDefinitions[1] = new RowDefinition(GridLength.Auto);
            sheetInnerGrid.RowDefinitions[2] = new RowDefinition(GridLength.Auto);
        }

        // BottomTabs: clip so content doesn't bleed under the tab bar
        IsClippedToBounds = Position == FloatingPanelPosition.BottomTabs;

        UpdateCornerRadius(PanelCornerRadius);
        UpdateClosedState();
    }

    void UpdateCornerRadius(double radius)
    {
        var corners = IsBottom
            ? new CornerRadius(radius, radius, 0, 0)
            : new CornerRadius(0, 0, radius, radius);
        sheetContainer.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
        {
            CornerRadius = corners
        };
    }

    /// <summary>
    /// Applies negative bottom margin so the panel extends past the OverlayHost into the
    /// iOS safe area, and adds matching bottom padding so content stays above the home indicator.
    /// The Border background color fills the gap seamlessly.
    /// </summary>
    void ApplyBottomSafeAreaExtension()
    {
        if (Position == FloatingPanelPosition.Bottom)
        {
            var bottomInset = GetBottomSafeAreaInset();
            if (bottomInset > 0)
            {
                Margin = new Thickness(0, 0, 0, -bottomInset);
                safeAreaFill.HeightRequest = bottomInset;
                // sheetContainer.BackgroundColor already reflects PanelBackgroundColor (via Tint()),
                // with SurfaceContainerLow as its theme fallback. Transparent covers the rest.
                safeAreaFill.Color = (IsOpen
                    ? sheetContainer.BackgroundColor
                    : HeaderBackgroundColor ?? sheetContainer.BackgroundColor) ?? Colors.Transparent;
                safeAreaFill.IsVisible = true;
                return;
            }
        }

        Margin = new Thickness(0);
        safeAreaFill.HeightRequest = 0;
        safeAreaFill.IsVisible = false;
    }

    void UpdateClosedState()
    {
        if (IsOpen) return;

        if (ShowHeaderWhenClosed && HeaderTemplate != null)
        {
            // Show just the header at the edge
            IsVisible = true;
            SetContentVisible(false);
            dragHandleContainer.IsVisible = false;
            HeightRequest = -1; // auto-size to header
            ApplyBottomSafeAreaExtension();
        }
        else
        {
            IsVisible = false;
            SetContentVisible(false);
            Margin = new Thickness(0);
            sheetContainer.Padding = new Thickness(0);
        }
    }

    void OnIsLockedChanged(bool locked)
    {
        dragHandle.IsVisible = !locked;
        if (locked)
            dragHandleContainer.GestureRecognizers.Remove(panGesture);
        else if (!dragHandleContainer.GestureRecognizers.Contains(panGesture))
            dragHandleContainer.GestureRecognizers.Add(panGesture);
    }

    OverlayHost? GetOverlayHost()
    {
        Element? current = Parent;
        while (current is not null)
        {
            if (current is OverlayHost host)
                return host;
            current = current.Parent;
        }
        return null;
    }

    #region Open / Close

    async Task OpenAsync()
    {
        var overlayHost = GetOverlayHost()
            ?? throw new InvalidOperationException(
                "FloatingPanel must be placed inside an OverlayHost or ShinyContentPage. " +
                "Wrap your page content and panels in an OverlayHost, or use ShinyContentPage as your page base class.");

        StopAnimations();
        isAnimating = true;

        IsVisible = true;
        ApplyBottomSafeAreaExtension();
        SetContentVisible(true);
        dragHandleContainer.IsVisible = ShowHandle;

        var available = GetAvailableHeight();

        // Compute fit-content detent if enabled
        if (FitContent && contentHost.Content is View fitView)
        {
            var measured = fitView.Measure(Width > 0 ? Width : 400, double.PositiveInfinity);
            var contentHeight = measured.Height + DragHandleHeight + 20;
            if (headerHost.Content is View headerView)
                contentHeight += headerView.Measure(Width > 0 ? Width : 400, double.PositiveInfinity).Height;
            var ratio = Math.Clamp(contentHeight / available, 0.1, 1.0);
            fitContentDetent = new DetentValue(ratio);
        }
        else
        {
            fitContentDetent = null;
        }

        // Animate to first detent
        currentDetentIndex = 0;
        var sortedDetents = GetSortedDetents();
        var targetHeight = sortedDetents[currentDetentIndex].Ratio * available;

        // Start from current height (header-only or 0)
        var startHeight = Height > 0 ? Height : 0;

        // Show backdrop
        if (HasBackdrop)
            overlayHost.ShowBackdrop(this, (uint)AnimationDuration);

        var animation = new Animation(v => HeightRequest = v, startHeight, targetHeight);
        animation.Commit(this, "OpenPanel", length: (uint)AnimationDuration, easing: Easing.CubicOut,
            finished: (_, _) =>
            {
                // Closed again before this finished: the close owns the panel now.
                if (!IsOpen)
                    return;

                isAnimating = false;

                if (UseFeedback)
                    FeedbackHelper.Execute(this, nameof(Opened));

                Opened?.Invoke(this, EventArgs.Empty);
            });
    }

    async Task CloseAsync()
    {
        StopAnimations();
        isAnimating = true;

        detentIndexBeforeKeyboard = -1;
        isKeyboardVisible = false;

        // Hide backdrop
        if (HasBackdrop)
            GetOverlayHost()?.HideBackdrop(this, (uint)AnimationDuration);

        var showHeader = ShowHeaderWhenClosed && HeaderTemplate != null;
        double targetHeight;

        if (showHeader)
        {
            // Measure header to know what height to collapse to
            var headerMeasured = headerHost.Content?.Measure(Width > 0 ? Width : 400, double.PositiveInfinity);
            targetHeight = headerMeasured?.Height ?? 0;
        }
        else
        {
            targetHeight = 0;
        }

        var startHeight = HeightRequest > 0 ? HeightRequest : Height;
        if (startHeight <= 0) startHeight = targetHeight;

        var animation = new Animation(v => HeightRequest = v, startHeight, targetHeight);
        animation.Commit(this, "ClosePanel", length: (uint)AnimationDuration, easing: Easing.CubicIn,
            finished: (_, _) =>
            {
                // Opened again before this finished: the open owns the panel now.
                if (IsOpen)
                    return;

                isAnimating = false;

                if (showHeader)
                {
                    SetContentVisible(false);
                    dragHandleContainer.IsVisible = false;
                    HeightRequest = -1; // auto-size to header
                    ApplyBottomSafeAreaExtension();
                }
                else
                {
                    IsVisible = false;
                    SetContentVisible(false);
                    HeightRequest = -1;
                    Margin = new Thickness(0);
                    sheetContainer.Padding = new Thickness(0);
                }

                if (UseFeedback)
                    FeedbackHelper.Execute(this, nameof(Closed));

                Closed?.Invoke(this, EventArgs.Empty);
            });
    }

    void OnHeaderTapped(object? sender, TappedEventArgs e)
    {
        // Header tap opens the panel, but never closes a locked panel
        if (!IsOpen)
            IsOpen = true;
        else if (!IsLocked)
            IsOpen = false;
    }

    #endregion

    #region Pan Gesture (Handle Only)

    void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (isAnimating || IsLocked || !IsOpen) return;

        var available = GetAvailableHeight();

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                panStartHeight = HeightRequest > 0 ? HeightRequest : Height;
                this.AbortAnimation("OpenPanel");
                this.AbortAnimation("ClosePanel");
                this.AbortAnimation("SnapPanel");
                break;

            case GestureStatus.Running:
                // For bottom panel: dragging up (negative TotalY) = increase height
                // For top panel: dragging down (positive TotalY) = increase height
                var delta = IsBottom ? -e.TotalY : e.TotalY;
                var newHeight = panStartHeight + delta;

                var sortedDetents = GetSortedDetents();
                var minHeight = sortedDetents.First().Ratio * available * 0.8; // allow slight undershoot
                var maxHeight = sortedDetents.Last().Ratio * available * 1.05; // allow slight overshoot
                newHeight = Math.Clamp(newHeight, minHeight, maxHeight);

                HeightRequest = newHeight;
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                SnapToNearestDetent(e.TotalY);
                break;
        }
    }

    void SnapToNearestDetent(double totalPanY)
    {
        isAnimating = true;
        var available = GetAvailableHeight();
        var currentHeight = HeightRequest > 0 ? HeightRequest : Height;
        var sortedDetents = GetSortedDetents();

        // Determine swipe direction
        bool swipeDismiss, swipeExpand;
        if (IsBottom)
        {
            swipeDismiss = totalPanY > 50;  // dragging down
            swipeExpand = totalPanY < -50;  // dragging up
        }
        else
        {
            swipeDismiss = totalPanY < -50; // dragging up
            swipeExpand = totalPanY > 50;   // dragging down
        }

        // If swiped to dismiss (below lowest detent threshold) — locked panels cannot be dismissed
        var lowestHeight = sortedDetents.First().Ratio * available;
        if (!IsLocked && swipeDismiss && currentHeight < lowestHeight * 0.9)
        {
            isAnimating = false;
            SetValue(IsOpenProperty, false);
            return;
        }

        // Find closest detent, biased by swipe direction
        var bestDetent = sortedDetents.First();
        var bestDistance = double.MaxValue;
        var bestIndex = 0;

        for (var i = 0; i < sortedDetents.Count; i++)
        {
            var detentHeight = sortedDetents[i].Ratio * available;
            var distance = Math.Abs(currentHeight - detentHeight);

            if (swipeExpand && detentHeight > currentHeight)
                distance *= 0.5; // bias toward expanding
            else if (swipeDismiss && detentHeight < currentHeight)
                distance *= 0.5; // bias toward collapsing

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestDetent = sortedDetents[i];
                bestIndex = i;
            }
        }

        currentDetentIndex = bestIndex;
        var targetHeight = bestDetent.Ratio * available;

        var animation = new Animation(v => HeightRequest = v, currentHeight, targetHeight);
        animation.Commit(this, "SnapPanel", length: (uint)(AnimationDuration * 0.75), easing: Easing.CubicOut,
            finished: (_, _) =>
            {
                isAnimating = false;

                if (UseFeedback)
                    FeedbackHelper.Execute(this, nameof(DetentChanged));

                DetentChanged?.Invoke(this, bestDetent);
            });
    }

    #endregion

    #region Detents

    List<DetentValue> GetSortedDetents()
    {
        if (fitContentDetent.HasValue)
            return new List<DetentValue> { fitContentDetent.Value };

        var list = Detents?.OrderBy(d => d.Ratio).ToList()
            ?? new List<DetentValue> { DetentValue.Half };
        return list;
    }

    public async Task AnimateToDetentAsync(DetentValue detent)
    {
        if (isAnimating) return;
        isAnimating = true;

        var available = GetAvailableHeight();
        var targetHeight = detent.Ratio * available;
        var currentHeight = HeightRequest > 0 ? HeightRequest : Height;

        var sortedDetents = GetSortedDetents();
        currentDetentIndex = sortedDetents.FindIndex(d => Math.Abs(d.Ratio - detent.Ratio) < 0.001);
        if (currentDetentIndex < 0) currentDetentIndex = 0;

        var animation = new Animation(v => HeightRequest = v, currentHeight, targetHeight);
        animation.Commit(this, "SnapPanel", length: (uint)AnimationDuration, easing: Easing.CubicOut,
            finished: (_, _) =>
            {
                isAnimating = false;
                DetentChanged?.Invoke(this, detent);
            });
    }

    #endregion

    #region Keyboard Handling

    void HookInputViews(View view)
    {
        if (view is InputView input)
        {
            input.Focused += OnInputFocused;
            input.Unfocused += OnInputUnfocused;
        }

        if (view is Layout layout)
        {
            foreach (var child in layout.Children.OfType<View>())
                HookInputViews(child);
        }
        else if (view is ContentView cv && cv.Content is not null)
        {
            HookInputViews(cv.Content);
        }
        else if (view is ScrollView sv && sv.Content is View svContent)
        {
            HookInputViews(svContent);
        }
        else if (view is Border b && b.Content is View bContent)
        {
            HookInputViews(bContent);
        }
    }

    void UnhookInputViews(View view)
    {
        if (view is InputView input)
        {
            input.Focused -= OnInputFocused;
            input.Unfocused -= OnInputUnfocused;
        }

        if (view is Layout layout)
        {
            foreach (var child in layout.Children.OfType<View>())
                UnhookInputViews(child);
        }
        else if (view is ContentView cv && cv.Content is not null)
        {
            UnhookInputViews(cv.Content);
        }
        else if (view is ScrollView sv && sv.Content is View svContent)
        {
            UnhookInputViews(svContent);
        }
        else if (view is Border b && b.Content is View bContent)
        {
            UnhookInputViews(bContent);
        }
    }

    void OnInputFocused(object? sender, FocusEventArgs e)
    {
        if (!IsOpen || !ExpandOnInputFocus) return;

        var sortedDetents = GetSortedDetents();
        // Already at the top — let the platform keyboard avoidance do its job
        // without triggering a competing height animation that drops keystrokes.
        if (currentDetentIndex >= sortedDetents.Count - 1) return;

        if (detentIndexBeforeKeyboard < 0)
            detentIndexBeforeKeyboard = currentDetentIndex;

        _ = AnimateToDetentAsync(sortedDetents.Last());
    }

    void OnInputUnfocused(object? sender, FocusEventArgs e)
    {
        if (!IsOpen || !ExpandOnInputFocus || isKeyboardVisible) return;

        if (detentIndexBeforeKeyboard >= 0)
        {
            var sortedDetents = GetSortedDetents();
            var targetIndex = Math.Clamp(detentIndexBeforeKeyboard, 0, sortedDetents.Count - 1);
            detentIndexBeforeKeyboard = -1;
            _ = AnimateToDetentAsync(sortedDetents[targetIndex]);
        }
    }

    #endregion
}
