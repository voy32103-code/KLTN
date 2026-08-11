param(
  [switch]$WithRealGemini
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
  Write-Host '1/3 AI unit tests (includes mocked SPA fallback)'
  Push-Location ai-service
  python -m unittest discover -s tests -v
  Pop-Location

  Write-Host '2/3 Backend build'
  dotnet build .\backend\ReqSimulator.API -c Release

  Write-Host '3/3 Frontend build and contract tests'
  Push-Location frontend
  npm.cmd test
  npm.cmd run build
  Pop-Location

  if ($WithRealGemini) {
    Write-Host 'Real-provider smoke is enabled. Start backend, worker and SPA fixture only in an isolated test environment.'
    Write-Host 'Upload a consented audio fixture through the Admin UI; do not use production credentials.'
  }
} finally {
  Pop-Location
}
