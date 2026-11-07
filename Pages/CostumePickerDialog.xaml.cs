using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Pages;

public sealed partial class CostumePickerDialog : ContentDialog
{
    private readonly IReadOnlyList<CostumeOption> _options;
    private bool _filtering;
    public CostumeOption? SelectedOption { get; private set; }

    public CostumePickerDialog(string title, IReadOnlyList<CostumeOption> options, string selectedId)
    {
        InitializeComponent();
        Title = title;
        _options = options;
        SelectedOption = options.FirstOrDefault(x => x.Id == selectedId);
        ApplyFilter();
        Opened += (_, _) =>
        {
            UpdateSize();
            XamlRoot.Changed += Root_Changed;
            DispatcherQueue.TryEnqueue(() =>
            {
                OptionsGrid.UpdateLayout();
                if (OptionsGrid.SelectedItem is { } selected)
                    OptionsGrid.ScrollIntoView(selected, ScrollIntoViewAlignment.Leading);
            });
        };
        Closed += (_, _) => XamlRoot.Changed -= Root_Changed;
    }

    private void Root_Changed(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateSize();
    private void UpdateSize()
    {
        PickerContent.Width = Math.Clamp(XamlRoot.Size.Width - 112, 280, 900);
        PickerContent.Height = Math.Clamp(XamlRoot.Size.Height - 240, 180, 560);
    }
    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_options is not null) ApplyFilter();
    }
    private void ApplyFilter()
    {
        string query = SearchBox.Text.Trim();
        var visible = _options.Where(x => x.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || x.Id.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        _filtering = true;
        OptionsGrid.ItemsSource = visible;
        OptionsGrid.SelectedItem = visible.FirstOrDefault(x => x.Id == SelectedOption?.Id);
        _filtering = false;
        IsPrimaryButtonEnabled = OptionsGrid.SelectedItem is CostumeOption;
        CountText.Text = $"{visible.Length} / {_options.Count} 件 · 画像を選んで「選択する」";
        EmptyText.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Options_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filtering) return;
        SelectedOption = OptionsGrid.SelectedItem as CostumeOption;
        IsPrimaryButtonEnabled = SelectedOption is not null;
    }
}
