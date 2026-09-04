using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TaikoDiveLauncher.Services;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace TaikoDiveLauncher.Controls;

public sealed partial class NamePlateAnimationView : UserControl
{
    private static readonly TimeSpan PreviewFrameInterval = TimeSpan.FromMilliseconds(1000d / 30);
    private readonly DispatcherTimer _timer = new() { Interval = PreviewFrameInterval };
    private readonly Stopwatch _clock = new();
    private readonly List<VisualElement> _visuals = [];
    private Aup2Animation? _animation;
    private int _loadVersion;

    public NamePlateAnimationView()
    {
        InitializeComponent();
        _timer.Tick += Timer_Tick;
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => ReleasePreview();
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
                await ShowAnimationAsync(animation, loadVersion);
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

    private async Task ShowAnimationAsync(Aup2Animation animation, int loadVersion)
    {
        SetStageSize(animation.Width, animation.Height);
        Dictionary<(string Path, bool RemoveBlack), DecodedImage> imageCache = [];
        foreach (Aup2Visual source in animation.Visuals)
        {
            bool removeBlack = source.BlendMode == Aup2BlendMode.Additive;
            (string Path, bool RemoveBlack) cacheKey = (source.ImagePath, removeBlack);
            if (!imageCache.TryGetValue(cacheKey, out DecodedImage? decoded))
            {
                decoded = await DecodeImageAsync(source.ImagePath, removeBlack);
                if (loadVersion != _loadVersion)
                {
                    return;
                }

                imageCache.Add(cacheKey, decoded);
            }

            Image image = new()
            {
                Source = decoded.Source,
                Width = decoded.Width,
                Height = decoded.Height,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            CompositeTransform transform = new();
            image.RenderTransform = transform;
            Canvas.SetZIndex(image, source.Layer);
            AnimationStage.Children.Add(image);
            VisualElement element = new(source, image, transform);
            _visuals.Add(element);
        }

        _animation = animation;
        UpdateFrame(0);
        Start();
    }

    private static async Task<DecodedImage> DecodeImageAsync(string path, bool removeBlackBackground)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(path);
        using var sourceStream = await file.OpenReadAsync();
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(sourceStream);
        PixelDataProvider pixelProvider = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        byte[] pixels = pixelProvider.DetachPixelData();
        await Task.Run(() => Aup2ImageProcessor.ConvertToPremultipliedBgra(pixels, removeBlackBackground));

        WriteableBitmap bitmap = new((int)decoder.PixelWidth, (int)decoder.PixelHeight);
        using (Stream target = bitmap.PixelBuffer.AsStream())
        {
            await target.WriteAsync(pixels);
        }

        bitmap.Invalidate();
        return new DecodedImage(bitmap, decoder.PixelWidth, decoder.PixelHeight);
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

    private void ReleasePreview()
    {
        _loadVersion++;
        Stop();
        _animation = null;
        _visuals.Clear();
        AnimationStage.Children.Clear();
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
        Image Image,
        CompositeTransform Transform);

    private sealed record DecodedImage(ImageSource Source, double Width, double Height);
}
