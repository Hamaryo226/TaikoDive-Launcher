using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace TaikoDiveLauncher.Controls;

internal static class PngImage
{
    public static async Task<BitmapImage> DecodeAsync(byte[] png)
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
}
