using Microsoft.UI.Xaml;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher;

public partial class App : Application
{
    public static MainWindow MainWindow { get; private set; } = null!;

    public LauncherContext Context { get; } = new();

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        await Context.InitializeAsync();
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
