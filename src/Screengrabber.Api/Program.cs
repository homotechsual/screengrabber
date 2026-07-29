using Screengrabber.Api;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Redis
var redisConn = builder.Configuration["REDIS_CONNECTION"] ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(
    _ => ConnectionMultiplexer.Connect(redisConn));
builder.Services.AddSingleton<ICacheService, CacheService>();

// Playwright / screenshot service
builder.Services.AddSingleton<ScreenshotService>();
builder.Services.AddSingleton<IScreenshotService>(
    sp => sp.GetRequiredService<ScreenshotService>());
builder.Services.AddHostedService(
    sp => sp.GetRequiredService<ScreenshotService>());

var app = builder.Build();

// API key middleware
var apiKeys = ApiKeyAuth.ParseConfiguredKeys(builder.Configuration["API_KEYS"]);

app.Use(async (context, next) =>
{
    if (!ApiKeyAuth.IsRequestAuthorized(context, apiKeys))
    {
        context.Response.StatusCode = 401;
        return;
    }
    await next(context);
});

// Screenshot route — catch-all
app.MapGet("/{**path}", ScreenshotEndpoint.HandleAsync);

app.Run();
