# Was wurde geändert?

-

## Warum?

-

## Checkliste vor dem Review

- [ ] PR-Titel nutzt Conventional Commits (z. B. `feat(bereich): ...` oder `fix(bereich): ...`)
- [ ] `dotnet build PixFix.sln` läuft lokal ohne Warnungen und Fehler durch
- [ ] `dotnet test PixFix.sln` läuft lokal vollständig grün durch
- [ ] Keine Datenbanken (`.db`), Sicherungen (`.pixfix`, `.zip`), Logs oder echten Kundendaten enthalten
- [ ] Keine UI-/Maus-/Tastatur-Automation (`SetCursorPos`, `SendKeys` o. ä.) und keine leeren `catch`-Blöcke
- [ ] Bei neuen/geänderten Funktionen: `CHANGELOG.md` (und falls nötig `docs/SPEC.md`) aktualisiert
