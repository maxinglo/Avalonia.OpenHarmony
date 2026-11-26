using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Avalonia.OpenHarmony;

internal sealed class OpenHarmonyIconLoader : IPlatformIconLoader
{
    public IWindowIconImpl LoadIcon(string fileName)
    {
        using var file = File.OpenRead(fileName);
        return LoadIcon(file);
    }

    public IWindowIconImpl LoadIcon(Stream stream)
    {
        using var bitmap = new Bitmap(stream);
        return Encode(stream => bitmap.Save(stream, PngBitmapEncoderOptions.Default));
    }

    public IWindowIconImpl LoadIcon(IBitmapImpl bitmap) =>
        Encode(stream => bitmap.Save(stream, PngBitmapEncoderOptions.Default));

    private static IWindowIconImpl Encode(Action<Stream> save)
    {
        using var stream = new MemoryStream();
        save(stream);
        return new OpenHarmonyWindowIcon(stream.ToArray());
    }
}

internal sealed class OpenHarmonyWindowIcon(byte[] png) : IWindowIconImpl
{
    public void Save(Stream stream) => stream.Write(png);
}
