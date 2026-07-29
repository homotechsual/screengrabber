# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Screengrabber.Api/Screengrabber.Api.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -o /publish

# Runtime stage — use the Playwright base image with browser dependencies pre-installed.
# This keeps the runtime aligned with the current Playwright/Node/browser stack.
FROM mcr.microsoft.com/playwright:v1.62.0-noble AS final

ENV DEBIAN_FRONTEND=noninteractive

# Install Microsoft Edge via Microsoft's apt repository.
# Playwright's Channel = "msedge" finds it at /usr/bin/microsoft-edge-stable.
RUN apt-get update && \
    apt-get install -y --no-install-recommends ca-certificates curl gnupg && \
    curl -sSL https://packages.microsoft.com/keys/microsoft.asc \
        | gpg --dearmor -o /usr/share/keyrings/microsoft.gpg && \
    sh -c 'echo "deb [arch=amd64 signed-by=/usr/share/keyrings/microsoft.gpg] https://packages.microsoft.com/repos/edge stable main" \
        > /etc/apt/sources.list.d/microsoft-edge.list' && \
    apt-get update && \
    apt-get install -y --no-install-recommends microsoft-edge-stable && \
    apt-get upgrade -y && \
    apt-get autoremove -y && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /publish .

RUN set -eux; \
    if ! getent group app >/dev/null 2>&1; then groupadd --system app; fi; \
    if ! id -u app >/dev/null 2>&1; then useradd --system --gid app --create-home --home-dir /home/app app; fi; \
    mkdir -p /home/app; \
    chown -R app:app /app /home/app; \
    chmod +x ./Screengrabber.Api

USER app

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
ENV HOME=/home/app

ENTRYPOINT ["./Screengrabber.Api"]
