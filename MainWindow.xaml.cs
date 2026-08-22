using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;
using Windows.Graphics;
using WinRT.Interop;

namespace TaikoDiveLauncher;

public sealed partial class MainWindow : Window
{
    private CancellationTokenSource? _placementSaveCancellation;

    private App AppInstance => (App)Application.Current;

    public MainWindow()
    {
        InitializeComponent();
        ApplyTheme(AppInstance.Context.Preferences.Theme);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        RestoreWindowPlacement();
        CaptureWindowPlacement();
        AppWindow.Changed += AppWindow_Changed;
        AppWindow.Closing += AppWindow_Closing;
        RootFrame.Navigate(typeof(MainPage));
    }

    public void ApplyTheme(string theme)
    {
        RootLayout.RequestedTheme = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
            ? ElementTheme.Light
            : ElementTheme.Dark;
    }

    public nint GetWindowHandle() => WindowNative.GetWindowHandle(this);

    private void RestoreWindowPlacement()
    {
        WindowPlacementPreferences? placement = AppInstance.Context.Preferences.WindowPlacement;
        if (placement is null)
        {
            AppWindow.Resize(new SizeInt32(1180, 760));
            return;
        }

        AppWindow.MoveAndResize(WindowPlacementService.GetInitialBounds(placement));
        if (placement.IsMaximized && AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPositionChange && !args.DidSizeChange && !args.DidPresenterChange)
        {
            return;
        }

        CaptureWindowPlacement();
        QueuePlacementSave();
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        _placementSaveCancellation?.Cancel();
        CaptureWindowPlacement();
        try
        {
            AppInstance.Context.SavePreferencesAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Closing must not be blocked if the preferences file is temporarily unavailable.
        }
    }

    private void CaptureWindowPlacement()
    {
        WindowPlacementPreferences placement = AppInstance.Context.Preferences.WindowPlacement ??= new();
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            if (presenter.State == OverlappedPresenterState.Minimized)
            {
                return;
            }

            placement.IsMaximized = presenter.State == OverlappedPresenterState.Maximized;
            if (presenter.State != OverlappedPresenterState.Restored)
            {
                return;
            }
        }

        placement.X = AppWindow.Position.X;
        placement.Y = AppWindow.Position.Y;
        placement.Width = AppWindow.Size.Width;
        placement.Height = AppWindow.Size.Height;
    }

    private void QueuePlacementSave()
    {
        _placementSaveCancellation?.Cancel();
        _placementSaveCancellation?.Dispose();
        _placementSaveCancellation = new CancellationTokenSource();
        _ = SavePlacementAfterDelayAsync(_placementSaveCancellation.Token);
    }

    private async Task SavePlacementAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(400, cancellationToken);
            await AppInstance.Context.SavePreferencesAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // A later move or normal close retries the save.
        }
    }
}
