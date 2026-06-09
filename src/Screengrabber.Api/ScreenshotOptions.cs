using Microsoft.Playwright;

namespace Screengrabber.Api;

public enum ScreenshotSize   { Small, Medium, Large, OpenGraph }
public enum AspectRatio      { OneOne, NineSixteen }
public enum ZoomLevel        { Normal, Bigger, Smaller }
public enum WaitLevel        { DomContentLoaded, Load, NetworkIdle }
public enum ImageFormat      { Png, Jpeg }

public record ScreenshotOptions(
    string TargetUrl,
    ScreenshotSize Size,
    AspectRatio AspectRatio,
    ZoomLevel Zoom,
    WaitLevel WaitLevel,
    int TimeoutSeconds,
    ImageFormat Format)
{
    public static ScreenshotOptions Parse(string rawPath, string? formatQuery)
    {
        var segments = rawPath.TrimStart('/').TrimEnd('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();

        var targetUrl     = segments.Length > 0 ? segments[0] : "";
        var size          = ScreenshotSize.Small;
        var aspectRatio   = AspectRatio.OneOne;
        var zoom          = ZoomLevel.Normal;
        var waitLevel     = WaitLevel.Load;
        var timeoutSecs   = 6;

        foreach (var segment in segments.Skip(1))
        {
            var lower = segment.ToLowerInvariant();
            if      (lower == "small")     { size = ScreenshotSize.Small;     continue; }
            else if (lower == "medium")    { size = ScreenshotSize.Medium;    continue; }
            else if (lower == "large")     { size = ScreenshotSize.Large;     continue; }
            else if (lower == "opengraph") { size = ScreenshotSize.OpenGraph; continue; }
            else if (lower == "1:1")       { aspectRatio = AspectRatio.OneOne;      continue; }
            else if (lower == "9:16")      { aspectRatio = AspectRatio.NineSixteen; continue; }
            else if (lower == "bigger")    { zoom = ZoomLevel.Bigger;  continue; }
            else if (lower == "smaller")   { zoom = ZoomLevel.Smaller; continue; }

            // modifier token segment: split on '_', ignore empty (leading underscore)
            foreach (var token in segment.Split('_', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.StartsWith("wait:", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(token[5..], out var w))
                {
                    waitLevel = w switch
                    {
                        0 => WaitLevel.DomContentLoaded,
                        1 => WaitLevel.Load,
                        _ => WaitLevel.NetworkIdle
                    };
                }
                else if (token.StartsWith("timeout:", StringComparison.OrdinalIgnoreCase)
                         && int.TryParse(token[8..], out var t))
                {
                    timeoutSecs = Math.Clamp(t, 3, 9);
                }
                // other tokens (cache-bust strings) silently ignored
            }
        }

        var format = string.Equals(formatQuery, "jpeg", StringComparison.OrdinalIgnoreCase)
            ? ImageFormat.Jpeg
            : ImageFormat.Png;

        return new ScreenshotOptions(targetUrl, size, aspectRatio, zoom, waitLevel, timeoutSecs, format);
    }

    public (int Width, int Height) GetViewport() => (Size, AspectRatio) switch
    {
        (ScreenshotSize.OpenGraph, _)                        => (1200, 630),
        (ScreenshotSize.Small,  AspectRatio.NineSixteen)     => (375,  667),
        (ScreenshotSize.Small,  _)                           => (375,  375),
        (ScreenshotSize.Medium, AspectRatio.NineSixteen)     => (650, 1156),
        (ScreenshotSize.Medium, _)                           => (650,  650),
        _                                                    => (1024, 1024)
    };

    public double GetDeviceScaleFactor() => Zoom switch
    {
        ZoomLevel.Bigger  => 1.4,
        ZoomLevel.Smaller => 0.71,
        _                 => 1.0
    };

    public WaitUntilState ToPlaywrightWaitUntil() => WaitLevel switch
    {
        WaitLevel.DomContentLoaded => WaitUntilState.DOMContentLoaded,
        WaitLevel.NetworkIdle      => WaitUntilState.NetworkIdle,
        _                          => WaitUntilState.Load
    };

    public string ContentType => Format == ImageFormat.Jpeg ? "image/jpeg" : "image/png";
}
