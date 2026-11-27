# Claude-Anweisungen für PixFix

Lies und befolge vor jeder Code- oder Konfigurationsänderung zwingend:

1. [`AGENTS.md`](AGENTS.md) (Architektur, Verbote, WPF-Regeln, Commit-Format, Checkliste)
2. [`docs/SPEC.md`](docs/SPEC.md) (fachliche Anforderungen und übernommene Regeln aus PixFix 69)

## Wichtige Befehle unter Windows (PowerShell)

- Git-Hooks einmalig nach dem Klonen aktivieren: `.\scripts\setup-hooks.ps1`
- Lösung bauen (0 Warnungen Pflicht): `dotnet build PixFix.sln`
- Alle Tests ausführen: `dotnet test PixFix.sln --no-build`
- Vor dem Commit arbeiten immer auf einem eigenen Branch (nie direkt auf `main`): `git checkout -b feat/kurze-beschreibung`
