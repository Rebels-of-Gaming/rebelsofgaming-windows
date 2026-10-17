#!/usr/bin/env python3
"""
Prüft die XAML-Dateien der App auf fehlende Namen für Screenreader (Narrator):
- Schaltflächen (Button, HyperlinkButton, ToggleButton, RepeatButton, AppBarButton), deren Inhalt nur ein Symbol/Bild ist,
  brauchen x:Uid mit AutomationProperties.Name in der .resw oder AutomationProperties.Name direkt.
- Eingabefelder (TextBox, PasswordBox, ComboBox, NumberBox, AutoSuggestBox, CalendarDatePicker, TimePicker, DatePicker,
  ToggleSwitch, Slider) brauchen Header, AutomationProperties.Name oder eine x:Uid, die eines davon setzt.
- Image braucht AutomationProperties.Name oder AccessibilityView="Raw" (bzw. sitzt in einer benannten Schaltfläche).
Aufruf: scripts/check-a11y.py   (Exit 1 bei Funden)
"""
import glob, os, re, sys
import xml.etree.ElementTree as ET

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
APP = os.path.join(ROOT, "src", "RoG.App")
X = "{http://schemas.microsoft.com/winfx/2006/xaml}"
P = "{http://schemas.microsoft.com/winfx/2006/xaml/presentation}"

resw = open(os.path.join(APP, "Strings", "de-DE", "Resources.resw"), encoding="utf-8").read()
uid_props = {}
for m in re.finditer(r'<data name="([^".]+)\.([^"]+)"', resw):
    uid_props.setdefault(m.group(1), set()).add(m.group(2))

BUTTONS = {"Button", "HyperlinkButton", "ToggleButton", "RepeatButton", "AppBarButton", "AppBarToggleButton", "DropDownButton", "SplitButton"}
INPUTS = {"TextBox", "PasswordBox", "ComboBox", "NumberBox", "AutoSuggestBox", "CalendarDatePicker", "TimePicker", "DatePicker", "ToggleSwitch", "Slider", "RichEditBox"}
ICONS = {"FontIcon", "SymbolIcon", "Image", "PathIcon", "BitmapIcon", "Viewbox"}
AUTO_NAME = "AutomationProperties.Name"


def local(tag):
    return tag.split("}")[-1]


def attrs(el):
    return {local(k) if not k.startswith(X) else "x:" + local(k): v for k, v in el.attrib.items()}


def has_name(el):
    a = attrs(el)
    if any(k.endswith(AUTO_NAME) for k in a):
        return True
    uid = a.get("x:Uid")
    return bool(uid and any(p.endswith(AUTO_NAME) for p in uid_props.get(uid, ())))


TEXT_INPUTS = {"TextBox", "PasswordBox", "AutoSuggestBox", "NumberBox", "RichEditBox"}


def has_header(el):
    # Textfelder nehmen ohne Header den PlaceholderText als Namen (TextBoxAutomationPeer) – andere nicht
    ok = {"Header", "PlaceholderText"} if local(el.tag) in TEXT_INPUTS else {"Header"}
    a = attrs(el)
    if ok & set(a):
        return True
    uid = a.get("x:Uid")
    return bool(uid and uid_props.get(uid, set()) & ok)


def content_text(el):
    a = attrs(el)
    if "Content" in a:
        return True
    uid = a.get("x:Uid")
    if uid and "Content" in uid_props.get(uid, set()):
        return True
    return any(local(c.tag) in {"TextBlock", "StackPanel", "Grid"} and any(local(d.tag) == "TextBlock" for d in c.iter()) for c in el)


problems = []
for path in sorted(glob.glob(os.path.join(APP, "**", "*.xaml"), recursive=True)):
    if "/obj/" in path or "/bin/" in path or "Themes" in path:
        continue
    text = open(path, encoding="utf-8").read()
    try:
        tree = ET.fromstring(text)
    except ET.ParseError as e:
        problems.append(f"{path}: XML-Fehler {e}")
        continue
    lines = text.splitlines()
    parents = {c: p for p in tree.iter() for c in p}

    def where(el):
        # Zeile grob über das erste Attribut finden
        a = attrs(el)
        key = a.get("x:Uid") or a.get("x:Name") or a.get("Command") or a.get("Text") or a.get("Source") or ""
        for i, line in enumerate(lines, 1):
            if f"<{local(el.tag)}" in line and (not key or key in line or key in " ".join(lines[i - 1:i + 6])):
                return i
        return 0

    for el in tree.iter():
        name = local(el.tag)
        rel = os.path.relpath(path, ROOT)
        if name in BUTTONS:
            children = [local(c.tag) for c in el]
            icon_only = children and all(c in ICONS for c in children) and not content_text(el)
            if icon_only and not has_name(el) and "ToolTipService.ToolTip" not in "".join(attrs(el)):
                problems.append(f"{rel}:{where(el)}: {name} nur mit Symbol ohne AutomationProperties.Name")
        elif name in INPUTS:
            if not has_header(el) and not has_name(el):
                problems.append(f"{rel}:{where(el)}: {name} ohne Header/AutomationProperties.Name")
        elif name == "Image":
            a = attrs(el)
            raw = a.get("AutomationProperties.AccessibilityView") == "Raw"
            parent = parents.get(el)
            in_named_button = parent is not None and local(parent.tag) in BUTTONS and has_name(parent)
            if not (has_name(el) or raw or in_named_button):
                problems.append(f"{rel}:{where(el)}: Image ohne AutomationProperties.Name (oder AccessibilityView=Raw)")

for p in problems:
    print(p)
print(f"{len(problems)} Funde" if problems else "Barrierefreiheit ok")
sys.exit(1 if problems else 0)
