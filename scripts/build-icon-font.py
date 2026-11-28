"""Erzeugt die Symbolschrift von PixFix aus Fluent UI System Icons (Microsoft, MIT-Lizenz).

Ergebnis:
  src/PixFix.App/Assets/Fonts/PixFixIcons.ttf   nur die unten aufgeführten Symbole (wenige KB statt ~3 MB)
  src/PixFix.App/Themes/Icons.xaml              Ressourcen "Icon.<Name>" mit dem jeweiligen Zeichen (für XAML)
  src/PixFix.App/Common/IconGlyphs.cs           dieselben Zeichen als Konstanten (für C#, z. B. Navigation und Hilfe)

Voraussetzungen (einmalig):
  1. Aus https://github.com/microsoft/fluentui-system-icons/tree/main/fonts die Dateien
     FluentSystemIcons-Regular.ttf und FluentSystemIcons-Regular.json nach assets/fonts/ legen
     (beide sind per .gitignore vom Repository ausgeschlossen).
  2. pip install fonttools

Aufruf aus dem Repository-Wurzelordner:  python scripts/build-icon-font.py

Neues Symbol: Zeile in ICONS ergänzen (Namen siehe JSON-Datei, ohne "ic_fluent_" und "_24_regular"),
Skript ausführen, alle drei erzeugten Dateien committen.
"""

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SOURCE_FONT = ROOT / "assets" / "fonts" / "FluentSystemIcons-Regular.ttf"
SOURCE_MAP = ROOT / "assets" / "fonts" / "FluentSystemIcons-Regular.json"
TARGET_FONT = ROOT / "src" / "PixFix.App" / "Assets" / "Fonts" / "PixFixIcons.ttf"
TARGET_XAML = ROOT / "src" / "PixFix.App" / "Themes" / "Icons.xaml"
TARGET_CS = ROOT / "src" / "PixFix.App" / "Common" / "IconGlyphs.cs"

# Schlüssel in PixFix -> Name in Fluent UI System Icons (Größe 24, Stil Regular)
ICONS = {
    # Allgemeine Aktionen
    "Add": "add",
    "Delete": "delete",
    "Trash": "bin_recycle",
    "MoveUp": "arrow_up",
    "MoveDown": "arrow_down",
    "Edit": "edit_settings",
    "Rename": "rename",
    "Copy": "copy",
    "Open": "open",
    "Refresh": "arrow_sync",
    "Search": "document_search",
    "Favorite": "star",
    "Protected": "lock_closed",
    "Archive": "archive",
    "Sections": "text_bullet_list_square_edit",
    "Download": "arrow_download",
    "Folder": "folder_open",
    "Dismiss": "dismiss",
    "Clock": "clock",
    "Reminder": "clock_alarm",
    "Template": "document_text",
    # Wissensdatenbank und Schreibbereiche
    "NewEntry": "document_add",
    "ImageSearch": "image_search",
    "NewTab": "tab_add",
    "Mail": "mail",
    "Attach": "attach",
    # Anrufe
    "NewCall": "call_add",
    "Call": "call",
    "HangUp": "call_end",
    "Fax": "print",
    "Invoice": "receipt",
    "QuickTexts": "text_quote",
    # Editor
    "Bold": "text_bold",
    "Italic": "text_italic",
    "Underline": "text_underline",
    "FontLarger": "font_increase",
    "FontSmaller": "font_decrease",
    "Bullets": "text_bullet_list_ltr",
    "Numbering": "text_number_list_ltr",
    "Undo": "arrow_undo",
    "Redo": "arrow_redo",
    "InsertImage": "image_add",
    "CopyButton": "copy_add",
    "Snippets": "text_add",
    "PreviousHit": "chevron_up",
    "NextHit": "chevron_down",
    # Navigation und Hilfe
    "Menu": "navigation",
    "Knowledge": "book",
    "Workspaces": "notebook",
    "Calls": "call",
    "Ti": "heart_pulse",
    "Settings": "settings",
    "Help": "question_circle",
    "Exit": "power",
    "Home": "home",
    "Keyboard": "keyboard",
    "Update": "arrow_sync",
}


def codepoint(mapping: dict[str, int], name: str) -> int:
    for size in ("24", "20"):
        key = f"ic_fluent_{name}_{size}_regular"
        if key in mapping:
            return mapping[key]
    sys.exit(f"Symbol '{name}' nicht in {SOURCE_MAP.name} gefunden")


def main() -> None:
    from fontTools import subset

    mapping = json.loads(SOURCE_MAP.read_text(encoding="utf-8"))
    glyphs = {key: codepoint(mapping, name) for key, name in ICONS.items()}

    TARGET_FONT.parent.mkdir(parents=True, exist_ok=True)
    options = subset.Options()
    options.layout_features = []
    options.name_IDs = ["*"]
    options.notdef_outline = True
    font = subset.load_font(str(SOURCE_FONT), options)
    subsetter = subset.Subsetter(options)
    subsetter.populate(unicodes=sorted(set(glyphs.values())))
    subsetter.subset(font)
    subset.save_font(font, str(TARGET_FONT), options)

    lines = [
        '<!-- Erzeugt von scripts/build-icon-font.py - nicht von Hand ändern. -->',
        '<!-- Symbole: Fluent UI System Icons, Copyright (c) Microsoft Corporation, MIT-Lizenz (THIRD-PARTY-NOTICES.md). -->',
        '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"',
        '                    xmlns:sys="clr-namespace:System;assembly=mscorlib">',
        '  <FontFamily x:Key="AppIconFont">pack://application:,,,/Assets/Fonts/#FluentSystemIcons-Regular</FontFamily>',
    ]
    for key, name in ICONS.items():
        lines.append(f'  <sys:String x:Key="Icon.{key}">&#x{glyphs[key]:X};</sys:String> <!-- {name} -->')
    lines.append("</ResourceDictionary>")
    TARGET_XAML.write_text("\r\n".join(lines) + "\r\n", encoding="utf-8")

    cs = [
        "// <auto-generated>Erzeugt von scripts/build-icon-font.py - nicht von Hand ändern.</auto-generated>",
        "namespace PixFix.App.Common;",
        "",
        "/// <summary>Zeichen der Symbolschrift (Fluent UI System Icons) für C#; XAML nutzt die Ressourcen Icon.*.</summary>",
        "public static class IconGlyphs",
        "{",
    ]
    for key in ICONS:
        cp = glyphs[key]
        literal = f"\\u{cp:04X}" if cp <= 0xFFFF else f"\\U{cp:08X}"
        cs.append(f'    public const string {key} = "{literal}";')
    cs.append("}")
    TARGET_CS.write_text("\r\n".join(cs) + "\r\n", encoding="utf-8")
    print(f"{len(ICONS)} Symbole -> {TARGET_FONT.relative_to(ROOT)} ({TARGET_FONT.stat().st_size} Bytes)")


if __name__ == "__main__":
    main()
