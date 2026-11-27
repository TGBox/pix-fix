# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionen nach SemVer.

## [Unreleased]

### Hinzugefügt

- Projektgerüst: PixFix.Core, PixFix.Infrastructure, PixFix.App (WPF, .NET 10), Tests.
- Spezifikation `docs/SPEC.md` aus PixFix 69.74.194 abgeleitet.
- Datenordner unter `%LOCALAPPDATA%\PixFix`, Datei-Logging, globale Fehlerbehandlung.
- `CallStatus` inkl. Einlesen der PixFix-69-Statuswerte, `TemplateRenderer` für Vorlagen-Platzhalter.
- Phase 1: Datenbankschema v1 mit Migrationen, inhaltsadressierte Dateiablage (`assets/`, SHA-256, Duplikate nur einmal).
- Phase 1: Import aus PixFix 69 (`pixfix.db` Schema 1, Bilder, RTF-Dokumente, PDFs, Fax-Dokumente, Vorlagen, Einstellungen,
  Papierkorb) in einer Transaktion mit Soll/Ist-Abgleich; Altdaten werden nur über eine temporäre Kopie gelesen.
- Einstellungen: Schaltfläche „PixFix-69-Ordner wählen und importieren …“ mit Importbericht.
