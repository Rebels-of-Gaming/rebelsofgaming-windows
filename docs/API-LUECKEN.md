# API-Lücken – Aufgaben für die Website-Sitzung (Windows-App)

Von der Windows-App-Sitzung gepflegt. Wahke gibt die Einträge an die Website-Sitzung (`/home/wahke/projects/rebelsofgaming`)
weiter. Erledigte Einträge mit Commit-Hash der Website abhaken. Lücken, die die Mobile-App schon gemeldet hat, stehen
in `/home/wahke/projects/rog-app/docs/API-LUECKEN.md` und werden hier nicht wiederholt.

Geprüft gegen: Spec `openapi/openapi.json` (Website v0.29.0, Kopie aus rog-app, 15.10.2026) und Guide `docs/api/de/35-apps.md`.

| # | Priorität | Was fehlt / was ändern | Warum (Windows-Bereich) | Vorschlag Route / Payload | Status |
|---|---|---|---|---|---|
| W1 | mittel (Stufe 3) | Push für Windows über **WNS** statt FCM | Benachrichtigungen bei geschlossener App. Guide 35 nennt `fcm` für Windows, aber Firebase hat kein Messaging-SDK für Windows-Desktop-Apps; Windows App SDK bringt `PushNotificationManager` (WNS) mit | `push_provider: "wns"` in `POST /v1/devices`, `push_token` = WNS-Channel-URI (`https://*.notify.windows.com/…`, nur diese Hosts akzeptieren); Versand per WNS (OAuth mit Package-SID + Secret aus dem Partner Center bzw. Entra-App), Toast-XML inhaltsfrei wie FCM/APNs (`type`, `id`, Titel/Text allgemein); Channel-URI läuft nach 30 Tagen ab → App meldet bei jedem Start neu; `410 Gone` → Token löschen | offen |
| W2 | niedrig (Stufe 3) | `/.well-known/windows-app-web-link` auf `rebelsofgaming.com` und `wiki.` | Web-Links öffnen direkt die Windows-App (App-URI-Handler), wie Universal/App Links | JSON-Array `[{ "packageFamilyName": "<aus Partner Center>", "paths": ["/de/news/*", "/forum/*", "/u/*", …], "excludePaths": ["/admin/*"] }]`, `application/json`, ohne Weiterleitung; Wert aus `.env` (`WINDOWS_PACKAGE_FAMILY_NAMES`), ohne Wert 404 – analog zu `assetlinks.json` | offen |
| W3 | niedrig | Spec: Parameter `confirmation` von `DELETE /v1/me` steht als `in: body` | ungültig in OpenAPI 3 – Generatoren (Kiota) brechen ab; die App entfernt ihn per Skript | Feld nur im `requestBody` dokumentieren | offen |
| W4 | niedrig | Spec: `/pages/{slug}` und `/pages/{page}` haben dieselbe Pfadsignatur | OpenAPI verlangt eindeutige Pfade; Kiota bricht ab | Parameter einheitlich benennen (z. B. beide `{page}`) oder Lese-Route unter eigenem Pfad | offen |
| W5 | niedrig | Spec: Schema `PreviewRecurrence` ist leer (`allOf` mit leerem Objekt) | Generatoren erzeugen keinen Request-Typ für `POST /v1/events/recurrence-preview` | Felder (`starts_at`, `timezone`, `recurrence`, `limit`, `from`) im FormRequest typisieren | offen |
| W6 | niedrig | Eigene Client-ID `rog-windows` | Hinweisseite im Browser zeigt „RoG-App“; eigener Name „RoG für Windows“ und getrennte Scope-Liste wären klarer | `APPS_CLIENT_IDS=rog-app,rog-windows`, Name je Client (aktuell ein gemeinsamer `APPS_CLIENT_NAME`) | offen, Owner-Entscheidung (Bauplan Frage 4) |
