using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace TaikoDiveLauncher.Pages;

public sealed class LibraryPage : ToolkitPageBase
{
    private readonly TextBox _search = new() { Header = "曲名・ファイル名で絞り込み", PlaceholderText = "検索" };
    private readonly ListView _list = new() { Height = 360, SelectionMode = ListViewSelectionMode.Single };
    private readonly TextBlock _details = Text("");
    private readonly MediaPlayer _player = new() { AutoPlay = false, Volume = 0.5 };
    private readonly DispatcherTimer _stopTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly CancellationTokenSource _cancellation = new();
    private IReadOnlyList<LibrarySong> _songs = [];
    private double _demoStart;

    public LibraryPage() : base("楽曲ライブラリ", "音源を試聴したり、同じ譜面や不足ファイルを確認できます。検査はファイルを書き換えません。")
    {
        Body.Children.Add(_search); Body.Children.Add(_list); Body.Children.Add(_details);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; Body.Children.Add(actions);
        Action("再読み込み", Scan, actions);
        Action("30秒試聴", async () =>
        {
            if (_list.SelectedItem is not LibrarySong song || song.AudioPath is null) throw new InvalidOperationException("音源のある曲を選択してください。");
            if (GameProcessService.IsRunning()) throw new InvalidOperationException("ゲームを終了してから試聴してください。");
            _player.Pause(); _demoStart = song.DemoStart; _player.Source = MediaSource.CreateFromUri(new Uri(song.AudioPath)); await Task.CompletedTask;
        }, actions);
        Action("停止", () => { Stop(); return Task.CompletedTask; }, actions);
        Action("不足・譜面形式を確認", () =>
        {
            _list.ItemsSource = _songs.Where(s => s.Problems.Count > 0).ToArray();
            Message($"問題候補: {_songs.Count(s => s.Problems.Count > 0)}曲。曲を選択すると詳細を表示します。"); return Task.CompletedTask;
        });
        Action("重複を確認", () =>
        {
            var groups = SongLibraryService.FindDuplicates(_songs);
            _list.ItemsSource = groups.SelectMany(g => g).ToArray();
            Message($"同一内容の譜面: {groups.Count}組。音源の違いや必要な配置を確認してから整理してください。"); return Task.CompletedTask;
        });
        _search.TextChanged += (_, _) => Filter();
        _list.SelectionChanged += (_, _) => { Stop(); _details.Text = _list.SelectedItem is LibrarySong s ? s.ChartPath + "\n" + string.Join("\n", s.Problems) : ""; };
        _player.MediaOpened += (_, _) => DispatcherQueue.TryEnqueue(() => { _player.PlaybackSession.Position = TimeSpan.FromSeconds(_demoStart); _player.Play(); _stopTimer.Start(); });
        _player.MediaFailed += (_, e) => DispatcherQueue.TryEnqueue(() => { Stop(); Message("試聴できません: " + e.ErrorMessage, true); });
        _stopTimer.Tick += (_, _) => Stop();
        Loaded += async (_, _) => { try { await Scan(); } catch (Exception ex) { Message(ex.Message, true); } };
        Unloaded += (_, _) => { _cancellation.Cancel(); Stop(); _player.Dispose(); };
    }

    private void Stop() { _stopTimer.Stop(); _player.Pause(); }
    private void Filter() => _list.ItemsSource = _songs.Where(s => s.Label.Contains(_search.Text, StringComparison.CurrentCultureIgnoreCase)).ToArray();
    private async Task Scan()
    {
        Message("楽曲を確認しています…");
        _songs = await SongLibraryService.ScanAsync(Installation.SongsDirectory, _cancellation.Token);
        Filter(); Message($"{_songs.Count:N0}曲を読み込みました。");
    }
}
