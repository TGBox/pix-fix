# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionen nach SemVer.

## [Unreleased]

### Behoben

- `SQLitePCLRaw.lib.e_sqlite3` auf 2.1.14 festgelegt (Sicherheitslücke GHSA-2m69-gcr7-jv3q in 2.1.11).

### Hinzugefügt

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
