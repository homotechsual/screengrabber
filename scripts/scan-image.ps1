param(
    [string]$ImageName = "screengrabber:local",
    [string[]]$Severities = @("HIGH", "CRITICAL")
)

$ErrorActionPreference = "Stop"

Write-Host "Building image $ImageName..."
docker build -t $ImageName .

$severityArg = ($Severities -join ",")

if (Get-Command trivy -ErrorAction SilentlyContinue) {
    Write-Host "Scanning with Trivy..."
    trivy image --severity $severityArg --format table $ImageName
}
else {
    Write-Host "Trivy is not installed locally; running it via Docker instead..."
    docker run --rm -v /var/run/docker.sock:/var/run/docker.sock aquasec/trivy:latest image --severity $severityArg --format table $ImageName
}
