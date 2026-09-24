using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TaikoDiveLauncher.Controls;

/// <summary>
/// A row of buttons that jump to the launcher's main pages, hiding the page it is placed on.
/// </summary>
public sealed partial class PageNavigationBar : UserControl
{
    public static readonly DependencyProperty CurrentTagProperty = DependencyProperty.Register(
        nameof(CurrentTag),
        typeof(string),
        typeof(PageNavigationBar),
        new PropertyMetadata(string.Empty, (d, _) => ((PageNavigationBar)d).UpdateButtons()));

    private readonly List<Button> _buttons;

    public PageNavigationBar()
    {
        InitializeComponent();
        _buttons = ButtonsPanel.Children.OfType<Button>().ToList();
        UpdateButtons();
    }

    public string CurrentTag
    {
        get => (string)GetValue(CurrentTagProperty);
        set => SetValue(CurrentTagProperty, value);
    }

    private void UpdateButtons()
    {
        if (_buttons is null)
        {
            return;
        }

        ButtonsPanel.Children.Clear();
        foreach (Button button in _buttons.Where(button => button.Tag as string != CurrentTag))
        {
            ButtonsPanel.Children.Add(button);
        }
    }

    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag })
        {
            MainPage.Current?.NavigateTo(tag);
        }
    }
}
