using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Screengrabber.Api;

namespace Screengrabber.Api.Tests;

public class ProgramTests
{
    private const string ValidRequestPath = "/https%3A%2F%2Fexample.com%2F";

    [Fact]
    public async Task Program_WithValidApiKey_CanReachScreenshotRoute()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        client.DefaultRequestHeaders.Add("X-Api-Key", "test-key");

        var response = await client.GetAsync(ValidRequestPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
    }

    [Fact]
    public async Task Program_WithInvalidApiKey_ReturnsUnauthorized()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");

        var response = await client.GetAsync(ValidRequestPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Program_WithMissingApiKey_ReturnsUnauthorized()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(ValidRequestPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory()
        => new TestApplicationFactory();

    private sealed class TestApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["API_KEYS"] = "test-key",
                    ["REDIS_CONNECTION"] = "unused:6379",
                    ["SCREENSHOT_CACHE_TTL_HOURS"] = "24"
                });
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IScreenshotService>();
                services.RemoveAll<ICacheService>();
                services.RemoveAll<ScreenshotService>();

                services.AddSingleton<IScreenshotService>(new StubScreenshotService());
                services.AddSingleton<ICacheService>(new StubCacheService());
            });
        }
    }

    private sealed class StubScreenshotService : IScreenshotService
    {
        public Task<byte[]> CaptureAsync(ScreenshotOptions options)
            => Task.FromResult(new byte[] { 1, 2, 3 });
    }

    private sealed class StubCacheService : ICacheService
    {
        public Task<byte[]?> GetAsync(string key) => Task.FromResult<byte[]?>(null);

        public Task SetAsync(string key, byte[] value, TimeSpan ttl) => Task.CompletedTask;
    }
}