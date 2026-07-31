using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using NSubstitute;
using Screengrabber.Api;

namespace Screengrabber.Api.Tests;

public class ScreenshotServiceTests
{
    [Fact]
    public async Task PlaywrightFactory_CreateAsync_UsesConfiguredDelegate()
    {
        var expected = Substitute.For<IPlaywright>();
        var calls = 0;
        var factory = new PlaywrightFactory(() =>
        {
            calls++;
            return Task.FromResult(expected);
        });

        var result = await factory.CreateAsync();

        Assert.Same(expected, result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task StartAsync_LaunchesBrowserAndLogsStartupMessages()
    {
        var logger = Substitute.For<ILogger<ScreenshotService>>();
        var service = CreateService(logger);
        var playwrightFactory = Substitute.For<IPlaywrightFactory>();
        var playwright = Substitute.For<IPlaywright>();
        var browserType = Substitute.For<IBrowserType>();
        var browser = Substitute.For<IBrowser>();

        playwrightFactory.CreateAsync().Returns(playwright);
        playwright.Chromium.Returns(browserType);
        browserType.LaunchAsync(Arg.Any<BrowserTypeLaunchOptions>()).Returns(browser);

        SetPrivateField(service, "_playwrightFactory", playwrightFactory);

        await service.StartAsync(CancellationToken.None);

        await playwrightFactory.Received(1).CreateAsync();
        await browserType.Received(1).LaunchAsync(Arg.Any<BrowserTypeLaunchOptions>());
        ReceivedLoggerMessage(logger, "Launching Edge browser via Playwright...");
        ReceivedLoggerMessage(logger, "Browser ready.");
    }

    [Fact]
    public async Task CaptureAsync_WhenBrowserIsNotReady_Throws()
    {
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CaptureAsync(CreateOptions()));

        Assert.Equal("Browser is not ready.", ex.Message);
    }

    [Fact]
    public async Task CaptureAsync_WithBrowser_RendersAndReturnsScreenshotBytes()
    {
        var service = CreateService();
        var browser = Substitute.For<IBrowser>();
        var context = Substitute.For<IBrowserContext>();
        var page = Substitute.For<IPage>();
        var expected = new byte[] { 1, 2, 3, 4 };

        browser.NewContextAsync(Arg.Any<BrowserNewContextOptions>()).Returns(context);
        context.NewPageAsync().Returns(page);
        page.GotoAsync(Arg.Any<string>(), Arg.Any<PageGotoOptions>()).Returns(Task.FromResult<IResponse?>(null));
        page.ScreenshotAsync(Arg.Any<PageScreenshotOptions>()).Returns(expected);

        SetPrivateField(service, "_browser", browser);

        var result = await service.CaptureAsync(
            ScreenshotOptions.Parse("/https%3A%2F%2Fexample.com%2F/small/1:1/bigger/_timeout:7/", null));

        Assert.Equal(expected, result);
        await browser.Received(1).NewContextAsync(Arg.Any<BrowserNewContextOptions>());
        await context.Received(1).NewPageAsync();
        await page.Received(1).GotoAsync(
            "https://example.com/",
            Arg.Is<PageGotoOptions>(opts =>
                opts.Timeout == 7000 &&
                opts.WaitUntil == WaitUntilState.Load));
        await page.Received(1).ScreenshotAsync(Arg.Is<PageScreenshotOptions>(opts =>
            opts.FullPage == false && opts.Type == ScreenshotType.Png));
    }

    [Fact]
    public async Task StopAsync_ClosesBrowserAndDisposesPlaywright()
    {
        var service = CreateService();
        var browser = Substitute.For<IBrowser>();
        var playwright = Substitute.For<IPlaywright>();

        SetPrivateField(service, "_browser", browser);
        SetPrivateField(service, "_playwright", playwright);

        await service.StopAsync(CancellationToken.None);

        await browser.Received(1).CloseAsync();
        playwright.Received(1).Dispose();
    }

    [Fact]
    public async Task StopAsync_WhenPlaywrightIsNull_DoesNotThrow()
    {
        var service = CreateService();
        var browser = Substitute.For<IBrowser>();

        SetPrivateField(service, "_browser", browser);

        await service.StopAsync(CancellationToken.None);

        await browser.Received(1).CloseAsync();
    }

    [Fact]
    public async Task CaptureAsync_WithJpegFormat_UsesJpegScreenshotType()
    {
        var service = CreateService();
        var browser = Substitute.For<IBrowser>();
        var context = Substitute.For<IBrowserContext>();
        var page = Substitute.For<IPage>();
        var expected = new byte[] { 9, 9, 9 };

        browser.NewContextAsync(Arg.Any<BrowserNewContextOptions>()).Returns(context);
        context.NewPageAsync().Returns(page);
        page.GotoAsync(Arg.Any<string>(), Arg.Any<PageGotoOptions>()).Returns(Task.FromResult<IResponse?>(null));
        page.ScreenshotAsync(Arg.Any<PageScreenshotOptions>()).Returns(expected);

        SetPrivateField(service, "_browser", browser);

        var result = await service.CaptureAsync(
            ScreenshotOptions.Parse("/https%3A%2F%2Fexample.com%2F", "jpeg"));

        Assert.Equal(expected, result);
        await page.Received(1).ScreenshotAsync(Arg.Is<PageScreenshotOptions>(opts =>
            opts.Type == ScreenshotType.Jpeg && opts.FullPage == false));
    }

    private static ScreenshotService CreateService()
        => CreateService(NullLogger<ScreenshotService>.Instance);

    private static ScreenshotService CreateService(ILogger<ScreenshotService> logger)
        => new(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SCREENSHOT_CONCURRENCY"] = "2"
                })
                .Build(),
            logger,
            Substitute.For<IPlaywrightFactory>());

    private static ScreenshotOptions CreateOptions()
        => ScreenshotOptions.Parse("/https%3A%2F%2Fexample.com%2F", null);

    private static void SetPrivateField<T>(T instance, string fieldName, object? value)
    {
        var field = typeof(T).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }

    private static void ReceivedLoggerMessage(ILogger<ScreenshotService> logger, string message)
    {
        logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(state => IsExpectedLogMessage(state, message)),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    private static bool IsExpectedLogMessage(object state, string message)
        => state.ToString() == message;
}