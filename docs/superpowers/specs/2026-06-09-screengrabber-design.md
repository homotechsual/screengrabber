# Screengrabber — Design Spec

**Date:** 2026-06-09  
**Status:** Approved

---

## Overview

A .NET 9 screenshot API service that mirrors the URL structure of [slorber/slorber-api-screenshot](https://github.com/slorber/slorber-api-screenshot). Given a URL and optional display parameters, it returns a screenshot as PNG (or JPEG) captured via Microsoft Edge through Playwright. Screenshots are cached in Redis. The service runs alongside the existing GCT Docker stack on the same host, sharing GCT's Caddy reverse proxy via an external Docker network.

---

## Architecture

### Containers

| Container | Image | Role |
|---|---|---|
| `screengrabber-api` | Custom (see Dockerfile) | .NET 9 Minimal API + Playwright + Edge |
| `screengrabber-redis` | `redis:7-alpine` | Screenshot byte cache (24h TTL, configurable) |

No Caddy container — GCT's Caddy handles TLS and routing for this service too.

### Networking

A shared external Docker network named `proxy` bridges the GCT Caddy container and the screengrabber API container:

```
[internet] → GCT Caddy (ports 80/443)
                ├── default network → gct-web:8080
                └── proxy network  → screengrabber-api:8080

screengrabber stack:
  screengrabber-api → screengrabber-redis (internal network only)
```

`screengrabber-redis` is **not** on the proxy network — only the API container needs external reachability.

### Volumes

| Volume | Purpose |
|---|---|
| `screengrabber-redis-data` | Persists cache across container restarts |

### Environment Variables

| Variable | Default | Description |
|---|---|---|
| `SCREENSHOT_CACHE_TTL_HOURS` | `24` | Redis TTL for cached screenshots |
| `SCREENSHOT_CONCURRENCY` | `4` | Max simultaneous Playwright pages (SemaphoreSlim) |
| `API_KEYS` | _(blank)_ | Comma-separated valid API keys; blank = open access |
| `REDIS_CONNECTION` | `screengrabber-redis:6379` | Redis connection string |
| `ASPNETCORE_URLS` | `http://+:8080` | Bind address |

---

## Changes to GCT

Three small changes are required to wire screengrabber into GCT's Caddy.

**`docker-compose.prod.yml`** — add proxy network and `SCREENGRABBER_DOMAIN` env var to the Caddy service:

```yaml
caddy:
  environment:
    GCT_DOMAIN: ${GCT_DOMAIN}
    SCREENGRABBER_DOMAIN: ${SCREENGRABBER_DOMAIN}
  networks:
    - default
    - proxy

networks:
  proxy:
    external: true
```

**`Caddyfile`** — add screengrabber virtual host:

```caddyfile
{$GCT_DOMAIN} {
    reverse_proxy gct-web:8080
}

{$SCREENGRABBER_DOMAIN} {
    reverse_proxy screengrabber-api:8080
}
```

**`deploy.yml`** — pass `SCREENGRABBER_DOMAIN` secret into the `.env` written on the server:

```yaml
echo "SCREENGRABBER_DOMAIN=${{ secrets.SCREENGRABBER_DOMAIN }}"
```

Add `SCREENGRABBER_DOMAIN` as a secret in the GCT GitHub repository.

---

## URL Structure

```
GET /{url}/
GET /{url}/{size}/
GET /{url}/{size}/{aspectratio}/
GET /{url}/{size}/{aspectratio}/{zoom}/
```

All segments after `{url}` are optional. Modifier tokens are embedded anywhere in the path as `_`-prefixed strings.

### Size

| Value | Viewport Width | Notes |
|---|---|---|
| `small` | 375px | Default |
| `medium` | 650px | |
| `large` | 1024px | |
| `opengraph` | 1200×630px fixed | Ignores aspect ratio |

### Aspect Ratio

| Value | Behaviour |
|---|---|
| `1:1` | Square crop (default) |
| `9:16` | Portrait crop |

### Zoom

| Value | Device Pixel Ratio |
|---|---|
| _(none)_ | 1× (default) |
| `bigger` | 1.4× |
| `smaller` | 0.71× |

### Modifier Tokens

Embedded in any path segment with a `_` prefix:

| Token | Example | Effect |
|---|---|---|
| Cache bust | `_20250609` | Arbitrary string — produces a unique cache key |
| Wait | `_wait:0` – `_wait:3` | 0=DOMContentLoaded, 1=load, 2=networkidle, 3=networkidle+500ms |
| Timeout | `_timeout:5` | Timeout in seconds, clamped to 3–9 (default 6) |

Tokens can be combined: `_20250609_wait:2_timeout:8`

### Query Parameters

| Param | Values | Default |
|---|---|---|
| `format` | `jpeg` | PNG |

### Cache Key

The full request path string (including any modifier tokens) is used as the Redis cache key. Cache-busting tokens naturally produce a different key without any special handling.

### Examples

```
/https%3A%2F%2Fexample.com/
/https%3A%2F%2Fexample.com/large/
/https%3A%2F%2Fexample.com/opengraph/_wait:2_timeout:8/
/https%3A%2F%2Fexample.com/medium/9:16/bigger/?format=jpeg
/https%3A%2F%2Fexample.com/small/_20250609/
```

---

## Authentication

`ApiKeyMiddleware` reads `API_KEYS`, splits on `,`, and trims whitespace. Behaviour:

- If `API_KEYS` is blank or unset → all requests pass through (open access)
- If `API_KEYS` has values → request must include `X-Api-Key: <key>` matching one of the configured keys; returns `401` otherwise

Multiple keys allow key rotation without downtime.

---

## .NET Project Structure

```
screengrabber/
├── src/
│   └── Screengrabber.Api/
│       ├── Screengrabber.Api.csproj   # net9.0
│       ├── Program.cs                  # DI, route registration, middleware
│       ├── ScreenshotEndpoint.cs       # Route handler + path segment parser
│       ├── ScreenshotOptions.cs        # Parsed options (record)
│       ├── ScreenshotService.cs        # SemaphoreSlim + Playwright/Edge logic
│       ├── CacheService.cs             # Redis get/set wrapper
│       └── ApiKeyMiddleware.cs         # Optional auth middleware
├── Dockerfile
├── docker-compose.yml
├── .env.example
└── docs/superpowers/specs/
    └── 2026-06-09-screengrabber-design.md
```

### Key Implementation Details

**`ScreenshotService`**
- Holds a single `IBrowser` (Edge via `playwright.Chromium.LaunchAsync(new() { Channel = "msedge" })`)
- Initialised at startup via `IHostedService`; browser is reused across requests
- Each request opens a new `IPage`, acquires a `SemaphoreSlim` slot, captures, then closes the page and releases the slot
- Viewport set from `ScreenshotOptions`; `opengraph` is always 1200×630 regardless of aspect ratio
- Wait behaviour mapped: 0→DOMContentLoaded, 1→Load, 2→NetworkIdle, 3→NetworkIdle (Playwright's networkidle covers both 2 and 3)
- Timeout applied via `CancellationToken`; returns HTTP 504 on timeout (no fallback image)
- Returns PNG by default; JPEG when `?format=jpeg`

**`ScreenshotOptions`**
- `record` type — parsed once in the endpoint handler, passed through cleanly
- Unknown/invalid path segments fall back to defaults (no 400 errors for unrecognised values — matches original behaviour)
- Modifier tokens extracted by scanning path segments for `_`-prefixed substrings

**`CacheService`**
- Stores raw `byte[]` in Redis
- Key = full request path string
- TTL = `SCREENSHOT_CACHE_TTL_HOURS`
- Cache hit returns bytes directly; miss triggers capture then stores result

**`ApiKeyMiddleware`**
- Registered before the screenshot route
- No-op (calls `next()`) when key set is empty

---

## Dockerfile

Multi-stage build:

1. **Build stage** — `mcr.microsoft.com/dotnet/sdk:9.0` publishes the app
2. **Runtime stage** — `mcr.microsoft.com/playwright/dotnet:v1.50.0-noble` as base (has .NET 8 runtime + all browser deps), with Edge installed via `RUN playwright install msedge`

> Note: The Playwright base image ships .NET 8. The published app targets net9.0 — we self-contain the publish (`-r linux-x64 --self-contained`) so the runtime image's .NET version is irrelevant.

---

## Deploy Workflow

Mirrors GCT's workflow exactly:

1. **Build & push** — image pushed to GHCR as `ghcr.io/<org>/screengrabber:latest` and `:${{ github.sha }}`
2. **Deploy** — SSH to server using the same deployer key/host/user as GCT
3. **Copy** `docker-compose.yml` to `/opt/screengrabber/`
4. **Write `.env`** via base64 pipe (same pattern as GCT)
5. **Roll out** — `docker compose pull && docker compose up -d && docker image prune -f`

### GitHub Secrets (screengrabber repo)

| Secret | Source |
|---|---|
| `DEPLOY_SSH_KEY` | Reuse from GCT |
| `DEPLOY_HOST` | Reuse from GCT |
| `DEPLOY_USER` | Reuse from GCT |
| `SCREENGRABBER_DOMAIN` | New — e.g. `screenshots.yourdomain.com` |
| `API_KEYS` | New — comma-separated keys, or blank |
| `SCREENSHOT_CACHE_TTL_HOURS` | Optional — defaults to 24 |

### GitHub Secrets (GCT repo — additions)

| Secret | Purpose |
|---|---|
| `SCREENGRABBER_DOMAIN` | Injected into Caddy's environment for virtual host routing |

---

## One-Time Server Setup

Run these once on the server before or immediately after first deploy:

```bash
# 1. Create the shared proxy network
docker network create proxy

# 2. Create the screengrabber deploy directory
sudo mkdir -p /opt/screengrabber
sudo chown <deployer-user>:<deployer-user> /opt/screengrabber

# 3. Redeploy GCT to pick up Caddyfile + compose network changes
#    Trigger via GitHub Actions workflow_dispatch on the GCT repo,
#    or push to GCT main
```

After step 3, GCT's Caddy will be on the `proxy` network and able to route to `screengrabber-api` once the screengrabber stack is running.
