# GitHub-Einrichtung für `main`-Schutz & automatische Releases

Diese einmaligen Einstellungen direkt in der GitHub-Weboberfläche stellen sicher, dass niemand versehentlich direkt auf `main` pusht/merged und der Release-Workflow Tags sowie Releases anlegen darf.

1. `main`-Branch schützen (Repository-Einstellungen)

Öffne auf GitHub: **Settings → Rules → Rulesets → New ruleset → New branch ruleset** (alternativ **Settings → Branches → Add classic branch protection rule**):

- **Ruleset Name:** `Protect main`
- **Enforcement status:** `Active`
- **Target branches:** `Include default branch` (bzw. `main`)
- **Regeln aktivieren:**
  - **Restrict deletions** (Löschen von `main` verbieten)
  - **Block force pushes** (Force-Push verbieten)
  - **Require a pull request before merging:**
    - **Required approvals:** `1`
    - **Dismiss stale pull request approvals when new commits are pushed** aktivieren
  - **Require status checks to pass:**
    - **Require branches to be up to date before merging** aktivieren
    - Als Pflicht-Check hinzufügen: **`build`** (aus `.github/workflows/build.yml`)
- **Bypass list:** Nur Admin/Maintainer (du selbst) bei Bedarf; normale Mitwirkende (Collaborator mit Write-Rechten) **nicht** auf die Bypass-Liste setzen, damit direktes Mergen/Pushen auf `main` für sie blockiert ist.

1. Merge-Einstellungen für saubere Versionsnummern

Unter **Settings → General → Pull Requests**:

- **Allow squash merging** aktivieren und als Standard-Commit-Titel **Pull request title** empfehlen (da `build.yml` den PR-Titel auf Conventional Commits prüft und `release.yml` daraus automatisch `fix`/`feat`/Breaking-Change für die SemVer-Nummer ableitet).

1. Rechte für automatische Tags und GitHub Releases

Unter **Settings → Actions → General → Workflow permissions**:

- **Read and write permissions** auswählen (oder in `.github/workflows/release.yml` explizit gesetztes `permissions: contents: write` zulassen).
- Hinweis: Falls ein Tag von `github-actions[bot]` durch ein Tag-Ruleset blockiert wäre, in den Tag-Rulesets keine Sperre für `v*` setzen.

1. Erster Test nach dem Einrichten

1. Feature-Branch mit diesem Setup über einen PR auf `main` mergen.
1. Unter **Actions** prüfen, ob `build` (auf dem PR) und anschließend `release` (auf `main`) grün durchlaufen.
1. Unter **Releases** prüfen, ob der erste Tag (z. B. `v1.0.0`) inklusive direktem Download von **`PixFix.exe`** und `PixFix.exe.sha256` erschienen ist.
