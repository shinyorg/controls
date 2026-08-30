using Shiny.Maui.Controls.Themes;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.Scheduler.Internal;

class CalendarSheetPicker : ContentView
{
    const double RowHeight = 34;
    const double RowSpacing = 2;
    const double HeaderHeight = 36;
    const double DayHeaderHeight = 24;
    const double HandleHeight = 20;
    const double PanThreshold = 40;

    readonly Grid rootGrid;
    readonly Grid headerGrid;
    readonly Label monthLabel;
    readonly Button prevButton;
    readonly Button nextButton;
    readonly Grid dayHeaderGrid;
    readonly Grid calendarGrid;
    readonly BoxView handleBar;
    readonly BoxView[] todayIndicators = new BoxView[42];
    readonly Label[] dayLabels = new Label[42];
    readonly Border[] cellBorders = new Border[42];
    readonly DateOnly[] cellDates = new DateOnly[42];

    DateOnly selectedDate = DateOnly.FromDateTime(DateTime.Today);
    DateOnly displayMonth = DateOnly.FromDateTime(DateTime.Today);
    DayOfWeek firstDayOfWeek = DayOfWeek.Sunday;
    bool isExpanded;
    bool buildPending;
    int selectedRow;
    int rowsNeeded = 5;

    public Action<DateOnly>? DateSelected { get; set; }

    public DateOnly SelectedDate
    {
        get => selectedDate;
        set
        {
            if (selectedDate == value) return;
            selectedDate = value;

            // Auto-navigate display month to match selected date
            var newMonth = new DateOnly(value.Year, value.Month, 1);
            if (displayMonth != newMonth)
                displayMonth = newMonth;

            QueueBuild();
        }
    }

    public DayOfWeek FirstDayOfWeek
    {
        get => firstDayOfWeek;
        set
        {
            firstDayOfWeek = value;
            QueueBuild();
        }
    }

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (isExpanded == value) return;
            isExpanded = value;
            AnimateExpansion();
        }
    }

    public CalendarSheetPicker()
    {
        IsClippedToBounds = true;

        // Month/year header with nav arrows
        monthLabel = new Label
        {
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };

        prevButton = new Button
        {
            Text = "<",
            BackgroundColor = Colors.Transparent,
            WidthRequest = 40,
            HeightRequest = HeaderHeight,
            BorderWidth = 0,
            Padding = 0
        }.Neutralize().WithFontSize(ShinyThemeKeys.Type.BodyLargeSize);
        prevButton.SetDynamicResource(Button.TextColorProperty, ShinyThemeKeys.Color.Primary);
        prevButton.Clicked += (_, _) => NavigateMonth(-1);

        nextButton = new Button
        {
            Text = ">",
            BackgroundColor = Colors.Transparent,
            WidthRequest = 40,
            HeightRequest = HeaderHeight,
            BorderWidth = 0,
            Padding = 0
        }.Neutralize().WithFontSize(ShinyThemeKeys.Type.BodyLargeSize);
        nextButton.SetDynamicResource(Button.TextColorProperty, ShinyThemeKeys.Color.Primary);
        nextButton.Clicked += (_, _) => NavigateMonth(1);

        headerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(40)),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(40))
            },
            HeightRequest = HeaderHeight,
            Padding = new Thickness(4, 0)
        };
        headerGrid.Add(prevButton, 0);
        headerGrid.Add(monthLabel, 1);
        headerGrid.Add(nextButton, 2);

        // Day-of-week headers
        dayHeaderGrid = new Grid { HeightRequest = DayHeaderHeight, Padding = new Thickness(4, 0) };
        for (var i = 0; i < 7; i++)
            dayHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        // Calendar grid - 6 rows x 7 columns
        calendarGrid = new Grid
        {
            Padding = new Thickness(4, 0),
            RowSpacing = RowSpacing,
            ColumnSpacing = 0
        };
        for (var i = 0; i < 7; i++)
            calendarGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var i = 0; i < 6; i++)
            calendarGrid.RowDefinitions.Add(new RowDefinition(new GridLength(RowHeight)));

        for (var i = 0; i < 42; i++)
        {
            var dayLabel = new Label
            {
                FontSize = 13,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            };
            dayLabels[i] = dayLabel;

            var todayDot = new BoxView
            {
                WidthRequest = 4,
                HeightRequest = 4,
                CornerRadius = 2,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.End,
                IsVisible = false,
                Margin = new Thickness(0, 0, 0, 2)
            };
            todayDot.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.Primary);
            todayIndicators[i] = todayDot;

            var cellContent = new Grid
            {
                Children = { dayLabel, todayDot }
            };

            var border = new Border
            {
                Content = cellContent,
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerLargeRadius),
                BackgroundColor = Colors.Transparent,
                WidthRequest = 32,
                HeightRequest = 32,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            cellBorders[i] = border;

            var idx = i;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => OnCellTapped(idx);
            border.GestureRecognizers.Add(tap);

            calendarGrid.Add(border, i % 7, i / 7);
        }

        // Pull handle bar
        handleBar = new BoxView
        {
            WidthRequest = 36,
            HeightRequest = 4,
            CornerRadius = 2,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        handleBar.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.OutlineVariant);
        var handleContainer = new ContentView
        {
            Content = handleBar,
            HeightRequest = HandleHeight,
            HorizontalOptions = LayoutOptions.Fill
        };

        // Pan gesture on the handle area for pull-to-expand
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPanUpdated;
        handleContainer.GestureRecognizers.Add(pan);

        // Tap the handle to toggle
        var handleTap = new TapGestureRecognizer();
        handleTap.Tapped += (_, _) => IsExpanded = !IsExpanded;
        handleContainer.GestureRecognizers.Add(handleTap);

        rootGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),   // 0: month header
                new RowDefinition(GridLength.Auto),   // 1: day-of-week headers
                new RowDefinition(GridLength.Auto),   // 2: calendar grid
                new RowDefinition(GridLength.Auto)    // 3: handle bar
            },
            RowSpacing = 0
        };
        rootGrid.Add(headerGrid, 0, 0);
        rootGrid.Add(dayHeaderGrid, 0, 1);
        rootGrid.Add(calendarGrid, 0, 2);
        rootGrid.Add(handleContainer, 0, 3);

        Content = rootGrid;
        Build();
        // Start collapsed - show only selected week
        ApplyCollapsedLayout(false);
    }

    double panStartY;

    void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                panStartY = 0;
                break;

            case GestureStatus.Running:
                panStartY = e.TotalY;
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                if (Math.Abs(panStartY) > PanThreshold)
                    IsExpanded = panStartY > 0;
                break;
        }
    }

    void QueueBuild()
    {
        if (buildPending) return;
        buildPending = true;
        Dispatcher.Dispatch(() =>
        {
            buildPending = false;
            Build();
            if (!isExpanded)
                ApplyCollapsedLayout(false);
        });
    }

    void Build()
    {
        var dm = displayMonth;
        monthLabel.Text = new DateTime(dm.Year, dm.Month, 1).ToString("MMMM yyyy");

        BuildDayHeaders();
        BuildDayCells();
    }

    void BuildDayHeaders()
    {
        dayHeaderGrid.Children.Clear();
        var names = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;
        var first = (int)firstDayOfWeek;

        for (var i = 0; i < 7; i++)
        {
            var idx = (first + i) % 7;
            var header = new Label
            {
                Text = names[idx].ToUpperInvariant()[..2],
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }.WithFontSize(ShinyThemeKeys.Type.LabelSmallSize);
            header.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
            dayHeaderGrid.Add(header, i);
        }
    }

    void BuildDayCells()
    {
        var dm = displayMonth;
        var firstOfMonth = new DateOnly(dm.Year, dm.Month, 1);
        var firstDayOffset = ((int)firstOfMonth.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        var startDate = firstOfMonth.AddDays(-firstDayOffset);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var totalDays = firstDayOffset + DateTime.DaysInMonth(dm.Year, dm.Month);
        rowsNeeded = (totalDays + 6) / 7;

        selectedRow = -1;

        for (var i = 0; i < 42; i++)
        {
            var date = startDate.AddDays(i);
            cellDates[i] = date;
            var isCurrentMonth = date.Month == dm.Month && date.Year == dm.Year;
            var isSelected = date == selectedDate;
            var isToday = date == today;
            var row = i / 7;

            if (isSelected)
                selectedRow = row;

            cellBorders[i].IsVisible = row < rowsNeeded;
            if (row >= rowsNeeded)
            {
                cellBorders[i].Opacity = 0;
                continue;
            }

            dayLabels[i].Text = date.Day.ToString();

            if (isSelected)
            {
                cellBorders[i].SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Primary);
                dayLabels[i].SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnPrimary);
                dayLabels[i].FontAttributes = FontAttributes.Bold;
                todayIndicators[i].IsVisible = false;
            }
            else if (isToday)
            {
                ClearFill(cellBorders[i]);
                dayLabels[i].SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.Primary);
                dayLabels[i].FontAttributes = FontAttributes.Bold;
                todayIndicators[i].IsVisible = true;
                todayIndicators[i].SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.Primary);
            }
            else
            {
                ClearFill(cellBorders[i]);
                dayLabels[i].SetDynamicResource(Label.TextColorProperty,
                    isCurrentMonth ? ShinyThemeKeys.Color.OnSurface : ShinyThemeKeys.Color.OutlineVariant);
                dayLabels[i].FontAttributes = FontAttributes.None;
                todayIndicators[i].IsVisible = false;
            }

            cellBorders[i].Opacity = 1;
        }

        if (selectedRow < 0)
            selectedRow = 0;
    }

    void ApplyCollapsedLayout(bool animate)
    {
        // Show only the row containing the selected date
        if (animate)
        {
            for (var r = 0; r < 6; r++)
            {
                var show = r == selectedRow;
                for (var c = 0; c < 7; c++)
                {
                    var idx = r * 7 + c;
                    if (r < rowsNeeded)
                        cellBorders[idx].FadeToAsync(show ? 1 : 0, 200, Easing.CubicInOut);
                }
                calendarGrid.RowDefinitions[r].Height = show ? new GridLength(RowHeight) : new GridLength(0);
            }
        }
        else
        {
            for (var r = 0; r < 6; r++)
            {
                var show = r == selectedRow;
                for (var c = 0; c < 7; c++)
                {
                    var idx = r * 7 + c;
                    if (r < rowsNeeded)
                        cellBorders[idx].Opacity = show ? 1 : 0;
                }
                calendarGrid.RowDefinitions[r].Height = show ? new GridLength(RowHeight) : new GridLength(0);
            }
        }

        // Hide nav arrows when collapsed
        prevButton.IsVisible = false;
        nextButton.IsVisible = false;
    }

    void ApplyExpandedLayout(bool animate)
    {
        prevButton.IsVisible = true;
        nextButton.IsVisible = true;

        for (var r = 0; r < 6; r++)
        {
            var visible = r < rowsNeeded;
            calendarGrid.RowDefinitions[r].Height = visible ? new GridLength(RowHeight) : new GridLength(0);

            for (var c = 0; c < 7; c++)
            {
                var idx = r * 7 + c;
                if (visible)
                {
                    if (animate)
                        cellBorders[idx].FadeToAsync(1, 200, Easing.CubicInOut);
                    else
                        cellBorders[idx].Opacity = 1;
                }
                else
                {
                    cellBorders[idx].Opacity = 0;
                }
            }
        }
    }

    void AnimateExpansion()
    {
        if (isExpanded)
            ApplyExpandedLayout(true);
        else
            ApplyCollapsedLayout(true);
    }

    /// <summary>Drops any themed fill so the cell goes back to transparent.</summary>
    static void ClearFill(Border border)
    {
        border.RemoveDynamicResource(VisualElement.BackgroundColorProperty);
        border.BackgroundColor = Colors.Transparent;
    }

    void OnCellTapped(int index)
    {
        var date = cellDates[index];
        selectedDate = date;

        if (date.Month != displayMonth.Month || date.Year != displayMonth.Year)
            displayMonth = new DateOnly(date.Year, date.Month, 1);

        Build();

        if (!isExpanded)
            ApplyCollapsedLayout(false);

        DateSelected?.Invoke(date);
    }

    void NavigateMonth(int direction)
    {
        displayMonth = displayMonth.AddMonths(direction);
        Build();

        if (!isExpanded)
            ApplyCollapsedLayout(false);
    }
}
