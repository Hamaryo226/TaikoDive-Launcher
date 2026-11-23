using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace TaikoDiveLauncher.Pages;

public sealed partial class PlayActivityPage : Page
{
    private const int Rows = 7;
    private const double MinimumPlotWidth = 440;
    private readonly List<Cell> _cells = [];
    private readonly List<(TextBlock Label, int Week)> _months = [];
    private readonly AccessibilitySettings _accessibility = new();
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(3) };
    private IReadOnlyList<PlayActivityEntry> _entries = [];
    private CancellationTokenSource? _loading;
    private DateTime? _historyStamp;
    private DateOnly _renderedDay;
    private Cell? _selected;
    private Cell? _tabStop;
    private DateOnly? _selectedDate;
    private bool _ready;
    private bool _scrollAtEnd = true;
    private int Mode => Math.Max(0, ModePicker.SelectedIndex);

    public PlayActivityPage()
    {
        InitializeComponent();
        PlayerPicker.ItemsSource = new[] { new PlayerOption(null, "全プレイヤー") };
        PlayerPicker.SelectedIndex = 0;
        Loaded += async (_, _) => { _refresh.Start(); await RefreshHistoryAsync(); };
        Unloaded += (_, _) => { _refresh.Stop(); _loading?.Cancel(); };
        _refresh.Tick += async (_, _) => await RefreshHistoryAsync();
        ActualThemeChanged += (_, _) => { if (_ready) Render(); };
    }

    private async Task RefreshHistoryAsync()
    {
        if (_loading is not null) return;
        using var load = new CancellationTokenSource();
        _loading = load;
        try
        {
            if (((App)Application.Current).Context.Installation is { } installation)
            {
                // The game publishes each record by rename; an unchanged directory needs no rescan.
                DateTime stamp = PlayActivityStore.GetHistoryStamp(installation.BuildDirectory);
                if (_ready && stamp == _historyStamp)
                {
                    if (_renderedDay != DateOnly.FromDateTime(DateTime.Today)) Render();
                    return;
                }
                var entries = await PlayActivityStore.LoadAsync(installation.BuildDirectory, load.Token);
                if (load.IsCancellationRequested) return;
                _historyStamp = stamp;
                if (_ready && entries.SequenceEqual(_entries) && _renderedDay == DateOnly.FromDateTime(DateTime.Today)) return;
                _entries = entries;
            }
            else if (_ready) return;

            var players = _entries.Select(p => p.PlayerName).Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCulture).Select(name => new PlayerOption(name, name));
            var options = new[] { new PlayerOption(null, "全プレイヤー") }.Concat(players).ToArray();
            if (PlayerPicker.ItemsSource is not PlayerOption[] current || !options.SequenceEqual(current))
            {
                string? player = (PlayerPicker.SelectedItem as PlayerOption)?.Name;
                _ready = false;
                PlayerPicker.ItemsSource = options;
                PlayerPicker.SelectedIndex = Math.Max(0, Array.FindIndex(options, item => string.Equals(item.Name, player, StringComparison.OrdinalIgnoreCase)));
            }
            PlayerPicker.IsEnabled = PlayerPicker.Items.Count > 1;
            LoadError.Visibility = Visibility.Collapsed;
            LoadError.IsOpen = false;
            _ready = true;
            Render();
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!load.IsCancellationRequested)
            {
                LoadError.Title = "プレイ記録を読み込めません";
                LoadError.Message = ex.Message;
                LoadError.Visibility = Visibility.Visible;
                LoadError.IsOpen = true;
            }
        }
        finally { if (ReferenceEquals(_loading, load)) _loading = null; }
    }

    private void PageRoot_SizeChanged(object sender, SizeChangedEventArgs e) =>
        PageRoot.Padding = (Thickness)Application.Current.Resources[e.NewSize.Width < 820 ? "PageNarrowPadding" : "PageWidePadding"];

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready) Render();
    }

    private void Render()
    {
        Plot.Children.Clear();
        Legend.Children.Clear();
        _cells.Clear();
        _months.Clear();
        _tabStop = _selected = null;
        int mode = Mode;
        _renderedDay = DateOnly.FromDateTime(DateTime.Today);
        var chart = PlayActivity.Build(_entries, _renderedDay, TimeZoneInfo.Local,
            (PlayerPicker.SelectedItem as PlayerOption)?.Name);
        DateOnly gridStart = chart.Weeks[0].Start;
        var periods = mode == 0 ? chart.Days : chart.Weeks;
        int Value(PlayActivityPeriod period) => mode == 2 ? period.CumulativeCount : period.Count;
        int maximum = Math.Max(1, periods.Max(Value));
        var palette = GetPalette();
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        foreach (var period in periods)
        {
            int value = Value(period);
            int index = period.Start.DayNumber - gridStart.DayNumber;
            int column = index / Rows;
            int row = index % Rows;
            int level = value == 0 ? 0 : maximum == 1 ? 1 : 1 + (int)((long)(value - 1) * (palette.Length - 2) / (maximum - 1));
            var button = new Button
            {
                Template = (ControlTemplate)Resources["ActivityCellTemplate"],
                Background = mode == 0 ? palette[level] : transparent,
                BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(3),
                Padding = new Thickness(0), MinWidth = 0, MinHeight = 0, IsTabStop = false,
            };
            if (mode != 0)
            {
                int filled = (int)(((long)value * Rows + maximum - 1) / maximum);
                var stack = new Grid();
                for (int r = 0; r < Rows; r++)
                {
                    stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                    var square = new Border { Background = palette[r >= Rows - filled ? level : 0], CornerRadius = new CornerRadius(3) };
                    Grid.SetRow(square, r);
                    stack.Children.Add(square);
                }
                button.Content = stack;
            }
            string text = $"{PeriodLabel(period, mode)}\n{(mode == 2 ? "累計 " : "")}{value:N0}回";
            ToolTipService.SetToolTip(button, text);
            AutomationProperties.SetName(button, text.Replace('\n', '、'));
            AutomationProperties.SetAutomationId(button, $"PlayCell-{mode}-{period.Start:yyyy-MM-dd}");
            Cell cell = new(button, period, column, row);
            button.Click += (_, _) => Select(cell);
            button.GotFocus += (_, _) => SetTabStop(cell);
            button.PreviewKeyDown += (_, e) => MoveFocus(cell, e);
            _cells.Add(cell);
            Plot.Children.Add(button);
        }

        DateOnly today = chart.Days[^1].End;
        for (int week = 0; week < chart.Weeks.Count; week++)
        {
            DateOnly start = gridStart.AddDays(week * Rows);
            DateOnly month = start.Day == 1 || week == 0 ? start : start.AddDays(1 - start.Day).AddMonths(1);
            if (month.DayNumber >= start.DayNumber + Rows || month > today) continue;
            var label = new TextBlock { Text = $"{month.Month}月", FontSize = 12, Opacity = 0.72, IsHitTestVisible = false };
            _months.Add((label, week));
            Plot.Children.Add(label);
        }
        foreach (SolidColorBrush brush in palette)
            Legend.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Background = brush });
        RangeText.Text = $"{periods[0].Start:yyyy/M/d} – {today:yyyy/M/d}";
        Reflow();
        AlignRecentWeeks();
        Select(_cells.FirstOrDefault(c => _selectedDate.HasValue && c.Period.Start <= _selectedDate.Value && c.Period.End >= _selectedDate.Value)
            ?? _cells.LastOrDefault(c => c.Period.Count > 0) ?? _cells[^1], preserveDate: _selectedDate.HasValue);
    }

    private void PlotScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.PreviousSize.Width >= MinimumPlotWidth && e.NewSize.Width < MinimumPlotWidth) _scrollAtEnd = true;
        Reflow();
        AlignRecentWeeks();
    }

    private void AlignRecentWeeks()
    {
        if (_scrollAtEnd && PlotScroll.ActualWidth > 0 && _cells.Count > 0)
        {
            _scrollAtEnd = false;
            DispatcherQueue.TryEnqueue(() =>
            {
                PlotScroll.UpdateLayout();
                PlotScroll.ChangeView(PlotScroll.ScrollableWidth, null, null, true);
            });
        }
    }

    private void Reflow()
    {
        double width = Math.Max(MinimumPlotWidth, PlotScroll.ActualWidth);
        double gap = width < 560 ? 2 : 3;
        double step = (width - gap * 2) / PlayActivity.WeeksShown;
        Plot.Width = width;
        Plot.Height = step * Rows + 31;
        bool daily = Mode == 0;
        foreach (var cell in _cells)
        {
            cell.Button.Width = step - gap;
            cell.Button.Height = daily ? step - gap : step * Rows - gap;
            if (cell.Button.Content is Grid stack) stack.RowSpacing = gap;
            Canvas.SetLeft(cell.Button, gap + cell.Column * step);
            Canvas.SetTop(cell.Button, 4 + (daily ? cell.Row * step : 0));
        }
        double last = -999;
        foreach (var (label, week) in _months)
        {
            double x = gap + week * step;
            bool visible = x - last >= 31 && x + 31 <= width;
            label.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible) last = x;
            Canvas.SetLeft(label, x);
            Canvas.SetTop(label, step * Rows + 10);
        }
    }

    private void Select(Cell cell, bool preserveDate = false)
    {
        if (_selected is not null) _selected.Button.BorderThickness = new Thickness(0);
        _selected = cell;
        if (!preserveDate) _selectedDate = cell.Period.End;
        cell.Button.BorderBrush = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        cell.Button.BorderThickness = new Thickness(1);
        SetTabStop(cell);
        PeriodText.Text = PeriodLabel(cell.Period, Mode);
        PeriodCountText.Text = Mode == 2 ? $"累計 {cell.Period.CumulativeCount:N0}回　·　この週 {cell.Period.Count:N0}回" : $"{cell.Period.Count:N0}回";
        EmptyText.Text = _entries.Count == 0 ? "プレイ記録がまだありません" : "この期間のプレイはありません";
        EmptyText.Visibility = cell.Period.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PlayList.Visibility = cell.Period.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        PlayList.ItemsSource = cell.Period.Plays.Select(play =>
            $"{play.PlayedAtUtc.ToLocalTime():M/d HH:mm}    {play.SongTitle}    {CourseLabel(play.Course)}    {play.Score:N0}点    {play.PlayerName}").ToArray();
    }

    private void SetTabStop(Cell cell)
    {
        if (_tabStop is not null) _tabStop.Button.IsTabStop = false;
        _tabStop = cell;
        cell.Button.IsTabStop = true;
    }

    private void MoveFocus(Cell source, KeyRoutedEventArgs e)
    {
        int delta = e.Key switch
        {
            VirtualKey.Left => Mode == 0 ? -7 : -1, VirtualKey.Right => Mode == 0 ? 7 : 1,
            VirtualKey.Up when Mode == 0 => -1, VirtualKey.Down when Mode == 0 => 1,
            _ => 0,
        };
        if (delta == 0) return;
        int index = _cells.IndexOf(source) + delta;
        if (index >= 0 && index < _cells.Count) _cells[index].Button.Focus(FocusState.Keyboard);
        e.Handled = true;
    }

    private SolidColorBrush[] GetPalette()
    {
        if (_accessibility.HighContrast)
        {
            var system = new UISettings();
            var background = system.GetColorValue(UIColorType.Background);
            var foreground = system.GetColorValue(UIColorType.Foreground);
            return [new(background), new(foreground), new(foreground), new(foreground), new(foreground)];
        }
        uint[] colors = ActualTheme == ElementTheme.Dark
            ? [0x43454A, 0x165384, 0x0869AD, 0x1D93F2, 0x74BEFF]
            : [0xE8EDF2, 0xA8CDF9, 0x72AEF1, 0x3987D9, 0x0B64BB];
        return colors.Select(rgb => new SolidColorBrush(Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb))).ToArray();
    }

    private static string PeriodLabel(PlayActivityPeriod period, int mode) =>
        mode == 0 ? $"{period.Start:yyyy年M月d日}" : $"{period.Start:yyyy年M月d日}からの週";

    private static string CourseLabel(int course) => course switch
    {
        0 => "かんたん", 1 => "ふつう", 2 => "むずかしい", 3 => "おに", 4 => "おに（裏）", _ => "",
    };

    private sealed record Cell(Button Button, PlayActivityPeriod Period, int Column, int Row);
    private sealed record PlayerOption(string? Name, string Label)
    {
        public override string ToString() => Label;
    }
}
