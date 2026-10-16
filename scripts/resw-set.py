#!/usr/bin/env python3
"""Texte in src/RoG.App/Strings/{de-DE,en-US,fr-FR}/Resources.resw setzen oder entfernen.

    scripts/resw-set.py texte.json

texte.json: {"set": {"Schlüssel": ["Deutsch", "English", "Français"], …}, "remove": ["Schlüssel", …]}
Vorhandene Schlüssel werden überschrieben, neue angehängt, die Datei bleibt sonst unverändert (Reihenfolge, Kopf).
Die Vollständigkeit prüft danach `dotnet test` (ReswCompletenessTests).
"""
import json
import re
import sys
from pathlib import Path
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parent.parent
LANGS = ["de-DE", "en-US", "fr-FR"]

spec = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
values = spec.get("set", {})
remove = set(spec.get("remove", []))
for key, texts in values.items():
    if len(texts) != 3 or any(not t.strip() for t in texts):
        sys.exit(f"{key}: genau drei nicht leere Texte (DE, EN, FR) nötig")

for i, lang in enumerate(LANGS):
    path = ROOT / "src" / "RoG.App" / "Strings" / lang / "Resources.resw"
    src = path.read_text(encoding="utf-8")
    for key in remove:
        src = re.sub(rf'  <data name="{re.escape(key)}" xml:space="preserve">\s*<value>.*?</value>\s*</data>\n', "", src, flags=re.S)
    for key, texts in values.items():
        entry = f'  <data name="{escape(key)}" xml:space="preserve">\n    <value>{escape(texts[i])}</value>\n  </data>\n'
        pattern = rf'  <data name="{re.escape(escape(key))}" xml:space="preserve">\s*<value>.*?</value>\s*</data>\n'
        if re.search(pattern, src, flags=re.S):
            src = re.sub(pattern, lambda _: entry, src, count=1, flags=re.S)
        else:
            src = src.replace("</root>", entry + "</root>")
    path.write_text(src, encoding="utf-8")

print(f"resw: {len(values)} gesetzt, {len(remove)} entfernt")
