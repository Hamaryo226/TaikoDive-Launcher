using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Pages;

public sealed partial class LauncherSettingsPage
{
    private async Task InstallManagedAsync(GameUpdateService service)
    {
        try
        {
            if (GameProcessService.IsRunning()) throw new InvalidOperationException("TaikoDiveを終了してから更新してください。");
            UpdatePreview preview = await service.PreviewAsync();
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = $"v{preview.Version} / 作業領域とバックアップの見積: {preview.RequiredBytes / 1048576d:N1} MB\n追加 {preview.Changes.Count(c => c.Kind == "追加")}件・変更 {preview.Changes.Count(c => c.Kind == "変更")}件・削除 {preview.Changes.Count(c => c.Kind == "削除")}件", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new ListView { Height = 280, SelectionMode = ListViewSelectionMode.None,
                ItemsSource = preview.Changes.Select(c => $"{c.Kind}  {c.Path}").ToArray() });
            panel.Children.Add(new TextBlock { Text = "設定・プロフィール・スコア・楽曲は保護対象です。置き換えるファイルは更新前にバックアップします。", TextWrapping = TextWrapping.Wrap });
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "更新内容を確認", Content = panel,
                PrimaryButtonText = "適用", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            if (service.AvailableUpdate?.Revision != preview.Revision) throw new InvalidOperationException("更新内容が変わりました。もう一度確認してください。");
            OperationResult result = await service.DownloadAndApplyAsync();
            ShowStatus(result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error, result.Message);
        }
        catch (Exception ex) { ShowStatus(InfoBarSeverity.Error, ex.Message); }
    }
}
