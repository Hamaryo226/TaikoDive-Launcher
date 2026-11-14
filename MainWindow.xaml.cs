using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;
using Windows.Graphics;
using WinRT.Interop;

namespace TaikoDiveLauncher;

public sealed partial class MainWindow : Window
{
    private readonly WindowPlacementPersistence _placementPersistence;
    private readonly SubclassProc _placementHook;

    private App AppInstance => (App)Application.Current;

    public MainWindow()
    {
        _placementPersistence = new(AppInstance.Context.SavePlacementAsync);
        InitializeComponent();
        ApplyTheme(AppInstance.Context.Preferences.Theme);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        RestoreWindowPlacement();
        _placementHook = WindowMessage;
        SetWindowSubclass(GetWindowHandle(), _placementHook, 1, 0);
        AppWindow.Closing += AppWindow_Closing;
        Closed += (_, _) => RemoveWindowSubclass(GetWindowHandle(), _placementHook, 1);
        RootFrame.Navigate(typeof(MainPage));
    }

    public void ApplyTheme(string theme)
    {
        RootLayout.RequestedTheme = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
            ? ElementTheme.Light : ElementTheme.Dark;
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
            presenter.Maximize();
    }

    // AppWindow.Changed also fires for programmatic changes. Only native user move/size
    // completion and system-menu maximize/restore commands are placement edits.
    private nint WindowMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        const uint WM_EXITSIZEMOVE = 0x0232;
        const uint WM_SYSCOMMAND = 0x0112;
        nint result = DefSubclassProc(window, message, wParam, lParam);
        if (message == WM_EXITSIZEMOVE ||
            (message == WM_SYSCOMMAND && ((wParam & 0xfff0) == 0xf030 || (wParam & 0xfff0) == 0xf120)))
        {
            DispatcherQueue.TryEnqueue(CaptureUserPlacement);
        }
        return result;
    }

    private void CaptureUserPlacement()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter || presenter.State == OverlappedPresenterState.Minimized)
            return;
        var prior = _placementPersistence.Pending ?? AppInstance.Context.Preferences.WindowPlacement;
        bool maximized = presenter.State == OverlappedPresenterState.Maximized;
        WindowPlacementPreferences placement = new()
        {
            X = maximized ? prior?.X ?? AppWindow.Position.X : AppWindow.Position.X,
            Y = maximized ? prior?.Y ?? AppWindow.Position.Y : AppWindow.Position.Y,
            Width = maximized ? prior?.Width ?? 1180 : AppWindow.Size.Width,
            Height = maximized ? prior?.Height ?? 760 : AppWindow.Size.Height,
            IsMaximized = maximized,
        };
        _placementPersistence.Register(placement, AppInstance.Context.PlacementGeneration);
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (AppInstance.Context.PreferenceTransitions.IsCloseCommitted) return;
        args.Cancel = true;
        await RequestCloseAsync();
    }

    public async Task<bool> RequestCloseAsync()
    {
        try
        {
            return await AppInstance.Context.PreferenceTransitions.RequestCloseAsync(
                () => RootFrame.Content is MainPage { PreferencesOperationBusy: true },
                async () =>
                {
                    if (!AppInstance.Context.HasUnsavedPreferenceEdits) return true;
                    var confirmation = new ContentDialog
                    {
                        Title = "未保存の変更があります。終了しますか？",
                        Content = "編集中のランチャー設定を破棄して終了します。設定ファイルへの保存は行いません。",
                        PrimaryButtonText = "破棄して終了", CloseButtonText = "キャンセル",
                        DefaultButton = ContentDialogButton.Close, XamlRoot = RootLayout.XamlRoot,
                    };
                    return await confirmation.ShowAsync() == ContentDialogResult.Primary;
                }, AppInstance.Context.RevertPreferenceEdits, PrepareCloseAsync, Close);
        }
        catch { return false; } // A failed close request must not escape an async UI event.
    }

    internal Task PrepareCloseAsync(Action close) => _placementPersistence.PrepareCloseAsync(close);

    private delegate nint SubclassProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint window, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint window, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
}
