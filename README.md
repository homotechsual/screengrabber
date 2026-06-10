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
