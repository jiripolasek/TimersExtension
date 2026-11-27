// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace JPSoftworks.TimersExtension.Services;

internal static class TimerProgressIconHelper
{
    private const int FrameCount = 60;
    private const int IconSize = 64;
    private const int BaseIconSize = 36;
    private const int BaseIconOffset = 14;
    private const int AntialiasSamplesPerAxis = 4;
    private const double RingCenter = IconSize / 2d;
    private const double RingInnerRadius = 25d;
    private const double RingOuterRadius = 29d;
    private const string LightBaseIconPath = "Assets/Icons/Timer_Generic.light.png";
    private const string DarkBaseIconPath = "Assets/Icons/Timer_Generic.dark.png";

    private static readonly BgraColor LightTrackColor = new(0xDE, 0xD7, 0xD0);
    private static readonly BgraColor LightProgressColor = new(0x83, 0xB1, 0x38);
    private static readonly BgraColor DarkTrackColor = new(0x67, 0x54, 0x47);
    private static readonly BgraColor DarkProgressColor = new(0xB8, 0xE4, 0x7F);
    private static readonly ConcurrentDictionary<int, Lazy<CachedProgressIcon>> Cache = [];

    private static readonly Lazy<Task<byte[]>> LightBaseIconPixels = new(
        () => LoadBaseIconPixelsAsync(LightBaseIconPath),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<Task<byte[]>> DarkBaseIconPixels = new(
        () => LoadBaseIconPixelsAsync(DarkBaseIconPath),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static IconInfo GetRunningTimerIcon(TimeSpan duration, TimeSpan remaining, bool animated)
    {
        if (!animated || duration <= TimeSpan.Zero)
        {
            return Icons.RunningTimer;
        }

        var frame = GetFrame(duration, remaining);
        var cachedIcon = Cache.GetOrAdd(
            frame,
            static value => new Lazy<CachedProgressIcon>(
                () => CreateIcon(value),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return cachedIcon.Value.Icon;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException
                                       or InvalidOperationException)
        {
            Cache.TryRemove(frame, out _);
            return Icons.RunningTimer;
        }
    }

    private static int GetFrame(TimeSpan duration, TimeSpan remaining)
    {
        var clampedRemaining = remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        var elapsed = duration - clampedRemaining;
        var progress = elapsed.TotalMilliseconds / duration.TotalMilliseconds;
        var normalized = Math.Clamp(progress, 0d, 1d);
        return (int)Math.Round(normalized * FrameCount, MidpointRounding.AwayFromZero);
    }

    private static CachedProgressIcon CreateIcon(int frame) =>
        CreateIconAsync(frame).GetAwaiter().GetResult();

    private static async Task<CachedProgressIcon> CreateIconAsync(int frame)
    {
        var progress = frame / (double)FrameCount;
        var lightBasePixels = await LightBaseIconPixels.Value.ConfigureAwait(false);
        var darkBasePixels = await DarkBaseIconPixels.Value.ConfigureAwait(false);

        var lightStream = await EncodePngAsync(
            RenderFrame(progress, lightBasePixels, LightTrackColor, LightProgressColor)).ConfigureAwait(false);
        InMemoryRandomAccessStream? darkStream = null;

        try
        {
            darkStream = await EncodePngAsync(
                RenderFrame(progress, darkBasePixels, DarkTrackColor, DarkProgressColor)).ConfigureAwait(false);

            var icon = new IconInfo(
                new IconData(RandomAccessStreamReference.CreateFromStream(lightStream)!),
                new IconData(RandomAccessStreamReference.CreateFromStream(darkStream)!));

            return new CachedProgressIcon(icon, lightStream, darkStream);
        }
        catch
        {
            lightStream.Dispose();
            darkStream?.Dispose();
            throw;
        }
    }

    private static async Task<byte[]> LoadBaseIconPixelsAsync(string relativePath)
    {
        var normalizedPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(AppContext.BaseDirectory, normalizedPath);
        var pngBytes = File.ReadAllBytes(fullPath);

        using var sourceStream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(sourceStream))
        {
            writer.WriteBytes(pngBytes);
            await writer.StoreAsync().AsTask().ConfigureAwait(false);
            _ = writer.DetachStream();
        }

        sourceStream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(sourceStream).AsTask().ConfigureAwait(false);
        var transform = new BitmapTransform
        {
            ScaledWidth = BaseIconSize,
            ScaledHeight = BaseIconSize,
            InterpolationMode = BitmapInterpolationMode.Fant
        };

        var pixelData = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage).AsTask().ConfigureAwait(false);

        return pixelData.DetachPixelData();
    }

    private static byte[] RenderFrame(
        double progress,
        byte[] baseIconPixels,
        BgraColor trackColor,
        BgraColor progressColor)
    {
        var pixels = new byte[IconSize * IconSize * 4];
        DrawRing(pixels, trackColor, 1d);
        DrawRing(pixels, progressColor, progress);
        CompositeBaseIcon(pixels, baseIconPixels);
        return pixels;
    }

    private static void DrawRing(byte[] pixels, BgraColor color, double progress)
    {
        if (progress <= 0d)
        {
            return;
        }

        var sweep = Math.Min(progress, 1d) * Math.Tau;
        var innerRadiusSquared = RingInnerRadius * RingInnerRadius;
        var outerRadiusSquared = RingOuterRadius * RingOuterRadius;
        var sampleCount = AntialiasSamplesPerAxis * AntialiasSamplesPerAxis;

        for (var y = 0; y < IconSize; y++)
        {
            for (var x = 0; x < IconSize; x++)
            {
                var coveredSamples = 0;
                for (var sampleY = 0; sampleY < AntialiasSamplesPerAxis; sampleY++)
                {
                    var yPosition = y + ((sampleY + 0.5d) / AntialiasSamplesPerAxis);
                    var deltaY = yPosition - RingCenter;

                    for (var sampleX = 0; sampleX < AntialiasSamplesPerAxis; sampleX++)
                    {
                        var xPosition = x + ((sampleX + 0.5d) / AntialiasSamplesPerAxis);
                        var deltaX = xPosition - RingCenter;
                        var distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
                        if (distanceSquared < innerRadiusSquared || distanceSquared > outerRadiusSquared)
                        {
                            continue;
                        }

                        if (progress < 1d)
                        {
                            var angle = Math.Atan2(deltaX, -deltaY);
                            if (angle < 0d)
                            {
                                angle += Math.Tau;
                            }

                            if (angle > sweep)
                            {
                                continue;
                            }
                        }

                        coveredSamples++;
                    }
                }

                if (coveredSamples > 0)
                {
                    var alpha = ((coveredSamples * byte.MaxValue) + (sampleCount / 2)) / sampleCount;
                    BlendColor(pixels, ((y * IconSize) + x) * 4, color, alpha);
                }
            }
        }
    }

    private static void BlendColor(byte[] pixels, int offset, BgraColor color, int sourceAlpha)
    {
        var inverseAlpha = byte.MaxValue - sourceAlpha;
        var sourceBlue = ((color.Blue * sourceAlpha) + 127) / byte.MaxValue;
        var sourceGreen = ((color.Green * sourceAlpha) + 127) / byte.MaxValue;
        var sourceRed = ((color.Red * sourceAlpha) + 127) / byte.MaxValue;

        pixels[offset] = (byte)(sourceBlue + (((pixels[offset] * inverseAlpha) + 127) / byte.MaxValue));
        pixels[offset + 1] = (byte)(sourceGreen + (((pixels[offset + 1] * inverseAlpha) + 127) / byte.MaxValue));
        pixels[offset + 2] = (byte)(sourceRed + (((pixels[offset + 2] * inverseAlpha) + 127) / byte.MaxValue));
        pixels[offset + 3] = (byte)(sourceAlpha + (((pixels[offset + 3] * inverseAlpha) + 127) / byte.MaxValue));
    }

    private static void CompositeBaseIcon(byte[] destination, byte[] source)
    {
        for (var y = 0; y < BaseIconSize; y++)
        {
            for (var x = 0; x < BaseIconSize; x++)
            {
                var sourceOffset = ((y * BaseIconSize) + x) * 4;
                var sourceAlpha = source[sourceOffset + 3];
                if (sourceAlpha == 0)
                {
                    continue;
                }

                var destinationOffset = (((y + BaseIconOffset) * IconSize) + x + BaseIconOffset) * 4;
                var inverseAlpha = byte.MaxValue - sourceAlpha;
                destination[destinationOffset] = (byte)(source[sourceOffset] +
                                                        (((destination[destinationOffset] * inverseAlpha) + 127) /
                                                         byte.MaxValue));
                destination[destinationOffset + 1] = (byte)(source[sourceOffset + 1] +
                                                            (((destination[destinationOffset + 1] * inverseAlpha) +
                                                              127) / byte.MaxValue));
                destination[destinationOffset + 2] = (byte)(source[sourceOffset + 2] +
                                                            (((destination[destinationOffset + 2] * inverseAlpha) +
                                                              127) / byte.MaxValue));
                destination[destinationOffset + 3] = (byte)(sourceAlpha +
                                                            (((destination[destinationOffset + 3] * inverseAlpha) +
                                                              127) / byte.MaxValue));
            }
        }
    }

    private static async Task<InMemoryRandomAccessStream> EncodePngAsync(byte[] pixels)
    {
        var stream = new InMemoryRandomAccessStream();
        try
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream).AsTask()
                .ConfigureAwait(false);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                IconSize,
                IconSize,
                96,
                96,
                pixels);

            await encoder.FlushAsync().AsTask().ConfigureAwait(false);
            stream.Seek(0);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private readonly record struct BgraColor(byte Blue, byte Green, byte Red);

    private sealed class CachedProgressIcon
    {
        public IconInfo Icon { get; }

        public InMemoryRandomAccessStream LightStream { get; }

        public InMemoryRandomAccessStream DarkStream { get; }

        public CachedProgressIcon(
            IconInfo icon,
            InMemoryRandomAccessStream lightStream,
            InMemoryRandomAccessStream darkStream)
        {
            this.Icon = icon;
            this.LightStream = lightStream;
            this.DarkStream = darkStream;
        }
    }
}