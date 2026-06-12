# Deployment Docs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `deployment.mdx` page covering standalone, proxied (Docker), and proxied (host) modes, and update `index.mdx` to link to it.

**Architecture:** Two file changes — a new MDX page and a small edit to the existing overview page. No code changes. Verification is visual (check rendered content) and structural (check sidebar link resolves).

**Tech Stack:** MDX, Docusaurus (existing docs site at `docs/screengrabber/`)

**Spec:** `docs/superpowers/specs/2026-06-12-deployment-docs-design.md`

---

### Task 1: Create `deployment.mdx`

**Files:**
- Create: `docs/screengrabber/deployment.mdx`

- [ ] **Step 1: Create the file with the full page content**

Create `docs/screengrabber/deployment.mdx` with this exact content:

```mdx
---
title: Deployment
sidebar_position: 3
---

Screengrabber supports three deployment modes. Choose the one that matches your infrastructure.

| | Standalone | Proxied (Docker) | Proxied (host) |
|---|---|---|---|
| Caddy included? | Yes | No | No |
| Compose file | `docker-compose.standalone.yml` | `docker-compose.yml` | `docker-compose.yml` + port override |
| When to use | Fresh install, no existing proxy | Existing Dockerised reverse proxy | Existing host-level proxy (Nginx, Apache, etc.) |

## Standalone

Use this when you are running Screengrabber on a host with no existing reverse proxy. Caddy is bundled in the stack and handles TLS automatically.

**Prerequisites:**
- A DNS A record for your domain pointing at the host
- Ports 80 and 443 open on the host

### Caddyfile

The repository includes a `Caddyfile` that Caddy reads at startup. It uses an environment variable for the domain so no manual editing is needed:

```caddyfile
{$SCREENGRABBER_DOMAIN} {
    reverse_proxy screengrabber-api:8080
}
```

Caddy obtains and renews HTTPS certificates automatically via ACME. No manual TLS configuration is required.

### Starting the stack

Create a `.env` file alongside `docker-compose.standalone.yml`:

```bash
SCREENGRABBER_DOMAIN=screenshots.example.com
SCREENGRABBER_IMAGE=ghcr.io/homotechsual/screengrabber:latest
API_KEYS=your-key-here
SCREENSHOT_CACHE_TTL_HOURS=24
SCREENSHOT_CONCURRENCY=4
```

Then start the stack:

```bash
docker compose -f docker-compose.standalone.yml up -d
```

:::note
`screengrabber-caddy-data` is a named volume that persists Caddy's TLS certificate across container restarts. Do not delete this volume — Caddy will need to re-request a certificate if it is removed.
:::

## Proxied (Docker)

Use this when an existing reverse proxy container (Caddy, Traefik, Nginx Proxy Manager, etc.) is already running in Docker and handling TLS for your host.

**Concept:** Screengrabber joins a shared external Docker network so the upstream proxy container can reach `screengrabber-api` by name.

### Create the shared network

Run this once on the host:

```bash
docker network create proxy
```

The network name `proxy` is the default used in `docker-compose.yml`. If your existing setup uses a different name, update the `networks` block in `docker-compose.yml` to match:

```yaml
networks:
  your-network-name:
    external: true
```

### Wire up your upstream proxy

Add the shared network to your existing proxy's compose service and update your proxy configuration.

**Compose entry for your proxy container:**

```yaml
services:
  caddy:                          # or traefik, nginx, etc.
    networks:
      - default
      - proxy                     # the shared network

networks:
  proxy:
    external: true
```

**Caddy virtual host example:**

```caddyfile
screenshots.example.com {
    reverse_proxy screengrabber-api:8080
}
```

For other proxies, add `screengrabber-api:8080` as an upstream using your proxy's equivalent directive.

### Start Screengrabber

Create a `.env` file alongside `docker-compose.yml`:

```bash
SCREENGRABBER_IMAGE=ghcr.io/homotechsual/screengrabber:latest
API_KEYS=your-key-here
SCREENSHOT_CACHE_TTL_HOURS=24
SCREENSHOT_CONCURRENCY=4
```

Then start the stack:

```bash
docker compose up -d
```

## Proxied (host)

Use this when Nginx, Apache, Caddy, or similar is running directly on the host — not in Docker.

### Expose the API port

The default `docker-compose.yml` does not publish any ports. Add a port binding to the `api` service so the host-level proxy can reach it. Binding to `127.0.0.1` keeps the port off the public network:

```yaml
services:
  api:
    ports:
      - "127.0.0.1:8080:8080"
```

The `proxy` external network is not needed. Remove the `networks` blocks from `docker-compose.yml`:

```yaml
# Remove these blocks entirely:
# networks:
#   - default
#   - proxy
#
# networks:
#   proxy:
#     external: true
```

### Configure your proxy

**Caddy:**

```caddyfile
screenshots.example.com {
    reverse_proxy localhost:8080
}
```

**Nginx:**

```nginx
server {
    listen 443 ssl;
    server_name screenshots.example.com;
    # ssl_certificate and ssl_certificate_key omitted — configure per your setup

    location / {
        proxy_pass http://localhost:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
    }
}
```

### Start Screengrabber

Create a `.env` file alongside `docker-compose.yml`:

```bash
SCREENGRABBER_IMAGE=ghcr.io/homotechsual/screengrabber:latest
API_KEYS=your-key-here
SCREENSHOT_CACHE_TTL_HOURS=24
SCREENSHOT_CONCURRENCY=4
```

Then start the stack:

```bash
docker compose up -d
```
```

- [ ] **Step 2: Verify the file was created**

```bash
ls docs/screengrabber/
```

Expected output includes `deployment.mdx`.

- [ ] **Step 3: Commit**

```bash
git add docs/screengrabber/deployment.mdx
git commit -m "docs: add deployment page covering standalone, proxied Docker, and proxied host modes"
```

---

### Task 2: Update `index.mdx`

**Files:**
- Modify: `docs/screengrabber/index.mdx:43-63`

- [ ] **Step 1: Replace the self-hosting section**

In `docs/screengrabber/index.mdx`, replace the entire `## Self-Hosting` section (from `## Self-Hosting` through the closing ` ``` ` and the `See the [Configuration]...` line) with:

```mdx
## Self-Hosting

Screengrabber ships as a Docker image and supports three deployment modes — standalone with a bundled Caddy reverse proxy, proxied behind an existing Dockerised reverse proxy, or proxied behind a host-level proxy such as Nginx or Apache. See [Deployment](./deployment) for full setup instructions.
```

The exact text to replace is:

```
## Self-Hosting

Screengrabber ships as a Docker image. The simplest way to run it is with `docker compose`.

```yaml
services:
  screengrabber:
    image: ghcr.io/homotechsual/screengrabber:latest
    ports:
      - "8080:8080"
    environment:
      REDIS_CONNECTION: redis:6379
      API_KEYS: changeme
    depends_on:
      - redis

  redis:
    image: redis:7-alpine
    restart: unless-stopped
```

See the [Configuration](./configuration) reference for a full list of environment variables.
```

- [ ] **Step 2: Verify the index looks right**

Read `docs/screengrabber/index.mdx` and confirm:
- The `## Self-Hosting` section contains only the three-sentence summary and a link to `./deployment`
- No docker-compose YAML block remains in the file

- [ ] **Step 3: Commit**

```bash
git add docs/screengrabber/index.mdx
git commit -m "docs: replace self-hosting section in overview with summary and link to deployment page"
```
