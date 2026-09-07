#!/usr/bin/env bash
# Compile-checks the whole solution and runs the tests on macOS/Linux.
#
# This deliberately does NOT produce a shippable .exe. EnableWindowsTargeting lets a WPF
# project compile here, but the resulting binary does not run on Windows. The real build
# happens on windows-latest in .github/workflows/build.yml, or via scripts/publish.ps1 on
# a Windows machine.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo "==> Building"
dotnet build "$ROOT" -c Release --nologo -v quiet

echo "==> Testing"
dotnet test "$ROOT" --nologo -v quiet

echo
echo "Compile and tests OK."
echo "For a runnable .exe, use the GitHub Actions build or run scripts/publish.ps1 on Windows."
