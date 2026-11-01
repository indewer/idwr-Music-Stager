using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MusicStagerWidget;

public static class ImageOptimizer
{
    public const int MaxDimension = 600;
    public const int DefaultQuality = 95;

    /// <summary>
    /// Optimizes image bytes: resizes to max 600x600 preserving aspect ratio, encodes as JPEG Q=95.
    /// </summary>
    public static byte[] OptimizeImageBytes(byte[] sourceBytes, int maxDim = MaxDimension, int quality = DefaultQuality)
    {
        if (sourceBytes == null || sourceBytes.Length == 0)
            return Array.Empty<byte>();

        try
        {
            using var stream = new MemoryStream(sourceBytes);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];

            int origWidth = frame.PixelWidth;
            int origHeight = frame.PixelHeight;

            // If already within bounds and is JPEG, but might be uncompressed/heavy, check if resize needed:
            if (origWidth <= maxDim && origHeight <= maxDim)
            {
                // Already within 600x600, re-encode to ensure 95% JPEG
                return EncodeToJpeg(frame, origWidth, origHeight, quality);
            }

            // Calculate proportional dimensions
            double scale = Math.Min((double)maxDim / origWidth, (double)maxDim / origHeight);
            int newWidth = Math.Max(1, (int)Math.Round(origWidth * scale));
            int newHeight = Math.Max(1, (int)Math.Round(origHeight * scale));

            // Render scaled bitmap
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(frame, new Rect(0, 0, newWidth, newHeight));
            }

            var rtb = new RenderTargetBitmap(newWidth, newHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);

            return EncodeToJpeg(rtb, newWidth, newHeight, quality);
        }
        catch
        {
            // Fallback: return original bytes if processing failed
            return sourceBytes;
        }
    }

    /// <summary>
    /// Loads image file and optimizes it.
    /// </summary>
    public static byte[] OptimizeImageFile(string filePath, int maxDim = MaxDimension, int quality = DefaultQuality)
    {
        if (!File.Exists(filePath)) return Array.Empty<byte>();
        byte[] bytes = File.ReadAllBytes(filePath);
        return OptimizeImageBytes(bytes, maxDim, quality);
    }

    private static byte[] EncodeToJpeg(BitmapSource source, int width, int height, int quality)
    {
        var encoder = new JpegBitmapEncoder
        {
            QualityLevel = Math.Clamp(quality, 10, 100)
        };
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var outStream = new MemoryStream();
        encoder.Save(outStream);
        return outStream.ToArray();
    }

    /// <summary>
    /// Creates a BitmapSource from byte array for UI preview.
    /// </summary>
    public static BitmapSource? CreatePreview(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        try
        {
            using var ms = new MemoryStream(bytes);
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = ms;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch
        {
            return null;
        }
    }
}
