#!/usr/bin/env python3
"""
Prüft x:Bind-Pfade der WinUI-App unter Linux: jeder Pfadschritt muss als Eigenschaft, Feld, generierte
[ObservableProperty]-Eigenschaft oder [RelayCommand]-Command (…Command) auf dem jeweiligen Typ existieren.
Hintergrund: Findet der XAML-Compiler einen Namen nicht, stürzt er teils mit „WMC9999 … ErrorMessages.resources“ ab,
ohne Datei und Zeile zu nennen. Bewusst einfach (Regex statt Roslyn) – meldet im Zweifel lieber zu viel.
Aufruf: scripts/check-xaml-bindings.py   (Exit 1 bei Funden)
"""
import glob, os, re, sys
import xml.etree.ElementTree as ET

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
SOURCES = [p for p in glob.glob(os.path.join(ROOT, "src", "**", "*.cs"), recursive=True)
           if "/obj/" not in p and "/bin/" not in p and "/Generated/" not in p]

types = {}      # Name -> {member: type}
bases = {}      # Name -> Basistyp
kw = r"(?:public|internal|private|protected|static|sealed|partial|abstract|readonly|required|override|virtual|new|\s)*"
for path in SOURCES:
    text = open(path, encoding="utf-8").read()
    for m in re.finditer(r"(class|record|struct|interface)\s+(\w+)(?:<[^>]*>)?\s*(\(([^)]*)\))?\s*(?::\s*([\w.<>, ]+))?", text):
        kind = m.group(1)
        name = m.group(2)
        members = types.setdefault(name, {})
        if m.group(5):
            bases[name] = m.group(5).split(",")[0].split("(")[0].split("<")[0].strip()
        if m.group(4) and kind == "record":
            for param in m.group(4).split(","):
                parts = param.strip().split()
                if len(parts) >= 2:
                    members[parts[-1]] = parts[-2].rstrip("?")
        # Körper der Klasse grob: bis zur nächsten Typdeklaration
        nxt = re.search(r"\n(?:public|internal)?\s*(?:sealed |static |abstract )*(?:partial )*(?:class|record|struct) \w+", text[m.end():])
        body = text[m.end(): m.end() + (nxt.start() if nxt else len(text))]
        for p in re.finditer(r"\n\s*public\s+" + kw + r"([\w.<>\[\],?]+)\s+(\w+)\s*(?:\{|=>)", body):
            members.setdefault(p.group(2), p.group(1).rstrip("?").split("<")[0])
        for p in re.finditer(r"\n\s*public\s+" + kw + r"([\w.<>\[\],?]+)\s+(\w+)\s*;", body):
            members.setdefault(p.group(2), p.group(1).rstrip("?"))
        if kind == "record":
            continue  # Toolkit-Generatoren ([RelayCommand], [ObservableProperty]) erzeugen in records nichts
        for p in re.finditer(r"\[RelayCommand[^\]]*\]\s*(?:\[[^\]]*\]\s*)*(?:public|private|internal|protected)?\s*(?:async\s+)?[\w<>]+\s+(\w+?)(?:Async)?\(", body):
            members.setdefault(p.group(1) + "Command", "ICommand")

def members_of(t):
    seen = set()
    while t and t not in seen:
        seen.add(t)
        yield from types.get(t, {}).items()
        t = bases.get(t)

def has(t, member):
    if t in ("object", "string", "x:String", "ICommand") or t not in types:
        return True  # unbekannte/fremde Typen nicht prüfen
    return any(n == member for n, _ in members_of(t))

def type_of(t, member):
    for n, mt in members_of(t):
        if n == member:
            return mt.split(".")[-1]
    return None

# Namen wie WinUI-Typen in x:Bind-Pfaden führten zu Fehlern des XAML-Compilers (Page, Paragraph, Image, Border …)
WINUI_TYPES = {"Page", "Paragraph", "Image", "Border", "Grid", "Run", "Frame", "Window", "Button", "TextBlock", "StackPanel",
               "Canvas", "Ellipse", "Rectangle", "Path", "Hyperlink", "Span", "Bold", "Italic", "Style", "Brush", "Thickness"}

problems = []
for xaml in glob.glob(os.path.join(ROOT, "src", "RoG.App", "**", "*.xaml"), recursive=True):
    if "/obj/" in xaml or "/bin/" in xaml:
        continue
    text = open(xaml, encoding="utf-8").read()
    cls = re.search(r'x:Class="[\w.]*\.(\w+)"', text)
    root_type = cls.group(1) if cls else None
    tree = ET.fromstring(text)

    def walk(el, ctx):
        dt = el.get("{http://schemas.microsoft.com/winfx/2006/xaml}DataType")
        if dt:
            ctx = dt.split(":")[-1]
        for attr, value in el.attrib.items():
            for b in re.finditer(r"\{x:Bind\s+([^,}]*)", value):
                path = b.group(1).strip()
                if path.startswith("Path="):
                    path = path[5:]
                if not path or "(" in path or path == "Mode=OneWay":
                    continue
                t = ctx
                for step in path.split("."):
                    step = step.strip()
                    if step in WINUI_TYPES:
                        line = text[: text.find(value)].count("\n") + 1 if value in text else 0
                        problems.append(f"{os.path.relpath(xaml, ROOT)}:{line}: '{step}' heißt wie ein WinUI-Typ – umbenennen (Pfad {path})")
                        break
                    if not step or t is None:
                        break
                    if not has(t, step):
                        line = text[: text.find(value)].count("\n") + 1 if value in text else 0
                        problems.append(f"{os.path.relpath(xaml, ROOT)}:{line}: '{step}' fehlt auf {t} (Pfad {path})")
                        break
                    t = type_of(t, step)
        for child in el:
            walk(child, ctx)

    walk(tree, root_type)

for p in problems:
    print(p)
print("x:Bind ok" if not problems else f"{len(problems)} verdächtige x:Bind-Pfade")
sys.exit(1 if problems else 0)
