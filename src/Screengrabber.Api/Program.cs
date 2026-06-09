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
var apiKeys = (builder.Configuration["API_KEYS"] ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .ToHashSet();

app.Use(async (context, next) =>
{
    var key = context.Request.Headers["X-Api-Key"].ToString();
    if (!ApiKeyMiddleware.IsAuthorized(string.IsNullOrEmpty(key) ? null : key, apiKeys))
    {
        context.Response.StatusCode = 401;
        return;
    }
    await next(context);
});

// Screenshot route — catch-all
app.MapGet("/{**path}", ScreenshotEndpoint.HandleAsync);

app.Run();
