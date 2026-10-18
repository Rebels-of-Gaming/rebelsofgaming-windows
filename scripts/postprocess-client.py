#!/usr/bin/env python3
"""Nachbearbeitung des NSwag-Clients (aufgerufen von scripts/generate-api.sh).

1. Weitere 2xx-Antworten (z. B. `201 Created` neben `200`) behandelt NSwag als Ausnahme
   (`GeneratedApiException<ResponseN>`). Laravel antwortet beim Anlegen oft mit 201 – die App würde jede
   Neuanlage als Fehler sehen. Hier werden solche Zweige auf den Rückgabetyp der Methode umgestellt und
   geben das Ergebnis zurück.
2. Erfolgs-Code, den die Spezifikation nicht nennt (z. B. 200 statt 201, wenn die Website eine alte Meldung wieder
   öffnet): der erste 2xx-Zweig jeder Methode nimmt jede 2xx-Antwort an. Fehler (4xx/5xx) wirft vorher schon
   `RoGHttpHandler` als `ApiProblemException`.
3. `[EnumMember]` allein wirkt nicht mit System.Text.Json; `[JsonStringEnumMemberName]` ergänzen, damit
   Enum-Werte so gesendet werden, wie die API sie erwartet (`windows` statt `Windows`).
"""
import re
import sys

path = sys.argv[1]
src = open(path, encoding="utf-8").read()

# --- 1. 2xx-Zweige -------------------------------------------------------------------------------------------
method_re = re.compile(
    r"public virtual async System\.Threading\.Tasks\.Task<(\w+)> (\w+)\((.*?)\n        \}\n", re.S)
branch_re = re.compile(
    r"(if \(status_ == 2\d\d\)\s*\{\s*var objectResponse_ = await ReadObjectResponseAsync<)(\w+)(>\(response_, headers_, cancellationToken\)\.ConfigureAwait\(false\);"
    r"\s*if \(objectResponse_\.Object == null\)\s*\{\s*throw new GeneratedApiException\(\"Response was null which was not expected\.\", status_, objectResponse_\.Text, headers_, null\);\s*\}\s*)"
    r"throw new GeneratedApiException<\w+>\([^;]*\);", re.S)

fixed = 0


def fix_method(m):
    global fixed
    ret = m.group(1)

    def fix_branch(b):
        global fixed
        if b.group(2) == ret:
            return b.group(0)
        fixed += 1
        return b.group(1) + ret + b.group(3) + "return objectResponse_.Object;"

    return branch_re.sub(fix_branch, m.group(0))


src = method_re.sub(fix_method, src)

# --- 2. jede 2xx-Antwort als Erfolg ------------------------------------------------------------------------
any2xx = 0
parts = src.split("public virtual async System.Threading.Tasks.Task")
for i in range(1, len(parts)):
    parts[i], n = re.subn(r"if \(status_ == 2\d\d\)", "if (status_ >= 200 && status_ < 300)", parts[i], count=1)
    any2xx += n
src = "public virtual async System.Threading.Tasks.Task".join(parts)

# --- 3. Enum-Namen -----------------------------------------------------------------------------------------
if "JsonStringEnumMemberName" not in src:
    src = re.sub(
        r'^( *)\[System\.Runtime\.Serialization\.EnumMember\(Value = @"([^"]*)"\)\]$',
        lambda e: f'{e.group(0)}\n{e.group(1)}[System.Text.Json.Serialization.JsonStringEnumMemberName(@"{e.group(2)}")]',
        src, flags=re.M)

open(path, "w", encoding="utf-8").write(src)
print(f"postprocess-client: {fixed} weitere 2xx-Antworten als Erfolg behandelt, {any2xx} Methoden nehmen jede 2xx-Antwort an")
