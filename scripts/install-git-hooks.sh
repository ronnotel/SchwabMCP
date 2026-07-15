#!/bin/sh
# Point this clone at version-controlled hooks under .githooks/
set -e
ROOT=$(git rev-parse --show-toplevel)
cd "$ROOT"
git config core.hooksPath .githooks
chmod +x .githooks/pre-commit 2>/dev/null || true
echo "Installed: core.hooksPath=.githooks"
echo "Pre-commit runs: gitleaks protect --staged (blocks secrets)"
if command -v gitleaks >/dev/null 2>&1; then
  echo "gitleaks: $(gitleaks version)"
else
  echo "WARNING: gitleaks not on PATH. Install from https://github.com/gitleaks/gitleaks"
  echo "Commits will be blocked until gitleaks is available."
fi
