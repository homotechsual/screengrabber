# Screengrabber

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

## Container publishing

The GitHub Actions workflow publishes the image to GitHub Container Registry by default. It can also publish to Docker Hub when the repository secrets `DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN` are configured. The Docker Hub image name defaults to `screengrabber` under the configured Docker Hub username.
