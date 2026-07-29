# Local image scanning

Run the following from the repository root to build the image and scan it for high and critical vulnerabilities:

```powershell
pwsh ./scripts/scan-image.ps1
```

If you want to scan a different tag:

```powershell
pwsh ./scripts/scan-image.ps1 -ImageName screengrabber:dev
```
