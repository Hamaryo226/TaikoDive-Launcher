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
        if (LauncherUpdateService.TryParseApplyCommand(Environment.GetCommandLineArgs(), out PendingUpdateCommand? command))
        {
            await LauncherUpdateService.ApplyPendingUpdateAsync(command!);
            Exit();
            return;
        }

        await Context.InitializeAsync();
        MainWindow = new MainWindow();
        MainWindow.Activate();
        _ = LauncherUpdateService.CleanupStagedUpdatesAsync();

        if (!string.Equals(
                Environment.GetEnvironmentVariable("TAIKODIVE_LAUNCHER_DISABLE_UPDATE_CHECK"),
                "1",
                StringComparison.Ordinal))
        {
            _ = Context.Updates.CheckAsync();
            _ = Context.GameUpdates.CheckAsync();
        }
    }
}
