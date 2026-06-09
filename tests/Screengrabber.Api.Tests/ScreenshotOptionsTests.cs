using Screengrabber.Api;
using Xunit;

namespace Screengrabber.Api.Tests;

public class ScreenshotOptionsTests
{
    [Fact]
    public void Parse_MinimalPath_ReturnsDefaults()
    {
        var opts = ScreenshotOptions.Parse("/https%3A%2F%2Fexample.com/", null);

        Assert.Equal("https://example.com", opts.TargetUrl);
        Assert.Equal(ScreenshotSize.Small, opts.Size);
        Assert.Equal(AspectRatio.OneOne, opts.AspectRatio);
        Assert.Equal(ZoomLevel.Normal, opts.Zoom);
        Assert.Equal(WaitLevel.Load, opts.WaitLevel);
        Assert.Equal(6, opts.TimeoutSeconds);
        Assert.Equal(ImageFormat.Png, opts.Format);
    }

    [Fact]
    public void Parse_SizeSegment_SetsSize()
    {
        Assert.Equal(ScreenshotSize.Small,     ScreenshotOptions.Parse("/u/small/", null).Size);
        Assert.Equal(ScreenshotSize.Medium,    ScreenshotOptions.Parse("/u/medium/", null).Size);
        Assert.Equal(ScreenshotSize.Large,     ScreenshotOptions.Parse("/u/large/", null).Size);
        Assert.Equal(ScreenshotSize.OpenGraph, ScreenshotOptions.Parse("/u/opengraph/", null).Size);
    }

    [Fact]
    public void Parse_AspectRatioSegment_SetsRatio()
    {
        Assert.Equal(AspectRatio.OneOne,      ScreenshotOptions.Parse("/u/small/1:1/", null).AspectRatio);
        Assert.Equal(AspectRatio.NineSixteen, ScreenshotOptions.Parse("/u/small/9:16/", null).AspectRatio);
    }

    [Fact]
    public void Parse_ZoomSegment_SetsZoom()
    {
        Assert.Equal(ZoomLevel.Bigger,  ScreenshotOptions.Parse("/u/small/1:1/bigger/", null).Zoom);
        Assert.Equal(ZoomLevel.Smaller, ScreenshotOptions.Parse("/u/small/1:1/smaller/", null).Zoom);
    }

    [Fact]
    public void Parse_WaitToken_SetsWaitLevel()
    {
        Assert.Equal(WaitLevel.DomContentLoaded, ScreenshotOptions.Parse("/u/_wait:0/", null).WaitLevel);
        Assert.Equal(WaitLevel.Load,             ScreenshotOptions.Parse("/u/_wait:1/", null).WaitLevel);
        Assert.Equal(WaitLevel.NetworkIdle,      ScreenshotOptions.Parse("/u/_wait:2/", null).WaitLevel);
        Assert.Equal(WaitLevel.NetworkIdle,      ScreenshotOptions.Parse("/u/_wait:3/", null).WaitLevel);
    }

    [Fact]
    public void Parse_TimeoutToken_ClampsTo3To9()
    {
        Assert.Equal(5, ScreenshotOptions.Parse("/u/_timeout:5/", null).TimeoutSeconds);
        Assert.Equal(3, ScreenshotOptions.Parse("/u/_timeout:1/", null).TimeoutSeconds);
        Assert.Equal(9, ScreenshotOptions.Parse("/u/_timeout:99/", null).TimeoutSeconds);
    }

    [Fact]
    public void Parse_CombinedTokens_ParsesAll()
    {
        var opts = ScreenshotOptions.Parse("/u/_20250609_wait:2_timeout:7/", null);
        Assert.Equal(WaitLevel.NetworkIdle, opts.WaitLevel);
        Assert.Equal(7, opts.TimeoutSeconds);
    }

    [Fact]
    public void Parse_UnknownSegments_FallBackToDefaults()
    {
        var opts = ScreenshotOptions.Parse("/u/banana/nonsense/", null);
        Assert.Equal(ScreenshotSize.Small, opts.Size);
        Assert.Equal(AspectRatio.OneOne, opts.AspectRatio);
    }

    [Fact]
    public void Parse_FormatJpeg_SetsJpeg()
    {
        Assert.Equal(ImageFormat.Jpeg, ScreenshotOptions.Parse("/u/", "jpeg").Format);
        Assert.Equal(ImageFormat.Jpeg, ScreenshotOptions.Parse("/u/", "JPEG").Format);
        Assert.Equal(ImageFormat.Png,  ScreenshotOptions.Parse("/u/", null).Format);
        Assert.Equal(ImageFormat.Png,  ScreenshotOptions.Parse("/u/", "png").Format);
    }

    [Fact]
    public void GetViewport_OpenGraph_IsAlways1200x630()
    {
        var opts = ScreenshotOptions.Parse("/u/opengraph/9:16/", null);
        Assert.Equal((1200, 630), opts.GetViewport());
    }

    [Fact]
    public void GetViewport_SmallOneOne_Is375x375()
    {
        var opts = ScreenshotOptions.Parse("/u/small/1:1/", null);
        Assert.Equal((375, 375), opts.GetViewport());
    }

    [Fact]
    public void GetViewport_SmallNineSixteen_Is375x667()
    {
        var opts = ScreenshotOptions.Parse("/u/small/9:16/", null);
        Assert.Equal((375, 667), opts.GetViewport());
    }

    [Fact]
    public void GetDeviceScaleFactor_Bigger_Is1Point4()
    {
        var opts = ScreenshotOptions.Parse("/u/small/1:1/bigger/", null);
        Assert.Equal(1.4, opts.GetDeviceScaleFactor(), 2);
    }

    [Fact]
    public void GetDeviceScaleFactor_Smaller_Is0Point71()
    {
        var opts = ScreenshotOptions.Parse("/u/small/1:1/smaller/", null);
        Assert.Equal(0.71, opts.GetDeviceScaleFactor(), 2);
    }

    [Fact]
    public void ContentType_PngByDefault()
    {
        Assert.Equal("image/png",  ScreenshotOptions.Parse("/u/", null).ContentType);
        Assert.Equal("image/jpeg", ScreenshotOptions.Parse("/u/", "jpeg").ContentType);
    }
}
