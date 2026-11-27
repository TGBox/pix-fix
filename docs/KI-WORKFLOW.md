# Arbeiten mit PixFix, Git-Hooks und KI (ChatGPT / Claude)

Diese Anleitung beschreibt Schritt für Schritt, wie neue Änderungen sauber ins Repository gelangen – ohne das Hauptprogramm (`main`) zu gefährden. Nach jedem freigegebenen Merge auf `main` baut GitHub automatisch ein neues **Release** mit fertiger `PixFix.exe` zum Herunterladen.

---

1. Einmalige Einrichtung auf deinem Windows-PC

1. Repository klonen (falls noch nicht geschehen) und in den Projektordner wechseln:

   ```powershell
   git clone https://github.com/TGBox/pix-fix.git
   cd pix-fix
   ```

1. Schutz-Hooks aktivieren (einmalig pro lokalem Klon):

   ```powershell
   .\scripts\setup-hooks.ps1
   ```

   Danach verhindern lokale Git-Hooks automatisch:
   - direktes Committen oder Pushen auf `main`,
   - versehentliches Hochladen von Datenbanken (`.db`), Backups (`.pixfix`, `.zip`), Logs oder echten Kundendaten,
   - leere `catch`-Blöcke oder fragile Maus-/Tastatur-Automation,
   - fehlerhafte Builds oder fehlgeschlagene Tests beim Push.

---

1. Arbeiten mit ChatGPT oder Claude

## Wenn du **Claude Code / Codex / einen Agenten im Projektordner** nutzt

Das Tool liest `AGENTS.md` (bzw. `CLAUDE.md`) automatisch. Starte trotzdem mit einer klaren Aufgabe, z. B.:
> Bitte lies `AGENTS.md` und `docs/SPEC.md`. Setze folgende Änderung um, ergänze passende Tests, prüfe `dotnet build PixFix.sln` und `dotnet test PixFix.sln` und schlage am Ende eine passende Conventional-Commit-Nachricht vor.

### Wenn du **ChatGPT oder Claude in der Desktop- oder Web-App** nutzt

Lade bei einem neuen Chat die Datei **`AGENTS.md`** (und bei fachlichen Fragen auch **`docs/SPEC.md`**) direkt in den Chat hoch und nutze folgenden Start-Prompt:

```text
Ich arbeite am WPF/.NET-10-Projekt PixFix. Im Anhang liegt unsere verbindliche Projektregel-Datei AGENTS.md.
Bitte halte dich bei allen Vorschlägen strikt an AGENTS.md (Architektur, keine Maus-/Tastatur-Automation, keine leeren catch-Blöcke, keine Kundendaten, Fluent Theme Light/Dark, deutsche UI-Texte).
Nenne mir am Ende immer:
1. welche Dateien geändert werden,
2. wie ich die Änderung teste (dotnet build / dotnet test),
3. eine passende Commit-Nachricht im Format "feat(bereich): ..." oder "fix(bereich): ...".

Meine Aufgabe:
[Hier kurz beschreiben, was geändert oder behoben werden soll]
```

---

1. Dein 5-Schritt-Ablauf für jede Änderung

1. **Immer mit aktuellem `main` starten und einen eigenen Branch anlegen:**

   ```powershell
   git checkout main
   git pull
   git checkout -b feat/kurzer-name
   ```

   ## *(Für Fehlerbehebungen gern `fix/kurzer-name`, z. B. `fix/rueckruf-speichern`.)*

1. **Änderung mit ChatGPT oder Claude umsetzen und lokal prüfen:**

   ```powershell
   dotnet build PixFix.sln
   dotnet test PixFix.sln --no-build
   ```

1. **Änderung committen (Conventional-Commits-Format):**

   ```powershell
   git add -A
   git commit -m "feat(calls): kurze Beschreibung der Aenderung"
   ```

   - `fix(...): ...` für Fehlerbehebungen → erhöht automatisch die Patch-Version (z. B. `v1.0.1`)
   - `feat(...): ...` für neue Funktionen → erhöht automatisch die Minor-Version (z. B. `v1.1.0`)

1. **Branch hochladen:**

   ```powershell
   git push -u origin HEAD
   ```

   ## *(Dabei laufen automatisch Build und alle Tests noch einmal durch.)*

   Vorher fragt der Hook, welcher Versionssprung gewünscht ist – mit der aktuellen Version und dem Ergebnis je Wahl:

   ```text
   [pre-push] Aktuelle Version (neuestes Release-Tag): v1.1.0
     1) Patch  -> v1.1.1   fix, docs, chore, refactor, ...
     2) Minor  -> v1.2.0   feat
     3) Major  -> v2.0.0   feat! / BREAKING CHANGE:
   [pre-push] Die Commits dieses Branches ergeben: Minor -> v1.2.0
   [pre-push] Welcher Versionssprung ist gewünscht? [1/2/3, Enter = Minor]:
   ```

   Passt die Wahl nicht zu den Commit-Typen, bricht der Push ab und nennt den Befehl zum Korrigieren. Ohne Terminal
   (Push aus Visual Studio/Rider) wird nur informiert; Antwort vorgeben geht mit `$env:PIXFIX_BUMP = "minor"` (PowerShell)
   bzw. `PIXFIX_BUMP=minor git push` (Git Bash). Wichtig: Beim Squash-Merge bestimmt der **PR-Titel** die Version.

1. **Pull Request auf GitHub erstellen & auf Review warten:**
   - GitHub zeigt nach dem Push direkt einen Button **Compare & pull request** an.
   - Pull Request gegen `main` öffnen, Titel ebenfalls als `feat(...): ...` oder `fix(...): ...` wählen und kurz beschreiben, was geändert und getestet wurde.
   - Sobald der PR geprüft und auf `main` gemerged wurde, erstellt GitHub automatisch nach wenigen Minuten unter **Releases** (rechte Spalte auf GitHub) einen neuen Tag plus fertige **`PixFix.exe`** zum Herunterladen.

---

1. Hilfe bei typischen Hook-Meldungen

- **`Direkte Commits auf 'main' sind nicht erlaubt`:**  
  Du bist noch auf `main`. Einfach `git checkout -b feat/mein-thema` ausführen und erneut committen.
- **`Bitte Conventional-Commits-Format verwenden`:**  
  Die Commit-Nachricht braucht ein Präfix wie `feat: ...`, `fix: ...` oder `docs: ...` (siehe Beispiele in der Fehlermeldung).
- **`Verbotener Dateityp` oder `Echte E-Mail-Adresse` / `Windows-Benutzerpfad`:**  
  Es würde eine Datenbank, Sicherung, Logdatei oder echte/lokale Angabe hochgeladen. Datei aus dem Staging nehmen (`git restore --staged <datei>`) bzw. Platzhalter (`*.invalid`) verwenden.
- **`Abbruch: Gewünscht ist … die Commits ergeben aber …` beim Push:**  
  Die Commit-Typen führen zu einem anderen Versionssprung als gewählt. Zu niedrig: `git commit --amend -m "feat(...): ..."`
  oder leerer Commit `git commit --allow-empty -m "feat(...): ..."`. Zu hoch: Nachrichten per `git rebase -i origin/main`
  umbenennen (`reword`). Danach erneut pushen.
- **`Build/Tests fehlgeschlagen` beim Push:**  
  Fehler im Code beheben (gern die Fehlermeldung direkt in ChatGPT/Claude einfügen), neu committen und erneut pushen.
