using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using TaikoDiveLauncher.Services;
using Windows.Storage;

namespace TaikoDiveLauncher.Controls;

public sealed partial class CharacterAnimationView : UserControl
{
    private readonly DispatcherTimer _timer = new();
    private IReadOnlyList<BitmapImage> _frames = [];
    private int _frameIndex;
    private int _loadVersion;

    public CharacterAnimationView()
    {
        InitializeComponent();
        _timer.Tick += Timer_Tick;
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => ReleaseFrames();
    }

    public async Task ShowCharacterAsync(TaikoDiveInstallation installation, string characterType)
    {
        int loadVersion = ++_loadVersion;
        _timer.Stop();
        SetLoading(true);

        CharacterPreviewData? preview;
        try
        {
            preview = await Task.Run(() => CharacterPreviewService.Load(installation, characterType));
        }
        catch
        {
            preview = null;
        }

        if (loadVersion != _loadVersion)
        {
            return;
        }

        if (preview is null)
        {
            _frames = [];
            PreviewImage.Source = null;
            EmptyMessage.Visibility = Visibility.Visible;
            SetLoading(false);
            return;
        }

        BitmapImage?[] loadedFrames = await Task.WhenAll(preview.Frames.Select(LoadFrameAsync));
        if (loadVersion != _loadVersion)
        {
            return;
        }

        if (loadedFrames.Any(frame => frame is null))
        {
            _frames = [];
            PreviewImage.Source = null;
            EmptyMessage.Text = "キャラクター画像を読み込めませんでした";
            EmptyMessage.Visibility = Visibility.Visible;
            SetLoading(false);
            return;
        }

        _frames = loadedFrames.Select(frame => frame!).ToArray();
        _frameIndex = 0;
        _timer.Interval = preview.FrameInterval;
        EmptyMessage.Text = "normal アニメが見つかりません";
        EmptyMessage.Visibility = Visibility.Collapsed;
        PreviewImage.Source = _frames[0];
        SetLoading(false);
        Start();
    }

    private static async Task<BitmapImage?> LoadFrameAsync(string path)
    {
        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            BitmapImage image = new() { DecodePixelHeight = 280 };
            await image.SetSourceAsync(stream);
            return image;
        }
        catch
        {
            return null;
        }
    }

    private void SetLoading(bool isLoading)
    {
        LoadingRing.IsActive = isLoading;
        LoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Start()
    {
        if (IsLoaded && _frames.Count > 1)
        {
            _timer.Start();
        }
    }

    private void Timer_Tick(object? sender, object e)
    {
        if (_frames.Count == 0)
        {
            _timer.Stop();
            return;
        }

        _frameIndex = (_frameIndex + 1) % _frames.Count;
        PreviewImage.Source = _frames[_frameIndex];
    }

    private void ReleaseFrames()
    {
        _loadVersion++;
        _timer.Stop();
        _frames = [];
        _frameIndex = 0;
        PreviewImage.Source = null;
    }
}
