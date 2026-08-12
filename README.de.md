# Codex Language Fix

[English](README.md) · **Deutsch**

Eine eigenständige Windows-App für die Ein-Klick-Korrektur von Prompts in der OpenAI-Codex-Desktop-App und in Antigravity. Die App verwendet Windows UI Automation und die kostenlose öffentliche LanguageTool-API. Sie benötigt weder ein Codex-Plugin noch eine Browsererweiterung oder einen eigenen Server.

> Dieses Projekt ist unabhängig und nicht mit OpenAI, Google oder LanguageTool verbunden.

## Funktionen

- Ein unaufdringlicher `Aa`-Knopf erscheint dynamisch in der aktuellen Eingabeleiste.
- Ein Klick prüft Rechtschreibung, Grammatik, Zeichensetzung und Stil.
- Deutsch und Englisch werden automatisch erkannt; bevorzugt werden `de-DE` und `en-US`.
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
4. Bestätige beim ersten Start den Hinweis, dass Prompts nur nach einem Klick an LanguageTool übertragen werden.

Die App benötigt keine Administratorrechte. Sie kann über das Symbol im Infobereich pausiert oder beendet werden. Der automatische Windows-Start lässt sich dort ebenfalls ändern.

Die veröffentlichte EXE ist derzeit nicht codesigniert. Windows kann deshalb beim ersten Start einen Sicherheitshinweis anzeigen.

## Datenschutz

Erst nach einem Klick sendet die App den aktuellen Prompt per HTTPS an `https://api.languagetool.org/v2/check`. Prompttexte und API-Antworten werden nicht protokolliert. Lokale Diagnoseprotokolle enthalten nur technische Metadaten wie Zeitpunkt, Dauer, Statuscode, Zeichen- und Trefferanzahl und werden nach sieben Tagen entfernt.

Weitere Einzelheiten stehen in [PRIVACY.md](PRIVACY.md).

## Öffentliche API-Grenzen

Die App begrenzt die Nutzung lokal auf höchstens 20 Anfragen und 75.000 Zeichen pro Minute sowie 20.000 Zeichen pro Anfrage. Sie erzeugt keine Warteschlange, keine Hintergrundprüfungen und keine automatischen Wiederholungen.

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
