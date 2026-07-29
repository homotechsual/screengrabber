# Screengrabber

[![Tests](https://img.shields.io/github/actions/workflow/status/homotechsual/screengrabber/ci.yml?branch=main\&style=for-the-badge\&label=tests)](https://github.com/homotechsual/screengrabber/actions/workflows/ci.yml)
[![Coverage](https://img.shields.io/codecov/c/github/homotechsual/screengrabber?style=for-the-badge)](https://codecov.io/gh/homotechsual/screengrabber)
[![GHCR](https://img.shields.io/badge/GHCR-ghcr.io%2Fhomotechsual%2Fscreengrabber-2ea44f?style=for-the-badge\&logo=github)](https://ghcr.io/homotechsual/screengrabber)
[![Docker Hub Version](https://img.shields.io/docker/v/homotechsual/screengrabber?sort=semver\&style=for-the-badge\&label=docker%20hub)](https://hub.docker.com/r/homotechsual/screengrabber)
[![Docker Pulls](https://img.shields.io/docker/pulls/homotechsual/screengrabber?style=for-the-badge\&label=pulls)](https://hub.docker.com/r/homotechsual/screengrabber)

Self-hosted screenshot API using Microsoft Edge via Playwright. Built with .NET 10, Redis caching, and Docker.

For full documentation see **[docs.homotechsual.dev/tools/screengrabber](https://docs.homotechsual.dev/tools/screengrabber)**.

## Quick Start

```http
GET /https%3A%2F%2Fexample.com/large
X-Api-Key: your-key
```

## Development

```bash
dotnet build
dotnet test
```

Code coverage is collected in GitHub Actions and uploaded to Codecov using OIDC (no token required).

## Container publishing

The GitHub Actions workflow publishes the image to GitHub Container Registry by default. It can also publish to Docker Hub when the repository secrets `DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN` are configured. The Docker Hub image name is `homotechsual/screengrabber`.

Main-branch deploy publishes the pre-release `edge` tag (plus commit SHA tags). Versioned releases publish numbered semver tags and `latest`.
