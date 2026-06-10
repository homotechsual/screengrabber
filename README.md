# Screengrabber

Self-hosted screenshot API using Microsoft Edge via Playwright. Mirrors the URL structure of [slorber/slorber-api-screenshot](https://github.com/slorber/slorber-api-screenshot).

Built with .NET 10 Minimal API, Redis caching, and Docker. Designed to sit behind a reverse proxy on a shared Docker network.

## URL Structure

```text
GET /{url}/
GET /{url}/{size}/
GET /{url}/{size}/{aspectratio}/
GET /{url}/{size}/{aspectratio}/{zoom}/
```

The `{url}` segment must be URL-encoded (e.g. `https://example.com` → `https%3A%2F%2Fexample.com`). All segments after `{url}` are optional.

### Size

| Value       | Viewport          |
| ----------- | ----------------- |
| `small`     | 375px (default)   |
| `medium`    | 650px             |
| `large`     | 1024px            |
| `opengraph` | 1200×630px fixed  |

### Aspect Ratio

| Value  | Behaviour        |
| ------ | ---------------- |
| `1:1`  | Square (default) |
| `9:16` | Portrait         |

`large` always produces a square regardless of aspect ratio. `opengraph` ignores aspect ratio.

### Zoom

| Value      | Device Pixel Ratio |
| ---------- | ------------------ |
| *(none)*   | 1× (default)       |
| `bigger`   | 1.4×               |
| `smaller`  | 0.71×              |

### Modifier Tokens

Embed anywhere in the path using a `_` prefix:

| Token        | Example               | Effect                                                              |
| ------------ | --------------------- | ------------------------------------------------------------------- |
| Cache bust   | `_20250609`           | Produces a unique cache key                                         |
| Wait         | `_wait:0` - `_wait:2` | 0=DOMContentLoaded, 1=Load (default), 2=NetworkIdle                 |
| Timeout      | `_timeout:5`          | Seconds, clamped 3-9 (default 6)                                    |
| Output width | `_width:800`          | Scale output to N px wide (proportional height); viewport unchanged |

Tokens can be combined: `_20250609_wait:2_timeout:8_width:800`

### Query Parameters

| Param    | Values | Default |
| -------- | ------ | ------- |
| `format` | `jpeg` | PNG     |

### Examples

```text
/https%3A%2F%2Fexample.com/
/https%3A%2F%2Fexample.com/large/
/https%3A%2F%2Fexample.com/large/_width:800/
/https%3A%2F%2Fexample.com/opengraph/_wait:2_timeout:8/
/https%3A%2F%2Fexample.com/medium/9:16/bigger/?format=jpeg
/https%3A%2F%2Fexample.com/small/_20250609/
```

## Authentication

Set `API_KEYS` to a comma-separated list of keys. Leave blank for open access.

When keys are configured, requests must include:

```http
X-Api-Key: your-key-here
```

Multiple keys allow rotation without downtime.

## Environment Variables

| Variable                      | Default                    | Description                                    |
| ----------------------------- | -------------------------- | ---------------------------------------------- |
| `REDIS_CONNECTION`            | `screengrabber-redis:6379` | Redis connection string                        |
| `SCREENSHOT_CACHE_TTL_HOURS`  | `24`                       | Cache TTL in hours                             |
| `SCREENSHOT_CONCURRENCY`      | `4`                        | Max simultaneous Playwright pages              |
| `API_KEYS`                    | *(blank)*                  | Comma-separated API keys; blank = open access  |
| `ASPNETCORE_URLS`             | `http://+:8080`            | Bind address                                   |

## Deployment

Two compose files are provided depending on your setup.

### Standalone (Caddy included)

`docker-compose.standalone.yml` bundles Caddy, the API, and Redis. Caddy provisions TLS automatically via Let's Encrypt. Use this if you don't already have a reverse proxy.

Required `.env`:

```bash
SCREENGRABBER_IMAGE=ghcr.io/your-org/screengrabber:latest
SCREENGRABBER_DOMAIN=screenshots.yourdomain.com
API_KEYS=                        # optional
SCREENSHOT_CACHE_TTL_HOURS=24    # optional
```

```bash
docker compose -f docker-compose.standalone.yml --env-file .env up -d
```

### External proxy

`docker-compose.yml` runs the API and Redis only, joining an external Docker network named `proxy`. Use this when Caddy (or another reverse proxy) is already running in a separate stack on the same host.

**1. Create the shared network** (once, on the server):

```bash
docker network create proxy
sudo mkdir -p /opt/screengrabber
sudo chown <deployer-user>:<deployer-user> /opt/screengrabber
```

**2. Add Caddy to the `proxy` network** in your existing Caddy stack's `docker-compose.yml`:

```yaml
services:
  caddy:
    # ... existing config ...
    networks:
      - default
      - proxy

networks:
  proxy:
    external: true
```

Then apply: `docker compose up -d`

**3. Add the virtual host block** to your existing `Caddyfile`:

```caddy
screenshots.yourdomain.com {
    reverse_proxy screengrabber-api:8080
}
```

**4. Reload Caddy** to pick up the new block without downtime:

```bash
docker exec <caddy-container-name> caddy reload --config /etc/caddy/Caddyfile
```

### GitHub Actions (CI/CD)

The included workflow deploys automatically on push to `main`:

1. Builds and pushes the image to GHCR
2. Copies `docker-compose.yml` to `/opt/screengrabber/` on the server
3. Writes `.env` via a base64 pipe
4. Runs `docker compose pull && up -d && image prune`

To use `docker-compose.standalone.yml` instead, update the `scp` step in `.github/workflows/deploy.yml` to also copy `Caddyfile` and change the compose file reference.

#### GitHub Secrets

| Secret                       | Description                                              |
| ---------------------------- | -------------------------------------------------------- |
| `DEPLOY_SSH_KEY`             | Base64-encoded deploy SSH private key                    |
| `DEPLOY_HOST`                | Server hostname                                          |
| `DEPLOY_USER`                | Deploy username                                          |
| `API_KEYS`                   | Comma-separated API keys, or blank                       |
| `SCREENSHOT_CACHE_TTL_HOURS` | Optional, defaults to 24                                 |

## Architecture

**Standalone** (`docker-compose.standalone.yml`)

```text
[internet] → Caddy (ports 80/443)
                └── default network → screengrabber-api:8080

screengrabber stack:
  screengrabber-api → screengrabber-redis (internal only)
```

**External proxy** (`docker-compose.yml`)

```text
[internet] → existing reverse proxy (ports 80/443)
                └── proxy network → screengrabber-api:8080

screengrabber stack:
  screengrabber-api → screengrabber-redis (internal only)
```

`screengrabber-redis` is not exposed externally in either setup.

## Development

```bash
dotnet build
dotnet test
```

The test suite covers URL parsing, viewport calculation, Redis cache behaviour, and API key auth. `ScreenshotService` requires a real browser and is not unit tested.
