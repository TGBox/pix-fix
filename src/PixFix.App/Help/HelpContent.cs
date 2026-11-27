namespace PixFix.App.Help;

/// <summary>Ein Kapitel im Bereich „Hilfe“. <paramref name="SectionIndex"/> = Bereich der Navigation (0-basiert) oder <c>null</c>.</summary>
public sealed record HelpTopic(string Icon, string Title, string Summary, IReadOnlyList<HelpSection> Sections, int? SectionIndex)
{
    public bool HasSection => SectionIndex is not null;
}

/// <summary>Zwischenüberschrift mit Stichpunkten.</summary>
public sealed record HelpSection(string Heading, IReadOnlyList<string> Points);

/// <summary>Ein Schritt der Einführungstour; <paramref name="SectionIndex"/> wird dabei im Hauptfenster angezeigt.</summary>
public sealed record TourStep(string Title, string Text, int? SectionIndex);

/// <summary>
/// Texte für Hilfe und Einführungstour. Bei neuen oder geänderten Funktionen hier mitpflegen – die Beschriftungen in
/// Anführungszeichen müssen zu den Schaltflächen in der Oberfläche passen.
/// </summary>
public static class HelpContent
{
    // Reihenfolge der Bereiche in MainViewModel.Sections.
    public const int Knowledge = 0;
    public const int Workspaces = 1;
    public const int Calls = 2;
    public const int Ti = 3;
    public const int Settings = 4;
    public const int Help = 5;

    /// <summary>Übersicht der Tastenkürzel (Taste, Wirkung) – auch in Einstellungen → Allgemein.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Shortcuts { get; } =
    [
        new("Strg+Alt+P", "Überall in Windows: PixFix nach vorn, Bildsuche mit dem Screenshot aus der Zwischenablage"),
        new("Strg+1 … Strg+6", "Bereich wechseln (Wissensdatenbank, Schreibbereiche, Anrufe, TI-Monitor, Einstellungen, Hilfe)"),
        new("F1", "Hilfe öffnen"),
        new("Strg+F", "Suchfeld des Bereichs (bei Anrufen: Anrufliste)"),
        new("Strg+N", "Neuer Eintrag, Reiter bzw. Anruf"),
        new("Entf", "Ausgewählten Eintrag, Reiter bzw. Anruf in den Papierkorb (in der Liste)"),
        new("Alt + unterstrichener Buchstabe", "Schaltfläche auslösen, z. B. Alt+A = Anrufen, Alt+L = Auflegen"),
        new("Strg+Umschalt+B", "Bildsuche (Wissensdatenbank)"),
        new("Alt+↑ / Alt+↓", "Eintrag im Abschnitt verschieben (Wissensdatenbank)"),
        new("Strg+S", "Editor sofort speichern"),
        new("Strg+Z / Strg+Y", "Rückgängig / Wiederholen im Editor"),
        new("F3 / Umschalt+F3", "Nächste / vorige Fundstelle im Editor"),
        new("Strg+V", "Screenshot einfügen (Editor, Bildsuche)"),
        new("Esc", "Suche leeren"),
    ];

    public static IReadOnlyList<HelpTopic> Topics { get; } =
    [
        new(
            "",
            "Erste Schritte",
            "PixFix ist dein Arbeitswerkzeug für die Data-Cur-Hotline: Fehlerbilder nachschlagen, Anrufe festhalten, "
            + "Rückrufe nicht vergessen und die TI im Blick behalten – alles in einem Fenster.",
            [
                new("So ist PixFix aufgebaut", [
                    "Links ist die Navigation mit den Bereichen Wissensdatenbank, Schreibbereiche, Anrufe, TI-Monitor, "
                    + "Einstellungen und Hilfe. Mit Strg+1 bis Strg+6 wechselst du direkt.",
                    "Die Schaltfläche ☰ oben links klappt die Navigation auf Symbole zusammen.",
                    "Fast alle Schaltflächen erklären sich per Tooltip, wenn du kurz mit der Maus darauf bleibst.",
                ]),
                new("PixFix läuft im Hintergrund weiter", [
                    "Das Schließen-Kreuz beendet PixFix nicht, sondern legt es in den Infobereich (unten rechts neben der Uhr). "
                    + "So bleiben Rückruf-Erinnerungen, TI-Monitor und automatische Sicherung aktiv.",
                    "Wirklich beenden: „PixFix beenden“ unten in der Navigation oder Rechtsklick auf das PixFix-Symbol im "
                    + "Infobereich → „PixFix beenden“.",
                    "Strg+Alt+P holt PixFix aus jedem Programm nach vorn und startet die Bildsuche.",
                ]),
                new("Deine Daten", [
                    "Alle Daten liegen nur auf deinem Rechner unter %LOCALAPPDATA%\\PixFix – unten in der Navigation steht der Ordner.",
                    "PixFix sichert sich automatisch einmal am Tag (Einstellungen → Sicherung).",
                    "Gelöschtes landet im Papierkorb des jeweiligen Bereichs und lässt sich wiederherstellen.",
                ]),
            ],
            null),
        new(
            "",
            "Wissensdatenbank",
            "Gesammelte Fehlerbilder mit Screenshot, Beschreibung und Lösung. Hier schaust du nach, wenn ein Kunde mit einer "
            + "Fehlermeldung anruft.",
            [
                new("Suchen", [
                    "Einfach ins Feld „Suchen …“ tippen (Strg+F). Gesucht wird in Titel, Abschnitt, Tags, im Text der Bilder "
                    + "(OCR) und im Inhalt. Titel und Tags zählen am meisten.",
                    "Treffer anklicken öffnet den Eintrag, die Trefferliste bleibt stehen. Fundstellen sind markiert – mit F3 und "
                    + "Umschalt+F3 springst du durch.",
                    "Esc leert die Suche. Ohne Suchbegriff sind die Einträge nach Abschnitten gruppiert.",
                    "Archivierte Einträge erscheinen nur, wenn „Archiv“ eingeschaltet ist.",
                ]),
                new("Einträge anlegen und bearbeiten", [
                    "„+ Neu ▾“ (Strg+N) legt einen Eintrag an – leer oder aus einer Eintragsvorlage.",
                    "Titel, Abschnitt und Tags stehen oben. Tags sind Stichwörter für die Suche, z. B. Fehlernummern oder Programmnamen.",
                    "Screenshots fügst du mit Strg+V ein oder ziehst Bilddateien hinein. Der Text im Bild wird automatisch "
                    + "erkannt und ist danach durchsuchbar.",
                    "Gespeichert wird automatisch (Strg+S speichert sofort).",
                    "★ markiert Favoriten, 🔒 schützt einen Eintrag vor Änderungen und Löschen.",
                    "Mit ↑/↓ bzw. Alt+↑/Alt+↓ sortierst du Einträge im Abschnitt, „Abschnitte …“ benennt Abschnitte um und sortiert sie.",
                ]),
                new("Editor-Werkzeuge", [
                    "„Kopier-Button“: Text markieren (z. B. einen Pfad oder Befehl) und daraus einen Button machen – "
                    + "ein Klick kopiert den Text in die Zwischenablage.",
                    "„Bausteine ▾“: häufige Lösungstexte an der Cursorposition einfügen oder markierten Text als Baustein speichern.",
                    "Die deutsche Rechtschreibprüfung unterstreicht Tippfehler rot.",
                ]),
            ],
            Knowledge),
        new(
            "",
            "Bildsuche",
            "Der Kunde schickt einen Screenshot oder du siehst die Meldung per Fernwartung? Die Bildsuche findet ähnliche "
            + "gespeicherte Fehlerbilder – auch wenn du nicht weißt, wonach du tippen sollst.",
            [
                new("So geht's am schnellsten", [
                    "Screenshot machen: Windows+Umschalt+S und den Bereich mit der Fehlermeldung aufziehen.",
                    "Strg+Alt+P drücken – egal in welchem Programm du gerade bist. PixFix kommt nach vorn und sucht sofort.",
                    "Alternativ in der Wissensdatenbank „Bildsuche …“ (Strg+Umschalt+B), dann Strg+V oder eine Bilddatei hineinziehen.",
                ]),
                new("Treffer auswerten", [
                    "Ein Klick auf einen Treffer zeigt die Vorschau, Doppelklick oder Enter öffnet den Eintrag und springt zum Bild.",
                    "Bewertet werden Ähnlichkeit des Bildes und der darin erkannte Text, z. B. Fehlercodes.",
                    "Unter „Erkannter Bildtext“ siehst du, was PixFix im Screenshot gelesen hat.",
                ]),
            ],
            Knowledge),
        new(
            "",
            "Schreibbereiche",
            "Freie Notizbücher für alles, was kein Fehlerbild ist: Anleitungen, aktuelle Störungen, Werkzeuge, Textvorlagen "
            + "– jeweils mit Abschnitten, Reitern und PDF-Anhängen.",
            [
                new("Bereiche und Reiter", [
                    "Oben wählst du den Bereich. Über „…“ legst du Bereiche an, benennst sie um, sortierst und schützt sie.",
                    "„+ Reiter“ (Strg+N) legt eine neue Seite im Abschnitt des ausgewählten Reiters an. Einen neuen Abschnitt "
                    + "erzeugst du, indem du im Feld „Abschnitt“ einen neuen Namen eintippst.",
                    "Reiter und Abschnitte sortierst du mit ↑/↓ bzw. „Abschnitt ↑“/„Abschnitt ↓“.",
                    "„Im Bereich suchen …“ durchsucht alle Reiter des Bereichs.",
                ]),
                new("PDFs und Weitergeben", [
                    "„PDF anhängen …“ oder PDF-Dateien aus dem Explorer auf den Kopf des Reiters ziehen. Ein Klick öffnet das PDF.",
                    "„Per Mail …“ öffnet den Reiter als Outlook-Entwurf – mit Formatierung, Bildern und den PDF-Anhängen.",
                ]),
            ],
            Workspaces),
        new(
            "",
            "Anrufe und Rückrufe",
            "Für jeden Anruf eine Anrufkarte: Kundendaten, Grund, Lösung, Status – und eine Erinnerung, damit kein Rückruf "
            + "verloren geht.",
            [
                new("Einen Anruf festhalten", [
                    "„+ Neuer Anruf“ (Strg+N) legt eine Anrufkarte an. Trage L-Nummer, ID und die Telefonnummern ein.",
                    "„Warum wird angerufen?“ und „Lösung / Was wurde gemacht?“ sind formatierte Felder – Screenshots sind erlaubt. "
                    + "Über „Vorlage ▾“ fügst du häufige Gründe und Lösungen ein.",
                    "„Cobra-Text kopieren“ legt „Grund --> Lösung“ in die Zwischenablage, damit du ihn in Cobra einfügen kannst.",
                    "Erledigte Anrufe sind ausgeblendet, „Erledigte anzeigen“ blendet sie ein.",
                ]),
                new("Status und Rückruf-Erinnerung", [
                    "Status: Noch zu bearbeiten → In Bearbeitung → Später/Morgen erneut anrufen → Erledigt.",
                    "Bei „Später erneut anrufen“ und „Morgen erneut anrufen“ fragt PixFix, wann du erinnert werden willst.",
                    "Ist der Rückruf fällig, erscheint ein Erinnerungsfenster und die Taskleiste blinkt. „OK“ setzt den Anruf auf "
                    + "„In Bearbeitung“, „Erneut erinnern“ verschiebt die Erinnerung.",
                    "Die Erinnerung kommt auch, wenn PixFix im Infobereich liegt – deshalb PixFix nicht beenden. Stehen heute "
                    + "noch Rückrufe an, warnt PixFix vor dem Beenden.",
                ]),
                new("Telefonieren, Fernwartung, E-Mail", [
                    "„📞 Anrufen“ wählt die Nummer aus „Rückruf an“, „Auflegen“ beendet das aktive Gespräch.",
                    "„AnyDesk“ und „TeamViewer“ öffnen die Fernwartung bzw. holen sie nach vorn.",
                    "„✉ E-Mail“ und „Fax“ erzeugen Nachrichten aus deinen Vorlagen, die Kundendaten werden eingesetzt.",
                ]),
                new("Kostenpflichtiger Support", [
                    "„Kostenpflichtig“ anhaken und Start- und Endzeit eintragen („Jetzt“ setzt die aktuelle Uhrzeit).",
                    "Beim Wechsel auf „Erledigt“ öffnet PixFix die Rechnungs-Mail, falls sie noch nicht versendet wurde. "
                    + "Manuell geht das über „Rechnungs-Mail“.",
                ]),
            ],
            Calls),
        new(
            "",
            "TI-Monitor",
            "Zeigt, ob die Telematikinfrastruktur gerade gestört ist (E-Rezept, ePA, KIM, VSDM, TI-Anschluss …). Praktisch, "
            + "wenn plötzlich viele Praxen mit demselben Problem anrufen.",
            [
                new("Farben", [
                    "Grün: keine aktive Störung. Rot: aktive Störung. Orange: eine Abfrage war nicht vollständig.",
                    "Das TI-Feld unten in der Navigation ist immer sichtbar und blinkt rot bei einer neuen Störung. "
                    + "Ein Klick öffnet den Monitor und beendet das Blinken.",
                ]),
                new("Bedienung", [
                    "PixFix prüft automatisch alle 5 Minuten, „Jetzt prüfen“ sofort.",
                    "„TI-Status öffnen“ und „TI-Lagebild öffnen“ zeigen die Originalseiten der gematik.",
                ]),
            ],
            Ti),
        new(
            "",
            "Einstellungen und Sicherung",
            "Hier richtest du PixFix für deinen Arbeitsplatz ein und kümmerst dich um Sicherungen.",
            [
                new("Einmal einrichten", [
                    "Allgemein: Farbschema (hell, dunkel, wie Windows), Verhalten beim Schließen, Strg+Alt+P.",
                    "Anrufe: wie Nummern gewählt werden (z. B. Unify Phone über tel:), welches E-Mail-Programm genutzt wird, "
                    + "Empfänger und Text der Rechnungs-Mail.",
                    "„Vorlagen, Cobra-Schnelltexte und Fax-Dokumente bearbeiten …“ pflegt alle Textvorlagen.",
                ]),
                new("Sicherung", [
                    "Automatisch: eine Sicherung pro Tag, nur wenn du PixFix gerade nicht benutzt; 7 Tage werden aufbewahrt.",
                    "„Jetzt sichern“ bzw. „Sichern unter …“ erstellt sofort eine Sicherung.",
                    "Tipp: Als Sicherungsordner einen OneDrive-Ordner wählen – dann sind die Daten auch bei einem Rechnerausfall sicher.",
                    "„Sicherung wiederherstellen …“ prüft die Sicherung vollständig und sichert vorher den aktuellen Stand.",
                ]),
                new("Neuer Rechner oder neue Kollegin", [
                    "Auf dem alten Rechner „Jetzt sichern“, die .pixfix-Datei mitnehmen und auf dem neuen Rechner "
                    + "„Sicherung wiederherstellen …“ wählen.",
                    "Daten aus dem alten PixFix 69 übernimmt der Reiter „Import aus PixFix 69“.",
                ]),
            ],
            Settings),
        new(
            "",
            "Updates",
            "PixFix sucht beim Start und alle 6 Stunden nach einer neuen Version und zeigt dann oben im Fenster einen Hinweis.",
            [
                new("Neue Version installieren", [
                    "Im Hinweis auf „Herunterladen“ klicken – der Browser lädt die neue PixFix.exe herunter.",
                    "PixFix beenden: „PixFix beenden“ unten in der Navigation (oder über das Symbol im Infobereich).",
                    "„Programmordner öffnen“ zeigt, wo deine PixFix.exe liegt. Ersetze sie durch die heruntergeladene Datei "
                    + "und starte PixFix neu.",
                    "Deine Daten liegen getrennt vom Programm und bleiben beim Update unverändert.",
                    "„Was ist neu?“ zeigt die Änderungen der neuen Version.",
                ]),
                new("Einstellungen", [
                    "Einstellungen → Allgemein → Updates zeigt deine Version und sucht auf Wunsch sofort nach Updates.",
                    "Die automatische Suche lässt sich dort abschalten.",
                ]),
            ],
            Settings),
        new(
            "\uE765",
            "Tastenkürzel",
            "Mit diesen Tasten geht die tägliche Arbeit schneller. Die Liste steht auch unter Einstellungen → Allgemein.",
            [new("Übersicht", [.. Shortcuts.Select(s => s.Key + ":  " + s.Value)])],
            null),
    ];

    public static IReadOnlyList<TourStep> Tour { get; } =
    [
        new(
            "Willkommen bei PixFix",
            "In ein paar Schritten zeigen wir dir die wichtigsten Bereiche. Die Tour kannst du jederzeit beenden und "
            + "später unter Hilfe → „Einführungstour starten“ wiederholen.",
            null),
        new(
            "Wissensdatenbank",
            "Hier sind die bekannten Fehlerbilder mit Lösung gesammelt. Tippe oben ins Suchfeld – gesucht wird auch im Text "
            + "der Screenshots. Mit „+ Neu“ legst du selbst Einträge an, Screenshots fügst du mit Strg+V ein.",
            Knowledge),
        new(
            "Bildsuche mit Strg+Alt+P",
            "Der schnellste Weg zur Lösung: Fehlermeldung mit Windows+Umschalt+S ausschneiden und Strg+Alt+P drücken. "
            + "PixFix kommt nach vorn und zeigt die ähnlichsten gespeicherten Fehlerbilder.",
            Knowledge),
        new(
            "Schreibbereiche",
            "Notizbücher für Anleitungen, aktuelle Fehler und Werkzeuge – mit Abschnitten, Reitern und PDF-Anhängen. "
            + "Über „…“ legst du eigene Bereiche an.",
            Workspaces),
        new(
            "Anrufe",
            "Für jeden Anruf eine Anrufkarte mit Kundendaten, Grund und Lösung. Setzt du den Status auf „Später erneut "
            + "anrufen“, erinnert dich PixFix rechtzeitig an den Rückruf. Von hier aus rufst du auch an und startest AnyDesk.",
            Calls),
        new(
            "TI-Monitor",
            "Zeigt Störungen der Telematikinfrastruktur. Das TI-Feld unten links blinkt rot, sobald eine neue Störung gemeldet wird.",
            Ti),
        new(
            "Einstellungen",
            "Richte hier einmal Telefonie und E-Mail-Programm ein. Unter „Sicherung“ kannst du einen OneDrive-Ordner für "
            + "die automatischen Sicherungen wählen.",
            Settings),
        new(
            "Im Hintergrund und immer aktuell",
            "Das Schließen-Kreuz legt PixFix in den Infobereich, damit Erinnerungen weiterlaufen – ganz beenden geht über "
            + "„PixFix beenden“ unten links. Gibt es eine neue Version, "
            + "erscheint oben im Fenster ein Hinweis mit Download-Link.",
            null),
        new(
            "Hilfe",
            "Alles noch einmal zum Nachlesen findest du hier im Bereich Hilfe – jederzeit mit F1. Viel Erfolg!",
            Help),
    ];
}
