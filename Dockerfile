# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Screengrabber.Api/Screengrabber.Api.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -o /publish

# Runtime stage — playwright/dotnet base has all browser dependencies pre-installed
FROM mcr.microsoft.com/playwright/dotnet:v1.50.0-noble AS final

# Install Microsoft Edge via Microsoft's apt repository.
# Playwright's Channel = "msedge" finds it at /usr/bin/microsoft-edge-stable.
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl gnupg && \
    curl -sSL https://packages.microsoft.com/keys/microsoft.asc \
        | gpg --dearmor -o /usr/share/keyrings/microsoft.gpg && \
    sh -c 'echo "deb [arch=amd64 signed-by=/usr/share/keyrings/microsoft.gpg] https://packages.microsoft.com/repos/edge stable main" \
        > /etc/apt/sources.list.d/microsoft-edge.list' && \
    apt-get update && \
    apt-get install -y --no-install-recommends microsoft-edge-stable && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /publish .

# Ensure the binary is executable
RUN chmod +x ./Screengrabber.Api

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright

ENTRYPOINT ["./Screengrabber.Api"]
