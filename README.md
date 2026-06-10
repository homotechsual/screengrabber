# Screengrabber

Screenshot API service using Microsoft Edge via Playwright. Mirrors the URL structure of [slorber/slorber-api-screenshot](https://github.com/slorber/slorber-api-screenshot).

Built with .NET 10 Minimal API, Redis caching, and deployed alongside the GCT stack via a shared Caddy reverse proxy.

## URL Structure

```
GET /{url}/
GET /{url}/{size}/
GET /{url}/{size}/{aspectratio}/
GET /{url}/{size}/{aspectratio}/{zoom}/
```

The `{url}` segment must be URL-encoded (e.g. `https://example.com` → `https%3A%2F%2Fexample.com`). All segments after `{url}` are optional.

### Size

| Value | Viewport |
|---|---|
| `small` | 375px (default) |
| `medium` | 650px |
| `large` | 1024px |
| `opengraph` | 1200×630px fixed |

### Aspect Ratio

| Value | Behaviour |
|---|---|
| `1:1` | Square (default) |
| `9:16` | Portrait |

`large` always produces a square regardless of aspect ratio. `opengraph` ignores aspect ratio.

### Zoom

| Value | Device Pixel Ratio |
|---|---|
| _(none)_ | 1× (default) |
| `bigger` | 1.4× |
| `smaller` | 0.71× |

### Modifier Tokens

Embed anywhere in the path using a `_` prefix:

| Token | Example | Effect |
|---|---|---|
| Cache bust | `_20250609` | Produces a unique cache key |
| Wait | `_wait:0` – `_wait:2` | 0=DOMContentLoaded, 1=Load (default), 2=NetworkIdle |
| Timeout | `_timeout:5` | Seconds, clamped 3–9 (default 6) |

Tokens can be combined: `_20250609_wait:2_timeout:8`

### Query Parameters

| Param | Values | Default |
|---|---|---|
| `format` | `jpeg` | PNG |

### Examples

```
/https%3A%2F%2Fexample.com/
/https%3A%2F%2Fexample.com/large/
/https%3A%2F%2Fexample.com/opengraph/_wait:2_timeout:8/
/https%3A%2F%2Fexample.com/medium/9:16/bigger/?format=jpeg
/https%3A%2F%2Fexample.com/small/_20250609/
```

## Authentication

Set `API_KEYS` to a comma-separated list of keys. Leave blank for open access.

When keys are configured, requests must include:

```
X-Api-Key: your-key-here
```

Multiple keys allow rotation without downtime.

## Environment Variables

| Variable | Default | Description |
|---|---|---|
| `REDIS_CONNECTION` | `screengrabber-redis:6379` | Redis connection string |
| `SCREENSHOT_CACHE_TTL_HOURS` | `24` | Cache TTL in hours |
| `SCREENSHOT_CONCURRENCY` | `4` | Max simultaneous Playwright pages |
| `API_KEYS` | _(blank)_ | Comma-separated API keys; blank = open access |
| `ASPNETCORE_URLS` | `http://+:8080` | Bind address |

## Deployment

Deployment is handled by GitHub Actions on push to `main`. The workflow:

1. Builds and pushes the image to GHCR
2. Copies `docker-compose.yml` to `/opt/screengrabber/` on the server
3. Writes `.env` via a base64 pipe
4. Runs `docker compose pull && up -d && image prune`

### GitHub Secrets (screengrabber repo)

| Secret | Description |
|---|---|
| `DEPLOY_SSH_KEY` | Base64-encoded deploy SSH private key |
| `DEPLOY_HOST` | Server hostname |
| `DEPLOY_USER` | Deploy username |
| `SCREENGRABBER_DOMAIN` | e.g. `screenshots.yourdomain.com` |
| `API_KEYS` | Comma-separated API keys, or blank |
| `SCREENSHOT_CACHE_TTL_HOURS` | Optional, defaults to 24 |

### GitHub Secrets (GCT repo — additions)

| Secret | Description |
|---|---|
| `SCREENGRABBER_DOMAIN` | Same domain as above — injected into Caddy |

## One-Time Server Setup

Run once before the first deploy:

```bash
# Create the shared proxy network
docker network create proxy

# Create the deploy directory
sudo mkdir -p /opt/screengrabber
sudo chown <deployer-user>:<deployer-user> /opt/screengrabber
```

Then add `SCREENGRABBER_DOMAIN` as a secret in both GitHub repos and redeploy GCT to pick up the Caddyfile and proxy network changes.

## Architecture

```
[internet] → GCT Caddy (ports 80/443)
                ├── default network → gct-web:8080
                └── proxy network  → screengrabber-api:8080

screengrabber stack:
  screengrabber-api → screengrabber-redis (internal only)
```

GCT's Caddy handles TLS and routing for both stacks. `screengrabber-redis` is not exposed externally.

## Development

```bash
dotnet build
dotnet test
```

The test suite covers URL parsing, viewport calculation, Redis cache behaviour, and API key auth. `ScreenshotService` requires a real browser and is not unit tested.
