#!/usr/bin/env python3
"""Bereitet openapi/openapi.json für NSwag vor (schreibt openapi/openapi.normalized.json, nicht im Repo).

Parameter mit `in: body` werden entfernt (in OpenAPI 3 ungültig, siehe docs/API-LUECKEN.md) – der Body steht
ohnehin im requestBody. Namenskollisionen generierter Klassen mit `System` behebt scripts/generate-api.sh danach
im erzeugten Code (NSwag ignoriert `title` bei nullable Inline-Objekten).
"""
import json
import sys
from pathlib import Path

def walk(node, stats):
    if isinstance(node, dict):
        params = node.get("parameters")
        if isinstance(params, list):
            kept = [p for p in params if not (isinstance(p, dict) and p.get("in") == "body")]
            stats["body_params"] += len(params) - len(kept)
            node["parameters"] = kept
        for value in node.values():
            walk(value, stats)
    elif isinstance(node, list):
        for value in node:
            walk(value, stats)

def main() -> int:
    root = Path(__file__).resolve().parent.parent / "openapi"
    src = Path(sys.argv[1]) if len(sys.argv) > 1 else root / "openapi.json"
    dst = Path(sys.argv[2]) if len(sys.argv) > 2 else root / "openapi.normalized.json"
    spec = json.loads(src.read_text(encoding="utf-8"))
    stats = {"body_params": 0}
    walk(spec, stats)
    dst.write_text(json.dumps(spec, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"normalized: {stats['body_params']} body parameter(s) removed -> {dst.name}")
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
