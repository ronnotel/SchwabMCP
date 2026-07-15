# Point this clone at version-controlled hooks under .githooks/
$ErrorActionPreference = "Stop"
$root = git rev-parse --show-toplevel
Set-Location $root
git config core.hooksPath .githooks
Write-Host "Installed: core.hooksPath=.githooks"
Write-Host "Pre-commit runs: gitleaks protect --staged (blocks secrets)"
Write-Host "Verify: git config --get core.hooksPath"
if (Get-Command gitleaks -ErrorAction SilentlyContinue) {
    Write-Host "gitleaks: $(gitleaks version)"
} else {
    Write-Host "WARNING: gitleaks not on PATH. Install with: winget install gitleaks"
    Write-Host "Commits will be blocked until gitleaks is available."
}
