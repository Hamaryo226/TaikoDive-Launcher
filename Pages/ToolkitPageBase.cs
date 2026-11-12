using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using TaikoDiveLauncher.Services;
using Windows.Storage.Pickers;

namespace TaikoDiveLauncher.Pages;

public class ToolkitPageBase : Page
{
    protected readonly StackPanel Body = new() { Spacing = 16 };
    protected readonly InfoBar Status = new() { IsClosable = true };
    protected App Host => (App)Application.Current;
    protected TaikoDiveInstallation Installation => Host.Context.Installation ?? throw new InvalidOperationException("ゲーム本体が見つかりません。");

    protected ToolkitPageBase(string title, string description)
    {
        var root = new Grid { Padding = new Thickness(24), RowSpacing = 12, MaxWidth = 1200, HorizontalAlignment = HorizontalAlignment.Stretch };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var heading = new StackPanel { Spacing = 6 };
        heading.Children.Add(Text(title, 28)); heading.Children.Add(Text(description));
        root.Children.Add(heading); Grid.SetRow(Status, 1); root.Children.Add(Status);
        var scroll = new ScrollViewer { Content = Body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Grid.SetRow(scroll, 2); root.Children.Add(scroll); Content = root;
    }

    protected static TextBlock Text(string value, double size = 14) => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };

    protected Button Action(string label, Func<Task> action, Panel? parent = null)
    {
        var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(button, label);
        button.Click += async (_, _) => { button.IsEnabled = false; try { await action(); } catch (Exception ex) { Message(ex.Message, true); } finally { button.IsEnabled = true; } };
        (parent ?? Body).Children.Add(button); return button;
    }

    protected void Message(string message, bool error = false) { Status.Message = message; Status.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Informational; Status.IsOpen = true; }

    protected async Task<bool> Confirm(string title, string message)
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = new ScrollViewer { Content = Text(message), MaxHeight = 400 }, PrimaryButtonText = "実行", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Close };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    internal static async Task<string?> SaveFileAsync(string suggestedName, string extension)
    {
        var picker = new FileSavePicker { SuggestedFileName = suggestedName };
        picker.FileTypeChoices.Add(extension, new List<string> { extension });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        return (await picker.PickSaveFileAsync())?.Path;
    }

    internal static async Task<string?> OpenFileAsync(string extension)
    {
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        return (await picker.PickSingleFileAsync())?.Path;
    }
}
