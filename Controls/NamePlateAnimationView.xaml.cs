using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TaikoDiveLauncher.Services;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace TaikoDiveLauncher.Controls;

public sealed partial class NamePlateAnimationView : UserControl
{
    private static readonly TimeSpan PreviewFrameInterval = TimeSpan.FromMilliseconds(1000d / 30);
    private readonly DispatcherTimer _timer = new() { Interval = PreviewFrameInterval };
    private readonly Stopwatch _clock = new();
    private readonly List<VisualElement> _visuals = [];
    private Aup2Animation? _animation;
    private int _loadVersion;
    private int _textVersion;
    private NamePlateTextBitmap? _nameBitmap;
    private NamePlateTextBitmap? _titleBitmap;
    private string? _loadedBasePath;

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

        string baseSheetPath = Path.Combine(installation.BuildDirectory, "Texture", "NamePlate", "0.png");
        if (File.Exists(baseSheetPath) && _loadedBasePath != baseSheetPath)
        {
            var layers = await Task.Run(() => NamePlateTextRenderer.RenderBaseLayers(baseSheetPath));
            if (loadVersion != _loadVersion) return;
            BackgroundImage.Source = await DecodePngAsync(layers.Background);
            if (loadVersion != _loadVersion) return;
            PlayerNumberImage.Source = await DecodePngAsync(layers.Number);
            _loadedBasePath = baseSheetPath;
        }

        string plateDirectory = Path.Combine(installation.NamePlateDirectory, namePlateType.ToString("00"));
        if (!Directory.Exists(plateDirectory))
        {
            plateDirectory = Path.Combine(installation.NamePlateDirectory, namePlateType.ToString());
        }
        ApplyPlatePosition(plateDirectory);

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

    public async Task SetTextAsync(TaikoDiveInstallation installation, string name, string title)
    {
        int version = ++_textVersion;
        var rendered = await Task.Run(() => NamePlateTextRenderer.Render(installation, name, title));
        if (version != _textVersion) return;
        BitmapImage nameSource = await DecodePngAsync(rendered.Name.Png);
        if (version != _textVersion) return;
        BitmapImage titleSource = await DecodePngAsync(rendered.Title.Png);
        if (version != _textVersion) return;
        _nameBitmap = rendered.Name;
        _titleBitmap = rendered.Title;
        NameImage.Source = nameSource;
        TitleImage.Source = titleSource;
        PlaceText();
    }

    private static async Task<BitmapImage> DecodePngAsync(byte[] png)
    {
        using InMemoryRandomAccessStream stream = new();
        using (DataWriter writer = new(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(png);
            await writer.StoreAsync();
        }
        stream.Seek(0);
        BitmapImage image = new();
        await image.SetSourceAsync(stream);
        return image;
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

    private void ApplyPlatePosition(string plateDirectory)
    {
        double adjustX = 0, adjustY = 0;
        string configPath = Path.Combine(plateDirectory, "PlateConfig.json");
        if (File.Exists(configPath))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
                JsonElement root = document.RootElement;
                if (root.TryGetProperty("adjustX", out JsonElement x)) adjustX = x.GetDouble();
                if (root.TryGetProperty("adjustY", out JsonElement y)) adjustY = y.GetDouble();
            }
            catch (JsonException) { }
        }
        AnimationStage.Margin = new Thickness(19 + adjustX, 52 + adjustY, 0, 0);
    }

    private void PlaceText()
    {
        // 本体の tDrawNamePlateCore の配置値。帯の左上をプレビュー中央へ合わせる。
        double originX = 7;
        double originY = 0;
        if (_nameBitmap is { } name)
        {
            NameImage.Width = name.Width * name.ScaleX;
            NameImage.Height = name.Height;
            Canvas.SetLeft(NameImage, originX + 181 - (int)(NameImage.Width / 2));
            Canvas.SetTop(NameImage, originY + 56);
        }
        if (_titleBitmap is { } title)
        {
            TitleImage.Width = title.Width * title.ScaleX;
            TitleImage.Height = title.Height * title.ScaleY;
            Canvas.SetLeft(TitleImage, originX + 185 - (int)(TitleImage.Width / 2));
            Canvas.SetTop(TitleImage, originY + 52 - TitleImage.Height / 2);
        }
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
        _textVersion++;
        Stop();
        _animation = null;
        _visuals.Clear();
        AnimationStage.Children.Clear();
        NameImage.Source = null;
        TitleImage.Source = null;
        BackgroundImage.Source = null;
        PlayerNumberImage.Source = null;
        _loadedBasePath = null;
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
