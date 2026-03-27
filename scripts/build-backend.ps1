param(
    [string]$BackendPath = (Join-Path $PSScriptRoot "..\\TunnelBackend"),
    [string]$FrontDist = (Join-Path $PSScriptRoot "..\\dist")
)

if (!(Test-Path $FrontDist)) {
    throw "Frontend build output not found: $FrontDist. Run npm run build."
}

$wwwroot = Join-Path $BackendPath "wwwroot"
New-Item -ItemType Directory -Force -Path $wwwroot | Out-Null

# Clean old frontend assets (keep uploads).
$assetsPath = Join-Path $wwwroot "assets"
if (Test-Path $assetsPath) {
    Remove-Item $assetsPath -Recurse -Force
}

$indexFile = Join-Path $wwwroot "index.html"
if (Test-Path $indexFile) {
    Remove-Item $indexFile -Force
}

$webConfig = Join-Path $wwwroot "web.config"
if (Test-Path $webConfig) {
    Remove-Item $webConfig -Force
}

Copy-Item -Path (Join-Path $FrontDist "*") -Destination $wwwroot -Recurse -Force

Write-Host "Copied frontend dist to backend wwwroot."
