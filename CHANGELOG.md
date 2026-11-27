# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionen nach SemVer.

## [Unreleased]

### Behoben

- `SQLitePCLRaw.lib.e_sqlite3` auf 2.1.14 festgelegt (Sicherheitslücke GHSA-2m69-gcr7-jv3q in 2.1.11).

### Hinzugefügt

- pre-push-Hook fragt nach dem gewünschten SemVer-Sprung (Patch/Minor/Major) und zeigt dazu das neueste Release-Tag und die
  jeweils entstehende Version. Passt die Wahl nicht zu den Commit-Typen des Branches, wird der Push mit Korrekturhinweis
  abgebrochen. Ohne Terminal nur Hinweis; Vorgabe per `PIXFIX_BUMP=patch|minor|major`.
- Update-Hinweis: PixFix prüft beim Start und alle 6 Stunden das neueste GitHub-Release (`TGBox/pix-fix`). Gibt es eine
  neuere Version, erscheint oben im Fenster eine Hinweisleiste mit „Herunterladen“ (direkter Link auf `PixFix.exe`),
  „Was ist neu?“, „Programmordner öffnen“ und „Später“; liegt PixFix im Hintergrund, kommt zusätzlich eine
  Windows-Benachrichtigung. Einstellungen → Allgemein → Updates zeigt die installierte Version, sucht sofort und lässt sich
  abschalten. Fehler bei der Abfrage bleiben in der Anzeige und stören PixFix nicht.
- Schaltfläche „PixFix beenden“ unten in der Navigation: beendet PixFix vollständig, ohne Umweg über das Symbol im
  Infobereich (das Schließen-Kreuz legt PixFix weiterhin nur in den Infobereich).
- Warnung vor dem Beenden (Schaltfläche, Infobereich-Menü oder Schließen-Kreuz ohne Infobereich), wenn heute noch
  Rückruf-Erinnerungen anstehen – mit Liste der Rückrufe (Uhrzeit, L-Nummer, Grund); Standard ist „Nein“. Beim Abmelden
  oder Herunterfahren von Windows kommt keine Rückfrage.
- Bereich „Hilfe“ (Strg+6, F1, Infobereich-Menü) mit Kapiteln zu allen Funktionen und „Zum Bereich wechseln“.
- Einführungstour für neue Mitarbeiter: startet einmalig beim ersten Programmstart, führt durch alle Bereiche und lässt
  sich unter Hilfe jederzeit wiederholen.
- Tastenkürzel-Liste gemeinsam für Hilfe und Einstellungen; Strg+6 und F1 ergänzt.
- Projektgerüst: PixFix.Core, PixFix.Infrastructure, PixFix.App (WPF, .NET 10), Tests.
- Spezifikation `docs/SPEC.md` aus PixFix 69.74.194 abgeleitet.
- Datenordner unter `%LOCALAPPDATA%\PixFix`, Datei-Logging, globale Fehlerbehandlung.
- `CallStatus` inkl. Einlesen der PixFix-69-Statuswerte, `TemplateRenderer` für Vorlagen-Platzhalter.
- Phase 1: Datenbankschema v1 mit Migrationen, inhaltsadressierte Dateiablage (`assets/`, SHA-256, Duplikate nur einmal).
- Phase 1: Import aus PixFix 69 (`pixfix.db` Schema 1, Bilder, RTF-Dokumente, PDFs, Fax-Dokumente, Vorlagen, Einstellungen,
  Papierkorb) in einer Transaktion mit Soll/Ist-Abgleich; Altdaten werden nur über eine temporäre Kopie gelesen.
- Einstellungen: Schaltfläche „PixFix-69-Ordner wählen und importieren …“ mit Importbericht.
- Import: Fortschrittsbalken in Prozent mit Anzeige des aktuellen Abschnitts („Bilder werden übernommen (120 von 644) …“).
- Phase 2a: Wissensdatenbank nachschlagen – Suche mit der Gewichtung aus PixFix 69 (Titel, Abschnitt, Tags, OCR, Inhalt),
  Trefferliste mit Textausschnitt, ohne Suchbegriff nach Abschnitten gruppiert; schreibgeschützte Dokumentanzeige mit Bildern,
  Fundstellen markieren und mit F3/Umschalt+F3 durchspringen.
- Bilder übernommener Dokumente: Metafile-Bilder werden durch die Originaldatei bzw. das enthaltene Bitmap (PNG) ersetzt.
- Phase 2b: Einträge anlegen, bearbeiten und in den Papierkorb verschieben; Papierkorb mit Wiederherstellen und endgültigem
  Löschen (auch für aus PixFix 69 übernommene Papierkorb-Einträge). Titel, Abschnitt (neu eintippbar) und Tags im Kopf,
  Favorit/Geschützt/Archiv per Schalter, Archiv in der Liste einblendbar.
- Editor: Formatleiste (fett, kursiv, unterstrichen, Schriftgröße, Listen, Rückgängig), Rechtschreibprüfung Deutsch,
  automatisches Speichern (2 s nach der letzten Änderung, beim Eintragswechsel, Strg+S, beim Schließen).
- Bilder per Strg+V (Screenshot), Datei-Dialog oder Drag & Drop einfügen; Texterkennung (Windows-OCR) neuer Bilder im
  Hintergrund, Text wird durchsuchbar. Speicherformat FlowDocument-XAML mit Bildverweisen auf die Ablage; übernommene
  Dokumente werden beim ersten Speichern umgewandelt und verwenden ihre Originalbilder weiter.
- Datenbankschema v2: OCR-Text je Bild.
- Phase 2c: Kopier-Buttons (markierten Text als Button speichern, Klick kopiert, Löschen per Kontextmenü);
  neue Einträge leer oder aus einer Eintragsvorlage; Abschnitte umbenennen, sortieren und leere löschen;
  Einträge im Abschnitt mit ↑/↓ bzw. Alt+↑/↓ verschieben.
- Phase 3: Bildsuche – Screenshot (Strg+V), Datei oder Drag & Drop; Bewertung 1:1 aus PixFix 69 (Signaturen per GDI+,
  Bild × 250 + Textabdeckung × 100 + Fehlercodes × 160 + Textsuche, höchstens 60 Treffer); Vorschau per Klick,
  Doppelklick/Enter öffnet den Eintrag und springt zum Bild. Bildsuch-Index in der Datenbank (Schema v3), wird beim Start
  und nach einem Import im Hintergrund ergänzt. Vergleichstest gegen den Bildindex von PixFix 69 (PIXFIX_LEGACY_DATA).
- Phase 4: Schreibbereiche – Bereich wählen, Reiter nach Abschnitten (ein-/ausklappbar, Zustand wird gespeichert),
  Suche im Bereich mit Markierung im Editor, Reiter anlegen/umbenennen/verschieben/schützen/löschen, Abschnitte
  verschieben, PDF-Anhänge öffnen/anhängen/entfernen, Kopier-Buttons; Bereiche anlegen, umbenennen, sortieren, schützen
  und leere löschen. Gelöschte Reiter landen als Abbild im Papierkorb (Wiederherstellen folgt). Schema v4.
- Editor als gemeinsames Bauteil (`DocumentEditor`) für Wissensdatenbank und Schreibbereiche.
- Phase 5a: Anrufe – Liste mit Kurzinfo und Erinnerungszeit, „Erledigte anzeigen“, Anrufkarte mit allen Feldern,
  „Warum wird angerufen?“ und Lösung als formatierte Felder (RTF, Speichern 1 s nach der Eingabe), Vorlagen für Grund
  und Lösung, Papierkorb mit Wiederherstellen (auch PixFix-69-Papierkorb).
- Statuswechsel wie PixFix 69: kostenpflichtige Anrufe nur mit Start- und Endzeit auf „Erledigt“, Rückfrage zur
  Rechnungs-Mail („Nein“ öffnet sie), „Später/Morgen erneut anrufen“ fragt den Erinnerungszeitpunkt ab.
- Rückruf-Erinnerungen: Prüfung alle 5 s unabhängig vom angezeigten Bereich, immer nur ein Fenster, Taskleiste blinkt;
  „OK“ setzt „In Bearbeitung“ und springt zum Anruf, „Erneut erinnern“ verschiebt.
- Anrufen (Nummernauswahl, `tel:` oder Windows-Wählhilfe) und Auflegen über TAPI (genau ein aktives Gespräch, sonst nichts).
- E-Mail mit Vorlage, Rechnungs-Mail und Fax-Dokumente; Outlook Desktop per COM, sonst `mailto:`.
- Cobra: Anruftext in die Zwischenablage, Cobra-Schnelltexte (Notizen, Lösungstext, Status). Abrechnungszeit „Jetzt“.
- Phase 5b: Vorlagenfenster mit allen Vorlagenarten (E-Mail mit Betreff, Anrufgründe, Lösungstexte, Fax-Vorlagen,
  Schnelltextbausteine, Eintragsvorlagen), Cobra-Schnelltexten und Fax-Dokumenten – anlegen, bearbeiten (sofort
  gespeichert), sortieren, löschen; Fax-Dokumente hinzufügen, umbenennen und öffnen.
- Einstellungen mit Reitern: Anrufe (Wählmethode, E-Mail-Programm, Rechnungs-Mail mit Empfänger/Betreff/Text) und
  Import aus PixFix 69. Ohne Einstellung gilt Outlook als E-Mail-Programm.
- Phase 6a: Sicherung im neuen Format 4.0 (`.pixfix`, ZIP mit `manifest.json`, Länge und SHA-256 je Datei,
  konsistenter SQLite-Snapshot per `VACUUM INTO`, ohne Bildindex). Jede Sicherung wird nach dem Schreiben vollständig geprüft.
- Einstellungen → Sicherung: Sicherungsordner wählbar (Standard: Datenordner\backups, z. B. OneDrive möglich), Jetzt sichern,
  Sichern unter, Sicherung prüfen, Sicherung wiederherstellen.
- Wiederherstellen von PixFix-Sicherungen 4.0 und PixFix-69-Sicherungen 3.0: prüfen → in Stage-Ordner entpacken bzw.
  importieren → aktuellen Stand sichern → Datenbank und Ablage austauschen, bei Fehler Rückgängig; Pfade aus dem Archiv
  werden gegen Pfad-Traversal geprüft.
- Automatische Sicherung: eine Datei pro Tag (`AUTO-Datum.pixfix`), höchstens alle 4 Stunden aktualisiert, nur wenn der
  Benutzer mindestens 1 Minute untätig ist, 7 Tage Aufbewahrung; Fehler nur im Log und in den Einstellungen.
- Import aus PixFix 69 funktioniert jetzt auch, wenn schon Daten vorhanden sind (nach Rückfrage, mit Sicherung vorher).
- Phase 6b: PixFix-69-Sicherungen im Format 2.1 (XML, vor der SQLite-Umstellung) wiederherstellen. Die XML-Dateien werden
  Feld für Feld wie in der SQLite-Erstmigration von 69.74.178 in eine PixFix-69-Datenbank umgewandelt, gegen die Anzahlen
  im Manifest geprüft und dann wie eine 3.0-Sicherung übernommen.
- Phase 6c: Fernwartung – Buttons „AnyDesk“ und „TeamViewer“ in den Anrufen. Laufende Programme werden in den Vordergrund
  geholt; AnyDesk startet nur über die normale Windows-Verknüpfung (gefundene Verknüpfung wird gemerkt), TeamViewer über
  App Paths bzw. die üblichen Installationsordner, beide mit `%TEMP%` als Arbeitsverzeichnis.
- TI-Monitor als eigener Bereich: gematik TI-Status und TI-Lagebild, grün/rot/orange, Einzelmeldungen, Links zu Fachportal
  und Lagebild, „Jetzt prüfen“, automatische Prüfung alle 5 Minuten. TI-Symbol links unten zeigt den Zustand und blinkt
  bei einer neuen Störung, bis der Monitor geöffnet wird. Abfragefehler bleiben im Modul.
- Einstellungen → TI-Monitor: aktivieren, automatisch prüfen, blinken (Werte aus PixFix 69 werden übernommen).
- Fenstergröße, -position und Maximiert sowie die Splitterbreiten (Navigation, Wissensdatenbank, Schreibbereiche, Anrufe,
  Bildsuche) werden gemerkt; liegt die gespeicherte Position auf einem nicht mehr vorhandenen Monitor, bleibt nur die Größe.
- Infobereich: Schließen lässt PixFix im Infobereich weiterlaufen (Erinnerungen, TI-Monitor, Sicherung bleiben aktiv),
  Menü mit Öffnen, Anrufe, Bildsuche, TI-Monitor und Beenden. Windows-Benachrichtigungen für fällige Rückrufe und neue
  TI-Störungen, wenn PixFix nicht im Vordergrund ist.
- Tastatur: Strg+1 … Strg+5 (Bereiche), Strg+F (Suche), Strg+N (neu), Entf in den Listen (Papierkorb), Zugriffstasten
  (Alt+Buchstabe) für die wichtigsten Schaltflächen. Systemweit Strg+Alt+P: PixFix nach vorn und Bildsuche mit dem
  Screenshot aus der Zwischenablage. Übersicht unter Einstellungen → Allgemein.
- „Rückgängig“-Hinweis nach dem Löschen eines Anrufs oder Wissensdatenbank-Eintrags (12 Sekunden).
- Drag & Drop: PDF-Dateien auf den Kopf eines Reiters ziehen hängt sie an; Fax-Dokumente per Drag & Drop hinzufügen.
- Sicherung, Prüfung, Wiederherstellung und Import aus PixFix 69 lassen sich abbrechen, solange die Daten noch nicht
  ausgetauscht werden.
- Programmsymbol `assets/pix-fix.ico` für EXE, Hauptfenster und Infobereich.
- Editor: „Bausteine ▾“ fügt Lösungsbausteine an der Cursorposition ein (Code-Bausteine als Consolas-Block), speichert
  markierten Text als Baustein und öffnet die Bausteinverwaltung – wie in PixFix 69.
- Schreibbereiche: Papierkorb für Reiter mit Wiederherstellen (inkl. Kopier-Buttons, PDF-Anhängen und Bildern) und
  endgültigem Löschen; „Rückgängig“-Hinweis auch nach dem Löschen eines Reiters.
- Schreibbereiche: „Per Mail …“ öffnet den Reiter als Outlook-Entwurf mit Formatierung, eingebetteten Bildern und den
  PDF-Anhängen (Empfänger: leer oder E-Mail-Adressen des geöffneten Anrufs); mit dem Standard-Mailprogramm nur Text.
- Import aus einem PixFix-69-Ordner, der nur XML-Daten enthält (vor der SQLite-Umstellung): Kopie wird umgewandelt, das
  Original bleibt unverändert.
