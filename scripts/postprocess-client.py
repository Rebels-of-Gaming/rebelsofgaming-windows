#!/usr/bin/env python3
"""Nachbearbeitung des NSwag-Clients (aufgerufen von scripts/generate-api.sh).

1. Weitere 2xx-Antworten (z. B. `201 Created` neben `200`) behandelt NSwag als Ausnahme
   (`GeneratedApiException<ResponseN>`). Laravel antwortet beim Anlegen oft mit 201 – die App würde jede
   Neuanlage als Fehler sehen. Hier werden solche Zweige auf den Rückgabetyp der Methode umgestellt und
   geben das Ergebnis zurück.
2. `[EnumMember]` allein wirkt nicht mit System.Text.Json; `[JsonStringEnumMemberName]` ergänzen, damit
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

# --- 2. Enum-Namen -----------------------------------------------------------------------------------------
if "JsonStringEnumMemberName" not in src:
    src = re.sub(
        r'^( *)\[System\.Runtime\.Serialization\.EnumMember\(Value = @"([^"]*)"\)\]$',
        lambda e: f'{e.group(0)}\n{e.group(1)}[System.Text.Json.Serialization.JsonStringEnumMemberName(@"{e.group(2)}")]',
        src, flags=re.M)

open(path, "w", encoding="utf-8").write(src)
print(f"postprocess-client: {fixed} weitere 2xx-Antworten als Erfolg behandelt")
