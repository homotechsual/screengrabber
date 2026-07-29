# Screengrabber

[![Tests](https://img.shields.io/github/actions/workflow/status/homotechsual/screengrabber/ci.yml?branch=main\&style=for-the-badge\&label=tests)](https://github.com/homotechsual/screengrabber/actions/workflows/ci.yml)
[![Coverage](https://img.shields.io/codecov/c/github/homotechsual/screengrabber?style=for-the-badge\&token=CODECOV_TOKEN)](https://codecov.io/gh/homotechsual/screengrabber)

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

Code coverage is collected in GitHub Actions and uploaded to Codecov when the `CODECOV_TOKEN` repository secret is configured.

## Container publishing

The GitHub Actions workflow publishes the image to GitHub Container Registry by default. It can also publish to Docker Hub when the repository secrets `DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN` are configured. The Docker Hub image name defaults to `screengrabber` under the configured Docker Hub username.
