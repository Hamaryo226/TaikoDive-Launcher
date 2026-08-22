using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Controls;

public sealed partial class CharacterAnimationView : UserControl
{
    private readonly DispatcherTimer _timer = new();
    private IReadOnlyList<BitmapImage> _frames = [];
    private int _frameIndex;

    public CharacterAnimationView()
    {
        InitializeComponent();
        _timer.Tick += Timer_Tick;
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    public void ShowCharacter(TaikoDiveInstallation installation, string characterType)
    {
        _timer.Stop();
        _frameIndex = 0;
        CharacterPreviewData? preview = CharacterPreviewService.Load(installation, characterType);
        if (preview is null)
        {
            _frames = [];
            PreviewImage.Source = null;
            EmptyMessage.Visibility = Visibility.Visible;
            return;
        }

        _frames = preview.Frames.Select(path => new BitmapImage(new Uri(path))
        {
            DecodePixelHeight = 280,
        }).ToList();
        _timer.Interval = preview.FrameInterval;
        EmptyMessage.Visibility = Visibility.Collapsed;
        PreviewImage.Source = _frames[0];
        Start();
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
        _frameIndex = (_frameIndex + 1) % _frames.Count;
        PreviewImage.Source = _frames[_frameIndex];
    }
}
