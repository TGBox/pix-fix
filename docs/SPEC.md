# PixFix – Spezifikation für den Neuaufbau

Stand: 27.11.2026 · Status: Entwurf zur Abstimmung

Quelle: PixFix 69.74.194 (Ordner `PixFix-69.74.194-Textsuche-Trefferlisten-Test`), insbesondere
`PixFix-Feste-Regeln.md`, `PixFix-Entwicklungsdokumentation.md`, `PixFix.Models.cs` und das SQLite-Schema.
Die Altversion wird im Folgenden **PixFix 69** genannt.

---

1. Zweck

PixFix ist das persönliche Arbeitswerkzeug eines Hotline-Mitarbeiters (After-Sales-Support Data-Cur).
Es bündelt in einem Fenster:

- eine **Wissensdatenbank zu Fehlerbildern** (Screenshot + Beschreibung + Lösung), durchsuchbar per Text und per Bild,
- frei strukturierbare **Schreibbereiche** (Notizen, Anleitungen, aktuelle Fehler, Werkzeuge) mit PDF-Anhängen,
- die **Anrufbearbeitung** (Anrufkarten mit Kundendaten, Grund, Lösung, Status, Rückruf-Erinnerung, Abrechnungszeit),
- Absprünge in **Telefonie, Fernwartung, E-Mail/Fax, Cobra** und einen **TI-Statusmonitor**.

1. Ziele und Nicht-Ziele

## **Ziele**

1. Funktional gleichwertiger Ersatz für PixFix 69 – der Mitarbeiter soll nichts vermissen.
2. Vollständige Übernahme seines Datenbestands (`pixfix.db`, Bilder, RTF, PDFs, Fax-Dokumente, `settings.xml`) und Import alter Sicherungen (Format 2.1 und 3.0).
3. Wartbarer, testbarer Code: lesbare Formatierung, getrennte Schichten, Logging, automatisierte Tests, Git.
4. Verteilung als fertig gebautes Programm – kein Kompilieren auf dem Zielrechner.
5. Später für weitere Hotline-Mitarbeiter nutzbar (Mehrbenutzerfähigkeit pro Windows-Profil, nicht geteilt).

## **Nicht-Ziele (vorerst)**

- Gemeinsame Datenbank für mehrere Mitarbeiter / Serverbetrieb.
- Exakte Nachbildung der Cobra-Bildschirmauslese per OCR (siehe F10).
- OneNote-/DOCX-Import (nur falls der Mitarbeiter ihn noch braucht – offene Frage).
- Pixelgenaue Nachbildung der WinForms-Oberfläche.

---

1. Funktionsumfang

Kennzeichnung der Priorität: **M** = muss für Version 1, **S** = soll, **K** = kann später.
„Übernommene Regel" = fachliche Anforderung aus PixFix 69, die bewusst erhalten bleibt.

### F1 – Wissensdatenbank (PixFix-Bereich) · M

- Einträge (`FixEntry`) mit Titel, Abschnitt/Kategorie, Tags, formatiertem Inhalt mit eingebetteten Bildern, OCR-Text der Bilder.
- Abschnitte frei anlegen, umbenennen, sortieren; Einträge innerhalb eines Abschnitts sortieren (Drag & Drop).
- Zustände **Favorit**, **Geschützt** (nicht versehentlich änderbar/löschbar), **Archiviert** (nicht in normaler Suche).
- **Papierkorb**: Löschen verschiebt in den Papierkorb; Wiederherstellen und endgültig Löschen getrennt.
- Fehlerbild aus der Zwischenablage oder per Drag & Drop einfügen; OCR läuft automatisch im Hintergrund.
- Eintragsvorlagen (`EntryTemplateDefinition`) für neue Einträge.
- **Copy-Actions**: Kopier-Buttons, die an einen Textbereich gebunden sind und dessen aktuellen Inhalt kopieren; per Kontextmenü löschbar.
- Undo/Redo, Autosave, Rechtschreibprüfung (Deutsch).
- Letzte geöffnete Einträge, Scroll-/Cursorposition je Eintrag werden gemerkt.

Übernommene Regeln:

- Bilddateien werden beim endgültigen Löschen nur entfernt, wenn kein anderer aktiver oder gelöschter Eintrag sie referenziert.
- Neue Abschnitte nutzen dieselbe Eintragsstruktur – keine Sonderstruktur.
- Bild, OCR-Text und Inhalt eines Eintrags dürfen nie voneinander entkoppelt werden.

### F2 – Suche · M

## **Textsuche**

- Über Titel, Abschnitt, Tags, OCR-Text und Inhalt; Archiv ausgeschlossen.
- Gewichtung: Titel > Tags > OCR > Inhalt; Bonus, wenn alle Suchwörter treffen.
- Trefferliste bleibt beim Öffnen eines Treffers offen (Vor/Zurück, Enter, Doppelklick navigieren nur; Schließen nur bewusst).
- Fundstellen im Dokument hervorheben, **ohne** den gespeicherten Inhalt zu verändern.
- Dieselbe Suche steht in jedem Schreibbereich zur Verfügung.
- Letzte Suchbegriffe werden gemerkt.

## **Bildsuche (Fehlerbildsuche)**

- Screenshot aus der Zwischenablage → ähnliche gespeicherte Fehlerbilder finden.
- Kombination aus visueller Ähnlichkeit (Average-Hash, Difference-Hash, Pixelmatrix 16×16, Histogramm, Seitenverhältnis – jeweils für Gesamtbild und Bildmitte) und Textähnlichkeit (OCR des Suchbilds gegen OCR der gespeicherten Bilder, Fehlercodes).
- Einfacher Klick = Vorschau, Doppelklick/Enter = Eintrag öffnen und zur Bildstelle springen.
- Der Bildindex ist ein regenerierbarer Cache; ein fehlender Index darf nie Datenverlust bedeuten.

Übernommene Regel: Die Trefferqualität der Bildsuche ist über viele Versionen erarbeitet.
**Der Scoring-Algorithmus wird 1:1 aus `PixFix.Search.cs` portiert** und mit Vergleichstests gegen PixFix 69
abgesichert (gleiche Eingabebilder → gleiche Trefferreihenfolge), bevor er verändert werden darf.

Umsetzung (Phase 3): Signaturen werden mit derselben Bibliothek (GDI+, HighQualityBicubic) und denselben Schritten
berechnet; der Test `SignaturesMatchPixFix69Index` vergleicht sie bitgenau mit `search-index/image-index.xml` aus
PixFix 69 (aufrufen mit Umgebungsvariable `PIXFIX_LEGACY_DATA` = Ordner `PixFix-Daten`). Zwei bewusste Abweichungen:
Bei Gleichstand ist die Reihenfolge stabil (Titel, Bild) statt zufällig, und schlägt die OCR des Suchbilds fehl, wird
kein Fehlertext („[OCR-Fehler …]“) als Suchtext verwendet.

### F3 – Schreibbereiche · M

- Mehrere Bereiche (`WritingWorkspace`) mit Titel, Symbol, Reihenfolge, Schutzstatus.
- Je Bereich Abschnitte und Reiter (`InformationEntry`) mit formatiertem Text, Copy-Actions, PDF-Anhängen.
- Abschnitte ein-/ausklappbar; Reihenfolge per Drag & Drop.
- PDF-Anhänge öffnen, hinzufügen, entfernen (Dateien sind Nutzdaten und Teil der Sicherung).

### F4 – Anrufe · M

- Anrufkarte (`CallEntry`) je Anruf: L-Nummer (VNUM1), ID, Telefon 1–3, privat, direkt, mobil, alternative Nummer, E-Mail 1–2, Ziel-E-Mail, Fax, Notizen.
- „Warum wird angerufen?" und „Was wurde durchgeführt / Lösung" als formatierte Felder (Bilder erlaubt), mit Vorlagen für Gründe und Lösungen.
- Status: `Noch zu bearbeiten`, `In Bearbeitung`, `Später erneut anrufen`, `Morgen erneut anrufen`, `Erledigt`.
- Kostenpflichtig-Kennzeichen mit Start/Ende der Abrechnungszeit und Dauer.
- Rückrufziel aus den vorhandenen Nummern wählbar.
- Karten ein-/ausklappen, löschen (Papierkorb), wiederherstellen.
- Bei `Erledigt` + kostenpflichtig + Rückfrage „Nein" → Rechnungs-Mail über **denselben** Weg wie der Button „Support-Mail für Rechnung".
- Spalten-/Splitterbreiten der Karten werden gemerkt und für neue Karten übernommen.
- *Neuaufbau:* Liste + eine Anrufkarte statt untereinander ausgeklappter Karten; Ein-/Ausklappen und gemerkte Kartenbreiten entfallen dadurch.

### F5 – Rückruf-Erinnerungen · M

- Erinnerung je Anruf (`ReminderAt`, `ReminderActive`, `ReminderLastShown`); keine zweite Erinnerungsstruktur.
- Statuswechsel auf „Später/Morgen erneut anrufen" öffnet den Erinnerungsdialog.
- Fällige Erinnerung: nur ein Popup gleichzeitig; Taskleiste blinkt.
- **OK** = Erinnerung beenden, Status `In Bearbeitung`, PixFix in den Vordergrund. **Erneut erinnern** = verschieben ohne Statuswechsel. **Erledigt** = Erinnerung deaktivieren.

### F6 – Vorlagen und Kommunikation · M

- E-Mail-Vorlagen, Fax-Vorlagen und Fax-Dokumente, Schnelltextbausteine, Anrufgründe, Lösungstexte, Cobra-Schnelltexte.
- Platzhalter: `{LNUM}`, `{ID}`, `{WARUM}`, `{LOESUNG}`, `{EMAIL}`, `{FAX}` – vollständige Liste in PixFix 69.
- E-Mail über das konfigurierte Mailprogramm (mailto bzw. Outlook) erzeugen; Fax als PDF/Druck.
- Vorlagen gehören zu den gesicherten Nutzdaten.

### F7 – Telefonie · M

- **Anrufen**: Einstellung „Windows-TAPI-Wählhilfe" → `dialer.exe /d "<nummer>"`, sonst `tel:`-URI (öffnet Unify Phone).
- **Auflegen**: TAPI nur beim Klick lokal öffnen; genau ein aktiver lokaler Call → beenden; keiner oder mehrere → nichts tun. Keine dauerhafte TAPI-Sitzung, kein exklusives Leitungs-Ownership. Unify Phone muss unbeeinträchtigt bleiben.

### F8 – Fernwartung · S

- AnyDesk über den Windows-Shortcut (`.lnk`) starten, nie direkt die EXE (UAC/Doppelstart); laufende Instanz in den Vordergrund holen; gefundener Shortcut wird gemerkt.
- TeamViewer starten.
- Beide mit neutralem Arbeitsverzeichnis (z. B. `%TEMP%`), damit der Datenordner nicht gesperrt wird.

### F9 – TI-Monitor · S

- Fragt die gematik-TI-Lage ab (`ti-lage.prod.ccs.gematik.solutions/lageapi/v2/tilage`, `/v1/tistatus/incident`) und zeigt Störungen an; Links zu Fachportal und Lagebild.
- Optional automatische Prüfung, Blinken bei Störung.
- Fehler (Netz, API, Parsing) bleiben im Modul und dürfen die Oberfläche nie blockieren.

### F10 – Cobra-Anbindung · K (Entscheidung vertagt)

PixFix 69 liest Kundendaten per Bildschirm-OCR aus Cobra und trägt Kontakte per Maus-/Tastatursteuerung ein.
Für den Neuaufbau wird zunächst nur eine Schnittstelle vorgesehen:

```csharp
public interface ICustomerDirectory   // Kundendaten zur L-Nummer holen
public interface IContactHistoryWriter // Kontakttext (Grund --> Lösung) zum Kunden schreiben
```

Version 1 liefert eine Implementierung **„Zwischenablage"**: Kundendaten werden manuell eingegeben, der Cobra-Text
(`<Grund> --> <Lösung>`) wird in die Zwischenablage kopiert. Spätere Optionen: direkte Cobra-Datenbank,
Weg über py-connect oder Portierung der UI-Automatisierung.

Übernommene Regeln (gelten für jede spätere Implementierung):

- Vor dem Schreiben wird die L-Nummer des Zielkunden zwingend geprüft; eine leere oder abweichende L-Nummer gilt nie als Übereinstimmung.
- Schnelltexte „Praxis (erneut) nicht erreicht" übertragen den Text, hängen `NE` an die Notizen und setzen optional „Später erneut anrufen" (→ F5).
- Beim Wechsel auf einen anderen Kunden dürfen keine alten Kontaktdaten stehen bleiben.

### F11 – Sicherung und Import · M

- Manuelle Komplettsicherung als ZIP mit Manifest (Dateiliste mit Länge und SHA-256, Anzahl der Datenobjekte).
- Automatische stille Sicherung (Tagesarchive, begrenzte Aufbewahrung) nur bei Inaktivität und mit konsistentem SQLite-Snapshot (`VACUUM INTO` bzw. Backup-API).
- Import: Archiv vollständig prüfen (Hashes, Pfad-Traversal-Schutz, Datenmodell) → aktuellen Stand vorab sichern → in einen Stage-Ordner entpacken und validieren → atomar übernehmen → bei Fehler Rollback.
- **Import alter PixFix-69-Sicherungen**: Format 2.1 (XML-Nutzdaten) und 3.0 (SQLite). Das ist Pflicht.
- Eigenes neues Sicherungsformat (4.0) mit Versionsfeld; alte Formate bleiben dauerhaft lesbar.
- Abgeleitete Daten (Bildindex, Suchindex) werden nicht gesichert, sondern neu aufgebaut.
- *Umsetzung 6a:* Format 4.0 (`manifest.json`), Wiederherstellung 4.0 und 3.0, automatische Sicherung. Sicherungen werden
  nicht verschlüsselt (Entscheidung); der Sicherungsordner ist einstellbar, Standard ist `Datenordner\backups`.
  Aufbewahrung der Tagessicherungen 7 Tage (PixFix 69: heute und gestern).
- *Umsetzung 6b:* Format 2.1 wird über eine Umwandlung XML → PixFix-69-Datenbank (Schema 1, wie 69.74.178) gelesen.

### F12 – Datenübernahme aus PixFix 69 · M

- Einmaliger Assistent: PixFix-69-Ordner auswählen → `pixfix.db` (Schema `schema_info`) + `documents/`, `images/`, PDFs, Fax-Dokumente und `settings.xml` lesen → in das neue Datenmodell überführen.
- RTF-Inhalte inklusive eingebetteter Bilder werden in das neue Inhaltsformat konvertiert (siehe 6.2).
- Abschlussbericht mit Soll/Ist-Anzahlen je Objektart; bei Abweichung wird nichts übernommen.
- Die Altdaten werden nie verändert.

### F13 – Einstellungen und Oberflächenzustand · M

- Fenstergröße/-position, zuletzt geöffneter Bereich, Bereichsreihenfolge, Spaltenbreiten der Trefferlisten, Splitterpositionen.
- Splitterregel: Gespeichert wird die **Wunschposition**; nur echte Benutzerbewegungen speichern; bei zu kleinem Fenster nur temporär begrenzen.
- Rechtschreibprüfung an/aus und Sprache, Mailprogramm, Wählmethode, AnyDesk-Pfad, TI-Monitor-Optionen.
- Neue Einstellungen haben immer sinnvolle Standardwerte; alte Einstellungsdateien bleiben lesbar.

### F14 – Sonstiges · K

- Hell-/Dunkel-Design.
- Tooltips für erklärungsbedürftige Bedienelemente (nicht in offensichtlichen Textfeldern, nicht über anderen Dialogen).
- Startbildschirm mit Fortschritt, falls der Start spürbar dauert.

---

1. Nicht-funktionale Anforderungen

| Thema | Anforderung |
| --- | --- |
| Plattform | Windows 10 22H2 / Windows 11, x64 |
| Laufzeit | .NET 10 (LTS), als self-contained Single-File oder mit Velopack-Installer verteilt |
| Start | < 3 s bis zur bedienbaren Oberfläche bei ~100 Einträgen / ~1.000 Bildern |
| Suche | Textsuche < 200 ms, Bildsuche < 2 s bei 2.000 Bildern |
| Robustheit | Keine leeren `catch`-Blöcke; jeder Fehler wird geloggt; Fehler in Integrationen (TI, Telefonie, Fernwartung) dürfen die Oberfläche nie blockieren |
| Datenablage | `%LOCALAPPDATA%\PixFix\` (einstellbar), getrennt vom Programmordner; keine Pfade > 260 Zeichen erforderlich |
| Datenschutz | Kundendaten (Telefon, E-Mail, Notizen) nur lokal; Sicherungen optional verschlüsselt (offene Frage); Logs enthalten keine Kundendaten |
| Logging | Datei-Log mit Rotation unter `%LOCALAPPDATA%\PixFix\logs` |
| Tests | Unit-Tests für Kernlogik (Suche, Scoring, Vorlagen, Import, Migration); Vergleichstests Bildsuche gegen PixFix 69 |
| Code | Lesbare Formatierung (`.editorconfig`, `dotnet format`), Nullable aktiv, Warnungen als Fehler |
| Versionierung | Git, SemVer (`1.0.0`), Changelog statt Versionsordner |

---

1. Architektur

```text
src/
  PixFix.Core            Domänenmodelle, Fachlogik, Interfaces (keine UI-, keine Datei-/DB-Abhängigkeit)
  PixFix.Infrastructure  SQLite (Microsoft.Data.Sqlite + FTS5), Dateiablage, OCR (Windows.Media.Ocr),
                         Telefonie (dialer/TAPI), Fernwartung, TI-API, Sicherung, Legacy-Import
  PixFix.App             WPF-Oberfläche (MVVM mit CommunityToolkit.Mvvm), Start, DI, Logging
tests/
  PixFix.Core.Tests
  PixFix.Infrastructure.Tests
```

Abhängigkeitsrichtung: `App → Infrastructure → Core`, `App → Core`. Core kennt keine Infrastruktur.

Wichtige Entscheidungen:

- **WPF statt WinForms**: `RichTextBox`/`FlowDocument` mit eingebauter Rechtschreibprüfung, Datenbindung, sauberes Layout ohne Resize-Sonderlogik.
- **Microsoft.Data.Sqlite** statt eigener P/Invoke-Schicht auf `winsqlite3.dll`.
- **Windows-OCR direkt über WinRT** (`TargetFramework net10.0-windows10.0.19041.0`) statt PowerShell-Prozess je Bild.
- **Textsuche im Speicher** mit der unveränderten Gewichtung aus PixFix 69 (bei einigen hundert Einträgen schnell genug).
  FTS5 kommt erst dazu, wenn die Datenmenge es erfordert.
- **Eine** Datenquelle zur Laufzeit (SQLite). Abgeleitete Daten (Bildsignaturen, Suchindex) liegen in eigenen Tabellen und sind jederzeit neu berechenbar.

---

1. Datenmodell

### 6.1 Tabellen (Entwurf)

| Tabelle | Inhalt | Herkunft PixFix 69 |
| --- | --- | --- |
| `knowledge_sections` | Abschnitte des PixFix-Bereichs (Name, Reihenfolge) | `fix_entries.category`, `category_order` |
| `knowledge_entries` | Einträge: Titel, Abschnitt, Tags, Inhalt, Favorit/Geschützt/Archiviert, Zeitstempel, Reihenfolge | `fix_entries` |
| `assets` | Bilder/PDFs: Id, relativer Pfad, SHA-256, Größe, MIME-Typ | `entry_images`, `information_pdf_attachments` |
| `knowledge_entry_images` | Zuordnung Eintrag ↔ Bild-Asset inkl. Reihenfolge | `entry_images` |
| `workspaces`, `information_entries`, `information_attachments` | Schreibbereiche, Reiter, PDF-Anhänge | `writing_workspaces`, `information_entries`, `information_pdf_attachments` |
| `copy_actions` | Kopier-Buttons je Eintrag/Reiter | Textfeld `copy_actions` |
| `calls` | Anrufe inkl. Erinnerung und Abrechnung | `calls` |
| `templates`, `cobra_quick_texts`, `fax_documents` | Vorlagen (Art in `kind`), Cobra-Schnelltexte, Fax-Dokumente | `settings.xml` |
| `trash` | gelöschte Einträge/Anrufe als JSON-Snapshot | `deleted_fix_entries`, `deleted_calls` |
| `settings` | Einstellungen als Schlüssel/Wert | `settings.xml` |
| `import_log` | Protokoll der Importe | neu |
| `image_signatures` | Bildsuch-Cache (Phase 3) | `search-index/image-index.xml` |
| `search_fts` | FTS5-Index (Phase 2) | neu |

Zeitstempel als UTC-ISO-8601, IDs als GUID-Text (IDs aus PixFix 69 bleiben erhalten). Schemaversion in `PRAGMA user_version`;
Änderungen nur über neue, nummerierte Migrationen in `Schema.cs`.

### 6.2 Inhaltsformat

PixFix 69 führt parallel RTF, HTML, `native_doc`, `legacy_rtf` und `content-v69`. Neu gibt es **genau ein**
gespeichertes Format: **FlowDocument-XAML**, in dem Bilder nur als Referenz (`pixfix-asset:<id>`) stehen;
die Bilddateien liegen im Asset-Ordner. Vorteile: diffbar, durchsuchbar, keine OLE-Objekte, Bilder dedupliziert.
Der Klartext für die Suche wird beim Speichern abgeleitet. RTF dient nur noch als Import-/Exportformat
(Zwischenablage, Legacy-Import).

**Übergang:** Der Import (Phase 1) übernimmt Inhalte unverändert als RTF (`content_format = 'rtf'`, große Dokumente als
Asset-Datei) bzw. HTML. Die Umwandlung nach XAML erfolgt in Phase 2 mit dem WPF-Editor beim ersten Öffnen eines Eintrags;
die `[[PFIMG:…]]`-Marker im RTF ordnen dabei die eingebetteten Bilder den Asset-Dateien zu.

**Anzeige übernommener Dokumente (Phase 2a):** WPF zeigt die von PixFix 69 als `\wmetafile8` eingebetteten Bilder
nicht zuverlässig an. Vor der Anzeige werden sie ersetzt – mit Marker durch die Originaldatei aus der Ablage, ohne Marker
durch das im Metafile enthaltene Bitmap als PNG. Geprüft am echten Bestand: 662 Bilder, davon 542 über Marker und 120
über das Metafile, keins ohne Ergebnis. Das Ergebnis liegt als Cache unter `cache/documents/`; das Original bleibt unverändert.

---

1. Phasenplan

| Phase | Inhalt | Ergebnis |
| --- | --- | --- |
| 0 | Projektgerüst, CI-Build, Logging, Einstellungen, Datenordner | startbare leere App |
| 1 | Datenmodell + Legacy-Import (F12) | Altbestand vollständig gelesen, Testbericht – **umgesetzt** |
| 2a | Wissensdatenbank anzeigen + Textsuche (F1 lesend, F2-Text) | Mitarbeiter kann nachschlagen – **umgesetzt** |
| 2b | Editor: Einträge anlegen/bearbeiten, Bilder einfügen, Papierkorb, Umwandlung nach XAML (F1) | **umgesetzt** |
| 2c | Copy-Actions, Eintragsvorlagen, Abschnitte umbenennen/sortieren, Einträge sortieren (F1) | **umgesetzt** (Sortieren per ↑/↓ statt Drag & Drop) |
| 3 | Bildsuche + OCR (F2-Bild) mit Vergleichstests | Trefferqualität = PixFix 69 – **umgesetzt** |
| 4 | Schreibbereiche (F3) | **umgesetzt** (Papierkorb für Reiter: nur Abbild, Wiederherstellen offen) |
| 5 | Anrufe, Erinnerungen, Vorlagen, Telefonie (F4–F7) | Parallelbetrieb im Alltag möglich – **umgesetzt** (offen: Schnelltextbausteine im Editor einfügen) |
| 6 | Sicherung/Import (F11), Fernwartung, TI-Monitor (F8, F9) | **Version 1.0**, Ablösung PixFix 69 – **umgesetzt** (6a/6b Sicherung und Wiederherstellung 4.0/3.0/2.1, 6c Fernwartung und TI-Monitor) |
| 7 | Cobra-Anbindung (F10) | nach Entscheidung |

Bis Phase 6 läuft PixFix 69 produktiv weiter; die neue Version liest dessen Daten nur.

---

1. Offene Fragen

1. Cobra-Anbindung: Datenbank direkt, über py-connect oder UI-Automatisierung? (vertagt)
1. Wird der OneNote-/DOCX-Import noch gebraucht?
1. ~~Sollen Sicherungen verschlüsselt werden?~~ Entschieden: nein; Sicherungsordner einstellbar, Standard lokal.
1. Soll PixFix später von mehreren Hotline-Mitarbeitern genutzt werden – jeweils eigene Daten oder ein gemeinsamer Wissensbestand?
1. Welche Fax-/Mail-Wege werden tatsächlich genutzt (Outlook, Standard-Mailprogramm, Faxdrucker)?
1. Ist die Anrufen-Funktion über `tel:`/Unify in allen Hotline-Rechnern gleich eingerichtet?

1. Bewusst nicht übernommen

- Kompilieren auf dem Zielrechner per `csc.exe` und `PixFix starten.bat`.
- Überschriebenes `BeginInvoke` mit Warteschlange, leere `catch`-Blöcke als Absturzschutz.
- Parallele Inhaltsformate und `content-v69`.
- Eigene SQLite-P/Invoke-Schicht und PowerShell-OCR.
- Versionsordner und `*-Pruefung.json` statt Git.
- Sonderlogik für WinForms-Splitter, Resize-Schleifen und RichEdit-/OLE-Verzögerungen – entfällt mit WPF; die **fachliche** Regel (Wunschposition merken) bleibt.
