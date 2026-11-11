using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Core;
using DynamicIsland.Services;

namespace DynamicIsland.Views.Widgets;

public partial class CalendarTaskView : UserControl
{
    private DateTime _displayMonth = DateTime.Today;
    private DateTime _selectedDate = DateTime.Today;

    // High performance frozen brushes (Zero GC allocation during render/hover)
    private static readonly SolidColorBrush TodayRedBrush       = CreateFrozenBrush("#FF453A");
    private static readonly SolidColorBrush SelectedCellBg      = CreateFrozenBrush("#35FFFFFF");
    private static readonly SolidColorBrush SelectedCellBorder  = CreateFrozenBrush("#60FFFFFF");
    private static readonly SolidColorBrush TrailingDayFg       = CreateFrozenBrush("#40FFFFFF");
    private static readonly SolidColorBrush DayHoverBg          = CreateFrozenBrush("#22FFFFFF");
    private static readonly SolidColorBrush CardNormalBg        = CreateFrozenBrush("#18FFFFFF");
    private static readonly SolidColorBrush CardNormalBorder    = CreateFrozenBrush("#20FFFFFF");
    private static readonly SolidColorBrush CardHoverBg         = CreateFrozenBrush("#2AFFFFFF");
    private static readonly SolidColorBrush HighlightCardBg     = CreateFrozenBrush("#30FF9F0A");
    private static readonly SolidColorBrush HighlightCardBorder = CreateFrozenBrush("#65FF9F0A");
    private static readonly SolidColorBrush HighlightCardHover  = CreateFrozenBrush("#45FF9F0A");
    private static readonly SolidColorBrush HighlightCardTitle  = CreateFrozenBrush("#FF9F0A");
    private static readonly SolidColorBrush HighlightCardTime   = CreateFrozenBrush("#EEFFC060");
    private static readonly SolidColorBrush TaskDotBrush        = CreateFrozenBrush("#30D158");
    private static readonly SolidColorBrush EventDotBrush       = CreateFrozenBrush("#FF9F0A");
    private static readonly SolidColorBrush CheckboxBorderBrush = CreateFrozenBrush("#60FFFFFF");
    private static readonly SolidColorBrush DeleteHoverBrush    = CreateFrozenBrush("#30FF453A");

    private static readonly Regex TimeRegex = new(
        @"(?:\bat\s+|@\s*)(\d{1,2}(?::\d{2})?\s*(?:am|pm)?|\d{1,2}:\d{2})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static SolidColorBrush CreateFrozenBrush(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    private static void DayCell_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border b) b.Background = DayHoverBg;
    }

    private static void DayCell_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border b) b.Background = Brushes.Transparent;
    }

    private static void Card_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border card)
        {
            card.Background = CardHoverBg;
            if (card.Tag is Brush b) card.BorderBrush = b;
            else if (card.Tag is ValueTuple<Brush, string> t1) card.BorderBrush = t1.Item1;
            else if (card.Tag is ValueTuple<Brush, string?> t2) card.BorderBrush = t2.Item1;
        }
    }

    private static void Card_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border card)
        {
            card.Background = CardNormalBg;
            card.BorderBrush = CardNormalBorder;
        }
    }

    private static void DeleteBtn_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border b)
        {
            b.Background = DeleteHoverBrush;
            if (b.Child is TextBlock t) t.Foreground = TodayRedBrush;
        }
    }

    private static void DeleteBtn_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border b)
        {
            b.Background = Brushes.Transparent;
            if (b.Child is TextBlock t) t.Foreground = ThemeBrushes.SecondaryText;
        }
    }

    private void DayCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int currentDay })
        {
            _selectedDate = new DateTime(_displayMonth.Year, _displayMonth.Month, currentDay);
            HeaderDayOfWeek.Text = _selectedDate.ToString("ddd", CultureInfo.InvariantCulture).ToUpperInvariant();
            HeaderDayNumber.Text = _selectedDate.Day.ToString();
            int weekOfYear = CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(
                _selectedDate, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
            HeaderWeekNumber.Text = $"W{weekOfYear}";
            PopulateCalendarGrid();
            PopulateScheduleCards();
            e.Handled = true;
        }
    }

    private void TaskCheckbox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string taskId })
        {
            Island?.TaskServiceInstance?.ToggleComplete(taskId);
            e.Handled = true;
        }
    }

    private void DeleteTaskBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string taskId })
        {
            Island?.TaskServiceInstance?.DeleteTask(taskId);
            e.Handled = true;
        }
    }

    private void TaskCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ValueTuple<Brush, string> tuple })
        {
            Island?.TaskServiceInstance?.ToggleComplete(tuple.Item2);
            e.Handled = true;
        }
    }

    private void DeleteEventBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string eventId })
        {
            Island?.CalendarServiceInstance?.DeleteEvent(eventId);
            e.Handled = true;
        }
    }

    private static void EventCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ValueTuple<Brush, string?> tuple } && !string.IsNullOrWhiteSpace(tuple.Item2))
        {
            CalendarService.LaunchMeeting(tuple.Item2);
            e.Handled = true;
        }
    }

    public CalendarTaskView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Island?.TaskServiceInstance is { } ts)
        {
            ts.TasksChanged -= OnTasksChanged;
            ts.TasksChanged += OnTasksChanged;
        }
        if (Island?.CalendarServiceInstance is { } cs)
        {
            cs.Updated -= OnCalendarUpdated;
            cs.Updated += OnCalendarUpdated;
        }
        RefreshAll();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (Island?.TaskServiceInstance is { } ts)
        {
            ts.TasksChanged -= OnTasksChanged;
        }
        if (Island?.CalendarServiceInstance is { } cs)
        {
            cs.Updated -= OnCalendarUpdated;
        }
    }

    private void OnTasksChanged() => Dispatcher.InvokeAsync(RefreshAll);
    private void OnCalendarUpdated(CalendarEventInfo? evt) => Dispatcher.InvokeAsync(RefreshAll);

    public void RefreshAll()
    {
        var now = DateTime.Now;
        HeaderDayOfWeek.Text = _selectedDate.ToString("ddd", CultureInfo.InvariantCulture).ToUpperInvariant();
        HeaderDayNumber.Text = _selectedDate.Day.ToString();

        int weekOfYear = CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(
            _selectedDate, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        HeaderWeekNumber.Text = $"W{weekOfYear}";

        HeaderMonthBadge.Text = _displayMonth.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant();

        PopulateScheduleCards();
        PopulateCalendarGrid();
    }

    private void PopulateScheduleCards()
    {
        ScheduleCardsPanel.Children.Clear();

        // 1. Date Header indicator if viewing a different date
        if (_selectedDate.Date != DateTime.Today)
        {
            var dateChip = new Border
            {
                Background = CreateFrozenBrush("#200A84FF"),
                BorderBrush = CreateFrozenBrush("#500A84FF"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var dateText = new TextBlock
            {
                Text = _selectedDate.ToString("dddd, MMMM d"),
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = CreateFrozenBrush("#0A84FF")
            };
            dateChip.Child = dateText;
            ScheduleCardsPanel.Children.Add(dateChip);
        }

        int itemsRendered = 0;

        // 2. WinRT upcoming calendar appointment (if viewing today)
        if (_selectedDate.Date == DateTime.Today && Island?.CalendarServiceInstance?.NextEvent is { } liveEvent)
        {
            var liveCard = CreateScheduleCard(
                liveEvent.Subject,
                $"{liveEvent.StartTime:h:mm tt} \u2013 {liveEvent.StartTime.Add(liveEvent.Duration):h:mm tt}",
                liveEvent.ColorTag,
                hasVideo: !string.IsNullOrWhiteSpace(liveEvent.JoinUrl),
                onCardClick: () => CalendarService.LaunchEventOrCalendar(liveEvent),
                isHighlighted: true
            );
            ScheduleCardsPanel.Children.Add(liveCard);
            itemsRendered++;
        }

        // 3. Local Custom Events for selected date
        if (Island?.CalendarServiceInstance is { } calSvc)
        {
            var customEvents = calSvc.GetEventsForDate(_selectedDate);
            foreach (var evt in customEvents)
            {
                string timeStr = $"{evt.StartTime:h:mm tt} \u2013 {evt.StartTime.Add(evt.Duration):h:mm tt}";
                var card = CreateCustomEventCard(evt, timeStr);
                ScheduleCardsPanel.Children.Add(card);
                itemsRendered++;
            }
        }

        // 4. Real User Tasks from TaskService for selected date
        if (Island?.TaskServiceInstance is { } taskSvc)
        {
            var tasks = taskSvc.GetTasksForDate(_selectedDate);
            foreach (var task in tasks)
            {
                var taskCard = CreateTaskCard(task, taskSvc);
                ScheduleCardsPanel.Children.Add(taskCard);
                itemsRendered++;
            }
        }

        // 5. Clean Apple Empty State if nothing is scheduled for this date
        if (itemsRendered == 0)
        {
            var emptyBorder = new Border
            {
                Margin = new Thickness(0, 6, 0, 0),
                Padding = new Thickness(12, 10, 12, 10),
                CornerRadius = new CornerRadius(9),
                Background = CreateFrozenBrush("#0CFFFFFF"),
                BorderBrush = CreateFrozenBrush("#14FFFFFF"),
                BorderThickness = new Thickness(1)
            };
            var emptyStack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var emptyTitle = new TextBlock
            {
                Text = _selectedDate.Date == DateTime.Today ? "No plans for today" : "No plans for this date",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeBrushes.PrimaryText,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var emptySub = new TextBlock
            {
                Text = "Type below to add a task or reminder",
                FontSize = 8.5,
                Foreground = ThemeBrushes.SecondaryText,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };
            emptyStack.Children.Add(emptyTitle);
            emptyStack.Children.Add(emptySub);
            emptyBorder.Child = emptyStack;
            ScheduleCardsPanel.Children.Add(emptyBorder);
        }
    }

    private Border CreateTaskCard(IslandTask task, TaskService taskSvc)
    {
        var accentBrush = (SolidColorBrush)new BrushConverter().ConvertFromString(task.ColorTag ?? "#30D158")!;
        accentBrush.Freeze();

        var card = new Border
        {
            Height = 33,
            Margin = new Thickness(0, 0, 0, 4),
            CornerRadius = new CornerRadius(9),
            Background = CardNormalBg,
            BorderBrush = CardNormalBorder,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = $"{task.Title} · Click to toggle completion",
            Opacity = task.IsCompleted ? 0.55 : 1.0
        };

        card.Tag = (accentBrush, task.Id);
        card.MouseEnter += Card_MouseEnter;
        card.MouseLeave += Card_MouseLeave;
        card.MouseLeftButtonDown += TaskCard_MouseLeftButtonDown;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left Checkbox: Apple circular checkbox (○ -> ✓)
        var checkBorder = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(8, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = task.IsCompleted ? accentBrush : Brushes.Transparent,
            BorderBrush = task.IsCompleted ? accentBrush : CheckboxBorderBrush,
            BorderThickness = new Thickness(1.2),
            Cursor = Cursors.Hand,
            Tag = task.Id
        };
        var checkGlyph = new TextBlock
        {
            Style = (Style)FindResource("Glyph"),
            Text = "\uE73E", // Checkmark
            FontSize = 9,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = task.IsCompleted ? Visibility.Visible : Visibility.Collapsed
        };
        checkBorder.Child = checkGlyph;
        Grid.SetColumn(checkBorder, 0);
        grid.Children.Add(checkBorder);

        checkBorder.MouseLeftButtonDown += TaskCheckbox_MouseLeftButtonDown;

        // Center: Title & Due Time
        var textStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 6, 0)
        };
        var titleText = new TextBlock
        {
            Text = task.Title,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrushes.PrimaryText,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextDecorations = task.IsCompleted ? TextDecorations.Strikethrough : null
        };
        textStack.Children.Add(titleText);

        if (!string.IsNullOrWhiteSpace(task.DueTime) || task.IsOverdue)
        {
            string timeLabel = task.IsOverdue ? $"Overdue · {task.DueTime ?? ""}" : (task.DueTime ?? "");
            var timeText = new TextBlock
            {
                Text = timeLabel.Trim(),
                FontSize = 8,
                Foreground = task.IsOverdue ? TodayRedBrush : ThemeBrushes.SecondaryText,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0.5, 0, 0)
            };
            textStack.Children.Add(timeText);
        }
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(textStack);

        // Right: Delete Button (Hover trash icon)
        var deleteBtn = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = "Delete task",
            Tag = task.Id
        };
        var deleteGlyph = new TextBlock
        {
            Style = (Style)FindResource("Glyph"),
            Text = "\uE74D", // Trash can
            FontSize = 9.5,
            Foreground = ThemeBrushes.SecondaryText,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        deleteBtn.Child = deleteGlyph;
        deleteBtn.MouseEnter += DeleteBtn_MouseEnter;
        deleteBtn.MouseLeave += DeleteBtn_MouseLeave;
        deleteBtn.MouseLeftButtonDown += DeleteTaskBtn_MouseLeftButtonDown;
        Grid.SetColumn(deleteBtn, 2);
        grid.Children.Add(deleteBtn);

        card.Child = grid;
        return card;
    }

    private Border CreateCustomEventCard(IslandCustomEvent evt, string time)
    {
        var accentBrush = (SolidColorBrush)new BrushConverter().ConvertFromString(evt.ColorTag ?? "#FF9F0A")!;
        accentBrush.Freeze();

        var card = new Border
        {
            Height = 33,
            Margin = new Thickness(0, 0, 0, 4),
            CornerRadius = new CornerRadius(9),
            Background = CardNormalBg,
            BorderBrush = CardNormalBorder,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = $"{evt.Subject} ({time}) · Click to open"
        };

        card.Tag = (accentBrush, evt.JoinUrl);
        card.MouseEnter += Card_MouseEnter;
        card.MouseLeave += Card_MouseLeave;
        card.MouseLeftButtonDown += EventCard_MouseLeftButtonDown;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left accent indicator bar
        var indicator = new Border
        {
            Width = 3,
            Margin = new Thickness(7, 6, 7, 6),
            CornerRadius = new CornerRadius(1.5),
            Background = accentBrush
        };
        Grid.SetColumn(indicator, 0);
        grid.Children.Add(indicator);

        // Center: Title & Time
        var textStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        var titleText = new TextBlock
        {
            Text = evt.Subject,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrushes.PrimaryText,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var timeText = new TextBlock
        {
            Text = time,
            FontSize = 8,
            Foreground = ThemeBrushes.SecondaryText,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0.5, 0, 0)
        };
        textStack.Children.Add(titleText);
        textStack.Children.Add(timeText);
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(textStack);

        // Right: Video icon or delete button
        var rightStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };

        if (!string.IsNullOrWhiteSpace(evt.JoinUrl))
        {
            var videoIcon = new TextBlock
            {
                Style = (Style)FindResource("Glyph"),
                Text = "\uE714", // Video camera glyph
                FontSize = 9.5,
                Foreground = accentBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = "Join video meeting"
            };
            rightStack.Children.Add(videoIcon);
        }

        var deleteBtn = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = "Delete event",
            Tag = evt.Id
        };
        var deleteGlyph = new TextBlock
        {
            Style = (Style)FindResource("Glyph"),
            Text = "\uE74D",
            FontSize = 9,
            Foreground = ThemeBrushes.SecondaryText,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        deleteBtn.Child = deleteGlyph;
        deleteBtn.MouseEnter += DeleteBtn_MouseEnter;
        deleteBtn.MouseLeave += DeleteBtn_MouseLeave;
        deleteBtn.MouseLeftButtonDown += DeleteEventBtn_MouseLeftButtonDown;
        rightStack.Children.Add(deleteBtn);

        Grid.SetColumn(rightStack, 2);
        grid.Children.Add(rightStack);

        card.Child = grid;
        return card;
    }

    private Border CreateScheduleCard(string title, string time, string accentHex, bool hasVideo, Action onCardClick, bool isHighlighted = false)
    {
        var accentBrush = (SolidColorBrush)new BrushConverter().ConvertFromString(accentHex)!;
        accentBrush.Freeze();

        var normalBg = isHighlighted ? HighlightCardBg : CardNormalBg;
        var normalBorder = isHighlighted ? HighlightCardBorder : CardNormalBorder;
        var hoverBg = isHighlighted ? HighlightCardHover : CardHoverBg;

        var card = new Border
        {
            Height = 33,
            Margin = new Thickness(0, 0, 0, 4),
            CornerRadius = new CornerRadius(9),
            Background = normalBg,
            BorderBrush = normalBorder,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = $"{title} ({time}) · Click to open"
        };

        card.MouseEnter += (_, _) =>
        {
            card.Background = hoverBg;
            card.BorderBrush = accentBrush;
        };
        card.MouseLeave += (_, _) =>
        {
            card.Background = normalBg;
            card.BorderBrush = normalBorder;
        };
        card.MouseLeftButtonDown += (_, e) =>
        {
            onCardClick();
            e.Handled = true;
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left accent indicator
        var indicator = new Border
        {
            Width = 3,
            Margin = new Thickness(7, 6, 7, 6),
            CornerRadius = new CornerRadius(1.5),
            Background = accentBrush
        };
        Grid.SetColumn(indicator, 0);
        grid.Children.Add(indicator);

        // Center: Title & Time
        var textStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = isHighlighted ? HighlightCardTitle : ThemeBrushes.PrimaryText,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var timeText = new TextBlock
        {
            Text = time,
            FontSize = 8,
            Foreground = isHighlighted ? HighlightCardTime : ThemeBrushes.SecondaryText,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0.5, 0, 0)
        };
        textStack.Children.Add(titleText);
        textStack.Children.Add(timeText);
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(textStack);

        // Right video icon (if video call present)
        if (hasVideo)
        {
            var videoIcon = new TextBlock
            {
                Style = (Style)FindResource("Glyph"),
                Text = "\uE714", // Video camera glyph
                FontSize = 10,
                Foreground = accentBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(videoIcon, 2);
            grid.Children.Add(videoIcon);
        }

        card.Child = grid;
        return card;
    }

    private void PopulateCalendarGrid()
    {
        CalendarDaysGrid.Children.Clear();
        CalendarMonthTitle.Text = _displayMonth.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

        var firstDayOfMonth = new DateTime(_displayMonth.Year, _displayMonth.Month, 1);
        int daysInMonth = DateTime.DaysInMonth(_displayMonth.Year, _displayMonth.Month);
        int startDayOfWeek = (int)firstDayOfMonth.DayOfWeek; // 0 = Sunday

        var prevMonth = _displayMonth.AddMonths(-1);
        int daysInPrevMonth = DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month);

        // Query pending task and event markers for the displayed month
        var taskDays = Island?.TaskServiceInstance?.GetDaysWithPendingTasks(_displayMonth.Year, _displayMonth.Month) ?? new HashSet<int>();
        var eventDays = Island?.CalendarServiceInstance?.GetDaysWithEvents(_displayMonth.Year, _displayMonth.Month) ?? new HashSet<int>();

        // 1. Previous month trailing days
        for (int i = 0; i < startDayOfWeek; i++)
        {
            int dayNum = daysInPrevMonth - startDayOfWeek + 1 + i;
            var dayBtn = CreateDayCell(dayNum, isCurrentMonth: false, isToday: false, isSelected: false, hasTask: false, hasEvent: false);
            CalendarDaysGrid.Children.Add(dayBtn);
        }

        // 2. Current month days
        var today = DateTime.Today;
        for (int day = 1; day <= daysInMonth; day++)
        {
            bool isToday = (_displayMonth.Year == today.Year && _displayMonth.Month == today.Month && day == today.Day);
            bool isSelected = (_displayMonth.Year == _selectedDate.Year && _displayMonth.Month == _selectedDate.Month && day == _selectedDate.Day);
            bool hasTask = taskDays.Contains(day);
            bool hasEvent = eventDays.Contains(day);
            var dayBtn = CreateDayCell(day, isCurrentMonth: true, isToday: isToday, isSelected: isSelected, hasTask: hasTask, hasEvent: hasEvent);
            dayBtn.Tag = day;
            dayBtn.MouseLeftButtonDown += DayCell_MouseLeftButtonDown;
            CalendarDaysGrid.Children.Add(dayBtn);
        }

        // 3. Next month leading days (fill up 35 or 42 cells)
        int totalCells = CalendarDaysGrid.Children.Count;
        int remainingCells = (totalCells <= 35 ? 35 : 42) - totalCells;
        for (int day = 1; day <= remainingCells; day++)
        {
            var dayBtn = CreateDayCell(day, isCurrentMonth: false, isToday: false, isSelected: false, hasTask: false, hasEvent: false);
            CalendarDaysGrid.Children.Add(dayBtn);
        }
    }

    private void TasksScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scroller)
        {
            scroller.ScrollToVerticalOffset(scroller.VerticalOffset - (e.Delta * 0.4));
            e.Handled = true;
        }
    }

    private Border CreateDayCell(int dayNumber, bool isCurrentMonth, bool isToday, bool isSelected, bool hasTask, bool hasEvent)
    {
        var border = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = isCurrentMonth ? Cursors.Hand : Cursors.Arrow,
            Background = isToday ? TodayRedBrush : (isSelected ? SelectedCellBg : Brushes.Transparent),
            BorderBrush = isSelected ? SelectedCellBorder : Brushes.Transparent,
            BorderThickness = isSelected ? new Thickness(1) : new Thickness(0)
        };

        if (isCurrentMonth && !isToday && !isSelected)
        {
            border.MouseEnter += DayCell_MouseEnter;
            border.MouseLeave += DayCell_MouseLeave;
        }

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var text = new TextBlock
        {
            Text = dayNumber.ToString(),
            FontSize = 9.5,
            FontWeight = (isToday || isSelected) ? FontWeights.Bold : FontWeights.Normal,
            Foreground = isToday
                ? Brushes.White
                : (isCurrentMonth ? ThemeBrushes.PrimaryText : TrailingDayFg),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        stack.Children.Add(text);

        // Apple dot markers beneath date number for tasks / events
        if (isCurrentMonth && (hasTask || hasEvent))
        {
            var dotsRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0.5, 0, 0)
            };

            if (hasEvent)
            {
                var eventDot = new System.Windows.Shapes.Ellipse
                {
                    Width = 3,
                    Height = 3,
                    Fill = isToday ? Brushes.White : EventDotBrush,
                    Margin = new Thickness(0.6, 0, 0.6, 0)
                };
                dotsRow.Children.Add(eventDot);
            }

            if (hasTask)
            {
                var taskDot = new System.Windows.Shapes.Ellipse
                {
                    Width = 3,
                    Height = 3,
                    Fill = isToday ? Brushes.White : TaskDotBrush,
                    Margin = new Thickness(0.6, 0, 0.6, 0)
                };
                dotsRow.Children.Add(taskDot);
            }

            stack.Children.Add(dotsRow);
        }

        border.Child = stack;
        return border;
    }

    private void PrevMonth_Click(object sender, RoutedEventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(-1);
        HeaderMonthBadge.Text = _displayMonth.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant();
        PopulateCalendarGrid();
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(1);
        HeaderMonthBadge.Text = _displayMonth.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant();
        PopulateCalendarGrid();
    }

    private void QuickAddToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        QuickTaskInput.Focus();
    }

    private void OpenCalendarAppBtn_Click(object sender, RoutedEventArgs e)
    {
        CalendarService.LaunchEventOrCalendar(null);
    }

    private void QuickTaskInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        QuickTaskPlaceholder.Visibility = string.IsNullOrEmpty(QuickTaskInput.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void QuickTaskInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitNewTask();
            e.Handled = true;
        }
    }

    private void SubmitTask_Click(object sender, RoutedEventArgs e)
    {
        CommitNewTask();
    }

    private void CommitNewTask()
    {
        string raw = QuickTaskInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return;

        string title = raw;
        string? timeStr = null;

        var match = TimeRegex.Match(raw);
        if (match.Success)
        {
            timeStr = match.Groups[1].Value.Trim().ToUpperInvariant();
            title = TimeRegex.Replace(raw, "").Trim();
        }

        if (string.IsNullOrWhiteSpace(title))
            title = raw;

        if (Island?.TaskServiceInstance is { } taskSvc)
        {
            taskSvc.AddTask(title, _selectedDate, timeStr, colorTag: "#30D158");
            QuickTaskInput.Text = "";
            PopulateScheduleCards();
            PopulateCalendarGrid();
            Island?.PushNotificationToast("\uE787", "Task Saved", $"{title}{(timeStr != null ? $" at {timeStr}" : "")}", ThemeBrushes.GreenColor);
        }
    }
}
