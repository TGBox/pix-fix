# PixFix – Verbindliche Leitplanken für KI-Agenten und Mitwirkende

Diese Datei ist die zentrale Arbeitsanweisung für alle KI-Tools (ChatGPT, Claude, Copilot, Antigravity) und Mitwirkenden in diesem Repository. Lies vor jeder Änderung zusätzlich `docs/SPEC.md`.

1. Projektzweck und Architektur

PixFix ist das persönliche Arbeitswerkzeug für die Hotline (`Windows 10/11`, `.NET 10`, `WPF`).

- `src/PixFix.Core`: Domänenmodelle, Such- und Bewertungslogik, Vorlagen-Rendering, reine Kontrast-/Codec-Logik. **Keine** UI-, Datei-, SQLite- oder Windows-API-Abhängigkeiten.
- `src/PixFix.Infrastructure`: SQLite (`Microsoft.Data.Sqlite`), Asset-Ablage, Windows-OCR (`Windows.Media.Ocr`), Telefonie (`tel:`/TAPI), Fernwartung, TI-Monitor-API, Sicherung/Wiederherstellung, Legacy-Import.
- `src/PixFix.App`: WPF-Oberfläche (MVVM mit `CommunityToolkit.Mvvm`), Dependency Injection, Serilog-Logging, Fluent Theme.
- **Erlaubte Abhängigkeitsrichtung:** `PixFix.App -> PixFix.Infrastructure -> PixFix.Core` und `PixFix.App -> PixFix.Core`. `Core` kennt niemals `Infrastructure` oder `App`.

1. Strenge Verbote (PixFix-69-Fehler nicht wiederholen)

1. **Keine UI-, Maus- oder Tastatur-Automation:** Niemals `SetCursorPos`, `mouse_event`, `SendKeys`, `keybd_event`, `SendInput`, feste Bildschirmkoordinaten oder Annahmen über maximierte Fenster bzw. einen zweiten Monitor verwenden. Externe Programme nur über saubere Schnittstellen, Zwischenablage, Protokolle (`tel:`, `mailto:`), CLI-Parameter oder Verknüpfungen ansprechen.
1. **Keine leeren `catch`-Blöcke:** Jeder Fehler wird mit Serilog protokolliert oder explizit typisiert behandelt. Fehler in externen Integrationen (TI-Monitor, Telefonie, Fernwartung, Outlook) dürfen die UI niemals blockieren oder zum Absturz bringen.
1. **Niemals echte Kunden- oder Nutzerdaten einchecken:** Keine `.db`-, `.sqlite`-, `.pixfix`-, `.zip`-, `.log`- oder `.env`-Dateien, keine echten E-Mail-Adressen, Telefonnummern oder lokale Pfade wie `C:\Users\<Benutzer>\...`. In Tests ausschließlich synthetische Platzhalter (`*.invalid`, `localhost`, `8.8.8.8`) nutzen.
1. **Bildsuch-Algorithmus nicht eigenmächtig ändern:** Das Scoring (`docs/SPEC.md` Abschnitt F2) entspricht 1:1 PixFix 69 und darf nur mit passenden Vergleichstests angepasst werden.
1. **Nie direkt auf `main` arbeiten:** Alle Änderungen erfolgen auf einem Feature-Branch (z. B. `feat/...` oder `fix/...`) und gehen über einen Pull Request mit Review auf `main`.
1. **Keine unnötigen NuGet-Pakete:** Neue Abhängigkeiten nur nach klarer Begründung; Paketversionen ausschließlich zentral in `Directory.Packages.props` pflegen (niemals in einzelnen `.csproj`-Dateien).

1. Code-, UI- und WPF-Standards

- **Build-Strenge:** `Nullable=enable`, `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`, `.editorconfig` (4 Leerzeichen in `.cs`, 2 Leerzeichen in `.xaml`/`.csproj`/`.yml`, file-scoped Namespaces, geschweifte Klammern Pflicht, `System`-Usings zuerst). Es gelten **0 Warnungen**.
- **Sprache:** Benutzersichtbare Texte (UI, Fehlermeldungen, Tooltips, Logs für Anwender) auf **Deutsch**; Code-Bezeichner und Code-Kommentare auf **Englisch** (oder konsistent zum umgebenden Bestand).
- **WPF Fluent Theme (`ThemeMode` Light/Dark/System):**
  - Eigene Control-Styles immer mit `BasedOn="{StaticResource {x:Type ...}}"` ableiten, damit der Fluent-Stil erhalten bleibt.
  - Keine festen hellen Farben (`White`, `Gray`, `#555`) verwenden, sondern `{DynamicResource ...}` aus `src/PixFix.App/Themes/LightPalette.xaml` und `DarkPalette.xaml`.
  - Buttons und Container mit `MinWidth` und ausreichendem `Padding` statt enger fester `Width`/`Height`-Werte definieren, damit Beschriftungen im Fluent Theme nicht abgeschnitten werden.
  - Änderungen an `RichTextBox` / `FlowDocument` müssen über `FlowDocumentThemeHelper` verlustfrei für Light/Dark Mode sowie sauber für RTF-Speicherung und Outlook-HTML-Export bleiben.
  - Bei neuen Fenstern/Ansichten Fenstergröße und -position nach Möglichkeit über `LayoutMemory.Key` merken.
  - Schaltflächen in Werkzeugleisten und Listen mit `Style="{StaticResource ActionButton}"` (Umschalter:
    `ActionToggleButton`), Symbol `common:ActionButton.Icon="{StaticResource Icon.…}"`, passender
    `common:ActionButton.Role` und `common:ActionButton.Description` statt `ToolTip` (SPEC F16). Keine Emojis oder
    Segoe-Zeichen als Symbole; fehlende Symbole in `scripts/build-icon-font.py` ergänzen und das Skript ausführen.
    Knöpfe, die einen Dialog abschließen, bleiben reine Textknöpfe.

1. Pflicht-Checkliste vor jeder Fertigmeldung

1. Änderungen gegen `docs/SPEC.md` prüfen (bestehende Fachregeln nicht brechen).
1. Für neue Fachlogik passende Unit-/Integrationstests in `tests/PixFix.Core.Tests` oder `tests/PixFix.Infrastructure.Tests` ergänzen.
1. Beide Befehle erfolgreich ausführen:
   - `dotnet build PixFix.sln --no-restore` (0 Warnungen, 0 Fehler)
   - `dotnet test PixFix.sln --no-build` (alle Tests bestanden)
1. Relevante Änderungen kurz in `CHANGELOG.md` unter `[Unreleased]` eintragen.
1. Commit-Nachricht im **Conventional-Commits-Format** verfassen (steuert direkt die automatische Release-Version auf `main`):
   - `fix(bereich): ...` oder `docs: ...` / `chore: ...` → Patch-Version (`v1.0.x`)
   - `feat(bereich): ...` → Minor-Version (`v1.x.0`)
   - `feat!...:` oder `BREAKING CHANGE:` → Major-Version (`vx.0.0`)
