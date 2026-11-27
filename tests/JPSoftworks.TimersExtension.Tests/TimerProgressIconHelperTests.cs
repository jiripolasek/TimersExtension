// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using JPSoftworks.TimersExtension.Services;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace JPSoftworks.TimersExtension.Tests;

public sealed class TimerProgressIconHelperTests
{
    [Fact]
    public async Task RunningTimerIconUsesDecodablePngStreams()
    {
        var icon = TimerProgressIconHelper.GetRunningTimerIcon(
            TimeSpan.FromMinutes(1),
            TimeSpan.FromSeconds(30),
            true);

        await AssertPngStreamAsync(icon.Light);
        await AssertPngStreamAsync(icon.Dark);
    }

    [Fact]
    public async Task HalfProgressArcRunsClockwiseFromTop()
    {
        var icon = TimerProgressIconHelper.GetRunningTimerIcon(
            TimeSpan.FromMinutes(1),
            TimeSpan.FromSeconds(30),
            true);
        var pixels = await DecodePixelsAsync(icon.Light);

        AssertPixel(pixels, 58, 32, 0x83, 0xB1, 0x38, 0xFF);
        AssertPixel(pixels, 5, 32, 0xDE, 0xD7, 0xD0, 0xFF);
    }

    [Fact]
    public void SameFrameReusesCachedIcon()
    {
        var first = TimerProgressIconHelper.GetRunningTimerIcon(
            TimeSpan.FromMinutes(1),
            TimeSpan.FromSeconds(30),
            true);
        var second = TimerProgressIconHelper.GetRunningTimerIcon(
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMilliseconds(29_600),
            true);

        Assert.Same(first, second);
    }

    [Fact]
    public void DifferentFramesUseDifferentIcons()
    {
        var first = TimerProgressIconHelper.GetRunningTimerIcon(
            TimeSpan.FromMinutes(1),
            TimeSpan.FromSeconds(30),
            true);
        var second = TimerProgressIconHelper.GetRunningTimerIcon(
            TimeSpan.FromMinutes(1),
            TimeSpan.FromSeconds(29),
            true);

        Assert.NotSame(first, second);
    }

    [Fact]
    public void ZeroDurationUsesStaticFallbackIcon()
    {
        var icon = TimerProgressIconHelper.GetRunningTimerIcon(TimeSpan.Zero, TimeSpan.Zero, true);

        Assert.Same(Icons.RunningTimer, icon);
    }

    [Fact]
    public void AnimationsDisabledUsesStaticIcon()
    {
        var icon = TimerProgressIconHelper.GetRunningTimerIcon(
            TimeSpan.FromMinutes(1),
            TimeSpan.FromSeconds(30),
            false);

        Assert.Same(Icons.RunningTimer, icon);
    }

    private static async Task AssertPngStreamAsync(IconData iconData)
    {
        Assert.True(string.IsNullOrEmpty(iconData.Icon));
        Assert.NotNull(iconData.Data);

        using var stream = await iconData.Data!.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);

        Assert.Equal(BitmapDecoder.PngDecoderId, decoder.DecoderInformation.CodecId);
        Assert.Equal((uint)64, decoder.PixelWidth);
        Assert.Equal((uint)64, decoder.PixelHeight);
    }

    private static async Task<byte[]> DecodePixelsAsync(IconData iconData)
    {
        using var stream = await iconData.Data!.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var pixelData = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);

        return pixelData.DetachPixelData();
    }

    private static void AssertPixel(
        byte[] pixels,
        int x,
        int y,
        byte blue,
        byte green,
        byte red,
        byte alpha)
    {
        var offset = ((y * 64) + x) * 4;
        Assert.Equal(blue, pixels[offset]);
        Assert.Equal(green, pixels[offset + 1]);
        Assert.Equal(red, pixels[offset + 2]);
        Assert.Equal(alpha, pixels[offset + 3]);
    }
}