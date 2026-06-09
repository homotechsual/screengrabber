# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Screengrabber.Api/Screengrabber.Api.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -o /publish

# Runtime stage — playwright/dotnet base has browser deps + playwright CLI
FROM mcr.microsoft.com/playwright/dotnet:v1.50.0-noble AS final

# Install Microsoft Edge
RUN playwright install msedge

WORKDIR /app
COPY --from=build /publish .

# Ensure the binary is executable
RUN chmod +x ./Screengrabber.Api

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright

ENTRYPOINT ["./Screengrabber.Api"]
