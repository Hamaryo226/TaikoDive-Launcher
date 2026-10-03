using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Controls;

public sealed partial class SongScoreDialog : ContentDialog
{
    private XamlRoot? _root;

    public SongScoreDialog(RecentSong song, IReadOnlyList<SongBestResult> results, ImageSource[] crownImages)
    {
        InitializeComponent();
        TextBlock title = new() { Text = song.Title, TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTipService.SetToolTip(title, song.Title);
        Title = title;
        string[] names = ["かんたん", "ふつう", "むずかしい", "おに", "裏"];
        SongDifficultyItem[] items = results.Select((result, index) =>
            new SongDifficultyItem(names[index], result, crownImages)).ToArray();
        DifficultyList.ItemsSource = items;
        DifficultyList.SelectedItem = items.LastOrDefault(item => item.Result.HasRecord) ?? items.FirstOrDefault();
    }

    private void Dialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        _root = XamlRoot;
        if (_root is not null) _root.Changed += Root_Changed;
        UpdateAvailableSize();
        UpdateContentLayout(DialogContent.ActualWidth);
        // Start keyboard navigation on the selected difficulty rather than the close button.
        if (DifficultyList.ContainerFromItem(DifficultyList.SelectedItem) is Control selected)
            selected.Focus(FocusState.Programmatic);
    }

    private void Dialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        if (_root is not null) _root.Changed -= Root_Changed;
        _root = null;
    }

    private void Root_Changed(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateAvailableSize();

    private void UpdateAvailableSize()
    {
        if (_root is null) return;
        ContentScroll.Width = Math.Clamp(_root.Size.Width - 80, 220, 480);
        ContentScroll.MaxHeight = Math.Clamp(_root.Size.Height - 220, 160, 600);
    }

    private void DialogContent_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateContentLayout(e.NewSize.Width);

    private void UpdateContentLayout(double width)
    {
        bool narrow = width < 360;
        Grid.SetColumn(CrownSummary, narrow ? 0 : 1);
        Grid.SetRow(CrownSummary, narrow ? 1 : 0);
        CrownSummary.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        if (DifficultyList.ItemsPanelRoot is ItemsWrapGrid panel)
        {
            int columns = narrow ? 3 : 5;
            panel.MaximumRowsOrColumns = columns;
            panel.ItemWidth = Math.Max(64, (width - 4) / columns);
        }
    }

    private void DifficultyList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.Item is SongDifficultyItem item)
            AutomationProperties.SetName(args.ItemContainer, item.Description);
    }

    private void DifficultyList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DifficultyList.SelectedItem is not SongDifficultyItem item) return;
        SongBestResult result = item.Result;
        SelectedDifficultyText.Text = result.Difficulty;
        BestScoreText.Text = result.HasRecord ? result.Score.ToString("N0") : "—";
        ScoreUnitText.Visibility = result.HasRecord ? Visibility.Visible : Visibility.Collapsed;
        SelectedCrownImage.Source = item.CrownImage;
        SelectedCrownImage.Visibility = item.ImageVisibility;
        CrownText.Text = result.HasRecord ? result.CrownLabel : "記録なし";
        ScoreRankText.Text = $"スコアランク: {result.ScoreRank}";
        CrownSummary.Visibility = result.HasRecord ? Visibility.Visible : Visibility.Collapsed;
        EmptyRecordBar.IsOpen = !result.HasRecord;
        EmptyRecordBar.Visibility = result.HasRecord ? Visibility.Collapsed : Visibility.Visible;
        BreakdownPanel.Visibility = result.HasRecord ? Visibility.Visible : Visibility.Collapsed;
        GreatText.Text = result.Great.ToString("N0");
        GoodText.Text = result.Good.ToString("N0");
        MissText.Text = result.Miss.ToString("N0");
        RollText.Text = result.RollCount.ToString("N0");
        ComboText.Text = result.MaxCombo.ToString("N0");
        GaugeText.Text = $"{result.Gauge:0.##}%";
        GaugeBar.Value = double.IsFinite(result.Gauge) ? Math.Clamp(result.Gauge, 0, 100) : 0;
        AutomationProperties.SetName(ScoreSummary, item.Description);
    }
}

public sealed class SongDifficultyItem
{
    public SongDifficultyItem(string shortName, SongBestResult result, ImageSource[] crownImages)
    {
        ShortName = shortName;
        Result = result;
        int index = result.Crown switch { "Clear" => 0, "FullCombo" => 1, "DondaFullCombo" => 2, _ => -1 };
        CrownImage = index >= 0 && index < crownImages.Length ? crownImages[index] : null;
        CrownFallback = index switch { 0 => "銀", 1 => "金", 2 => "虹", _ => "—" };
    }

    public SongBestResult Result { get; }
    public string ShortName { get; }
    public ImageSource? CrownImage { get; }
    public string CrownFallback { get; }
    public string ScoreText => Result.HasRecord ? Result.Score.ToString("N0") : "未プレイ";
    public string RankText => Result.HasRecord && Result.ScoreRank != "なし" ? Result.ScoreRank : string.Empty;
    public string Description => Result.HasRecord
        ? $"{Result.Difficulty}、{Result.Score:N0}点、王冠 {Result.CrownLabel}、スコアランク {Result.ScoreRank}"
        : $"{Result.Difficulty}、記録なし";
    public Visibility ImageVisibility => CrownImage is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility FallbackVisibility => CrownImage is null ? Visibility.Visible : Visibility.Collapsed;
    public override string ToString() => Description;
}
