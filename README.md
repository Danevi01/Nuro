# Nuro

Nuro ist ein lokaler KI-Assistent für Windows im Stil von Claude. Er läuft mit [Ollama](https://ollama.com) auf deiner NVIDIA-Grafikkarte und arbeitet selbstständig mit Werkzeugen: Er liest und schreibt Dateien in freigegebenen Ordnern, pflegt sein eigenes GitHub-Repository und behält die Grafikkarte im Blick.

## Installation

1. Unter **[Releases](../../releases/latest)** die neueste `Nuro.exe` herunterladen.
2. In einen eigenen Ordner legen, z. B. `C:\Tools\Nuro\Nuro.exe`, und starten.

Weitere Voraussetzungen:
- **Ollama** muss installiert sein. Läuft es nicht, startet Nuro es selbst.
- **WebView2** ist bei Windows 10/11 schon dabei.
- Eine **.NET-Installation** ist nicht nötig, sie steckt in der exe.

Beim ersten Start meldet Windows SmartScreen eventuell „unbekannter Herausgeber“, weil die exe nicht signiert ist. Klick auf „Weitere Informationen“ und dann auf „Trotzdem ausführen“.

## Einrichten (Einstellungen ⚙)

- **Freigegebene Ordner:** In diesen Ordnern darf Nuro ohne Rückfrage lesen, schreiben, verschieben und löschen. Außerhalb davon hat Nuro keinen Zugriff.
- **GitHub-Token:** Unter [github.com/settings/personal-access-tokens/new](https://github.com/settings/personal-access-tokens/new) einen *Fine-grained token* erstellen.
  - Repository access: *Only select repositories* → `Danevi01/Nuro`
  - Permissions: **Contents** Read and write, **Issues** Read and write, **Actions** Read-only (Metadata wird automatisch gesetzt)

  Den Token in Nuro eintragen und auf „Verbindung testen“ klicken. Er wird mit Windows-Verschlüsselung (DPAPI) nur für dein Benutzerkonto gespeichert.

## Funktionen

- **Chat:** mehrere Chats, Suche, automatische Titel, Live-Antworten, Markdown mit Code-Hervorhebung, Bilder und Dateien anhängen, Nachricht bearbeiten, Antwort neu erzeugen, Export
- **Selbstständiges Arbeiten:** Nuro plant, ruft Werkzeuge auf, prüft die Ergebnisse und macht weiter, bis die Aufgabe erledigt ist (einstellbar, Standard: max. 25 Schritte). Jeder Schritt wird als aufklappbare Karte angezeigt. Mit 🔧 im Eingabefeld schaltest du die Werkzeuge an oder aus.
- **Werkzeuge:**

  | Bereich | Werkzeuge |
  |---|---|
  | Dateien | Ordner ansehen, lesen, schreiben, gezielt ändern, anlegen, verschieben, löschen, suchen (Name und Inhalt) |
  | GitHub | Dateien auflisten, lesen, schreiben und ändern (mit direktem Commit), löschen, Commits, Issues (lesen, anlegen, kommentieren, schließen), Build-Status |
  | Grafikkarte | Auslastung, VRAM, Temperatur, Stromverbrauch, geladene Modelle |

- **GPU-Anzeige:** oben rechts, mit Details zum geladenen Modell beim Darüberfahren
- **Selbst-Update:** Jeder Commit auf `main` baut automatisch eine neue `Nuro.exe` (GitHub Actions → Releases). Gibt es eine neuere Version, zeigt Nuro oben den Knopf **⬆ Update** und tauscht sich auf Klick selbst aus. So kann Nuro seinen eigenen Code im Repo verbessern und danach die neue Version installieren.

> **Hinweis:** Die Werkzeuge brauchen ein Modell mit Werkzeug-Unterstützung. Das Modell muss in Ollama die Fähigkeit *tools* haben; die Plakette „Werkzeuge“ neben der Modellwahl zeigt es an.

## Aufbau

| Pfad | Zweck |
|---|---|
| `src/Nuro/wwwroot/index.html` | Oberfläche und Arbeitsschleife (spricht mit Ollama auf `127.0.0.1:11434`) |
| `src/Nuro/MainForm.cs` | Fenster mit WebView2 |
| `src/Nuro/LocalServer.cs` | lokaler Server auf `127.0.0.1:8765`; liefert die Oberfläche aus und stellt die Werkzeuge bereit (nur mit Sitzungs-Token erreichbar) |
| `src/Nuro/FileTools.cs` | Dateizugriff, auf freigegebene Ordner beschränkt |
| `src/Nuro/GitHubTools.cs` | GitHub-REST-API für das eigene Repo |
| `src/Nuro/GpuTools.cs` | NVIDIA-Status über `nvidia-smi` |
| `src/Nuro/Updater.cs` | Selbst-Update aus den Releases |
| `.github/workflows/build.yml` | baut die exe und veröffentlicht sie als Release |

Selbst bauen: `dotnet publish src/Nuro/Nuro.csproj -c Release -o out` (.NET 8 SDK, Windows)

## Daten

Alles bleibt auf deinem PC:
- **Chats:** `%LOCALAPPDATA%\Nuro\webview`
- **Einstellungen:** `%LOCALAPPDATA%\Nuro\config.json`
