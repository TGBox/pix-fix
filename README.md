# PixFix

Arbeitswerkzeug für die Data-Cur-Hotline: Wissensdatenbank zu Fehlerbildern mit Text- und Bildsuche,
Schreibbereiche, Anrufbearbeitung mit Rückruf-Erinnerungen, Vorlagen, Telefonie und TI-Monitor.

Neuaufbau von PixFix 69 (ChatGPT-Eigenbau) – Anforderungen und Phasenplan: [`docs/SPEC.md`](docs/SPEC.md).

## Voraussetzungen

- Windows 10 22H2 oder Windows 11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2026 oder JetBrains Rider (optional)

## Bauen, testen, starten

```powershell
dotnet build
dotnet test
dotnet run --project src/PixFix.App
```

## Verteilen

```powershell
dotnet publish src/PixFix.App -c Release -o publish
```

Ergebnis ist eine einzelne `publish\PixFix.exe`, die ohne installiertes .NET läuft.

## Projektstruktur

| Projekt | Inhalt |
| --- | --- |
| `src/PixFix.Core` | Domänenmodelle und Fachlogik, keine UI-/Datei-/DB-Abhängigkeiten |
| `src/PixFix.Infrastructure` | SQLite, Dateiablage, OCR, Telefonie, TI-API, Sicherung, Legacy-Import |
| `src/PixFix.App` | WPF-Oberfläche (MVVM), Start, Dependency Injection, Logging |
| `tests/PixFix.Core.Tests` | Unit-Tests der Fachlogik |
| `tests/PixFix.Infrastructure.Tests` | Integrationstests (SQLite, Import mit synthetischer PixFix-69-Installation) – laufen nur unter Windows |

### Vergleich der Bildsuche mit PixFix 69

```powershell
$env:PIXFIX_LEGACY_DATA = "C:\Pfad\zu\PixFix-69.74.194\PixFix-Daten"
dotnet test --filter SignaturesMatchPixFix69Index
```

Ohne die Variable läuft der Test leer durch.

## Daten

Nutzerdaten liegen getrennt vom Programm unter `%LOCALAPPDATA%\PixFix\`
(`pixfix.db`, `assets\`, `backups\`, `logs\`). Abweichender Ordner über die Umgebungsvariable `PIXFIX_DATA_DIR`.

### Übernahme aus PixFix 69

Unter **Einstellungen → PixFix-69-Ordner wählen und importieren …** den alten Programmordner (oder `PixFix-Daten`) wählen.
Der Import ist nur in eine leere Datenbank möglich, verändert die Altdaten nicht und übernimmt nichts, wenn eine Anzahl
nicht stimmt. Der Bericht steht danach in der Oberfläche und in der Tabelle `import_log`.

**Keine echten Daten (Datenbank, Bilder, Sicherungen) ins Repository einchecken** – sie enthalten Kundendaten.

## Regeln

- Formatierung: `dotnet format` vor jedem Commit; Warnungen gelten als Fehler.
- Keine leeren `catch`-Blöcke – Fehler werden geloggt oder bewusst behandelt.
- Fachliche Regeln aus PixFix 69 stehen in `docs/SPEC.md`; Änderungen daran dort dokumentieren.
- Änderungen im `CHANGELOG.md` festhalten.
