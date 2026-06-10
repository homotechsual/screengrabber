using Microsoft.AspNetCore.Http.Features;
using Microsoft.Playwright;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Screengrabber.Api;

public static class ScreenshotEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext context,
        IScreenshotService screenshotService,
        ICacheService cacheService,
        IConfiguration config)
    {
        // Use raw target to preserve %2F in the encoded URL segment
        var rawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget
                        ?? context.Request.Path.Value
                        ?? "/";

        var rawPath = rawTarget.Contains('?')
            ? rawTarget[..rawTarget.IndexOf('?')]
            : rawTarget;

        var format   = context.Request.Query["format"].ToString();
        var cacheKey = string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase)
                       ? rawPath + "?format=jpeg"
                       : rawPath;
        var options  = ScreenshotOptions.Parse(rawPath, format);

        if (string.IsNullOrEmpty(options.TargetUrl) ||
            !options.TargetUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest("A URL is required: /{url}/{size}/{aspectratio}/{zoom}/");

        // Cache hit
        var cached = await cacheService.GetAsync(cacheKey);
        if (cached is not null)
            return Results.Bytes(cached, options.ContentType);

        // Capture
        byte[] bytes;
        try
        {
            bytes = await screenshotService.CaptureAsync(options);
        }
        catch (PlaywrightException ex) when (
            ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return Results.StatusCode(504);
        }
        catch (PlaywrightException ex) when (
            ex.Message.Contains("invalid url", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest("Invalid URL.");
        }

        if (options.OutputWidth.HasValue)
            bytes = Resize(bytes, options.OutputWidth.Value, options.Format);

        // Store and return
        var ttlHours = config.GetValue("SCREENSHOT_CACHE_TTL_HOURS", 24);
        await cacheService.SetAsync(cacheKey, bytes, TimeSpan.FromHours(ttlHours));

        return Results.Bytes(bytes, options.ContentType);
    }

    private static byte[] Resize(byte[] input, int width, ImageFormat format)
    {
        using var image = Image.Load(input);
        image.Mutate(x => x.Resize(width, 0, KnownResamplers.Lanczos3));
        using var ms = new MemoryStream();
        if (format == ImageFormat.Jpeg)
            image.SaveAsJpeg(ms, new JpegEncoder { Quality = 85 });
        else
            image.SaveAsPng(ms);
        return ms.ToArray();
    }
}
