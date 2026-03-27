param(
    [string]$BackendPath = (Join-Path $PSScriptRoot "..\\TunnelBackend"),
    [string]$PublishOut = (Join-Path $PSScriptRoot "..\\publish")
)

Write-Host "1) Build frontend..."
if (-not $env:VITE_BACKEND_BASE_URL) {
    throw "Set VITE_BACKEND_BASE_URL before running publish.ps1"
}

if (-not $env:VITE_API_KEY) {
    throw "Set VITE_API_KEY before running publish.ps1"
}

npm run build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "2) Copy frontend to backend wwwroot..."
& (Join-Path $PSScriptRoot "build-backend.ps1")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "3) Publish backend..."
dotnet publish $BackendPath -c Release -o $PublishOut
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Done: $PublishOut"
