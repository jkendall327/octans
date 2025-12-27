#!/bin/bash
# Single validation script for AI agents and developers
# Exit code 0 = all checks passed, non-zero = failure

set -e  # Exit on first error

echo "=== Octans Validation ==="
echo ""

echo "[1/3] Building solution..."
dotnet build --warnaserror --no-incremental -v quiet
echo "✓ Build succeeded"
echo ""

echo "[2/3] Running tests..."
dotnet test --no-build --verbosity minimal --logger "console;verbosity=minimal"
echo "✓ Tests passed"
echo ""

echo "[3/3] Checking for uncommitted changes to generated files..."
if git diff --quiet; then
    echo "✓ No unexpected file changes"
else
    echo "⚠ Warning: Uncommitted changes detected"
    git diff --stat
fi
echo ""

echo "=== All validation checks passed ==="
