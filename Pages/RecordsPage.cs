using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Pages;

public sealed class RecordsPage : ToolkitPageBase
{
    private readonly CalendarView _calendar = new() { SelectionMode = CalendarViewSelectionMode.Single, MaxWidth = 440, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ComboBox _user = new() { Header = "ユーザー", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _day = Text("");
    private readonly ListView _replays = new() { Height = 280 };
    private readonly TextBox _search = new() { Header = "リプレイを検索（曲名・ユーザー・難易度）" };
    private IReadOnlyList<PlayHistoryEntry> _history = [];
    private IReadOnlyList<ReplayEntry> _entries = [];

    public RecordsPage() : base("プレイ記録", "プレイカレンダーは対応版から記録します。以前の自己ベストの更新日時をプレイ回数として数えません。")
    {
        Body.Children.Add(_user); Body.Children.Add(_calendar); Body.Children.Add(_day);
        Body.Children.Add(Text("リプレイライブラリ", 20)); Body.Children.Add(_search); Body.Children.Add(_replays);
        Action("選択したリプレイを再生", async () =>
        {
            if (_replays.SelectedItem is not ReplayEntry entry) throw new InvalidOperationException("リプレイを選択してください。");
            if (entry.Problem is not null) throw new InvalidOperationException(entry.Problem);
            if (!File.Exists(Path.Combine(Installation.BuildDirectory, "Info", "launcher-capabilities.json")))
                throw new InvalidOperationException("リプレイ起動に対応したTaikoDive本体へ更新してください。");
            var result = GameProcessService.Launch(Installation, ["--launcher-replay", entry.FilePath, "--user-slot", entry.UserSlot.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            Message(result.Message, !result.Succeeded);
            if (result.Succeeded && Host.Context.Preferences.CloseAfterLaunch) Host.Exit();
            await Task.CompletedTask;
        });
        Action("再読み込み", Refresh);
        _search.TextChanged += (_, _) => Filter(); _user.SelectionChanged += (_, _) => { Filter(); UpdateDay(); };
        _calendar.SelectedDatesChanged += (_, _) => UpdateDay();
        _calendar.CalendarViewDayItemChanging += (_, e) =>
        {
            if (e.Phase == 0)
                e.Item.SetDensityColors(DayEntries(e.Item.Date.Date).Any() ? new[] { Windows.UI.Color.FromArgb(255, 0, 167, 190) } : Array.Empty<Windows.UI.Color>());
        };
        Loaded += async (_, _) => { try { await Refresh(); } catch (Exception ex) { Message(ex.Message, true); } };
    }

    private IEnumerable<PlayHistoryEntry> DayEntries(DateTime date) => _history.Where(e => e.PlayedAtUtc.ToLocalTime().Date == date && (_user.SelectedItem as string == "全ユーザー" || e.PlayerName == _user.SelectedItem as string));
    private void UpdateDay()
    {
        var entries = DayEntries(_calendar.SelectedDates.FirstOrDefault(DateTimeOffset.Now).Date).ToArray();
        _day.Text = $"{entries.Length}プレイ / {entries.Select(e => e.SongTitle).Distinct().Count()}曲\n" + string.Join("\n", entries.Select(e => $"{e.SongTitle} — {e.Score:N0}点"));
        // 表示セルを作り直してユーザー切替を反映。
        var display = _calendar.SelectedDates.FirstOrDefault(DateTimeOffset.Now); _calendar.SetDisplayDate(display);
    }
    private void Filter() => _replays.ItemsSource = _entries.Where(e => e.Label.Contains(_search.Text, StringComparison.CurrentCultureIgnoreCase) && (_user.SelectedItem as string == "全ユーザー" || e.PlayerName == _user.SelectedItem as string)).ToArray();
    private async Task Refresh()
    {
        _history = await LauncherToolkitStore.ReadHistoryAsync(Installation);
        _entries = await SongLibraryService.ReadReplaysAsync(Installation);
        _user.ItemsSource = new[] { "全ユーザー" }.Concat(_history.Select(e => e.PlayerName)).Concat(_entries.Select(e => e.PlayerName)).Distinct().ToArray(); _user.SelectedIndex = 0;
        Filter(); UpdateDay();
    }
}
