using Microsoft.AspNetCore.Http.Features;
using Microsoft.Playwright;

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
            ex.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return Results.StatusCode(504);
        }

        // Store and return
        var ttlHours = config.GetValue("SCREENSHOT_CACHE_TTL_HOURS", 24);
        await cacheService.SetAsync(cacheKey, bytes, TimeSpan.FromHours(ttlHours));

        return Results.Bytes(bytes, options.ContentType);
    }
}
