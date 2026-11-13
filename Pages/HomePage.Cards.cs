using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TaikoDiveLauncher.Pages;

public sealed partial class HomePage
{
    private Border? _updatesCard;
    private TextBlock? _updateSummary;

    private void LoadHomeCards()
    {
        try
        {
            if (AppInstance.Context.Installation is not { } installation) return;
            if (_previewCancellation?.IsCancellationRequested == true) return;
            _updateSummary = new TextBlock { TextWrapping = TextWrapping.Wrap };
            _updatesCard = CreateCard("更新情報", _updateSummary, "launcher-settings");
            AppInstance.Context.GameUpdates.StateChanged += HomeUpdatesChanged;
            AppInstance.Context.AssetUpdates.StateChanged += HomeUpdatesChanged;
            AppInstance.Context.Updates.StateChanged += HomeUpdatesChanged;
            RefreshHomeUpdateText();
            SummaryGrid.Children.Add(_updatesCard); ApplyHomeCards();
        }
        catch (Exception ex) { ShowStatus(InfoBarSeverity.Warning, "ホームカードを読み込めません: " + ex.Message); }
    }

    private void HomeUpdatesChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(RefreshHomeUpdateText);

    private void RefreshHomeUpdateText()
    {
        if (_updateSummary is null) return;
        var context = AppInstance.Context;
        _updateSummary.Text = $"本体: {context.GameUpdates.CurrentVersionText} — {context.GameUpdates.StatusMessage}\nAsset: {context.AssetUpdates.CurrentVersionText} — {context.AssetUpdates.StatusMessage}\nランチャー: {context.Updates.CurrentVersionText} — {context.Updates.StatusMessage}";
    }

    private void DetachHomeUpdates()
    {
        AppInstance.Context.GameUpdates.StateChanged -= HomeUpdatesChanged;
        AppInstance.Context.AssetUpdates.StateChanged -= HomeUpdatesChanged;
        AppInstance.Context.Updates.StateChanged -= HomeUpdatesChanged;
    }

    private Border CreateCard(string title, UIElement content, string? tag = null)
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, TextWrapping = TextWrapping.Wrap }); panel.Children.Add(content);
        if (tag is not null)
        {
            var button = new Button { Content = "詳しく見る" };
            button.Click += (_, _) => MainPage.Current?.NavigateTo(tag);
            panel.Children.Add(button);
        }
        return new Border { Child = panel, Padding = new Thickness(16), Style = (Style)Application.Current.Resources["QuietSurfaceStyle"] };
    }

    private void ApplyHomeCards()
    {
        FrameworkElement?[] cards = [CharacterSurface, ActivitySurface, _updatesCard];
        SummaryPrimaryColumn.Width = new GridLength(1, GridUnitType.Star); SummarySecondaryColumn.Width = new GridLength(0);
        SummaryGrid.RowDefinitions.Clear(); int row = 0;
        foreach (var element in cards)
        {
            if (element is null) continue;
            SummaryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(element, 0); Grid.SetColumnSpan(element, 2); Grid.SetRow(element, row++);
        }
    }
}
