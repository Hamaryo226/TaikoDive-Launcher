using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Pages;

public sealed partial class ToolkitPage : ToolkitPageBase
{
    private readonly ComboBox _section = new() { Header = "設定する機能", HorizontalAlignment = HorizontalAlignment.Stretch,
        ItemsSource = new[] { "ホームの配置", "Discord・OBS・履歴", "容量と整理", "診断レポート", "互換性と更新履歴", "初回セットアップ" }, SelectedIndex = 0 };
    private readonly StackPanel _sectionBody = new() { Spacing = 12 };

    public ToolkitPage() : base("ランチャーツール", "ホームの表示、配信連携、データの確認とメンテナンスを行います。")
    {
        Body.Children.Add(_section); Body.Children.Add(_sectionBody);
        _section.SelectionChanged += async (_, _) => await Render();
        Loaded += async (_, _) => await Render();
    }

    private async Task Render()
    {
        _sectionBody.Children.Clear(); _section.IsEnabled = false;
        try
        {
            switch (_section.SelectedIndex)
            {
                case 0: HomeLayout(); break;
                case 1: await Integrations(); break;
                case 2: Storage(); break;
                case 3: Diagnostics(); break;
                case 4: await Updates(); break;
                default:
                    _sectionBody.Children.Add(Text("名前・入力・音量・画面を順に設定します。いつでも途中からやり直せます。"));
                    Action("セットアップを始める", () => { MainPage.Current?.NavigateTo("setup"); return Task.CompletedTask; }, _sectionBody); break;
            }
        }
        catch (Exception ex) { Message(ex.Message, true); }
        finally { _section.IsEnabled = true; }
    }

    private void HomeLayout()
    {
        string Label(string key) => key switch { "character" => "どんちゃん", "activity" => "最近の曲", "calendar" => "プレイカレンダー", _ => "更新情報" };
        var preferences = Host.Context.Preferences;
        var keys = preferences.HomeCards.Where(k => new[] { "character", "activity", "calendar", "updates" }.Contains(k)).Distinct().ToList();
        foreach (string key in new[] { "character", "activity", "calendar", "updates" }) if (!keys.Contains(key)) keys.Add(key);
        var list = new ListView { ItemsSource = keys.Select(Label).ToArray(), Height = 200 };
        _sectionBody.Children.Add(list);
        var visible = new CheckBox { Content = "選択したカードを表示" }; _sectionBody.Children.Add(visible);
        list.SelectionChanged += (_, _) => { if (list.SelectedIndex >= 0) visible.IsChecked = !preferences.HiddenHomeCards.Contains(keys[list.SelectedIndex]); };
        visible.Click += async (_, _) =>
        {
            if (list.SelectedIndex < 0) return;
            string key = keys[list.SelectedIndex];
            preferences.HiddenHomeCards.RemoveAll(k => k == key);
            if (visible.IsChecked != true) preferences.HiddenHomeCards.Add(key);
            try { await Host.Context.SavePreferencesAsync(); Message("ホームの表示を保存しました。"); } catch (Exception ex) { Message(ex.Message, true); }
        };
        async Task Move(int direction)
        {
            int index = list.SelectedIndex, next = index + direction;
            if (index < 0 || next < 0 || next >= keys.Count) return;
            (keys[index], keys[next]) = (keys[next], keys[index]);
            preferences.HomeCards = keys.ToList(); await Host.Context.SavePreferencesAsync(); HomeLayoutRefresh();
        }
        void HomeLayoutRefresh() { _sectionBody.Children.Clear(); HomeLayout(); }
        Action("上へ", () => Move(-1), _sectionBody); Action("下へ", () => Move(1), _sectionBody);
        list.SelectedIndex = 0;
    }

    private async Task Integrations()
    {
        var settings = await LauncherToolkitStore.LoadIntegrationsAsync(Installation);
        var discord = new ToggleSwitch { Header = "Discordにプレイ状況を表示", IsOn = settings.DiscordEnabled };
        var song = new ToggleSwitch { Header = "Discordに曲名・難易度も表示", IsOn = settings.DiscordShowSong };
        var overlay = new ToggleSwitch { Header = "OBS用オーバーレイを出力", IsOn = settings.OverlayEnabled };
        var history = new ToggleSwitch { Header = "プレイ履歴を記録", IsOn = settings.RecordHistory };
        var goal = new TextBox { Header = "配信に表示する目標", Text = settings.Goal, MaxLength = 200 };
        foreach (var element in new FrameworkElement[] { discord, song, overlay, history, goal }) _sectionBody.Children.Add(element);
        _sectionBody.Children.Add(Text("次回の本体起動から反映します。Discordはアプリ起動中に状態を公開します。OBSでは、ゲームフォルダーの Info/TaikoDiveLauncher/overlay/index.html をブラウザーソースのローカルファイルに指定してください。"));
        Action("連携設定を保存", async () =>
        {
            settings.DiscordEnabled = discord.IsOn; settings.DiscordShowSong = song.IsOn;
            settings.OverlayEnabled = overlay.IsOn; settings.RecordHistory = history.IsOn; settings.Goal = goal.Text;
            await LauncherToolkitStore.SaveIntegrationsAsync(Installation, settings);
            Message("保存しました。対応した本体の次回起動から反映します。");
        }, _sectionBody);
        Action("OBS用フォルダーを開く", () =>
        {
            string path = Path.Combine(LauncherToolkitStore.Root(Installation), "overlay");
            Directory.CreateDirectory(path); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            return Task.CompletedTask;
        }, _sectionBody);
    }

    private void Storage()
    {
        var results = new ListView { Height = 350 }; _sectionBody.Children.Add(results);
        Action("容量を計測", async () => { results.ItemsSource = await LauncherMaintenance.MeasureAsync(Installation); Message("更新バックアップは、履歴からの復帰に使うため保持します。"); }, _sectionBody);
        Action("7日以上前のダウンロード・一時ファイルを整理", async () =>
        {
            EnsureUpdatesIdle();
            if (await Confirm("キャッシュを整理", "7日以上前のダウンロード済みファイルと一時ファイルを削除します。必要な更新は再ダウンロードできます。"))
            {
                Message($"{await LauncherMaintenance.CleanOldCacheAsync(Installation)}ファイルを整理しました。");
                results.ItemsSource = await LauncherMaintenance.MeasureAsync(Installation);
            }
        }, _sectionBody);
    }

    private void Diagnostics()
    {
        var preview = new TextBox { Header = "共有前プレビュー", IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 350 };
        _sectionBody.Children.Add(preview);
        _sectionBody.Children.Add(Text("プロフィール・スコア・入力割当・接続先設定は含めません。エラーログの内容は確認してから共有してください。"));
        Action("レポートを作成", async () => preview.Text = await LauncherMaintenance.DiagnosticReportAsync(Host.Context), _sectionBody);
        Action("表示中のレポートを保存", async () =>
        {
            if (preview.Text.Length == 0) throw new InvalidOperationException("先にレポートを作成してください。");
            if (await SaveFileAsync("TaikoDive-diagnostics", ".json") is { } path) await File.WriteAllTextAsync(path, preview.Text, new UTF8Encoding(false));
        }, _sectionBody);
    }

    private async Task Updates()
    {
        var context = Host.Context;
        _sectionBody.Children.Add(Text($"ランチャー: {context.Updates.CurrentVersionText}\n本体: {context.GameUpdates.CurrentVersionText}\nAsset: {context.AssetUpdates.CurrentVersionText}"));
        foreach (var (label, service) in new[] { ("本体", context.GameUpdates), ("Asset", context.AssetUpdates) })
        {
            _sectionBody.Children.Add(Text(label + "の更新履歴", 20));
            var list = new ListView { ItemsSource = await service.GetHistoryAsync(), Height = 180 }; _sectionBody.Children.Add(list);
            if (service.LatestUpdate is { } manifest)
            {
                var problems = LauncherMaintenance.CheckCompatibility(manifest, context.GameUpdates.CurrentVersionText, context.AssetUpdates.CurrentVersionText, LauncherBuildInfo.InformationalVersion);
                _sectionBody.Children.Add(Text(problems.Count > 0 ? string.Join("\n", problems) : "配信マニフェストの最低バージョン条件を満たしています。条件未指定の組み合わせは動作保証を意味しません。"));
            }
            Action(label + "を更新前へ戻す", async () =>
            {
                if (list.SelectedItem is not UpdateHistoryEntry entry) throw new InvalidOperationException("更新履歴を選択してください。");
                if (!await Confirm("更新前の状態へ復帰", "選択した更新が置き換えたファイルをバックアップから復元します。更新後に加えた同じファイルへの変更も戻ります。")) return;
                var result = await service.RestorePreviousAsync(entry.Id); Message(result.Message, !result.Succeeded); list.ItemsSource = await service.GetHistoryAsync();
            }, _sectionBody);
        }
        _sectionBody.Children.Add(Text("ランチャーの更新履歴", 20));
        var launcherHistory = new ListView { Height = 150, ItemsSource = await context.Updates.GetRollbackHistoryAsync() };
        _sectionBody.Children.Add(launcherHistory);
        Action("ランチャーを更新前へ戻す", async () =>
        {
            if (launcherHistory.SelectedItem is not LauncherRollbackEntry entry) throw new InvalidOperationException("更新履歴を選択してください。");
            if (!await Confirm("ランチャーを復帰", "更新前のランチャーへ戻して再起動します。")) return;
            var result = await context.Updates.RestoreLauncherAsync(entry.Id); Message(result.Message, !result.Succeeded);
            if (result.Succeeded) Host.Exit();
        }, _sectionBody);
        Action("更新を確認", async () => { await context.GameUpdates.CheckAsync(); await context.AssetUpdates.CheckAsync(); await Render(); }, _sectionBody);
    }

    private void EnsureUpdatesIdle()
    {
        var context = Host.Context;
        if (new[] { context.GameUpdates.State, context.AssetUpdates.State }.Any(s => s is GameUpdateState.Downloading or GameUpdateState.Verifying or GameUpdateState.Applying)
            || context.Updates.State is LauncherUpdateState.Downloading or LauncherUpdateState.StartingInstaller)
            throw new InvalidOperationException("アップデート処理の完了後に整理してください。");
    }
}
