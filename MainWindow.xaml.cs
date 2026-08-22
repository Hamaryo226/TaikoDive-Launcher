using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace TaikoDiveLauncher;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(1180, 760));
        RootFrame.Navigate(typeof(MainPage));
    }

    public nint GetWindowHandle() => WindowNative.GetWindowHandle(this);
}
