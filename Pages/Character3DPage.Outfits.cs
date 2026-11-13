using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text;
using System.Text.Json;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Pages;

public sealed partial class Character3DPage
{
    private async void Outfits_Click(object sender, RoutedEventArgs e)
    {
        if (Installation is not { } installation || _loading) return;
        try
        {
            var collection = await LauncherToolkitStore.LoadOutfitsAsync(installation);
            var list = new ListView { ItemsSource = collection.Presets.ToArray(), Height = 220, SelectionMode = ListViewSelectionMode.Single };
            var name = new TextBox { Header = "コーデ名", MaxLength = 80, PlaceholderText = "例: 配信用" };
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var panel = new StackPanel { Spacing = 12, MinWidth = 260 };
            panel.Children.Add(list); panel.Children.Add(name);
            void Add(string label, Func<Task> action)
            {
                var button = new Button { Content = label };
                button.Click += async (_, _) => { button.IsEnabled = false; try { await action(); } catch (Exception ex) { status.Text = ex.Message; } finally { button.IsEnabled = true; } };
                panel.Children.Add(button);
            }
            void Refresh() => list.ItemsSource = collection.Presets.ToArray();
            Add("現在のコーデを名前付きで保存", async () =>
            {
                if (collection.Presets.Count >= 100) throw new InvalidOperationException("コーデは100件まで保存できます。");
                collection.Presets.Add(new OutfitPreset { Name = name.Text.Trim(), Settings = _settings with { } });
                try { await LauncherToolkitStore.SaveOutfitsAsync(installation, collection); }
                catch { collection.Presets.RemoveAt(collection.Presets.Count - 1); throw; }
                Refresh(); status.Text = "コーデを保存しました。";
            });
            Add("選択したコーデを削除", async () =>
            {
                if (list.SelectedItem is not OutfitPreset preset) return;
                collection.Presets.Remove(preset); await LauncherToolkitStore.SaveOutfitsAsync(installation, collection); Refresh();
            });
            Add("選択したコーデを書き出す", async () =>
            {
                if (list.SelectedItem is not OutfitPreset preset) throw new InvalidOperationException("コーデを選択してください。");
                if (await LauncherFilePicker.SaveFileAsync("TaikoDive-outfit", ".json") is { } path)
                {
                    var export = new OutfitCollection { Presets = [new OutfitPreset { Name = preset.Name, Settings = preset.Settings with { ModelsPath = "Info/Chara/Model" } }] };
                    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(export, LauncherJsonContext.Default.OutfitCollection), new UTF8Encoding(false));
                }
            });
            Add("コーデファイルを取り込む", async () =>
            {
                if (await LauncherFilePicker.OpenFileAsync(".json") is not { } path) return;
                if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("コーデファイルは1MBまでです。");
                var imported = LauncherToolkitStore.ParseOutfits(await File.ReadAllTextAsync(path));
                if (collection.Presets.Count + imported.Presets.Count > 100) throw new InvalidOperationException("保存できるコーデは100件までです。");
                foreach (var preset in imported.Presets) preset.Id = Guid.NewGuid().ToString("N");
                collection.Presets.AddRange(imported.Presets); await LauncherToolkitStore.SaveOutfitsAsync(installation, collection); Refresh();
                status.Text = "取り込みました。選択すると必要素材の有無を表示します。";
            });
            panel.Children.Add(status);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "コーデ保存・共有", Content = new ScrollViewer { Content = panel, MaxHeight = 550 },
                PrimaryButtonText = "選択したコーデを使う", CloseButtonText = "閉じる", IsPrimaryButtonEnabled = false };
            list.SelectionChanged += (_, _) =>
            {
                if (list.SelectedItem is not OutfitPreset preset) { dialog.IsPrimaryButtonEnabled = false; return; }
                var missing = LauncherToolkitStore.MissingAssets(installation, preset.Settings, _settings.ModelsPath);
                status.Text = missing.Count == 0 ? "必要な素材がそろっています。" : "不足している素材: " + string.Join("、", missing);
                dialog.IsPrimaryButtonEnabled = missing.Count == 0;
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && list.SelectedItem is OutfitPreset chosen)
            {
                _settings = chosen.Settings with { ModelsPath = _settings.ModelsPath };
                _modeChosen = true; _loading = true; LoadCatalog(); UpdateColors(); UpdateMode(); _loading = false;
                HasUnsavedChanges = true; UpdateEditState(); QueuePreview();
                StatusBar.Message = "コーデを読み込みました。プレビューを確認して保存してください。"; StatusBar.IsOpen = true;
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }
}
