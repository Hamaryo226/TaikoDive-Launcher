using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace TaikoDiveLauncher;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ApplyTheme(((App)Application.Current).Context.Preferences.Theme);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(1180, 760));
        RootFrame.Navigate(typeof(MainPage));
    }

    public void ApplyTheme(string theme)
    {
        RootLayout.RequestedTheme = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
            ? ElementTheme.Light
            : ElementTheme.Dark;
    }

    public nint GetWindowHandle() => WindowNative.GetWindowHandle(this);
}
