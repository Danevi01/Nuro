# Nuro

Nuro ist eine Chat-App für Windows im Stil von Claude, die komplett lokal mit [Ollama](https://ollama.com) läuft. Du musst nichts installieren außer Ollama selbst: Die App nutzt PowerShell und Microsoft Edge, die bei Windows schon dabei sind.

## Starten

1. Repo herunterladen (**Code → Download ZIP**) und an einen festen Ort entpacken, z. B. `C:\Tools\Nuro`.
2. Einmal `Verknuepfung-erstellen.bat` ausführen. Danach liegt **Nuro** auf dem Desktop und im Startmenü.
3. Über die Verknüpfung starten (oder mit `Start.bat`).

Die App öffnet sich als eigenes Fenster. Wenn Ollama nicht läuft, wird es automatisch gestartet. Schließt du das Fenster, beendet sich auch der Hintergrund-Server.

## Funktionen

- **Chats:** mehrere Chats mit Seitenleiste, Suche, Umbenennen und Löschen; automatische Titel, sortiert nach Datum
- **Antworten:** erscheinen live (Streaming) und lassen sich mit dem Knopf oder mit Esc stoppen
- **Darstellung:** Markdown mit Code-Hervorhebung und Kopieren-Knopf, Tabellen, Listen
- **Gedankengang:** einklappbar bei Modellen mit Denkmodus, mit Schalter Auto / An / Aus
- **Anhänge:** Bilder (bei Modellen mit Bildverständnis) sowie Text- und Code-Dateien, per Büroklammer, Hineinziehen oder Strg+V
- **Nachrichten:** eigene Nachricht bearbeiten und neu senden, Antwort neu erzeugen
- **Modelle:** Auswahl oben; die Fähigkeiten des Modells werden als Plakette angezeigt
- **Einstellungen:** eigene Anweisungen (System-Prompt), Temperatur, Kontextlänge, helles oder dunkles Design, wie lange das Modell geladen bleibt
- **Sichern:** einzelne Chats als Markdown exportieren, alle Chats sichern und wiederherstellen

## Aufbau

| Datei | Zweck |
|---|---|
| `Nuro.ps1` | Mini-Webserver auf `http://127.0.0.1:8765` (nur lokal erreichbar); öffnet das App-Fenster und startet bei Bedarf Ollama |
| `app/index.html` | die komplette Oberfläche; spricht direkt mit Ollama auf `http://127.0.0.1:11434` |
| `Start.bat` | startet die App ohne Konsolenfenster |
| `Verknuepfung-erstellen.bat/.ps1` | legt die Verknüpfungen auf dem Desktop und im Startmenü an |

## Daten

Alles bleibt auf deinem PC. Die Chats liegen im App-Profil unter `%LOCALAPPDATA%\Nuro\browser-profil`.

## Probleme?

- **„Ollama nicht erreichbar“:** Ollama starten oder in den Einstellungen die Adresse prüfen.
- **Ollama läuft auf einem anderen Rechner:** dort `OLLAMA_ORIGINS` passend setzen.
- **Port 8765 ist belegt:** in `Nuro.ps1` oben `$Port` ändern. Achtung: Mit einer anderen Port-Nummer werden die Chats getrennt gespeichert.
