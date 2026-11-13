---
navigation.order: 13
description: Spike report (German) that verifies NetVips and libvips under .NET Native AOT on every target platform.
---

# NetVips AOT Spike

Stand: 13.11.2026 · Zu Risiko R-01 in den [Anforderungen](../Requirements.md#11-risiken) · Code in [`Source/Spikes/NetVipsAot`](https://github.com/capjan/mdu-assets/tree/main/Source/Spikes/NetVipsAot)

## Frage

Funktioniert NetVips/libvips in einer .NET-10-Native-AOT-Anwendung für alles, was der Asset Server mit Bildern tun muss, und liefern die mitgelieferten libvips-Builds AVIF?

## Ergebnis

**Ja.** NetVips 3.2.0 mit NetVips.Native 8.18.7 (libvips 8.18.7) baut mit `PublishAot=true` ohne eine einzige Trim- oder AOT-Warnung und besteht alle 16 Prüfungen als natives Programm auf macOS arm64 und Linux arm64 und x64 (`runtime-deps:10.0-noble-chiseled`, ohne zusätzliche Pakete). Windows ist derzeit kein Zielsystem und wurde nicht geprüft.

| Plattform | AOT-Build | Warnungen | Prüfungen | Wie geprüft |
| --- | --- | --- | --- | --- |
| osx-arm64 | ✓ | 0 | 16/16 | nativ auf dem Mac |
| linux-arm64 | ✓ | 0 | 16/16 | Docker, gebaut in `sdk:10.0-noble`, ausgeführt in `runtime-deps:10.0-noble-chiseled` |
| linux-x64 | ✓ | 0 | 16/16 | Cross-Compile in einem arm64-SDK-Container (Ubuntu-amd64-Pakete), ausgeführt in `runtime-deps:10.0-noble-chiseled` unter x64-Emulation; Zeiten daher etwa zehnmal langsamer und nicht aussagekräftig |

## Geprüfte Fälle

| Prüfung | Anforderung | Ergebnis (osx-arm64, AOT) |
| --- | --- | --- |
| libvips initialisiert, Version | – | 8.18.7 |
| Lader und Speicherer vorhanden | UP-06, DLV-04 | png, jpeg, webp, heif (AVIF), gif speichern + laden; svg laden |
| Dateityp am Inhalt erkennen | UP-06 | PNG erkannt, HTML abgelehnt (`Image.FindLoadBuffer`) |
| Nur Kopfdaten lesen (Pixellimit) | OPS-01 | Breite/Höhe ohne Dekodieren |
| Abgeschnittene Datei | – | `VipsException`, kein Absturz |
| EXIF/GPS entfernen, Ausrichtung anwenden, ICC behalten | UP-08 | `Autorot()` + `keep: ForeignKeep.Icc`: EXIF und GPS weg, 1600×900 → 900×1600, ICC-Profil bleibt |
| Vorschaubild 320 px | DLV-06 | 21 ms inkl. WebP |
| `?w=800`, nie vergrößern | DLV-03 | `ThumbnailBuffer(…, size: Down)`: 800×450; 4000 bleibt 1600 |
| WebP kodieren | DLV-04 | 1600×900: 4,0 MB PNG → 478 KB WebP in 167 ms |
| AVIF kodieren | DLV-04 | 1600×900: 4,0 MB PNG → 220 KB AVIF in **1,0–1,3 s** (effort 4) |
| AVIF als Upload dekodieren | UP-06 | ✓ |
| Animiertes GIF verkleinern | DLV-03 | alle 4 Frames bleiben erhalten (`n=-1`) |
| SVG rendern (Vorschau) | DLV-06 | ✓ (librsvg ist enthalten) |
| SVG mit `file://`-Verweis | UP-07 | rendert ohne Fehler; ob librsvg die Datei liest, prüft der Spike nicht |
| 8 parallele Vorschauen + WebP | NF-07 | ca. 100–140 ms gesamt |
| Cache und Parallelität begrenzen | NF-07 | `Cache.Max/MaxMem/MaxFiles`, `NetVips.Concurrency` funktionieren |

Die Testbilder entstehen im Programm (Verlauf + Rauschen + sRGB-Profil, JPEG mit selbst gebautem EXIF-Block mit Ausrichtung 6 und GPS-IFD), es gibt keine Testdateien.

## Größen und Speicher

| | Größe |
| --- | --- |
| AOT-Programm (osx-arm64 / linux-arm64) | 2,4 MB / 2,6 MB |
| `libvips.42.dylib` / `libvips.so.42` | 17 MB / 18 MB |
| Höchster Arbeitsspeicher des Spike-Laufs (macOS) | ca. 185 MB, vor allem durch AVIF und die Rauschbilder |

## Folgerungen für die Anforderungen

1. **R-01 ist für macOS und Linux erledigt.** NetVips bleibt die Bildbibliothek (NF-03); eine eigene P/Invoke-Bindung ist nicht nötig.
2. **AVIF ist zu langsam für die Anfrage.** Eine AVIF-Fassung braucht bei 1600×900 rund eine Sekunde. DLV-04 sollte AVIF-Fassungen im Hintergrund erzeugen (nach dem Upload bzw. beim ersten Abruf in eine Warteschlange) und bis dahin WebP oder das Original ausliefern. WebP (~170 ms) und Vorschaubilder (~20 ms) gehen auch synchron.
3. **R-02 bestätigt:** Die Auslieferung ist das AOT-Programm plus eine native Bibliothek von 17–18 MB im selben Ordner, keine Einzeldatei. Für das Docker-Image reicht das chiselled-Basisimage von `runtime-deps`, libvips braucht keine weiteren Pakete.
4. **macOS-Signierung:** `libvips.42.dylib` muss im Release-Workflow ebenfalls mit der Developer ID signiert werden, bevor notarisiert wird (zusätzlich zum Programm).
5. **SVG nie direkt rendern:** Hochgeladene SVGs zuerst bereinigen (UP-07), erst danach mit libvips Vorschauen erzeugen.
6. **Speicher begrenzen:** libvips-Operationscache ausschalten oder klein halten und `NetVips.Concurrency` sowie die Zahl gleichzeitiger Bildjobs begrenzen (z. B. ein `SemaphoreSlim`), sonst wächst der Speicher bei parallelen Uploads.
7. **Multi-Arch-Builds nicht unter Emulation:** Das .NET SDK stürzt unter x64-Emulation (QEMU in Colima) mit Exit-Code 139 ab. Das Docker-Image und die Linux-Binaries müssen daher auf nativen Runnern je Architektur gebaut oder per Cross-Compile erzeugt werden (wie im Spike für linux-x64); nur das fertige AOT-Programm läuft auch emuliert.
8. **Lizenz:** libvips steht unter LGPL-2.1+, die mitgelieferten Bibliotheken unter verschiedenen freien Lizenzen. Da libvips dynamisch geladen wird und austauschbar neben dem Programm liegt, ist das mit LGPL vereinbar; die Lizenztexte gehören in die Release-Archive und das Docker-Image.

## Offen

- **librsvg und externe Verweise:** Gezielt prüfen, ob `svgload` lokale Dateien oder URLs liest. Für die Anforderungen ändert das nichts, weil SVGs vorher bereinigt werden.
- **Sehr große Bilder:** Verhalten bei Dekompressionsbomben (z. B. 30 000 × 30 000 PNG) mit Pixellimit vor dem Dekodieren prüfen.

## Selbst ausführen

```sh
cd Source/Spikes/NetVipsAot
dotnet publish -c Release -r osx-arm64 -o out/osx-arm64
out/osx-arm64/NetVipsAot
```

Linux in der Architektur des Docker-Hosts (mit laufendem Docker, z. B. Colima), ebenfalls im Ordner `Source/Spikes/NetVipsAot`:

```sh
docker build -t netvips-aot-spike .
docker run --rm netvips-aot-spike
```

Das `Dockerfile` baut nur für die Architektur des Hosts; für die jeweils andere Architektur siehe Folgerung 7.

Der Exit-Code ist die Zahl der fehlgeschlagenen Prüfungen.
