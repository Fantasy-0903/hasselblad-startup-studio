using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using System.Runtime.InteropServices;
namespace StartupStudio;

public static class Images
{
    public static GrayImage Import(string path)
    {
        Check.That(new FileInfo(path).Length <= 32 * 1024 * 1024, "图片文件过大，请先缩小文件。");
        using var stream = File.OpenRead(path); using var codec = SKCodec.Create(stream);
        Check.That(codec != null, "图片解码失败，请选择 PNG 或 JPEG。");
        Check.That(codec!.EncodedFormat is SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg && codec.FrameCount <= 1, "请选择单张 PNG 或 JPEG 图片。");
        var w = codec.Info.Width; var h = codec.Info.Height; var origin = codec.EncodedOrigin;
        var rotate = (int)origin >= 5; var ow = rotate ? h : w; var oh = rotate ? w : h;
        Check.That(ow is >= 1 and <= 1024 && oh is >= 1 and <= 768, "导入图片宽度不得超过 1024、高度不得超过 768 像素，请先调整大小。");
        using var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Check.That(codec.GetPixels(bitmap.Info, bitmap.GetPixels()) == SKCodecResult.Success, "图片解码不完整。");
        var rgba = new byte[w * h * 4]; Marshal.Copy(bitmap.GetPixels(), rgba, 0, rgba.Length); var gray = new byte[ow * oh];
        for (var y = 0; y < oh; y++) for (var x = 0; x < ow; x++)
        {
            var (sx, sy) = Orient((int)origin, x, y, w, h); var at = (sy * w + sx) * 4;
            var r = (rgba[at] * rgba[at + 3] + 127) / 255; var g = (rgba[at + 1] * rgba[at + 3] + 127) / 255; var b = (rgba[at + 2] * rgba[at + 3] + 127) / 255;
            gray[y * ow + x] = (byte)((299 * r + 587 * g + 114 * b + 500) / 1000);
        }
        return new(ow, oh, gray);
    }
    public static (int X, int Y) Orient(int origin, int x, int y, int w, int h) => origin switch
    {
        1 => (x, y), 2 => (w - 1 - x, y), 3 => (w - 1 - x, h - 1 - y), 4 => (x, h - 1 - y),
        5 => (y, x), 6 => (y, h - 1 - x), 7 => (w - 1 - y, h - 1 - x), 8 => (w - 1 - y, x), _ => throw new StudioException("图片方向无效。")
    };
    public static void Export(GrayImage image, string path)
    {
        image.Validate(); using var bitmap = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Gray8, SKAlphaType.Opaque));
        for (var row = 0; row < image.Height; row++) Marshal.Copy(image.Pixels, row * image.Width, bitmap.GetPixels() + row * bitmap.RowBytes, image.Width);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100); Check.That(data != null, "图片保存失败。"); Check.Atomic(path, data!.ToArray());
    }
    public static WriteableBitmap Bitmap(GrayImage image)
    {
        image.Validate(); var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var framebuffer = bitmap.Lock(); var row = new byte[image.Width * 4];
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++) { row[x * 4] = row[x * 4 + 1] = row[x * 4 + 2] = image.Pixels[y * image.Width + x]; row[x * 4 + 3] = 255; }
            Marshal.Copy(row, 0, framebuffer.Address + y * framebuffer.RowBytes, row.Length);
        }
        return bitmap;
    }
}
