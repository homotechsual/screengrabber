# Deployment Docs — Design Spec

**Date:** 2026-06-12  
**Status:** Approved

---

## Overview

Add a dedicated Deployment page to the Screengrabber docs covering the three supported deployment modes: standalone (bundled Caddy), proxied behind an existing Dockerised reverse proxy, and proxied behind a host-level reverse proxy. Move the existing self-hosting content from `index.mdx` to the new page and replace it with a brief summary and link.

---

## Files Changed

| File | Change |
|---|---|
| `docs/screengrabber/deployment.mdx` | New page |
| `docs/screengrabber/index.mdx` | Replace self-hosting section with summary + link |

---

## Page Structure: `deployment.mdx`

**Frontmatter:** `title: "Deployment"`, `sidebar_position: 3`

### Section 1 — Intro + comparison table

One-sentence intro, then a comparison table:

| | Standalone | Proxied (Docker) | Proxied (host) |
|---|---|---|---|
| Caddy included? | Yes | No | No |
| Compose file | `docker-compose.standalone.yml` | `docker-compose.yml` | `docker-compose.yml` + port override |
| When to use | Fresh install, no existing proxy | Existing Dockerised reverse proxy | Existing host-level proxy (Nginx, Apache, etc.) |

### Section 2 — Standalone

**When to use:** running Screengrabber on a host with no existing reverse proxy.

**Prerequisites:**
- DNS A record for `SCREENGRABBER_DOMAIN` pointing at the host
- Ports 80 and 443 open on the host

**The Caddyfile:** show the repo's `Caddyfile` verbatim. Note that Caddy obtains and renews HTTPS certificates automatically via ACME — no manual TLS configuration required.

```caddyfile
{$SCREENGRABBER_DOMAIN} {
    reverse_proxy screengrabber-api:8080
}
```

**Compose usage:**

```bash
SCREENGRABBER_DOMAIN=screenshots.example.com \
SCREENGRABBER_IMAGE=ghcr.io/homotechsual/screengrabber:latest \
docker compose -f docker-compose.standalone.yml up -d
```

Or via a `.env` file with:
- `SCREENGRABBER_DOMAIN`
- `SCREENGRABBER_IMAGE`
- `API_KEYS` (optional)
- `SCREENSHOT_CACHE_TTL_HOURS` (optional, default 24)
- `SCREENSHOT_CONCURRENCY` (optional, default 4)

**Volume note:** `screengrabber-caddy-data` persists the TLS certificate — do not delete this volume.

### Section 3 — Proxied (Docker)

**When to use:** an existing Caddy (or other) container is already running in Docker and handling TLS.

**Concept:** Screengrabber joins a shared external Docker network so the upstream proxy container can reach `screengrabber-api:8080` by name.

**One-time setup — create the shared network:**

```bash
docker network create proxy
```

The network name `proxy` matches the default in `docker-compose.yml`. It can be changed — update the `networks` block in `docker-compose.yml` to match whatever name your existing setup uses.

**Upstream proxy wiring (Caddy example):**

Add the shared network to your existing proxy's compose service:

```yaml
services:
  caddy:
    networks:
      - default
      - proxy

networks:
  proxy:
    external: true
```

Add a virtual host in your Caddyfile:

```caddyfile
screenshots.example.com {
    reverse_proxy screengrabber-api:8080
}
```

For non-Caddy proxies: add `screengrabber-api:8080` as an upstream on the shared network using your proxy's equivalent directive.

**Screengrabber side:** use `docker-compose.yml` as-is. The `proxy` network is declared `external: true` — compose will connect to the network created above.

### Section 4 — Proxied (host)

**When to use:** Nginx, Apache, Caddy, or similar is running directly on the host (not in Docker).

**Expose the API port on the host.** To avoid binding publicly, bind to loopback only:

```yaml
services:
  api:
    ports:
      - "127.0.0.1:8080:8080"
```

The `proxy` external network is not needed — remove the `networks` blocks from `docker-compose.yml` or simply don't create the `proxy` network.

**Upstream proxy snippets:**

Caddy:
```caddyfile
screenshots.example.com {
    reverse_proxy localhost:8080
}
```

Nginx:
```nginx
server {
    listen 443 ssl;
    server_name screenshots.example.com;

    location / {
        proxy_pass http://localhost:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
    }
}
```

---

## `index.mdx` Update

Replace the current "Self-Hosting" section (lines 43–63, which contains a bare docker-compose snippet) with:

> Screengrabber ships as a Docker image and supports three deployment modes — standalone with a bundled Caddy reverse proxy, proxied behind an existing Dockerised reverse proxy, or proxied behind a host-level proxy such as Nginx or Apache. See [Deployment](./deployment) for full setup instructions.

---

## Sidebar Position

Existing pages:
- `index.mdx` → `sidebar_position: 0`
- `endpoint.mdx` → `sidebar_position: 1`
- `configuration.mdx` → `sidebar_position: 2`

New page:
- `deployment.mdx` → `sidebar_position: 3`
