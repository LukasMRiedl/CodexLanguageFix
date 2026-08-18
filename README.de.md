# Codex Language Fix

[English](README.md) · **Deutsch**

Eine eigenständige Windows-App für die Ein-Klick-Korrektur von Prompts in der OpenAI-Codex-Desktop-App und in Antigravity. Als Anbieter stehen die kostenlose öffentliche LanguageTool-API und GPT-5.6 Luna mit niedriger Denkstufe über die offizielle Codex-ChatGPT-OAuth-Sitzung zur Verfügung. Die App benötigt weder ein Codex-Plugin noch eine Browsererweiterung, einen API-Schlüssel oder einen eigenen Server.

> Dieses Projekt ist unabhängig und nicht mit OpenAI, Google oder LanguageTool verbunden.

## Funktionen

- Ein unaufdringlicher `Aa`-Knopf erscheint dynamisch in der aktuellen Eingabeleiste.
- Ein Klick prüft Rechtschreibung, Grammatik, Zeichensetzung und Stil.
- LanguageTool bleibt der Standardanbieter. OpenAI Luna lässt sich im Infobereich auswählen und wird ausschließlich als `gpt-5.6-luna` mit niedriger Denkstufe verwendet.
- Luna nutzt den offiziellen Codex App Server und dessen verwalteten ChatGPT-OAuth-Ablauf. Die App liest oder speichert niemals OAuth-Tokens.
- Deutsch und Englisch werden automatisch erkannt; bevorzugt werden `de-DE` und `en-US`.
- Die gesamte Oberfläche ist auf Deutsch und Englisch verfügbar. Standardmäßig folgt sie der Windows-Anzeigesprache und lässt sich jederzeit über das Menü im Infobereich umschalten.
- Markdown-Codeblöcke, Inline-Code, URLs, E-Mail-Adressen, Pfade, Befehlsoptionen und typische Codebezeichner bleiben unverändert.
- Die Zwischenablage wird weder gelesen noch verändert.
- Der Prompt wird nur nach einem ausdrücklichen Klick übertragen.
- Änderungen lassen sich direkt rückgängig machen.
- Unterschiedliche Fensterbreiten, DPI-Skalierungen sowie Chat-, Work- und Planen-Layouts werden dynamisch erkannt.
- Kein Hintergrunddienst und keine automatische Updatefunktion.

## Installation

1. Lade `CodexLanguageFix-win-x64.zip` aus dem [aktuellen Release](https://github.com/LukasMRiedl/CodexLanguageFix/releases/latest) herunter.
2. Entpacke die ZIP-Datei in einen dauerhaft verfügbaren Ordner.
3. Starte `CodexLanguageFix.exe`.
4. Bestätige beim ersten Start den Hinweis, dass Prompts nur nach einem Klick an den ausgewählten Anbieter übertragen werden.

Die App benötigt keine Administratorrechte. Über das Symbol im Infobereich lassen sich der Korrekturanbieter auswählen, OpenAI verbinden, der ausgewählte Anbieter testen, die App pausieren oder beenden und der automatische Windows-Start ändern.

LanguageTool funktioniert ohne zusätzliche Software. Der optionale Luna-Anbieter setzt eine installierte, aktuelle offizielle Codex-Laufzeit und ein in Codex verfügbares ChatGPT-Konto voraus. Ist Luna Low in Codex nicht aktiviert, bleibt der Prompt unverändert; Codex Language Fix wechselt niemals still auf ein anderes Modell, eine andere Denkstufe oder einen anderen Anbieter.

Die veröffentlichte EXE ist derzeit nicht codesigniert. Windows kann deshalb beim ersten Start einen Sicherheitshinweis anzeigen.

## Datenschutz

Erst nach einem Klick sendet die App den aktuellen Prompt an den ausgewählten Anbieter. LanguageTool-Anfragen gehen per HTTPS an `https://api.languagetool.org/v2/check`. Luna-Anfragen verwenden einen ephemeren Codex-App-Server-Thread mit `gpt-5.6-luna`, niedriger Denkstufe, einer schreibgeschützten Sandbox ohne Netzwerk und ausdrücklichem Werkzeugverbot. Jede beobachtete Werkzeugaktion verwirft die gesamte Antwort. Technische Abschnitte werden vor der Übertragung an Luna lokal durch zufällige Platzhalter ersetzt und erst nach strenger Validierung wiederhergestellt.

Prompttext, korrigierter Text, Anbieterantworten, OAuth-Tokens, E-Mail-Adressen und Kontodaten werden niemals protokolliert. Lokale Diagnoseprotokolle enthalten ausschließlich technische Metadaten wie Anbieter, Zeitpunkt, Dauer, Statuscode, Zeichen- und Änderungsanzahl und werden nach sieben Tagen entfernt.

Weitere Einzelheiten stehen in [PRIVACY.md](PRIVACY.md).

## Anbietergrenzen

LanguageTool wird lokal auf höchstens 20 Anfragen und 75.000 Zeichen pro Minute begrenzt. Beide Anbieter akzeptieren höchstens 20.000 Zeichen pro Korrektur. Die App erzeugt keine Warteschlange und keine Hintergrundkorrekturen. Für Luna gelten außerdem die Grenzen des mit Codex verbundenen ChatGPT-Kontos.

## Bauen und testen

Voraussetzung ist das .NET 8 SDK unter Windows.

```powershell
dotnet test .\CodexLanguageFix.sln
dotnet publish .\src\CodexLanguageFix\CodexLanguageFix.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true
```

Die selbstenthaltende Einzeldatei liegt anschließend unter:

`src\CodexLanguageFix\bin\Release\net8.0-windows\win-x64\publish\CodexLanguageFix.exe`

## Unterstützte Anwendungen

- OpenAI Codex Desktop für Windows
- Antigravity für Windows

Die Erkennung verwendet zugängliche UI-Strukturen und keine fest codierten Bildschirmkoordinaten.

## Rechtlicher Status

Der Quellcode wird öffentlich bereitgestellt. Es wurde noch keine Open-Source-Lizenz erteilt. Ohne eine ausdrückliche Lizenz gelten die gesetzlichen Urheberrechtsbestimmungen.
