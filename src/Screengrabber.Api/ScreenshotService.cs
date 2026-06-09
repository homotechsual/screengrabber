using Microsoft.Playwright;

namespace Screengrabber.Api;

public interface IScreenshotService
{
    Task<byte[]> CaptureAsync(ScreenshotOptions options);
}

public sealed class ScreenshotService : BackgroundService, IScreenshotService
{
    private IPlaywright? _playwright;
    private IBrowser?    _browser;
    private readonly SemaphoreSlim _semaphore;
    private readonly ILogger<ScreenshotService> _logger;

    public ScreenshotService(IConfiguration config, ILogger<ScreenshotService> logger)
    {
        var concurrency = config.GetValue("SCREENSHOT_CONCURRENCY", 4);
        _semaphore = new SemaphoreSlim(concurrency, concurrency);
        _logger    = logger;
    }

    public override async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("Launching Edge browser via Playwright...");
        _playwright = await Playwright.CreateAsync();
        _browser    = await _playwright.Chromium.LaunchAsync(new()
        {
            Channel = "msedge",
            Args    = ["--no-sandbox"]
        });
        _logger.LogInformation("Browser ready.");
        await base.StartAsync(ct);
    }

    protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;

    public override async Task StopAsync(CancellationToken ct)
    {
        await base.StopAsync(ct);
        if (_browser is not null) await _browser.CloseAsync();
        _playwright?.Dispose();
    }

    public async Task<byte[]> CaptureAsync(ScreenshotOptions options)
    {
        await _semaphore.WaitAsync();
        try
        {
            var (width, height) = options.GetViewport();

            await using var context = await _browser!.NewContextAsync(new()
            {
                ViewportSize      = new() { Width = width, Height = height },
                DeviceScaleFactor = (float?)options.GetDeviceScaleFactor()
            });

            var page = await context.NewPageAsync();

            await page.GotoAsync(options.TargetUrl, new()
            {
                Timeout   = (float?)((float)(options.TimeoutSeconds * 1000)),
                WaitUntil = options.ToPlaywrightWaitUntil()
            });

            return await page.ScreenshotAsync(new()
            {
                Type     = options.Format == ImageFormat.Jpeg ? ScreenshotType.Jpeg : ScreenshotType.Png,
                FullPage = false
            });
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
