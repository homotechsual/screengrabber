# Screengrabber Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a .NET 9 screenshot API that mirrors the slorber-api-screenshot URL structure, using Microsoft Edge via Playwright, with Redis caching and a GitHub Actions deploy workflow that mirrors GCT's.

**Architecture:** Single container (Playwright + .NET 9 self-contained publish on `mcr.microsoft.com/playwright/dotnet` base) alongside a Redis container. GCT's existing Caddy handles TLS and routing via a shared external Docker network named `proxy`. A `BackgroundService` holds a single persistent Edge browser instance; a `SemaphoreSlim` caps concurrent captures at 4.

**Tech Stack:** .NET 9 (self-contained, linux-x64), Microsoft.Playwright 1.50, StackExchange.Redis, xUnit, NSubstitute, Docker Compose, GitHub Actions, GHCR.

---

## File Map

| File | Action | Responsibility |
|---|---|---|
| `Screengrabber.sln` | Create | Solution |
| `src/Screengrabber.Api/Screengrabber.Api.csproj` | Create | Project file, net9.0 |
| `src/Screengrabber.Api/Program.cs` | Create | DI wiring, middleware, route |
| `src/Screengrabber.Api/ScreenshotOptions.cs` | Create | Parsed options record + path parser |
| `src/Screengrabber.Api/ScreenshotService.cs` | Create | Playwright/Edge + SemaphoreSlim |
| `src/Screengrabber.Api/CacheService.cs` | Create | Redis get/set wrapper |
| `src/Screengrabber.Api/ApiKeyMiddleware.cs` | Create | Optional auth static helper |
| `src/Screengrabber.Api/ScreenshotEndpoint.cs` | Create | Minimal API route handler |
| `tests/Screengrabber.Api.Tests/Screengrabber.Api.Tests.csproj` | Create | Test project |
| `tests/Screengrabber.Api.Tests/ScreenshotOptionsTests.cs` | Create | Unit tests for path parsing |
| `tests/Screengrabber.Api.Tests/ApiKeyMiddlewareTests.cs` | Create | Unit tests for auth logic |
| `tests/Screengrabber.Api.Tests/CacheServiceTests.cs` | Create | Unit tests for Redis wrapper |
| `Dockerfile` | Create | Multi-stage build → Edge installed |
| `docker-compose.yml` | Create | api + redis, proxy external network |
| `.env.example` | Create | Documented env vars |
| `.gitignore` | Create | Standard .NET ignore |
| `.github/workflows/deploy.yml` | Create | Mirror of GCT deploy workflow |
| `j:\Projects\GCT\docker-compose.prod.yml` | Modify | Add proxy network + SCREENGRABBER_DOMAIN to caddy |
| `j:\Projects\GCT\Caddyfile` | Modify | Add screengrabber virtual host block |
| `j:\Projects\GCT\.github\workflows\deploy.yml` | Modify | Pass SCREENGRABBER_DOMAIN into .env |

---

### Task 1: Solution scaffold

**Files:**
- Create: `Screengrabber.sln`
- Create: `src/Screengrabber.Api/Screengrabber.Api.csproj`
- Create: `tests/Screengrabber.Api.Tests/Screengrabber.Api.Tests.csproj`
- Create: `.gitignore`

- [ ] **Step 1: Create solution and projects**

Run from `j:\Projects\screengrabber`:
```powershell
dotnet new sln -n Screengrabber
dotnet new web -n Screengrabber.Api -o src/Screengrabber.Api
dotnet new xunit -n Screengrabber.Api.Tests -o tests/Screengrabber.Api.Tests
dotnet sln add src/Screengrabber.Api/Screengrabber.Api.csproj
dotnet sln add tests/Screengrabber.Api.Tests/Screengrabber.Api.Tests.csproj
dotnet add tests/Screengrabber.Api.Tests reference src/Screengrabber.Api/Screengrabber.Api.csproj
```

- [ ] **Step 2: Add NuGet packages**

```powershell
dotnet add src/Screengrabber.Api package Microsoft.Playwright
dotnet add src/Screengrabber.Api package StackExchange.Redis
dotnet add tests/Screengrabber.Api.Tests package NSubstitute
```

- [ ] **Step 3: Replace `src/Screengrabber.Api/Screengrabber.Api.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Playwright" Version="1.50.0" />
    <PackageReference Include="StackExchange.Redis" Version="2.8.16" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Replace `src/Screengrabber.Api/Program.cs` with empty shell**

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.Run();
```

- [ ] **Step 5: Create `.gitignore`**

```
bin/
obj/
.env
*.user
.vs/
.idea/
```

- [ ] **Step 6: Build to verify**

```powershell
dotnet build
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 7: Commit**

```bash
git add .
git commit -m "chore: scaffold solution and projects"
```

---

### Task 2: ScreenshotOptions — record, enums, parser, and viewport helpers

**Files:**
- Create: `src/Screengrabber.Api/ScreenshotOptions.cs`
- Create: `tests/Screengrabber.Api.Tests/ScreenshotOptionsTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Screengrabber.Api.Tests/ScreenshotOptionsTests.cs`:

```csharp
using Screengrabber.Api;
using Xunit;

namespace Screengrabber.Api.Tests;

public class ScreenshotOptionsTests
{
    [Fact]
    public void Parse_MinimalPath_ReturnsDefaults()
    {
        var opts = ScreenshotOptions.Parse("/https%3A%2F%2Fexample.com/", null);

        Assert.Equal("https://example.com", opts.TargetUrl);
        Assert.Equal(ScreenshotSize.Small, opts.Size);
        Assert.Equal(AspectRatio.OneOne, opts.AspectRatio);
        Assert.Equal(ZoomLevel.Normal, opts.Zoom);
        Assert.Equal(WaitLevel.Load, opts.WaitLevel);
        Assert.Equal(6, opts.TimeoutSeconds);
        Assert.Equal(ImageFormat.Png, opts.Format);
    }

    [Fact]
    public void Parse_SizeSegment_SetsSize()
    {
        Assert.Equal(ScreenshotSize.Small,     ScreenshotOptions.Parse("/u/small/", null).Size);
        Assert.Equal(ScreenshotSize.Medium,    ScreenshotOptions.Parse("/u/medium/", null).Size);
        Assert.Equal(ScreenshotSize.Large,     ScreenshotOptions.Parse("/u/large/", null).Size);
        Assert.Equal(ScreenshotSize.OpenGraph, ScreenshotOptions.Parse("/u/opengraph/", null).Size);
    }

    [Fact]
    public void Parse_AspectRatioSegment_SetsRatio()
    {
        Assert.Equal(AspectRatio.OneOne,      ScreenshotOptions.Parse("/u/small/1:1/", null).AspectRatio);
        Assert.Equal(AspectRatio.NineSixteen, ScreenshotOptions.Parse("/u/small/9:16/", null).AspectRatio);
    }

    [Fact]
    public void Parse_ZoomSegment_SetsZoom()
    {
        Assert.Equal(ZoomLevel.Bigger,  ScreenshotOptions.Parse("/u/small/1:1/bigger/", null).Zoom);
        Assert.Equal(ZoomLevel.Smaller, ScreenshotOptions.Parse("/u/small/1:1/smaller/", null).Zoom);
    }

    [Fact]
    public void Parse_WaitToken_SetsWaitLevel()
    {
        Assert.Equal(WaitLevel.DomContentLoaded, ScreenshotOptions.Parse("/u/_wait:0/", null).WaitLevel);
        Assert.Equal(WaitLevel.Load,             ScreenshotOptions.Parse("/u/_wait:1/", null).WaitLevel);
        Assert.Equal(WaitLevel.NetworkIdle,      ScreenshotOptions.Parse("/u/_wait:2/", null).WaitLevel);
        Assert.Equal(WaitLevel.NetworkIdle,      ScreenshotOptions.Parse("/u/_wait:3/", null).WaitLevel);
    }

    [Fact]
    public void Parse_TimeoutToken_ClampsTo3To9()
    {
        Assert.Equal(5, ScreenshotOptions.Parse("/u/_timeout:5/", null).TimeoutSeconds);
        Assert.Equal(3, ScreenshotOptions.Parse("/u/_timeout:1/", null).TimeoutSeconds);
        Assert.Equal(9, ScreenshotOptions.Parse("/u/_timeout:99/", null).TimeoutSeconds);
    }

    [Fact]
    public void Parse_CombinedTokens_ParsesAll()
    {
        var opts = ScreenshotOptions.Parse("/u/_20250609_wait:2_timeout:7/", null);
        Assert.Equal(WaitLevel.NetworkIdle, opts.WaitLevel);
        Assert.Equal(7, opts.TimeoutSeconds);
    }

    [Fact]
    public void Parse_UnknownSegments_FallBackToDefaults()
    {
        var opts = ScreenshotOptions.Parse("/u/banana/nonsense/", null);
        Assert.Equal(ScreenshotSize.Small, opts.Size);
        Assert.Equal(AspectRatio.OneOne, opts.AspectRatio);
    }

    [Fact]
    public void Parse_FormatJpeg_SetsJpeg()
    {
        Assert.Equal(ImageFormat.Jpeg, ScreenshotOptions.Parse("/u/", "jpeg").Format);
        Assert.Equal(ImageFormat.Jpeg, ScreenshotOptions.Parse("/u/", "JPEG").Format);
        Assert.Equal(ImageFormat.Png,  ScreenshotOptions.Parse("/u/", null).Format);
        Assert.Equal(ImageFormat.Png,  ScreenshotOptions.Parse("/u/", "png").Format);
    }

    [Fact]
    public void GetViewport_OpenGraph_IsAlways1200x630()
    {
        var opts = ScreenshotOptions.Parse("/u/opengraph/9:16/", null);
        Assert.Equal((1200, 630), opts.GetViewport());
    }

    [Fact]
    public void GetViewport_SmallOneOne_Is375x375()
    {
        var opts = ScreenshotOptions.Parse("/u/small/1:1/", null);
        Assert.Equal((375, 375), opts.GetViewport());
    }

    [Fact]
    public void GetViewport_SmallNineSixteen_Is375x667()
    {
        var opts = ScreenshotOptions.Parse("/u/small/9:16/", null);
        Assert.Equal((375, 667), opts.GetViewport());
    }

    [Fact]
    public void GetDeviceScaleFactor_Bigger_Is1Point4()
    {
        var opts = ScreenshotOptions.Parse("/u/small/1:1/bigger/", null);
        Assert.Equal(1.4, opts.GetDeviceScaleFactor(), 2);
    }

    [Fact]
    public void GetDeviceScaleFactor_Smaller_Is0Point71()
    {
        var opts = ScreenshotOptions.Parse("/u/small/1:1/smaller/", null);
        Assert.Equal(0.71, opts.GetDeviceScaleFactor(), 2);
    }

    [Fact]
    public void ContentType_PngByDefault()
    {
        Assert.Equal("image/png",  ScreenshotOptions.Parse("/u/", null).ContentType);
        Assert.Equal("image/jpeg", ScreenshotOptions.Parse("/u/", "jpeg").ContentType);
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```powershell
dotnet test tests/Screengrabber.Api.Tests
```

Expected: Compilation failure — `ScreenshotOptions`, `ScreenshotSize`, etc. not defined.

- [ ] **Step 3: Implement `src/Screengrabber.Api/ScreenshotOptions.cs`**

```csharp
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
        (ScreenshotSize.OpenGraph, _)               => (1200, 630),
        (ScreenshotSize.Small,  AspectRatio.NineSixteen) => (375,  667),
        (ScreenshotSize.Small,  _)                  => (375,  375),
        (ScreenshotSize.Medium, AspectRatio.NineSixteen) => (650, 1156),
        (ScreenshotSize.Medium, _)                  => (650,  650),
        _                                           => (1024, 1024)
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
```

- [ ] **Step 4: Run tests to confirm they pass**

```powershell
dotnet test tests/Screengrabber.Api.Tests
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat: add ScreenshotOptions with path parser and viewport helpers"
```

---

### Task 3: CacheService — Redis wrapper with tests

**Files:**
- Create: `src/Screengrabber.Api/CacheService.cs`
- Create: `tests/Screengrabber.Api.Tests/CacheServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/Screengrabber.Api.Tests/CacheServiceTests.cs`:

```csharp
using NSubstitute;
using Screengrabber.Api;
using StackExchange.Redis;
using Xunit;

namespace Screengrabber.Api.Tests;

public class CacheServiceTests
{
    private readonly IDatabase _db = Substitute.For<IDatabase>();
    private readonly ICacheService _cache;

    public CacheServiceTests()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_db);
        _cache = new CacheService(multiplexer);
    }

    [Fact]
    public async Task GetAsync_WhenKeyMissing_ReturnsNull()
    {
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
           .Returns(RedisValue.Null);

        var result = await _cache.GetAsync("/some/key");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_WhenKeyExists_ReturnsBytes()
    {
        var expected = new byte[] { 1, 2, 3 };
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
           .Returns((RedisValue)expected);

        var result = await _cache.GetAsync("/some/key");

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task SetAsync_CallsStringSetWithCorrectTtl()
    {
        var bytes = new byte[] { 4, 5, 6 };
        var ttl   = TimeSpan.FromHours(24);

        await _cache.SetAsync("/some/key", bytes, ttl);

        await _db.Received(1).StringSetAsync(
            "/some/key",
            (RedisValue)bytes,
            ttl,
            keepTtl: false,
            when: When.Always,
            flags: CommandFlags.None);
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```powershell
dotnet test tests/Screengrabber.Api.Tests
```

Expected: Compilation failure — `ICacheService`, `CacheService` not defined.

- [ ] **Step 3: Implement `src/Screengrabber.Api/CacheService.cs`**

```csharp
using StackExchange.Redis;

namespace Screengrabber.Api;

public interface ICacheService
{
    Task<byte[]?> GetAsync(string key);
    Task SetAsync(string key, byte[] value, TimeSpan ttl);
}

public sealed class CacheService(IConnectionMultiplexer redis) : ICacheService
{
    private readonly IDatabase _db = redis.GetDatabase();

    public async Task<byte[]?> GetAsync(string key)
    {
        var value = await _db.StringGetAsync(key);
        return value.HasValue ? (byte[])value! : null;
    }

    public Task SetAsync(string key, byte[] value, TimeSpan ttl)
        => _db.StringSetAsync(key, value, ttl);
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```powershell
dotnet test tests/Screengrabber.Api.Tests
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat: add CacheService with Redis backend"
```

---

### Task 4: ScreenshotService — Playwright/Edge + SemaphoreSlim

**Files:**
- Create: `src/Screengrabber.Api/ScreenshotService.cs`

No unit tests for `ScreenshotService` — it wraps Playwright which requires a real browser. It is verified by running the full service in Task 7.

- [ ] **Step 1: Implement `src/Screengrabber.Api/ScreenshotService.cs`**

```csharp
using Microsoft.Extensions.Hosting;
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
                DeviceScaleFactor = options.GetDeviceScaleFactor()
            });

            var page = await context.NewPageAsync();

            await page.GotoAsync(options.TargetUrl, new()
            {
                Timeout    = options.TimeoutSeconds * 1000f,
                WaitUntil  = options.ToPlaywrightWaitUntil()
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
```

- [ ] **Step 2: Build to verify no compilation errors**

```powershell
dotnet build src/Screengrabber.Api
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/Screengrabber.Api/ScreenshotService.cs
git commit -m "feat: add ScreenshotService with Edge browser and concurrency cap"
```

---

### Task 5: ApiKeyMiddleware — optional auth with tests

**Files:**
- Create: `src/Screengrabber.Api/ApiKeyMiddleware.cs`
- Create: `tests/Screengrabber.Api.Tests/ApiKeyMiddlewareTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/Screengrabber.Api.Tests/ApiKeyMiddlewareTests.cs`:

```csharp
using Screengrabber.Api;
using Xunit;

namespace Screengrabber.Api.Tests;

public class ApiKeyMiddlewareTests
{
    [Fact]
    public void IsAuthorized_NoConfiguredKeys_AlwaysTrue()
    {
        var empty = new HashSet<string>();
        Assert.True(ApiKeyMiddleware.IsAuthorized(null,    empty));
        Assert.True(ApiKeyMiddleware.IsAuthorized("",      empty));
        Assert.True(ApiKeyMiddleware.IsAuthorized("anykey", empty));
    }

    [Fact]
    public void IsAuthorized_ValidKey_ReturnsTrue()
    {
        var keys = new HashSet<string> { "key-one", "key-two" };
        Assert.True(ApiKeyMiddleware.IsAuthorized("key-one", keys));
        Assert.True(ApiKeyMiddleware.IsAuthorized("key-two", keys));
    }

    [Fact]
    public void IsAuthorized_InvalidKey_ReturnsFalse()
    {
        var keys = new HashSet<string> { "key-one" };
        Assert.False(ApiKeyMiddleware.IsAuthorized("wrong",  keys));
        Assert.False(ApiKeyMiddleware.IsAuthorized(null,     keys));
        Assert.False(ApiKeyMiddleware.IsAuthorized("",       keys));
    }

    [Fact]
    public void IsAuthorized_KeysAreCaseSensitive()
    {
        var keys = new HashSet<string> { "SecretKey" };
        Assert.False(ApiKeyMiddleware.IsAuthorized("secretkey", keys));
        Assert.False(ApiKeyMiddleware.IsAuthorized("SECRETKEY", keys));
        Assert.True(ApiKeyMiddleware.IsAuthorized("SecretKey",  keys));
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```powershell
dotnet test tests/Screengrabber.Api.Tests
```

Expected: Compilation failure — `ApiKeyMiddleware` not defined.

- [ ] **Step 3: Implement `src/Screengrabber.Api/ApiKeyMiddleware.cs`**

```csharp
namespace Screengrabber.Api;

public static class ApiKeyMiddleware
{
    public static bool IsAuthorized(string? providedKey, IReadOnlySet<string> validKeys)
    {
        if (validKeys.Count == 0) return true;
        return !string.IsNullOrEmpty(providedKey) && validKeys.Contains(providedKey);
    }
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```powershell
dotnet test tests/Screengrabber.Api.Tests
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat: add ApiKeyMiddleware auth helper"
```

---

### Task 6: ScreenshotEndpoint — route handler

**Files:**
- Create: `src/Screengrabber.Api/ScreenshotEndpoint.cs`

- [ ] **Step 1: Implement `src/Screengrabber.Api/ScreenshotEndpoint.cs`**

```csharp
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

        var format    = context.Request.Query["format"].ToString();
        var cacheKey  = string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase)
                        ? rawPath + "?format=jpeg"
                        : rawPath;
        var options   = ScreenshotOptions.Parse(rawPath, format);

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
```

- [ ] **Step 2: Build to verify**

```powershell
dotnet build src/Screengrabber.Api
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/Screengrabber.Api/ScreenshotEndpoint.cs
git commit -m "feat: add ScreenshotEndpoint route handler"
```

---

### Task 7: Program.cs — wire everything together

**Files:**
- Modify: `src/Screengrabber.Api/Program.cs`

- [ ] **Step 1: Replace `src/Screengrabber.Api/Program.cs`**

```csharp
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
```

- [ ] **Step 2: Build to verify**

```powershell
dotnet build
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Run all tests**

```powershell
dotnet test
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/Screengrabber.Api/Program.cs
git commit -m "feat: wire DI, middleware, and route in Program.cs"
```

---

### Task 8: Dockerfile — multi-stage, self-contained, Edge installed

**Files:**
- Create: `Dockerfile`

- [ ] **Step 1: Create `Dockerfile`**

```dockerfile
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Screengrabber.Api/Screengrabber.Api.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -o /publish

# Runtime stage — playwright/dotnet base has browser deps + playwright CLI
FROM mcr.microsoft.com/playwright/dotnet:v1.50.0-noble AS final

# Install Microsoft Edge
RUN playwright install msedge

WORKDIR /app
COPY --from=build /publish .

# Ensure the binary is executable
RUN chmod +x ./Screengrabber.Api

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright

ENTRYPOINT ["./Screengrabber.Api"]
```

- [ ] **Step 2: Build the Docker image locally to verify (requires Docker)**

```powershell
docker build -t screengrabber:local .
```

Expected: Image builds successfully. The `playwright install msedge` step downloads Edge (~200MB), so first build takes a few minutes.

- [ ] **Step 3: Commit**

```bash
git add Dockerfile
git commit -m "feat: add multi-stage Dockerfile with self-contained publish and Edge"
```

---

### Task 9: docker-compose.yml and .env.example

**Files:**
- Create: `docker-compose.yml`
- Create: `.env.example`

- [ ] **Step 1: Create `docker-compose.yml`**

```yaml
services:
  api:
    image: ${SCREENGRABBER_IMAGE}
    container_name: screengrabber-api
    restart: unless-stopped
    environment:
      REDIS_CONNECTION: screengrabber-redis:6379
      SCREENSHOT_CACHE_TTL_HOURS: ${SCREENSHOT_CACHE_TTL_HOURS:-24}
      SCREENSHOT_CONCURRENCY: ${SCREENSHOT_CONCURRENCY:-4}
      API_KEYS: ${API_KEYS:-}
    networks:
      - default
      - proxy
    depends_on:
      - redis

  redis:
    image: redis:7-alpine
    container_name: screengrabber-redis
    restart: unless-stopped
    volumes:
      - screengrabber-redis-data:/data
    networks:
      - default

volumes:
  screengrabber-redis-data:

networks:
  proxy:
    external: true
```

- [ ] **Step 2: Create `.env.example`**

```
# Image built and pushed by GitHub Actions
SCREENGRABBER_IMAGE=ghcr.io/<your-org>/screengrabber:latest

# Cache TTL in hours (default: 24)
SCREENSHOT_CACHE_TTL_HOURS=24

# Max concurrent Playwright pages (default: 4)
SCREENSHOT_CONCURRENCY=4

# Comma-separated API keys. Leave blank for open access.
# Example: API_KEYS=key-one,key-two
API_KEYS=
```

- [ ] **Step 3: Commit**

```bash
git add docker-compose.yml .env.example
git commit -m "feat: add docker-compose and env example"
```

---

### Task 10: GitHub Actions deploy workflow

**Files:**
- Create: `.github/workflows/deploy.yml`

- [ ] **Step 1: Create `.github/workflows/deploy.yml`**

```yaml
name: Deploy

on:
  push:
    branches: [main]
  workflow_dispatch:

env:
  REGISTRY: ghcr.io
  DEPLOY_PATH: /opt/screengrabber

jobs:
  build-and-push:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write
    outputs:
      image: ${{ steps.image.outputs.name }}
    steps:
      - uses: actions/checkout@v4

      - name: Compute image name
        id: image
        run: echo "name=${REGISTRY}/$(echo '${{ github.repository }}' | tr '[:upper:]' '[:lower:]')" >> "$GITHUB_OUTPUT"

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v3

      - name: Log in to GHCR
        uses: docker/login-action@v3
        with:
          registry: ${{ env.REGISTRY }}
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Build and push image
        uses: docker/build-push-action@v6
        with:
          context: .
          file: ./Dockerfile
          push: true
          tags: |
            ${{ steps.image.outputs.name }}:latest
            ${{ steps.image.outputs.name }}:${{ github.sha }}
          cache-from: type=gha
          cache-to: type=gha,mode=max

  deploy:
    needs: build-and-push
    runs-on: ubuntu-latest
    env:
      SCREENGRABBER_IMAGE: ${{ needs.build-and-push.outputs.image }}:latest
      API_KEYS: ${{ secrets.API_KEYS }}
      SCREENSHOT_CACHE_TTL_HOURS: ${{ secrets.SCREENSHOT_CACHE_TTL_HOURS || '24' }}
    steps:
      - uses: actions/checkout@v4

      - name: Set up SSH
        run: |
          mkdir -p ~/.ssh
          echo "${{ secrets.DEPLOY_SSH_KEY }}" | base64 -d > ~/.ssh/deploy_key
          chmod 600 ~/.ssh/deploy_key
          ssh-keygen -y -f ~/.ssh/deploy_key > /dev/null
          ssh-keyscan -H "${{ secrets.DEPLOY_HOST }}" >> ~/.ssh/known_hosts

      - name: Copy stack files to server
        run: |
          scp -i ~/.ssh/deploy_key -o StrictHostKeyChecking=no \
            docker-compose.yml \
            ${{ secrets.DEPLOY_USER }}@${{ secrets.DEPLOY_HOST }}:${{ env.DEPLOY_PATH }}/

      - name: Write .env on server
        run: |
          {
            echo "SCREENGRABBER_IMAGE=$SCREENGRABBER_IMAGE"
            echo "API_KEYS=$API_KEYS"
            echo "SCREENSHOT_CACHE_TTL_HOURS=$SCREENSHOT_CACHE_TTL_HOURS"
          } | base64 -w0 | ssh -i ~/.ssh/deploy_key -o StrictHostKeyChecking=no \
            ${{ secrets.DEPLOY_USER }}@${{ secrets.DEPLOY_HOST }} \
            'base64 -d > ${{ env.DEPLOY_PATH }}/.env && chmod 600 ${{ env.DEPLOY_PATH }}/.env'

      - name: Roll out stack
        run: |
          ssh -i ~/.ssh/deploy_key -o StrictHostKeyChecking=no \
            ${{ secrets.DEPLOY_USER }}@${{ secrets.DEPLOY_HOST }} \
            "cd ${{ env.DEPLOY_PATH }} && \
             docker compose --env-file .env pull && \
             docker compose --env-file .env up -d && \
             docker image prune -f"

      - name: Clean up SSH key
        if: always()
        run: rm -f ~/.ssh/deploy_key
```

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/deploy.yml
git commit -m "feat: add GitHub Actions deploy workflow mirroring GCT"
```

---

### Task 11: GCT changes — proxy network, Caddyfile, deploy secret

**Files:**
- Modify: `j:\Projects\GCT\docker-compose.prod.yml`
- Modify: `j:\Projects\GCT\Caddyfile`
- Modify: `j:\Projects\GCT\.github\workflows\deploy.yml`

- [ ] **Step 1: Update `j:\Projects\GCT\docker-compose.prod.yml`**

Add `SCREENGRABBER_DOMAIN` to the `caddy` environment block and join the `proxy` network. Replace the `caddy` service definition and add the `proxy` network at the bottom:

```yaml
  caddy:
    image: caddy:2
    container_name: gct-caddy
    restart: unless-stopped
    environment:
      GCT_DOMAIN: ${GCT_DOMAIN}
      SCREENGRABBER_DOMAIN: ${SCREENGRABBER_DOMAIN}
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile
      - caddy_data:/data
      - caddy_config:/config
    networks:
      - default
      - proxy
    depends_on:
      - gct-web
```

And at the bottom of the file, add the `proxy` external network alongside the existing `volumes:` block:

```yaml
networks:
  proxy:
    external: true
```

- [ ] **Step 2: Update `j:\Projects\GCT\Caddyfile`**

```caddyfile
{$GCT_DOMAIN} {
    reverse_proxy gct-web:8080
}

{$SCREENGRABBER_DOMAIN} {
    reverse_proxy screengrabber-api:8080
}
```

- [ ] **Step 3: Update `j:\Projects\GCT\.github\workflows\deploy.yml`**

In the `deploy` job, add `SCREENGRABBER_DOMAIN` to the `env:` block:

```yaml
      SCREENGRABBER_DOMAIN: ${{ secrets.SCREENGRABBER_DOMAIN }}
```

And in the "Write .env on server" step, add this line inside the `{ }` block:

```bash
echo "SCREENGRABBER_DOMAIN=$SCREENGRABBER_DOMAIN"
```

- [ ] **Step 4: Build GCT to verify no regressions**

```powershell
cd j:\Projects\GCT
dotnet build
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit GCT changes**

```bash
cd j:\Projects\GCT
git add docker-compose.prod.yml Caddyfile .github/workflows/deploy.yml
git commit -m "feat: add screengrabber proxy network and virtual host to Caddy"
```

- [ ] **Step 6: Commit screengrabber final state**

```bash
cd j:\Projects\screengrabber
git add .
git commit -m "chore: finalize screengrabber project"
```

---

## One-Time Server Setup (after first deploy of both stacks)

Run on the server as the deployer user:

```bash
# 1. Create the shared proxy network (once, ever)
docker network create proxy

# 2. Create the screengrabber deploy directory
sudo mkdir -p /opt/screengrabber
sudo chown <deployer-user>:<deployer-user> /opt/screengrabber

# 3. Add SCREENGRABBER_DOMAIN secret to the GCT GitHub repo
#    (GitHub → GCT repo → Settings → Secrets → Actions)

# 4. Redeploy GCT (workflow_dispatch or push to main)
#    This picks up the new Caddyfile and proxy network.

# 5. Deploy screengrabber for the first time (push to screengrabber main)
#    After this, Caddy routes screengrabber-domain → screengrabber-api:8080.
```

## GitHub Secrets to Add

**Screengrabber repo:**

| Secret | Value |
|---|---|
| `DEPLOY_SSH_KEY` | Same base64-encoded key as GCT |
| `DEPLOY_HOST` | Same server hostname as GCT |
| `DEPLOY_USER` | Same deployer username as GCT |
| `SCREENGRABBER_DOMAIN` | e.g. `screenshots.yourdomain.com` |
| `API_KEYS` | Comma-separated keys, or leave blank |
| `SCREENSHOT_CACHE_TTL_HOURS` | Optional, defaults to 24 |

**GCT repo (additions):**

| Secret | Value |
|---|---|
| `SCREENGRABBER_DOMAIN` | Same domain as above |
