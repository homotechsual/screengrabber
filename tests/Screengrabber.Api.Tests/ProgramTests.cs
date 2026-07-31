using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http.Features;
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
        var response = await SendAsync(factory, "test-key");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.ContentType);
        var bytes = response.Body;
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
    }

    [Fact]
    public async Task Program_WithInvalidApiKey_ReturnsUnauthorized()
    {
        await using var factory = CreateFactory();
        var response = await SendAsync(factory, "wrong-key");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Program_WithMissingApiKey_ReturnsUnauthorized()
    {
        await using var factory = CreateFactory();
        var response = await SendAsync(factory, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory()
        => new TestApplicationFactory();

    private static async Task<(HttpStatusCode StatusCode, string? ContentType, byte[] Body)> SendAsync(
        WebApplicationFactory<Program> factory,
        string? apiKey)
    {
        var responseBody = new MemoryStream();

        var context = await factory.Server.SendAsync(requestContext =>
        {
            requestContext.Request.Method = "GET";
            requestContext.Request.Path = ValidRequestPath;
            requestContext.Response.Body = responseBody;

            if (apiKey is not null)
                requestContext.Request.Headers["X-Api-Key"] = apiKey;

            var requestFeature = requestContext.Features.Get<IHttpRequestFeature>();
            if (requestFeature is not null)
            {
                requestFeature.Method = "GET";
                requestFeature.Path = ValidRequestPath;
                requestFeature.RawTarget = ValidRequestPath;
            }
        });

        responseBody.Position = 0;
        return ((HttpStatusCode)context.Response.StatusCode, context.Response.ContentType, responseBody.ToArray());
    }

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