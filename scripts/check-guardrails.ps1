param(
    [ValidateSet('Staged', 'PullRequest')]
    [string]$Mode = 'Staged',
    [string]$BaseRef = 'origin/main'
)

$ErrorActionPreference = 'Stop'

function Get-ChangedFiles {
    if ($Mode -eq 'Staged') {
        $output = & git -c core.quotepath=false diff --cached --name-only --diff-filter=ACMR
    }
    else {
        $output = & git -c core.quotepath=false diff "$BaseRef...HEAD" --name-only --diff-filter=ACMR
    }

    if ($LASTEXITCODE -ne 0) {
        throw "git diff --name-only failed with exit code $LASTEXITCODE."
    }

    return @($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Get-AddedDiffLines {
    if ($Mode -eq 'Staged') {
        $output = & git -c core.quotepath=false diff --cached -U0 --diff-filter=ACMR
    }
    else {
        $output = & git -c core.quotepath=false diff "$BaseRef...HEAD" -U0 --diff-filter=ACMR
    }

    if ($LASTEXITCODE -ne 0) {
        throw "git diff -U0 failed with exit code $LASTEXITCODE."
    }

    return @($output)
}

$forbiddenExtensions = @(
    '.db', '.db-shm', '.db-wal', '.sqlite', '.sqlite3',
    '.pixfix', '.zip', '.7z', '.rar', '.log',
    '.env', '.key', '.pem', '.pfx', '.p12', '.crt', '.cer'
)

$forbiddenPrefixes = @('PixFix-Daten/', 'publish/', 'logs/')
$maxFileBytes = 2097152
$placeholderUsers = @('Name', 'Benutzer', 'User', 'Pfad')
$allowedEmailDomains = @('.invalid', '.example', 'example.com', 'example.org', 'example.net', 'localhost')

$violations = New-Object System.Collections.Generic.List[string]
$changedFiles = Get-ChangedFiles

if ($changedFiles.Count -eq 0) {
    exit 0
}

foreach ($rawFile in $changedFiles) {
    $file = $rawFile.Replace('\', '/')
    $lowerFile = $file.ToLowerInvariant()
    $baseName = [System.IO.Path]::GetFileName($file)
    $extension = [System.IO.Path]::GetExtension($file).ToLowerInvariant()

    if ($forbiddenExtensions -contains $extension -or $baseName.StartsWith('.env.', [System.StringComparison]::OrdinalIgnoreCase)) {
        $violations.Add("$file : Verbotener Dateityp ('$baseName') darf nicht eingecheckt werden (Datenbanken, Backups, Logs, Keys oder .env-Dateien).")
        continue
    }

    $hasForbiddenPrefix = $false
    foreach ($prefix in $forbiddenPrefixes) {
        if ($lowerFile.StartsWith($prefix.ToLowerInvariant())) {
            $violations.Add("$file : Dateien im Ordner '$prefix' duerfen nicht in Git gespeichert werden.")
            $hasForbiddenPrefix = $true
            break
        }
    }

    if ($hasForbiddenPrefix) {
        continue
    }

    if ((Test-Path -LiteralPath $file) -and -not $lowerFile.StartsWith('assets/')) {
        $item = Get-Item -LiteralPath $file
        if (-not $item.PSIsContainer -and $item.Length -gt $maxFileBytes) {
            $sizeMb = [Math]::Round($item.Length / 1MB, 2)
            $violations.Add("$file : Datei ist zu gross ($sizeMb MB > 2 MB). Binaere Nutzdaten/Artefakte gehoeren nicht ins Repo.")
        }
    }
}

$diffLines = Get-AddedDiffLines
$currentFile = ''

foreach ($line in $diffLines) {
    if ($line.StartsWith('+++ b/')) {
        $currentFile = $line.Substring(6).Replace('\', '/')
        continue
    }

    if ($line.StartsWith('+++') -or $line.StartsWith('---') -or $line.StartsWith('@@') -or $line.StartsWith('diff --git') -or $line.StartsWith('index ')) {
        continue
    }

    if (-not $line.StartsWith('+') -or [string]::IsNullOrEmpty($currentFile)) {
        continue
    }

    $normalizedCurrent = $currentFile.ToLowerInvariant()
    if ($normalizedCurrent.StartsWith('.githooks/') -or $normalizedCurrent -eq 'scripts/check-guardrails.ps1') {
        continue
    }

    $added = $line.Substring(1)

    if ($added -match '^(<{7}|={7}|>{7})(\s|$)') {
        $violations.Add("$currentFile : Unaufgeloester Merge-Konflikt-Marker gefunden.")
    }

    if ($added -match '-----BEGIN (RSA |EC |OPENSSH |DSA |PGP )?PRIVATE KEY-----') {
        $violations.Add("$currentFile : Privater Schluessel (PRIVATE KEY) erkannt.")
    }

    if ($added -match '(ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|sk-[A-Za-z0-9]{20,}|AKIA[0-9A-Z]{16}|AIza[0-9A-Za-z_-]{35})') {
        $violations.Add("$currentFile : Moeglicher API-Token oder Zugriffsschluessel erkannt.")
    }

    $pathMatches = [regex]::Matches($added, '(?i)[A-Z]:[/\\]Users[/\\]([^/\\\s"''`<>]+)')
    foreach ($match in $pathMatches) {
        $userSegment = $match.Groups[1].Value
        if ($userSegment -ne '...' -and ($placeholderUsers -notcontains $userSegment)) {
            $violations.Add("$currentFile : Hartkodierter lokaler Windows-Benutzerpfad ('$($match.Value)') gefunden. Bitte relative Pfade oder Platzhalter verwenden.")
        }
    }

    if ($normalizedCurrent.StartsWith('src/') -or $normalizedCurrent.StartsWith('tests/')) {
        $emailMatches = [regex]::Matches($added, '[A-Za-z0-9._%+-]+@([A-Za-z0-9.-]+\.[A-Za-z]{2,})')
        foreach ($match in $emailMatches) {
            $domain = $match.Groups[1].Value.ToLowerInvariant()
            $isAllowed = $false
            foreach ($allowed in $allowedEmailDomains) {
                if ($domain -eq $allowed -or $domain.EndsWith($allowed)) {
                    $isAllowed = $true
                    break
                }
            }

            if (-not $isAllowed) {
                $violations.Add("$currentFile : Echte E-Mail-Adresse ('$($match.Value)') in Quellcode oder Tests gefunden. Fuer Tests bitte *.invalid verwenden.")
            }
        }
    }

    if ($normalizedCurrent.EndsWith('.cs')) {
        if ($added -match 'catch\s*(\([^)]*\))?\s*\{\s*\}') {
            $violations.Add("$currentFile : Leerer catch-Block gefunden. Fehler muessen geloggt oder gezielt behandelt werden.")
        }

        if ($added -match '\b(mouse_event|SetCursorPos|SendKeys|keybd_event|SendInput)\b') {
            $violations.Add("$currentFile : Fragile UI-/Maus-/Tastatur-Automation ('$($Matches[1])') ist laut Projektregeln verboten.")
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host ''
    Write-Host '[Guardrails] Folgende Probleme muessen vor dem Commit/Merge behoben werden:' -ForegroundColor Red
    foreach ($entry in ($violations | Select-Object -Unique)) {
        Write-Host "  - $entry" -ForegroundColor Red
    }
    Write-Host ''
    exit 1
}

exit 0
