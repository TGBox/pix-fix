$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Push-Location $repoRoot
try {
    $requiredHooks = @('.githooks/pre-commit', '.githooks/commit-msg', '.githooks/pre-push')
    foreach ($hook in $requiredHooks) {
        if (-not (Test-Path -LiteralPath $hook)) {
            throw "Fehlende Hook-Datei: $hook"
        }
    }

    & git config core.hooksPath .githooks
    if ($LASTEXITCODE -ne 0) {
        throw "git config core.hooksPath .githooks fehlgeschlagen."
    }

    Write-Host ''
    Write-Host 'Git-Hooks erfolgreich aktiviert (core.hooksPath = .githooks):' -ForegroundColor Green
    Write-Host '  - pre-commit : blockt direkte Commits auf main/master, Kundendaten, Secrets, leere catch-Bloecke und UI-Hacks'
    Write-Host '  - commit-msg : erzwingt Conventional Commits (z. B. feat: ... oder fix: ...) fuer automatische Releases'
    Write-Host '  - pre-push   : blockt direktes Pushen auf main/master und prueft dotnet build + dotnet test'
    Write-Host ''
    Write-Host 'Wichtiger Hinweis: Immer auf einem eigenen Branch arbeiten, z. B.:' -ForegroundColor Cyan
    Write-Host '  git checkout -b feat/kurze-beschreibung'
    Write-Host ''
}
finally {
    Pop-Location
}
