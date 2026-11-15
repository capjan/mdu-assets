# MDU Asset Server – Anforderungen

Stand: 13.11.2026 · Status: Entwurf nach Anforderungsinterview · Arbeitsname: `mdu-assets`

## 1. Ziel

Der MDU Asset Server ist eine selbst gehostete Webanwendung, über die Autorinnen und Autoren von MDU-Dokumentationen Bilder hochladen und sofort einen dauerhaften Link erhalten, der sich direkt in Markdown einfügen lässt. Er wird als .NET-Native-AOT-Anwendung gebaut und verwendet das MDU-Design-System, damit er optisch zu MDU-Sites passt.

Der Server ergänzt lokale Bilder in MDU-Projekten, er ersetzt sie nicht. Markdown-Dateien bleiben portabel: Ein Asset-Link ist eine gewöhnliche HTTPS-URL, die jeder Markdown-Viewer anzeigen kann.

## 2. Begriffe

| Begriff | Bedeutung |
| --- | --- |
| Asset | Ein hochgeladenes Bild mit allen Versionen, Metadaten und Statistiken |
| Asset-ID | Kurze, zufällige, nicht erratbare ID (Base62), die das Asset dauerhaft identifiziert |
| Öffentlicher Link | `https://<public-host>/i/<asset-id>/<dateiname>` – ändert sich nie |
| Version | Ein konkreter Dateiinhalt eines Assets; neue Versionen ersetzen den Inhalt unter demselben Link |
| Projekt | Container für Assets, mit eigenen Mitgliedern |
| Tag | Freies Schlagwort an einem Asset; ein Asset kann mehrere haben. Ordnet und filtert, beeinflusst den Link nicht. Tags ersetzen die früheren Ordner ([ADR-0020](Architecture/Architecture-Decisions/0020-Tags-Instead-of-Folders.md)). |
| Snippet | Kopierfertiger Text für ein Asset, z. B. Markdown-Bild mit Alt-Text |
| Hell/Dunkel-Paar | Zwei Assets, die als Light- und Dark-Variante desselben Bildes verknüpft sind |

## 3. Entscheidungen aus dem Interview

| Thema | Entscheidung |
| --- | --- |
| Link-Format | Kurz-ID + lesbarer Dateiname: `/i/k3F9xQ/architektur.png`; `/i/k3F9xQ` liefert dieselbe Datei |
| Sichtbarkeit | Bilder sind öffentlich für jeden, der den Link kennt; IDs sind nicht erratbar |
| Ersetzen | Neue Version unter gleichem Link, Versionshistorie mit Wiederherstellen |
| Speicher | Dateisystem für Dateien, SQLite für Metadaten, ein Volume |
| Projektrollen | Owner / Editor / Viewer |
| API-Anmeldung | Persönliche API-Tokens (benannt, mit Ablauf, optional auf ein Projekt beschränkt, widerrufbar) |
| Web-Anmeldung | Eigene Anmeldeseite mit Session-Cookie; später OIDC/SSO und Passkeys |
| Benutzeranlage | Nur durch Admins; der erste Admin wird beim Start über Umgebungsvariablen angelegt |
| Snippets | Markdown mit Alt-Text, Hell/Dunkel-Paar, MDU-Attribute, reine URL und HTML |
| Bildverarbeitung | Metadaten entfernen, Format direkt nach dem Upload wählen (WebP), Vorschaubilder, Größen per Parameter; mit NetVips/libvips |
| Dateitypen | Nur die MDU-Bildformate: png, jpg/jpeg, gif, webp, avif, bmp, svg |
| Statistik | Zähler, letzter Abruf, verweisende Seiten; Bots und Abrufe aus der Verwaltungs-UI werden nicht gezählt |
| mdu-CLI | Neuer Befehl `mdu assets upload`; `mdu lint` prüft Asset-Links |
| Löschen | Papierkorb mit 30 Tagen Frist |
| UI-Technik | Eingebettetes statisches HTML/CSS + Vanilla JavaScript, das die JSON-API nutzt |
| Design | `tokens.css` aus dem MDU-Design-System übernehmen; ein Test prüft, dass die Kopie aktuell ist |
| UI-Sprache | Englisch, wie MDU |
| Ordnen und Finden | Tags (mehrere pro Bild), Suche und Filter, Rückwärtssuche über die URL |
| Limits | Max. Dateigröße, Speicherquote pro Projekt, Rate Limiting, Audit-Log |
| Betrieb | Docker-Image `capjan/mdu-assets`, native Binaries, eigenes Repository, Projekt-Export/-Import |
| Erste Version | Enthält alle oben genannten Punkte außer den als „später“ markierten Anmeldearten |

## 4. Rollen und Berechtigungen

### 4.1 Systemrollen

| Rolle | Rechte |
| --- | --- |
| Admin | Sieht und verwaltet alle Projekte, verwaltet Benutzer, Quoten, globale Einstellungen, Audit-Log, Papierkorb aller Projekte |
| Benutzer | Sieht nur Projekte, in denen er Mitglied ist; verwaltet eigenes Passwort und eigene API-Tokens |

### 4.2 Projektrollen

| Aktion | Viewer | Editor | Owner |
| --- | :-: | :-: | :-: |
| Assets, Tags, Statistiken ansehen, Snippets kopieren | ✓ | ✓ | ✓ |
| Hochladen, ersetzen, Metadaten bearbeiten, verschieben, taggen | | ✓ | ✓ |
| Tags vergeben, umbenennen, zusammenführen, löschen | | ✓ | ✓ |
| Assets in den Papierkorb legen und wiederherstellen | | ✓ | ✓ |
| Mitglieder hinzufügen, Rollen ändern, entfernen | | | ✓ |
| Projekt umbenennen, exportieren, löschen | | | ✓ |
| Papierkorb endgültig leeren | | | ✓ |

Ein Projekt hat immer mindestens einen Owner. Admins haben in jedem Projekt Owner-Rechte.

## 5. Funktionale Anforderungen

Priorität: **M** = Muss (erste Version), **S** = Soll (erste Version, darf notfalls nachgezogen werden), **L** = Später.

### 5.1 Anmeldung und Benutzer

| ID | Anforderung | Prio |
| --- | --- | :-: |
| AUTH-01 | Die Web-UI hat eine eigene Anmeldeseite mit Benutzername und Passwort; ein Menü hinter dem Benutzernamen enthält Konto, Admin und Abmelden. Werkzeuge melden sich an der API mit HTTP Basic Auth oder einem API-Token an; Browser bekommen nie die Basic-Auth-Abfrage. Passwörter werden nur über HTTPS akzeptiert (direkt oder hinter einem Reverse Proxy mit `X-Forwarded-Proto`). | M |
| AUTH-02 | Passwörter werden mit PBKDF2 (SHA-256, mindestens 600 000 Iterationen, zufälliges Salt) gespeichert. | M |
| AUTH-03 | Beim ersten Start ohne Benutzer wird ein Admin aus `MDU_ASSETS_ADMIN_USER` und `MDU_ASSETS_ADMIN_PASSWORD` angelegt. Fehlen die Variablen, startet der Server mit einer klaren Fehlermeldung. | M |
| AUTH-04 | Admins legen Benutzer mit Startpasswort an, sperren, entsperren, setzen Passwörter zurück und vergeben die Admin-Rolle. Der letzte Admin kann nicht entfernt oder gesperrt werden. | M |
| AUTH-05 | Benutzer ändern ihr eigenes Passwort; ein Startpasswort muss bei der ersten Anmeldung geändert werden. | S |
| AUTH-06 | Benutzer erzeugen persönliche API-Tokens mit Name, Ablaufdatum (Standard 90 Tage, „nie“ wählbar) und optionaler Beschränkung auf ein Projekt und auf Lese- oder Schreibrechte. Das Token wird nur einmal angezeigt und nur gehasht gespeichert. Die Liste zeigt Name, Präfix, Ablauf und letzte Nutzung. | M |
| AUTH-07 | Tokens werden als `Authorization: Bearer <token>` übergeben. Ein Token hat höchstens die Rechte seines Benutzers. | M |
| AUTH-08 | Fehlgeschlagene Anmeldungen werden pro Benutzer und IP begrenzt (Rate Limiting, siehe OPS-05). | M |
| AUTH-09 | Session-Cookie (`HttpOnly`, `Secure`, `SameSite=Strict`), wahlweise „angemeldet bleiben“; Sperren, Löschen und Passwortwechsel beenden die Sessions des Benutzers sofort; Schutz vor Anfragen fremder Seiten ([ADR-0021](Architecture/Architecture-Decisions/0021-Sign-In-Page-and-Session-Cookie.md)). | M |
| AUTH-10 | Anmeldung über OIDC (z. B. GitHub, Entra ID, Keycloak) mit Zuordnung zu bestehenden Benutzern. | L |
| AUTH-11 | Anmeldung mit Passkeys (WebAuthn). | L |

### 5.2 Projekte und Mitglieder

| ID | Anforderung | Prio |
| --- | --- | :-: |
| PRJ-01 | Admins und (falls vom Admin erlaubt) Benutzer legen Projekte mit Name, Kurzname (Slug) und Beschreibung an. Der Ersteller wird Owner. | M |
| PRJ-02 | Die Projektübersicht zeigt jedem Benutzer seine Projekte mit Anzahl der Assets, belegtem Speicher und letzter Änderung; Admins sehen alle Projekte. | M |
| PRJ-03 | Owner verwalten Mitglieder und ihre Rollen (Viewer, Editor, Owner). | M |
| PRJ-04 | Ein Projekt kann umbenannt werden, ohne dass sich Asset-Links ändern. | M |
| PRJ-05 | Ein Projekt wird erst gelöscht, wenn es leer ist oder der Owner das Löschen aller Assets ausdrücklich bestätigt; die Assets gehen dabei in den Papierkorb. | M |

### 5.3 Hochladen

| ID | Anforderung | Prio |
| --- | --- | :-: |
| UP-01 | Dateien lassen sich per Drag & Drop auf das gesamte Projektfenster hochladen. Beim Ziehen zeigt eine Ablagefläche das Zielprojekt und den Zielordner. | M |
| UP-02 | Bilder aus der Zwischenablage lassen sich mit ++ctrl+v++ / ++cmd+v++ überall in der Projektansicht einfügen (Screenshot-Workflow). Eingefügte Bilder ohne Dateinamen erhalten einen Namen wie `screenshot-2026-11-13-1742.png`. | M |
| UP-03 | Mehrere Dateien werden gleichzeitig hochgeladen, mit Fortschritt pro Datei. | M |
| UP-04 | Vor oder direkt nach dem Upload kann man Dateiname und Alt-Text festlegen. Der Alt-Text wird aus dem Dateinamen vorgeschlagen (`app-settings.png` → „App settings“). | M |
| UP-05 | Nach dem Upload erscheint das Asset sofort mit dem Markdown-Snippet und einer hervorgehobenen Kopieren-Schaltfläche. Wo der Browser es erlaubt, wird das Snippet direkt in die Zwischenablage kopiert; ein Hinweis bestätigt das. Bei mehreren Dateien gibt es „Alle Snippets kopieren“. | M |
| UP-06 | Erlaubt sind nur png, jpg/jpeg, gif, webp, avif, bmp und svg. Der Typ wird am Dateiinhalt (Signatur) geprüft, nicht nur an der Endung. | M |
| UP-07 | SVG-Dateien werden beim Upload bereinigt (keine Scripts, Event-Handler, externen Referenzen, `foreignObject`); die Bereinigung folgt der Allowlist des SVG-Lesers von MDU, ist aber ein eigener Sanitizer für vollständige Dateien ([ADR-0012](Architecture/Architecture-Decisions/0012-Own-SVG-Sanitizer.md)). | M |
| UP-08 | Bei Pixelbildern werden EXIF-, GPS- und sonstige Metadaten entfernt; die Farbprofil-Information bleibt erhalten, die Ausrichtung wird vorher angewendet. | M |
| UP-09 | Lädt jemand eine Datei hoch, deren Inhalt im Projekt schon existiert (SHA-256), weist die UI darauf hin und bietet den vorhandenen Link an. | S |
| UP-10 | Uploads über Größenlimit oder Quote werden mit verständlicher Meldung abgelehnt, bevor die Datei vollständig übertragen ist (wenn `Content-Length` bekannt). | M |
| UP-11 | Direkt nach dem Upload wählt der Server das Format mit dem besten Verhältnis von Größe und Qualität und liefert nur noch dieses aus: Fotos (JPEG) werden verlustbehaftetes WebP in hoher Qualität; PNG, BMP und unbewegtes GIF werden verlustfreies WebP, außer der Inhalt ist fotografisch (verlustfrei mehr als doppelt so groß wie verlustbehaftet), dann verlustbehaftet. Jeweils nur, wenn die Datei kleiner wird. Beim Upload und beim Ersetzen kann man „Lossless“ wählen (`?lossless=true`): dann geht keine Qualität verloren, JPEG bleibt unverändert. SVG, AVIF, WebP und Animationen bleiben, wie sie sind. Dateiname und Endung folgen dem gespeicherten Format. Gespeichert wird nur die umgewandelte Datei; Typ und Größe des Uploads werden angezeigt. Pro Projekt abschaltbar; bestehende Bilder bleiben unverändert ([ADR-0019](Architecture/Architecture-Decisions/0019-Format-Chosen-on-Upload.md)). | M |

### 5.4 Links und Snippets

| ID | Anforderung | Prio |
| --- | --- | :-: |
| LNK-01 | Jedes Asset hat einen dauerhaften Link `/i/<asset-id>/<dateiname>`. Tags, Umbenennen von Projekten und neue Versionen ändern ihn nicht. | M |
| LNK-02 | `/i/<asset-id>` und `/i/<asset-id>/<beliebiger-name>` liefern dieselbe Datei. Nach einer Umbenennung funktionieren alte Links weiter. | M |
| LNK-03 | Asset-IDs sind mindestens 10 Zeichen Base62 aus einem kryptografischen Zufallsgenerator und nicht fortlaufend. | M |
| LNK-04 | Die öffentliche Basis-URL ist konfigurierbar und darf ein anderer Host als die Verwaltungs-UI sein (empfohlen, z. B. `img.example.com` und `assets.example.com`), damit ausgelieferte Dateien keinen Zugriff auf UI-Cookies haben. | M |
| LNK-05 | Snippet „Markdown“: `![<alt>](<link>)`. | M |
| LNK-06 | Snippet „Hell/Dunkel“: Zwei Assets lassen sich als Paar verknüpfen; das Snippet enthält beide Zeilen mit `#only-light` und `#only-dark` und demselben Alt-Text. Beim Upload zweier Dateien mit Endungen wie `-light`/`-dark` schlägt die UI das Paar vor. | M |
| LNK-07 | Schalter für MDU-Attribute, die ans Snippet angehängt werden: `{.image-frame}`, `{.image-zoom-on}`, `{.image-zoom-off}`, Bildunterschrift und Breite (`?w=`). Die zuletzt gewählten Schalter merkt sich die UI pro Benutzer. | M |
| LNK-08 | Snippets „URL“ und „HTML“ (`<img src alt width height>`). | M |
| LNK-09 | Die Rückwärtssuche nimmt eine Asset-URL (auch mit Größenparameter oder Fragment) entgegen und öffnet das Asset, sofern der Benutzer Zugriff hat. | M |

### 5.5 Auslieferung und Bildverarbeitung

| ID | Anforderung | Prio |
| --- | --- | :-: |
| DLV-01 | Öffentliche Links liefern die aktuelle Version ohne Anmeldung, mit korrektem `Content-Type`, `ETag`, `Last-Modified` und Unterstützung für `If-None-Match`/`If-Modified-Since`. | M |
| DLV-02 | Da sich der Inhalt hinter einem Link ändern kann, nutzen Links `Cache-Control: public, max-age=300, stale-while-revalidate=86400` (konfigurierbar). Zusätzlich gibt es versionsfeste Links `/i/<asset-id>/v<n>/<dateiname>` mit `immutable`. | M |
| DLV-03 | `?w=<breite>` liefert eine proportional verkleinerte Fassung (nie vergrößert). Erlaubt sind nur konfigurierte Breiten (Standard: 320, 640, 800, 1024, 1280, 1600, 2048), damit Angreifer keinen unbegrenzten Cache erzeugen. Abgeleitete Fassungen werden auf der Platte zwischengespeichert. | M |
| DLV-04 | Ein Link liefert immer das gespeicherte Format (UP-11), unabhängig vom `Accept`-Header; `?w=` verkleinert im selben Format. So passen Endung und `Content-Type` zusammen. Gleichzeitige Bildjobs sind begrenzt. | M |
| DLV-05 | SVG wird mit `Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; sandbox` ausgeliefert; alle Antworten tragen `X-Content-Type-Options: nosniff`. | M |
| DLV-06 | Vorschaubilder für die UI werden beim Upload erzeugt und über einen angemeldeten, nicht gezählten Endpunkt ausgeliefert. | M |
| DLV-07 | Assets im Papierkorb liefern `410 Gone` (optional ein konfigurierbares Platzhalterbild); unbekannte IDs liefern `404`. | M |
| DLV-08 | Die Auslieferung unterstützt `HEAD` und `Range`-Anfragen. | S |

### 5.6 Ordnen, Suchen und Ansichten

| ID | Anforderung | Prio |
| --- | --- | :-: |
| ORG-01 | *Entfallen:* verschachtelte Ordner. Tags ersetzen sie; beim Update werden der Ordner eines Bildes und alle darüber zu Tags ([ADR-0020](Architecture/Architecture-Decisions/0020-Tags-Instead-of-Folders.md)). | – |
| ORG-02 | Die Seitenleiste zeigt die Tags des Projekts mit ihrer Anzahl; ein Klick filtert danach, mehrere Tags grenzen weiter ein. Bilder werden einzeln und als Mehrfachauswahl per Drag & Drop auf einen Tag oder über „Tags…“ getaggt. | M |
| ORG-03 | Freie Tags pro Asset, mit Vorschlägen aus den im Projekt vorhandenen Tags. Mehrere Bilder lassen sich auf einmal taggen (Auswahl oder Ziehen auf einen Tag in der Seitenleiste); Tags lassen sich im ganzen Projekt umbenennen, zusammenführen und löschen. Uploads bekommen die Tags, nach denen gerade gefiltert wird. | M |
| ORG-04 | Suche über Dateiname, Alt-Text, Beschreibung und Tags, schon während der Eingabe; Filter nach einem oder mehreren Tags (alle müssen zutreffen) oder „ohne Tags“, Dateityp, Upload-Zeitraum, Uploader und „nie abgerufen“ / „seit N Tagen nicht abgerufen“. | M |
| ORG-05 | Projektansicht als Kachelraster (Vorschau, Name, Größe, Abrufe) und als Liste mit sortierbaren Spalten (Name, Größe, Abmessungen, Upload, letzter Abruf, Abrufe). | M |
| ORG-06 | Detailansicht: große Vorschau (hell/dunkel Hintergrund umschaltbar, per Klick oder Tastatur im Bildbetrachter von MDU mit Zoom und Verschieben), alle Snippets, Metadaten (Typ, Abmessungen, Größe, Hash, Uploader, Datum), Versionen, Statistik, verweisende Seiten, Hell/Dunkel-Partner. | M |
| ORG-07 | Tastaturbedienung: Pfeiltasten im Raster und in der Liste, auch ohne vorher ein Bild zu fokussieren und mit ++down++ aus dem Suchfeld, ++enter++ öffnet die Details, ++c++ kopiert das Markdown-Snippet des ausgewählten Assets, ++delete++ legt in den Papierkorb. | S |

### 5.7 Versionen und Papierkorb

| ID | Anforderung | Prio |
| --- | --- | :-: |
| VER-01 | Editoren ersetzen ein Asset durch eine neue Datei (Drag & Drop auf die Detailansicht, Einfügen oder Dateiauswahl). Der Link bleibt, die Versionsnummer steigt. | M |
| VER-02 | Ein Ersatz darf das Format wechseln (z. B. png → webp, auch durch UP-11). Der Dateiname bekommt dann die neue Endung; Links mit dem alten Namen funktionieren weiter, weil nur die ID zählt, und Versionslinks älterer Versionen behalten ihre Endung. Die UI zeigt einen Hinweis. | M |
| VER-03 | Die Versionsliste zeigt Datum, Benutzer, Größe und Abmessungen; jede Version ist abrufbar und kann zur aktuellen gemacht werden (als neue Version). | M |
| VER-04 | Alte Versionen zählen zur Quote. Owner können einzelne alte Versionen endgültig löschen. Optional: automatische Bereinigung nach N Versionen (Projekteinstellung). | S |
| DEL-01 | Löschen legt Assets in den Papierkorb. Wurde das Asset in den letzten 30 Tagen abgerufen, warnt die UI vorher mit Anzahl und verweisenden Seiten. | M |
| DEL-02 | Assets im Papierkorb sind 30 Tage (konfigurierbar) wiederherstellbar, mit demselben Link, und werden danach automatisch endgültig gelöscht. | M |
| DEL-03 | Owner und Admins können den Papierkorb vorzeitig leeren; das endgültige Löschen erfordert eine Bestätigung. | M |

### 5.8 Abrufstatistik

| ID | Anforderung | Prio |
| --- | --- | :-: |
| STA-01 | Pro Asset werden die Gesamtzahl der Abrufe und der Zeitpunkt des letzten Abrufs gespeichert. | M |
| STA-02 | Gezählt werden erfolgreiche öffentliche `GET`-Antworten (200 und 304), unabhängig von Größenparameter oder Format. `HEAD`, Vorschaubilder der UI und Abrufe angemeldeter UI-Sitzungen werden nicht gezählt. | M |
| STA-03 | Bekannte Bots und Crawler (User-Agent-Liste, konfigurierbar) werden nicht gezählt. | M |
| STA-04 | Verweisende Seiten: Aus dem `Referer` werden Origin und Pfad (ohne Query und Fragment) mit Anzahl und letztem Zeitpunkt gespeichert, höchstens 100 Einträge pro Asset (die ältesten fallen heraus). | M |
| STA-05 | Zähler werden im Speicher gesammelt und gebündelt (z. B. alle 10 s) in SQLite geschrieben, damit Abrufe nicht auf Datenbankschreibzugriffe warten. | M |
| STA-06 | Es werden keine IP-Adressen oder User-Agents pro Abruf dauerhaft gespeichert. | M |

**Bekannte Grenzen** (in der UI erklärt): Browser- und CDN-Caches verringern die Zahl der Abrufe, die den Server erreichen. Browser senden bei Abrufen von fremden Sites standardmäßig nur den Origin als Referer (`strict-origin-when-cross-origin`); verweisende Seiten sind daher meist nur Sites, nicht einzelne Seiten. Siehe offene Frage Q-03.

### 5.9 API

| ID | Anforderung | Prio |
| --- | --- | :-: |
| API-01 | JSON-API unter `/api/v1`, angemeldet über die Session der Web-UI, Basic Auth oder API-Token. Jede UI-Funktion ist über die API verfügbar; die UI nutzt ausschließlich diese API. | M |
| API-02 | Die API wird als OpenAPI-Dokument unter `/api/v1/openapi.json` beschrieben, das sich mit MDU als API-Referenz rendern lässt. | M |
| API-03 | Fehler werden einheitlich als `application/problem+json` (RFC 9457) gemeldet. | M |
| API-04 | Der Upload-Endpunkt akzeptiert `multipart/form-data` und den Rohinhalt im Body (`Content-Type: image/png`, Dateiname per `?name=`), damit auch einfache Tools wie `curl --data-binary` oder ShareX funktionieren. | M |
| API-05 | Die Upload-Antwort enthält mindestens `id`, `url`, `versionUrl`, `markdown`, `html`, `width`, `height`, `size`, `contentType`. | M |
| API-06 | Ein öffentlicher, nicht angemeldeter Prüf-Endpunkt beantwortet für eine Liste von Asset-URLs, ob sie existieren, im Papierkorb liegen oder unbekannt sind (für `mdu lint`). Er verrät keine Metadaten außer dem Status. | M |
| API-07 | Die UI bietet unter „Integrations“ eine fertige ShareX-Konfiguration (`.sxcu`) und ein `curl`-Beispiel für das gewählte Projekt und Token zum Download an. | S |

Skizze der Endpunkte:

| Methode und Pfad | Zweck |
| --- | --- |
| `POST /api/v1/projects/{project}/assets` | Hochladen (optional `alt`, `tag`, wiederholbar) |
| `GET /api/v1/projects/{project}/assets` | Liste mit Suche, Filter, Sortierung, Seitenweise |
| `GET /api/v1/assets/{id}` | Metadaten, Snippets, Statistik-Kurzform |
| `PATCH /api/v1/assets/{id}` | Name, Alt-Text, Beschreibung, Tags, Hell/Dunkel-Partner |
| `PUT /api/v1/assets/{id}/content` | Neue Version hochladen |
| `GET /api/v1/assets/{id}/versions` · `POST …/versions/{n}/restore` | Versionen |
| `GET /api/v1/assets/{id}/stats` | Zähler, letzter Abruf, verweisende Seiten |
| `DELETE /api/v1/assets/{id}` · `POST /api/v1/assets/{id}/restore` | Papierkorb |
| `POST /api/v1/assets/lookup` | Rückwärtssuche (angemeldet) |
| `POST /api/v1/public/check` | Existenzprüfung für Lint (öffentlich) |
| `…/projects`, `…/projects/{project}/tags`, `…/projects/{project}/assets/tags`, `…/projects/{project}/members` | Projekte, Tags, Mitglieder |
| `…/me`, `…/me/tokens` | Eigenes Profil und Tokens |
| `…/admin/users`, `…/admin/audit`, `…/admin/settings` | Verwaltung |
| `GET /api/v1/projects/{project}/export` · `POST /api/v1/projects/import` | Export/Import |

### 5.10 Anbindung an das mdu-CLI

Diese Anforderungen betreffen das Repository `capjan/mdu`.

| ID | Anforderung | Prio |
| --- | --- | :-: |
| CLI-01 | `mdu.yml` erhält einen Abschnitt `assets` mit `server` (Basis-URL der API), `public` (öffentliche Basis-URL, falls abweichend) und `project` (Standardprojekt). Das Token kommt aus der Umgebungsvariable `MDU_ASSETS_TOKEN`, nie aus `mdu.yml`. | M |
| CLI-02 | `mdu assets upload <datei>… [--project] [--tag] [--alt] [--format markdown\|url\|html]` lädt Dateien hoch und gibt pro Datei das Snippet als Klartext aus, damit es sich pipen lässt. Bei `-` liest der Befehl von stdin. | M |
| CLI-03 | `mdu lint` meldet Bild- und Linkziele auf dem konfigurierten Asset-Server, die unbekannt sind oder im Papierkorb liegen, als neue Regel (voraussichtlich `MDU029`). Die Prüfung nutzt den gebündelten Prüf-Endpunkt (API-06), läuft nur, wenn `assets.server` konfiguriert ist, und meldet Netzwerkfehler als Warnung, nicht als Fehler. Mit `--offline` wird sie übersprungen. | M |
| CLI-04 | Die CLI-Ausgabe bleibt Klartext und abhängigkeitsarm; der HTTP-Zugriff nutzt `HttpClient` ohne zusätzliche Pakete. | M |

### 5.11 Verwaltung, Limits und Protokoll

| ID | Anforderung | Prio |
| --- | --- | :-: |
| OPS-01 | Max. Dateigröße pro Upload: global (Standard 50 MB) und pro Projekt niedriger einstellbar. Max. Pixelzahl (Standard 50 MP) gegen Dekompressionsbomben. | M |
| OPS-02 | Speicherquote pro Projekt (aktuelle Versionen + alte Versionen + Papierkorb), vom Admin festgelegt; die UI zeigt Belegung und warnt ab 90 %. Zwischengespeicherte Größen-/Formatfassungen zählen nicht. | M |
| OPS-03 | Audit-Log für Anmeldungen (erfolgreich/fehlgeschlagen), Uploads, neue Versionen, Verschieben, Umbenennen, Metadatenänderungen, Löschen, Wiederherstellen, Mitglieder-, Token- und Benutzeränderungen. Admins sehen alles, Owner das Log ihres Projekts. Aufbewahrung konfigurierbar (Standard 365 Tage). | M |
| OPS-04 | Admin-Übersicht: Benutzer, Projekte mit Belegung, Gesamtspeicher, Papierkorb, Einstellungen. | M |
| OPS-05 | Rate Limiting (ASP.NET-Core-Middleware) für Anmeldungen, Uploads, den öffentlichen Prüf-Endpunkt und 404-Antworten auf `/i/` pro IP. | M |
| OPS-06 | Export eines Projekts als Zip (aktuelle Dateien, optional alle Versionen, Metadaten als JSON); Import in eine andere Instanz behält Asset-IDs bei, sofern sie frei sind. | M |
| OPS-07 | Health-Endpunkt `/healthz` und strukturierte Logs auf stdout. | M |

### 5.12 Oberfläche und Design

| ID | Anforderung | Prio |
| --- | --- | :-: |
| UI-01 | Die UI nutzt die Tokens aus `tokens.css` des MDU-Design-Systems unverändert (Farben, Typografie, Abstände, Radien, Schatten) und baut Komponenten nach dessen Vorgaben: Kopfzeile, Seitenleiste mit Baum, Steuerelemente, Karten/Kacheln wie im Dashboard, Callouts für Hinweise. | M |
| UI-02 | Hell-, Dunkel- und Automatikmodus wie bei MDU, mit demselben Umschalter. | M |
| UI-03 | Tastaturfokus wie bei MDU: nur `:focus-visible`, `outline: 2px solid var(--link); outline-offset: 3px`, Rundungen und Inset-Ring nach den MDU-Regeln. | M |
| UI-04 | Barrierefreiheit nach dem Kapitel „Barrierefreiheit“ des Design-Systems: Kontrast, reduzierte Bewegung, vollständige Tastaturbedienung, Live-Region für Upload-Status und Kopierbestätigung. | M |
| UI-05 | Responsives Layout ab 360 px Breite; Hochladen über Dateiauswahl auch auf Mobilgeräten. | S |
| UI-06 | Alle UI-Dateien sind in die Anwendung eingebettet; keine CDNs, keine externen Schriften. | M |
| UI-07 | Ein Test im Repository prüft, dass `tokens.css` mit der Version im Design-System übereinstimmt (z. B. über einen Hash in einer Pin-Datei), und ein Werkzeug aktualisiert die Kopie. | M |

Seiten: Anmeldung (später), Projekte, Projekt (Baum + Raster/Liste + Upload-Fläche), Asset-Details, Papierkorb, Projekteinstellungen (Mitglieder, Limits, Formatkonvertierung), Profil (Passwort, Tokens, Integrationen), Admin (Benutzer, Projekte, Audit-Log, Einstellungen).

## 6. Nichtfunktionale Anforderungen

| ID | Anforderung |
| --- | --- |
| NF-01 | .NET 10, ASP.NET Core Minimal APIs mit Request Delegate Generator, `PublishAot=true`, ohne Trim- oder AOT-Warnungen. Keine Razor Pages, kein MVC, kein reflektionsbasiertes JSON (System.Text.Json Source Generator). |
| NF-02 | Datenzugriff mit `Microsoft.Data.Sqlite` und handgeschriebenem SQL (kein EF Core), Schema-Migrationen versioniert im Code. SQLite im WAL-Modus. |
| NF-03 | Bildverarbeitung mit NetVips/libvips (NetVips 3.2.0, NetVips.Native 8.18.7 oder neuer). Der [AOT-Spike](Architecture/NetVips-AOT-Spike.md) hat die Tauglichkeit unter Native AOT bestätigt (siehe R-01). |
| NF-04 | Nur .NET als Build-, Test- und Generatorwerkzeug, kein Node/npm und kein Python – wie im MDU-Repository. Tests mit xUnit. |
| NF-05 | Dateien werden unter einem inhaltsadressierten Pfad abgelegt (`blobs/ab/cd/<sha256>`), Metadaten verweisen darauf. Schreiben erfolgt atomar (temporäre Datei + Umbenennen). |
| NF-06 | Backup: Ein Kopieren des Datenverzeichnisses nach `sqlite3 .backup` bzw. einem eingebauten Befehl `mdu-assets backup <ziel>` ergibt eine konsistente Sicherung. |
| NF-07 | Leistung: Ausgelieferte Originale und zwischengespeicherte Fassungen mit p95 < 20 ms (ohne Netzwerk) auf einem kleinen VPS; Kaltstart < 200 ms; Leerlauf-Speicher < 80 MB. |
| NF-08 | Konfiguration über `appsettings.json` und Umgebungsvariablen (`MDU_ASSETS_…`), u. a. Datenverzeichnis, öffentliche URL, UI-URL, Limits, vertrauenswürdige Proxies. |
| NF-09 | Datenschutz: keine Cookies auf öffentlichen Links, keine Speicherung personenbezogener Abrufdaten (STA-06), Metadaten aus Bildern entfernt (UP-08). |
| NF-10 | Repository-Konventionen wie bei MDU: Conventional Commits, git-cliff für Versionen und Changelog, Release-Workflow von `main`. |

## 7. Betrieb und Auslieferung

| ID | Anforderung | Prio |
| --- | --- | :-: |
| DEP-01 | Eigenes Repository `mdu-assets` (lokal `/Volumes/SSK/Code/mdu-assets`). | M |
| DEP-02 | Multi-Arch-Docker-Image `capjan/mdu-assets` (linux/amd64, linux/arm64) mit libvips, als Nicht-Root-Benutzer, ein Volume `/data`. | M |
| DEP-03 | Native Binaries für linux-x64, linux-arm64 und osx-arm64 als Release-Assets (Windows ist derzeit kein Ziel); libvips (17–18 MB) liegt als native Bibliothek daneben (Archiv statt Einzeldatei), zusammen mit den Lizenztexten. Auf macOS werden Programm und `libvips.42.dylib` signiert und notarisiert wie bei MDU. | M |
| DEP-04 | Beispielkonfiguration für den Betrieb hinter einem Reverse Proxy (Caddy oder nginx) mit getrennten Hosts für UI und öffentliche Links. | S |

## 8. Datenmodell (Skizze)

```text
User(id, name, display_name, password_hash, is_admin, is_locked, must_change_password, created_at)
ApiToken(id, user_id, name, prefix, hash, project_id?, scope, expires_at?, last_used_at?, created_at)
Project(id, slug, name, description, quota_bytes?, max_file_bytes?, convert_formats, created_at)
Membership(project_id, user_id, role)
Asset(id /*public base62*/, project_id, file_name, alt_text, description,
      pair_asset_id?, pair_role?, current_version, deleted_at?, created_by, created_at)
AssetVersion(asset_id, number, blob_sha256, content_type, size, width, height, created_by, created_at)
Tag(asset_id, name)
AssetStats(asset_id, hit_count, last_hit_at)
AssetReferrer(asset_id, origin, path, hit_count, last_hit_at)
AuditEntry(id, at, user_id?, project_id?, action, target, details_json, ip)
```

## 9. Akzeptanzkriterien der Kernabläufe

1. **Screenshot einfügen:** Angemeldet in einem Projekt einen Screenshot mit ++cmd+v++ einfügen → innerhalb von 2 s steht ein Markdown-Snippet mit Link der Form `/i/<id>/screenshot-….png` bereit und lässt sich mit einem Klick oder ++c++ kopieren. Der Link zeigt das Bild in einer neuen, nicht angemeldeten Browsersitzung.
2. **Neu ordnen:** Dem Asset andere Tags geben und einen Tag umbenennen → der kopierte Link liefert weiterhin dasselbe Bild.
3. **Ersetzen:** Eine neue Datei auf die Detailansicht ziehen → der Link zeigt nach Ablauf der Cache-Zeit das neue Bild, Version 1 bleibt unter `/v1/` abrufbar und lässt sich wiederherstellen.
4. **Zusammenarbeit:** Ein Owner fügt einen zweiten Benutzer als Editor hinzu → dieser sieht das Projekt, kann hochladen, aber keine Mitglieder verwalten; ein Viewer sieht keine Upload-Fläche und erhält bei API-Uploads `403`.
5. **Admin:** Ein Admin sieht alle Projekte, auch ohne Mitgliedschaft, und kann Benutzer anlegen und sperren; ein gesperrter Benutzer und seine Tokens werden sofort abgewiesen.
6. **API:** `curl -H "Authorization: Bearer …" --data-binary @bild.png -H "Content-Type: image/png" "…/api/v1/projects/docs/assets?name=bild.png"` liefert `201` mit `url` und `markdown`; `GET /api/v1/assets/{id}` liefert Metadaten und Statistik.
7. **Statistik:** Drei Abrufe aus einem normalen Browser erhöhen den Zähler um 3 (bzw. um die Zahl der nicht aus dem Cache bedienten Anfragen) und setzen den letzten Abruf; Abrufe mit Googlebot-User-Agent und Ansichten in der UI ändern den Zähler nicht.
8. **mdu:** `mdu assets upload bild.png` gibt eine Markdown-Zeile aus; nach dem Löschen des Assets meldet `mdu lint` den Link als `MDU029`.
9. **Design:** Hell- und Dunkelmodus entsprechen den Tokens des Design-Systems; ein Tab-Durchlauf durch Kopfzeile, Baum, Raster und Detailansicht zeigt überall den MDU-Fokusring.

## 10. Ausbaustufen

| Stufe | Inhalt |
| --- | --- |
| 1 (erste Version) | Alle Anforderungen mit Priorität M und S |
| 2 | Login-Formular mit Session (AUTH-09), Passkeys (AUTH-11) |
| 3 | OIDC/SSO (AUTH-10) |
| Ideen, nicht entschieden | Einladungslinks; S3-kompatibler Speicher; Paste-to-Asset im Editor von `mdu serve`; Befehl, der Asset-Links in lokale Dateien übernimmt (Offline/PDF) oder lokale Bilder hochlädt und Links ersetzt; Bildbearbeitung in der UI (Zuschneiden, Markierungen, Unkenntlichmachen); Abrufverlauf pro Tag als Diagramm; Webhooks bei Uploads; weitere Dateitypen (PDF, Video) |

## 11. Risiken

| ID | Risiko | Umgang |
| --- | --- | --- |
| R-01 | NetVips unter Native AOT und AVIF-Kodierung im mitgelieferten libvips. | **Durch [Spike](Architecture/NetVips-AOT-Spike.md) entschärft:** 0 AOT-Warnungen, alle Prüfungen bestanden auf osx-arm64 und Linux. Hinweis: Multi-Arch-Builds nicht unter Emulation, sondern nativ oder per Cross-Compile. |
| R-02 | Native Binaries sind wegen libvips keine Einzeldatei. | Bestätigt. Release als Archiv; Docker (chiselled `runtime-deps` reicht) als empfohlener Weg. |
| R-07 | AVIF-Kodierung ist langsam (≈ 1 s für 1600×900) und speicherhungrig. | Der Server erzeugt kein AVIF mehr, sondern WebP beim Upload (≈ 0,2 s, UP-11); begrenzte Parallelität, libvips-Cache begrenzt. |
| R-03 | Öffentliche Bilder mit nicht erratbaren IDs sind keine Zugriffskontrolle; einmal geteilte Links lassen sich nicht zurückholen. | In UI und Doku klar benennen; Löschen liefert sofort 410. |
| R-04 | SVG-Uploads sind ein XSS-Vektor. | Bereinigung (UP-07), CSP mit `sandbox` (DLV-05), getrennter Host (LNK-04). |
| R-05 | Abrufzahlen sind durch Caches und CDNs systematisch zu niedrig. | Kurze `max-age` (DLV-02), Grenze in der UI erklären. |
| R-06 | Basic Auth erlaubt kein sauberes Abmelden im Browser. | Erledigt: Anmeldeseite mit Session ([ADR-0021](Architecture/Architecture-Decisions/0021-Sign-In-Page-and-Session-Cookie.md)). |

## 12. Offene Fragen

| ID | Frage |
| --- | --- |
| Q-01 | Dürfen normale Benutzer selbst Projekte anlegen, oder nur Admins? (PRJ-01 sieht eine Admin-Einstellung vor.) **Entschieden:** Admins immer, andere Benutzer nur mit der Admin-Einstellung, die anfangs aus ist ([ADR-0014](Architecture/Architecture-Decisions/0014-Project-Creation-by-Admin-Setting.md)). |
| Q-02 | Soll eine mit Größenparameter oder Format konvertierte Fassung beim Ersetzen sofort verworfen werden (ja, vorgesehen) – und soll der Server bei CDN-Betrieb einen Purge-Hook anbieten? **Entschieden:** Fassungen bleiben für die versionsfesten Links, kein Purge-Hook ([ADR-0016](Architecture/Architecture-Decisions/0016-Version-Retention-and-Cache-Purging.md)). |
| Q-03 | Soll MDU beim Rendern für Bilder vom Asset-Server `referrerpolicy="no-referrer-when-downgrade"` setzen, damit der Server einzelne Seiten statt nur Sites sieht? Das ist eine Datenschutzabwägung für Leser. **Entschieden:** nein, MDU behält die Voreinstellung der Browser ([ADR-0017](Architecture/Architecture-Decisions/0017-Referrer-Policy-for-MDU-Pages.md)). |
| Q-04 | Wie viele Versionen pro Asset sollen standardmäßig aufbewahrt werden (unbegrenzt vs. z. B. 20)? **Entschieden:** unbegrenzt, Owner können pro Projekt eine Grenze setzen ([ADR-0016](Architecture/Architecture-Decisions/0016-Version-Retention-and-Cache-Purging.md)). |
| Q-05 | Sollen Asset-IDs bei einem Import in eine andere Instanz kollidieren dürfen (dann neue IDs und Mapping-Datei) oder schlägt der Import fehl? **Entschieden:** neue IDs für belegte, Zuordnung in der Antwort ([ADR-0018](Architecture/Architecture-Decisions/0018-Import-Id-Collisions.md)). |
| Q-06 | ~~Welcher Hostname ist vorgesehen?~~ Geklärt: Die Hostnamen für Oberfläche und öffentliche Links sind Konfiguration pro Installation (LNK-04, NF-08), es gibt keinen festen Hostnamen. |
