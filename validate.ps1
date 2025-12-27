# Single validation script for AI agents and developers (Windows)
# Exit code 0 = all checks passed, non-zero = failure

$ErrorActionPreference = "Stop"

Write-Host "=== Octans Validation ===" -ForegroundColor Cyan
Write-Host ""

Write-Host "[1/3] Building solution..." -ForegroundColor Yellow
dotnet build --warnaserror --no-incremental -v quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "✓ Build succeeded" -ForegroundColor Green
Write-Host ""

Write-Host "[2/3] Running tests..." -ForegroundColor Yellow
dotnet test --no-build --verbosity minimal --logger "console;verbosity=minimal"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "✓ Tests passed" -ForegroundColor Green
Write-Host ""

Write-Host "[3/3] Checking for uncommitted changes..." -ForegroundColor Yellow
$changes = git diff --stat
if ($changes) {
    Write-Host "⚠ Warning: Uncommitted changes detected" -ForegroundColor Yellow
    Write-Host $changes
} else {
    Write-Host "✓ No unexpected file changes" -ForegroundColor Green
}
Write-Host ""

Write-Host "=== All validation checks passed ===" -ForegroundColor Green
