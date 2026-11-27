# GitHub Copilot Anweisungen für PixFix

Befolge bei allen Code-Vorschlägen, Änderungen und Pull-Request-Reviews die Regeln in [`AGENTS.md`](../AGENTS.md) und die fachliche Spezifikation in [`docs/SPEC.md`](../docs/SPEC.md).

Kurzfassung:

- Schichten einhalten: `PixFix.App -> PixFix.Infrastructure -> PixFix.Core` (`Core` hat keine UI-, Datei- oder DB-Abhängigkeiten).
- Keine Maus-/Tastatur-UI-Automation (`SetCursorPos`, `mouse_event`, `SendKeys`, `SendInput`), keine leeren `catch`-Blöcke, keine echten Kundendaten oder lokalen Pfade.
- `dotnet build PixFix.sln` (0 Warnungen durch `TreatWarningsAsErrors=true`) und `dotnet test PixFix.sln` müssen immer bestehen.
- WPF Fluent Theme: Styles mit `BasedOn="{StaticResource {x:Type ...}}"`, Farben über `DynamicResource` aus `Themes/LightPalette.xaml` und `Themes/DarkPalette.xaml`.
- Commits und PR-Titel im Conventional-Commits-Format (`feat(...)`, `fix(...)`, `docs(...)`).
