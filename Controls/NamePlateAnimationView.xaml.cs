using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TaikoDiveLauncher.Services;
using Windows.Foundation;

namespace TaikoDiveLauncher.Controls;

public sealed partial class NamePlateAnimationView : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = new();
    private readonly List<VisualElement> _visuals = [];
    private Aup2Animation? _animation;
    private int _loadVersion;

    public NamePlateAnimationView()
    {
        InitializeComponent();
        _timer.Tick += Timer_Tick;
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    public async Task ShowNamePlateAsync(TaikoDiveInstallation installation, int namePlateType)
    {
        int loadVersion = ++_loadVersion;
        Stop();
        _animation = null;
        _visuals.Clear();
        AnimationStage.Children.Clear();
        EmptyMessage.Visibility = Visibility.Collapsed;

        string plateDirectory = Path.Combine(installation.NamePlateDirectory, namePlateType.ToString("00"));
        if (!Directory.Exists(plateDirectory))
        {
            plateDirectory = Path.Combine(installation.NamePlateDirectory, namePlateType.ToString());
        }

        string animationPath = Path.Combine(plateDirectory, "Anime.aup2");
        if (File.Exists(animationPath))
        {
            Aup2Animation? animation = await Task.Run(() => Aup2Animation.Load(animationPath));
            if (loadVersion != _loadVersion)
            {
                return;
            }

            if (animation is not null)
            {
                ShowAnimation(animation);
                return;
            }
        }

        string basePath = Path.Combine(plateDirectory, "Base.png");
        if (File.Exists(basePath))
        {
            ShowStaticImage(basePath);
            return;
        }

        EmptyMessage.Visibility = Visibility.Visible;
    }

    private void ShowAnimation(Aup2Animation animation)
    {
        _animation = animation;
        SetStageSize(animation.Width, animation.Height);
        foreach (Aup2Visual source in animation.Visuals)
        {
            BitmapImage bitmap = new(new Uri(source.ImagePath));
            Image image = new()
            {
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            CompositeTransform transform = new();
            image.RenderTransform = transform;
            Canvas.SetZIndex(image, source.Layer);
            AnimationStage.Children.Add(image);
            VisualElement element = new(source, bitmap, image, transform);
            _visuals.Add(element);
            image.ImageOpened += (_, _) =>
            {
                image.Width = bitmap.PixelWidth;
                image.Height = bitmap.PixelHeight;
                UpdateFrame(0);
            };
            image.Source = bitmap;
        }

        Start();
    }

    private void ShowStaticImage(string path)
    {
        BitmapImage bitmap = new(new Uri(path));
        Image image = new() { Stretch = Stretch.Uniform };
        image.ImageOpened += (_, _) =>
        {
            SetStageSize(Math.Max(1, bitmap.PixelWidth), Math.Max(1, bitmap.PixelHeight));
            image.Width = bitmap.PixelWidth;
            image.Height = bitmap.PixelHeight;
        };
        image.Source = bitmap;
        AnimationStage.Children.Add(image);
    }

    private void SetStageSize(double width, double height)
    {
        AnimationStage.Width = width;
        AnimationStage.Height = height;
        AnimationStage.Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, height) };
    }

    private void Start()
    {
        if (!IsLoaded || _animation is null)
        {
            return;
        }

        _clock.Restart();
        _timer.Start();
    }

    private void Stop()
    {
        _timer.Stop();
        _clock.Stop();
    }

    private void Timer_Tick(object? sender, object e)
    {
        if (_animation is null)
        {
            return;
        }

        double frame = (_clock.Elapsed.TotalSeconds * _animation.FrameRate) % _animation.TotalFrames;
        UpdateFrame(frame);
    }

    private void UpdateFrame(double frame)
    {
        if (_animation is null)
        {
            return;
        }

        int discreteFrame = Math.Min((int)frame, _animation.TotalFrames - 1);
        foreach (VisualElement element in _visuals)
        {
            Aup2Visual source = element.Source;
            bool isVisible = discreteFrame >= source.StartFrame && discreteFrame <= source.EndFrame;
            element.Image.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            if (!isVisible || element.Image.Width <= 0 || element.Image.Height <= 0)
            {
                continue;
            }

            double progress = source.Progress(frame);
            double scale = source.Scale.At(progress) / 100 * source.EffectScale.At(progress) / 100;
            double aspect = source.Aspect.At(progress) / 100;
            double scaleX = (aspect > 0 ? scale * (1 - aspect) : scale) * source.EffectScaleX.At(progress) / 100;
            double scaleY = (aspect < 0 ? scale * (1 + aspect) : scale) * source.EffectScaleY.At(progress) / 100;
            double rotation = source.Rotation.At(progress);
            double centerX = source.CenterX.At(progress) * scaleX;
            double centerY = source.CenterY.At(progress) * scaleY;
            double radians = rotation * Math.PI / 180;
            double offsetX = centerX * Math.Cos(radians) - centerY * Math.Sin(radians);
            double offsetY = centerX * Math.Sin(radians) + centerY * Math.Cos(radians);

            element.Transform.ScaleX = scaleX;
            element.Transform.ScaleY = scaleY;
            element.Transform.Rotation = rotation;
            element.Image.Opacity = Math.Clamp((100 - source.Transparency.At(progress)) / 100, 0, 1);
            Canvas.SetLeft(element.Image, (_animation.Width / 2d) + source.X.At(progress) - offsetX - (element.Image.Width / 2d));
            Canvas.SetTop(element.Image, (_animation.Height / 2d) + source.Y.At(progress) - offsetY - (element.Image.Height / 2d));
        }
    }

    private sealed record VisualElement(
        Aup2Visual Source,
        BitmapImage Bitmap,
        Image Image,
        CompositeTransform Transform);
}
