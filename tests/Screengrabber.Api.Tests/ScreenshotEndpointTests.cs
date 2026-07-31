using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using NSubstitute;
using Screengrabber.Api;
using SkiaSharp;

namespace Screengrabber.Api.Tests;

public class ScreenshotEndpointTests
{
    [Fact]
    public async Task HandleAsync_CacheHit_ReturnsCachedBytes_AndSkipsCapture()
    {
        var context = CreateContext("/https%3A%2F%2Fexample.com%2F");
        var screenshot = Substitute.For<IScreenshotService>();
        var cache = Substitute.For<ICacheService>();
        var cached = new byte[] { 9, 8, 7 };
        cache.GetAsync("/https%3A%2F%2Fexample.com%2F").Returns(cached);

        var result = await ScreenshotEndpoint.HandleAsync(
            context,
            screenshot,
            cache,
            BuildConfig(),
            NullLogger<Program>.Instance);

        var (status, contentType, body) = await ExecuteAsync(result);

        Assert.Equal(200, status);
        Assert.Equal("image/png", contentType);
        Assert.Equal(cached, body);
        await screenshot.DidNotReceive().CaptureAsync(Arg.Any<ScreenshotOptions>());
        await cache.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task HandleAsync_CacheMiss_CapturesAndStoresWithConfiguredTtl()
    {
        var context = CreateContext("/https%3A%2F%2Fexample.com%2Fsmall%2F");
        var screenshot = Substitute.For<IScreenshotService>();
        var cache = Substitute.For<ICacheService>();
        var bytes = CreatePng(8, 8);

        cache.GetAsync("/https%3A%2F%2Fexample.com%2Fsmall%2F").Returns((byte[]?)null);
        screenshot.CaptureAsync(Arg.Any<ScreenshotOptions>()).Returns(bytes);

        var result = await ScreenshotEndpoint.HandleAsync(
            context,
            screenshot,
            cache,
            BuildConfig(ttlHours: 12),
            NullLogger<Program>.Instance);

        var (status, contentType, body) = await ExecuteAsync(result);

        Assert.Equal(200, status);
        Assert.Equal("image/png", contentType);
        Assert.Equal(bytes, body);
        await screenshot.Received(1).CaptureAsync(Arg.Any<ScreenshotOptions>());
        await cache.Received(1).SetAsync(
            "/https%3A%2F%2Fexample.com%2Fsmall%2F",
            Arg.Any<byte[]>(),
            TimeSpan.FromHours(12));
    }

    [Fact]
    public async Task HandleAsync_InvalidTargetUrl_ReturnsBadRequest()
    {
        var context = CreateContext("/not-a-url/");

        var result = await ScreenshotEndpoint.HandleAsync(
            context,
            Substitute.For<IScreenshotService>(),
            Substitute.For<ICacheService>(),
            BuildConfig(),
            NullLogger<Program>.Instance);

        var (status, _, body) = await ExecuteAsync(result);

        Assert.Equal(400, status);
        Assert.Contains("A URL is required", System.Text.Encoding.UTF8.GetString(body));
    }

    [Fact]
    public async Task HandleAsync_WithoutRawTargetFeature_FallsBackToRequestPath()
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpRequestFeature>(new HttpRequestFeature
        {
            RawTarget = null,
            Path = "/http/small",
            QueryString = string.Empty
        });
        context.Request.Path = "/http/small";
        context.Response.Body = new MemoryStream();

        var screenshot = Substitute.For<IScreenshotService>();
        var cache = Substitute.For<ICacheService>();
        var bytes = CreatePng(6, 6);

        cache.GetAsync("/http/small").Returns((byte[]?)null);
        screenshot.CaptureAsync(Arg.Any<ScreenshotOptions>()).Returns(bytes);

        var result = await ScreenshotEndpoint.HandleAsync(
            context,
            screenshot,
            cache,
            BuildConfig(),
            NullLogger<Program>.Instance);

        var (status, contentType, body) = await ExecuteAsync(result);

        Assert.Equal(200, status);
        Assert.Equal("image/png", contentType);
        Assert.Equal(bytes, body);
        await screenshot.Received(1).CaptureAsync(Arg.Is<ScreenshotOptions>(opts =>
            opts.TargetUrl == "http" && opts.Size == ScreenshotSize.Small));
    }

    [Fact]
    public async Task HandleAsync_WithoutRawTargetOrPath_FallsBackToSlashAndReturnsBadRequest()
    {
        var context = new DefaultHttpContext();

        var result = await ScreenshotEndpoint.HandleAsync(
            context,
            Substitute.For<IScreenshotService>(),
            Substitute.For<ICacheService>(),
            BuildConfig(),
            NullLogger<Program>.Instance);

        var (status, _, body) = await ExecuteAsync(result);

        Assert.Equal(400, status);
        Assert.Contains("A URL is required", System.Text.Encoding.UTF8.GetString(body));
    }

    [Fact]
    public async Task HandleAsync_JpegQuery_UsesJpegCacheKeyAndContentType()
    {
        var context = CreateContext("/https%3A%2F%2Fexample.com%2F");
        context.Request.QueryString = new QueryString("?format=jpeg");

        var screenshot = Substitute.For<IScreenshotService>();
        var cache = Substitute.For<ICacheService>();
        var bytes = CreateJpeg(6, 6);

        cache.GetAsync("/https%3A%2F%2Fexample.com%2F?format=jpeg").Returns((byte[]?)null);
        screenshot.CaptureAsync(Arg.Any<ScreenshotOptions>()).Returns(bytes);

        var result = await ScreenshotEndpoint.HandleAsync(
            context,
            screenshot,
            cache,
            BuildConfig(),
            NullLogger<Program>.Instance);

        var (status, contentType, body) = await ExecuteAsync(result);

        Assert.Equal(200, status);
        Assert.Equal("image/jpeg", contentType);
        Assert.Equal(bytes, body);
        await cache.Received(1).SetAsync(
            "/https%3A%2F%2Fexample.com%2F?format=jpeg",
            Arg.Any<byte[]>(),
            TimeSpan.FromHours(24));
    }

    [Fact]
    public async Task HandleAsync_WhenRawTargetContainsQuery_StripsItBeforeParsing()
    {
        var context = CreateContext("/https%3A%2F%2Fexample.com%2F?format=jpeg");
        context.Request.QueryString = new QueryString("?format=jpeg");

        var screenshot = Substitute.For<IScreenshotService>();
        var cache = Substitute.For<ICacheService>();
        var bytes = CreateJpeg(4, 4);

        cache.GetAsync("/https%3A%2F%2Fexample.com%2F?format=jpeg").Returns((byte[]?)null);
        screenshot.CaptureAsync(Arg.Any<ScreenshotOptions>()).Returns(bytes);

        var result = await ScreenshotEndpoint.HandleAsync(
            context,
            screenshot,
            cache,
            BuildConfig(),
            NullLogger<Program>.Instance);

        var (status, contentType, body) = await ExecuteAsync(result);

        Assert.Equal(200, status);
        Assert.Equal("image/jpeg", contentType);
        Assert.Equal(bytes, body);
    }

    [Fact]
    public async Task HandleAsync_WhenWidthProvided_ResizesImage()
    {
        var context = CreateContext("/https%3A%2F%2Fexample.com%2F/_width:20/");
        var screenshot = Substitute.For<IScreenshotService>();
        var cache = Substitute.For<ICacheService>();
        var original = CreatePng(100, 50);

        cache.GetAsync("/https%3A%2F%2Fexample.com%2F/_width:20/").Returns((byte[]?)null);
        screenshot.CaptureAsync(Arg.Any<ScreenshotOptions>()).Returns(original);

        var result = await ScreenshotEndpoint.HandleAsync(
            context,
            screenshot,
            cache,
            BuildConfig(),
            NullLogger<Program>.Instance);

        var (status, contentType, body) = await ExecuteAsync(result);

        Assert.Equal(200, status);
        Assert.Equal("image/png", contentType);

        using var decoded = SKBitmap.Decode(body);
        Assert.Equal(20, decoded.Width);
        Assert.Equal(10, decoded.Height);
    }

    [Fact]
    public async Task HandleAsync_WhenWidthProvidedForJpeg_ResizesAndReturnsJpeg()
    {
        var context = CreateContext("/https%3A%2F%2Fexample.com%2F/_width:20/");
        context.Request.QueryString = new QueryString("?format=jpeg");

        var screenshot = Substitute.For<IScreenshotService>();
        var cache = Substitute.For<ICacheService>();
        var original = CreateJpeg(100, 50);

        cache.GetAsync("/https%3A%2F%2Fexample.com%2F/_width:20/?format=jpeg").Returns((byte[]?)null);
        screenshot.CaptureAsync(Arg.Any<ScreenshotOptions>()).Returns(original);

        var result = await ScreenshotEndpoint.HandleAsync(
            context,
            screenshot,
            cache,
            BuildConfig(),
            NullLogger<Program>.Instance);

        var (status, contentType, body) = await ExecuteAsync(result);

        Assert.Equal(200, status);
        Assert.Equal("image/jpeg", contentType);

        using var decoded = SKBitmap.Decode(body);
        Assert.Equal(20, decoded.Width);
        Assert.Equal(10, decoded.Height);
    }

    [Fact]
    public async Task HandleAsync_WhenCacheReadThrows_StillCapturesAndReturnsBytes()
    {
        var context = CreateContext("/https%3A%2F%2Fexample.com%2F");
        var screenshot = Substitute.For<IScreenshotService>();
        var cache = Substitute.For<ICacheService>();
        var bytes = CreatePng(10, 10);

        cache.GetAsync("/https%3A%2F%2Fexample.com%2F")
            .Returns(Task.FromException<byte[]?>(new Exception("redis read failed")));
        screenshot.CaptureAsync(Arg.Any<ScreenshotOptions>()).Returns(bytes);

        var result = await ScreenshotEndpoint.HandleAsync(
            context,
            screenshot,
            cache,
            BuildConfig(),
            NullLogger<Program>.Instance);

        var (status, contentType, body) = await ExecuteAsync(result);

        Assert.Equal(200, status);
        Assert.Equal("image/png", contentType);
        Assert.Equal(bytes, body);
        await screenshot.Received(1).CaptureAsync(Arg.Any<ScreenshotOptions>());
    }

    [Fact]
    public async Task HandleAsync_WhenCacheWriteThrows_StillReturnsBytes()
    {
        var context = CreateContext("/https%3A%2F%2Fexample.com%2F");
        var screenshot = Substitute.For<IScreenshotService>();
        var cache = Substitute.For<ICacheService>();
        var bytes = CreatePng(12, 12);

        cache.GetAsync("/https%3A%2F%2Fexample.com%2F").Returns((byte[]?)null);
        cache.When(x => x.SetAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<TimeSpan>()))
            .Do(_ => throw new Exception("redis write failed"));
        screenshot.CaptureAsync(Arg.Any<ScreenshotOptions>()).Returns(bytes);

        var result = await ScreenshotEndpoint.HandleAsync(
            context,
            screenshot,
            cache,
            BuildConfig(),
            NullLogger<Program>.Instance);

        var (status, contentType, body) = await ExecuteAsync(result);

        Assert.Equal(200, status);
        Assert.Equal("image/png", contentType);
        Assert.Equal(bytes, body);
    }

    [Fact]
    public async Task HandleAsync_WhenCaptureTimesOut_Returns504()
    {
        var context = CreateContext("/https%3A%2F%2Fexample.com%2F");
        var screenshot = Substitute.For<IScreenshotService>();
        var cache = Substitute.For<ICacheService>();

        cache.GetAsync("/https%3A%2F%2Fexample.com%2F").Returns((byte[]?)null);
        screenshot.CaptureAsync(Arg.Any<ScreenshotOptions>())
            .Returns(Task.FromException<byte[]>(new PlaywrightException("navigation timeout")));

        var result = await ScreenshotEndpoint.HandleAsync(
            context,
            screenshot,
            cache,
            BuildConfig(),
            NullLogger<Program>.Instance);

        var (status, _, _) = await ExecuteAsync(result);
        Assert.Equal(504, status);
    }

    [Fact]
    public async Task HandleAsync_WhenCaptureGetsInvalidUrl_Returns400()
    {
        var context = CreateContext("/https%3A%2F%2Fexample.com%2F");
        var screenshot = Substitute.For<IScreenshotService>();
        var cache = Substitute.For<ICacheService>();

        cache.GetAsync("/https%3A%2F%2Fexample.com%2F").Returns((byte[]?)null);
        screenshot.CaptureAsync(Arg.Any<ScreenshotOptions>())
            .Returns(Task.FromException<byte[]>(new PlaywrightException("invalid url")));

        var result = await ScreenshotEndpoint.HandleAsync(
            context,
            screenshot,
            cache,
            BuildConfig(),
            NullLogger<Program>.Instance);

        var (status, _, body) = await ExecuteAsync(result);
        Assert.Equal(400, status);
        Assert.Contains("Invalid URL", System.Text.Encoding.UTF8.GetString(body));
    }

    private static IConfiguration BuildConfig(int ttlHours = 24)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SCREENSHOT_CACHE_TTL_HOURS"] = ttlHours.ToString()
            })
            .Build();

    private static DefaultHttpContext CreateContext(string rawTarget)
    {
        var context = new DefaultHttpContext();
        var path = rawTarget.Contains('?')
            ? rawTarget[..rawTarget.IndexOf('?')]
            : rawTarget;

        context.Features.Set<IHttpRequestFeature>(new HttpRequestFeature
        {
            RawTarget = rawTarget,
            Path = path,
            QueryString = string.Empty
        });

        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<(int StatusCode, string? ContentType, byte[] Body)> ExecuteAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.RequestServices = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        using var ms = new MemoryStream();
        await context.Response.Body.CopyToAsync(ms);
        return (context.Response.StatusCode, context.Response.ContentType, ms.ToArray());
    }

    private static byte[] CreatePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] CreateJpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.ForestGreen);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }
}