# Codex Language Fix

[English](README.md) · **Deutsch**

Eine eigenständige Windows-App für die Ein-Klick-Korrektur von Prompts in der OpenAI-Codex-Desktop-App und in Antigravity. Als Anbieter stehen die kostenlose öffentliche LanguageTool-API und GPT-5.6 Luna über die offizielle Codex-ChatGPT-OAuth-Sitzung zur Verfügung. Die App benötigt weder ein Codex-Plugin noch eine Browsererweiterung, einen OpenAI-API-Schlüssel, zusätzliche API-Abrechnung oder einen eigenen Server.

> Dieses Projekt ist unabhängig und nicht mit OpenAI, Google oder LanguageTool verbunden.

Der aktuelle Quellstand enthält den **Release-Kandidaten 1.3.1** für formaterhaltendes Einsetzen und die Knopfpositionierung. Die automatischen Formatprüfungen beider Anbieter sind bestanden; die Abnahme in den echten Eingabefeldern ist noch offen. Siehe [Prüfbericht](docs/format-repair-2026-09-08.md). Es ist noch kein vollständig abgenommener stabiler Release.

## Funktionen

- Ein unaufdringlicher `Aa`-Knopf erscheint dynamisch in der aktuellen Eingabeleiste.
- Ein Klick prüft Rechtschreibung, Grammatik, Zeichensetzung und Stil.
- LanguageTool bleibt der Standardanbieter. OpenAI Luna lässt sich im Infobereich auswählen. Die Produktion verwendet das qualifizierte Profil `baseline / none / full-v1` von `gpt-5.6-luna` mit Fast Mode; experimentelle Profile werden niemals automatisch aktiviert.
- Luna nutzt den offiziellen Codex App Server und dessen verwalteten ChatGPT-OAuth-Ablauf. Die App liest oder speichert niemals OAuth-Tokens.
- Deutsch und Englisch werden automatisch erkannt; bevorzugt werden `de-DE` und `en-US`.
- Die gesamte Oberfläche ist auf Deutsch und Englisch verfügbar. Standardmäßig folgt sie der Windows-Anzeigesprache und lässt sich jederzeit über das Menü im Infobereich umschalten.
- Markdown-Codeblöcke, Inline-Code, URLs, E-Mail-Adressen, Pfade, Befehlsoptionen und typische Codebezeichner bleiben unverändert.
- Die Zwischenablage wird weder gelesen noch verändert.
- Der Prompt wird nur nach einem ausdrücklichen Klick übertragen.
- Absätze, Leerzeilen, Einrückungen, Listenzeichen, Checkboxen und Markdown-Zeilenumbrüche werden vor dem Einsetzen geprüft. Unsichere Luna-Antworten werden verworfen, schädliche LanguageTool-Einzelkorrekturen ausgelassen.
- Änderungen werden über exakt geprüfte Textbereiche eingesetzt, ohne Tippanimation oder erneute Eingabe des gesamten Felds. Rückgängig ist nur im ursprünglichen Editor mit unverändertem Korrekturergebnis möglich.
- Der kompakte Knopf erscheint nur auf einer geprüften freien Fläche am eindeutig zugeordneten Composer. Bei unklarer Zuordnung oder Platzmangel wird er ausgeblendet. Die Positionierung berücksichtigt die Skalierung des Zielmonitors und negative Bildschirmkoordinaten.
- Änderungen am Ausgangstext, Editor- oder Anbieterwechsel sowie Deaktivierung brechen laufende Anfragen ab. Nach unklaren Teilschreibfehlern gibt es keinen blinden Volltextersatz; ist eine sichere Rücknahme unmöglich, bleibt das Original in einem lokalen Wiederherstellungsfenster verfügbar.
- Kein Hintergrunddienst und keine automatische Updatefunktion.

## Installation

1. Lade `CodexLanguageFix-win-x64.zip` aus dem [aktuellen Release](https://github.com/LukasMRiedl/CodexLanguageFix/releases/latest) herunter.
2. Entpacke die ZIP-Datei in einen dauerhaft verfügbaren Ordner.
3. Starte `CodexLanguageFix.exe`.
4. Bestätige beim ersten Start den Hinweis, dass Prompts nur nach einem Klick an den ausgewählten Anbieter übertragen werden.

Die App benötigt keine Administratorrechte. Über das Symbol im Infobereich lassen sich der Korrekturanbieter auswählen, OpenAI verbinden, der ausgewählte Anbieter testen, die App pausieren oder beenden und der automatische Windows-Start ändern.

LanguageTool funktioniert ohne zusätzliche Software. Der optionale Luna-Anbieter setzt eine installierte, aktuelle offizielle Codex-Laufzeit und ein in Codex verfügbares ChatGPT-Konto voraus. Ist Luna oder der Fast Mode nicht verfügbar, bleibt der Prompt unverändert; Codex Language Fix wechselt niemals still auf ein anderes Modell, einen anderen Modus oder einen anderen Anbieter.

Wenn der aktive Codex-Modellkatalog Lunas Modus ohne Thinking bewusst ausblendet, erzeugt Codex Language Fix unter `%LOCALAPPDATA%\CodexLanguageFix\luna-runtime` eine app-eigene Katalogkopie und aktiviert ihn ausschließlich für seinen eigenen App-Server-Prozess. Der globale Codex-Katalog wird nicht verändert; dieser zusätzliche Modus erscheint deshalb nicht in Codex Desktop.

Entfällt später der externe Katalogeintrag, wird eine bereits vorhandene gültige app-eigene Kopie unverändert weiterverwendet. Der globale Modellcache wird weder importiert noch konvertiert. Ein ausdrücklich konfigurierter fehlerhafter Pfad bleibt ein Fehler.

Die veröffentlichte EXE ist derzeit nicht codesigniert. Windows kann deshalb beim ersten Start einen Sicherheitshinweis anzeigen.

## Datenschutz

Erst nach einem Klick sendet die App den aktuellen Prompt an den ausgewählten Anbieter. LanguageTool-Anfragen gehen per HTTPS an `https://api.languagetool.org/v2/check`. Luna verwendet ChatGPT-OAuth über den offiziellen Codex App Server; einen API-Schlüssel-Pfad gibt es nicht. Die Produktion nutzt das qualifizierte Profil `baseline / none / full-v1` in einem ephemeren `gpt-5.6-luna`-Thread mit Fast Mode, einer schreibgeschützten Sandbox ohne Netzwerk und ausdrücklichem Werkzeugverbot. Der App Server und ein leerer Einmal-Thread dürfen ohne Composer-Inhalt vorbereitet werden; während des Tippens startet weder eine Korrektur noch ein lokaler Vorabentwurf. Segment- und Span-Protokolle sowie `low` und `medium` bleiben deaktiviert. Technische Abschnitte werden vor der Übertragung an Luna lokal durch zufällige Platzhalter ersetzt und erst nach strenger Validierung wiederhergestellt.

Prompttext, korrigierter Text, Anbieterantworten, OAuth-Tokens, E-Mail-Adressen und Kontodaten werden niemals protokolliert. Bis zu 64 bereits validierte Luna-Ergebnisse werden ausschließlich im Arbeitsspeicher zwischengespeichert und beim Beenden verworfen. Lokale Diagnoseprotokolle enthalten ausschließlich technische Metadaten wie Anbieter, Zeitpunkt, Dauer, Statuscode, Zeichen- und Änderungsanzahl und werden nach sieben Tagen entfernt.

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

Der externe Qualitäts- und Geschwindigkeitsbenchmark ist bewusst opt-in und benötigt die Codex-ChatGPT-OAuth-Sitzung. Seine feste, deterministische Suche in vier Stufen prüft alle 15 Prompt-/Denkstufenkombinationen, wählt je Denkstufe einen Prompt, vergleicht alle sechs Ausgabeprotokolle an Langtexten und prüft den Sieger direkt gegen `baseline / none / full-v1`. Die Suchstufen verwenden zwei parallele Luna-Anfragen; der abschließende zweisprachige LanguageTool-Vergleich läuft ohne konkurrierende Luna-Last und hält LanguageTools öffentliche Grenze ein. Es gibt keinen globalen Zeitabbruch: Jede Stufe wird vollständig ausgeführt und schreibt sofort einen dauerhaften Schema-v3-Bericht ohne Rohtexte. Bei den beobachteten rund sieben Sekunden je Luna-Korrektur benötigte der qualifizierte Lauf für alle 144 geplanten Messungen 15 Minuten und 44 Sekunden.

```powershell
.\scripts\run-full-luna-qualification.ps1
```

Die Qualifikation vom 20. August 2026 wählte `baseline / none / full-v1`. Das Profil bestand sämtliche Sicherheitsgatter und erreichte im finalen gepaarten Vergleich einen F0,5-Wert von 0,75 gegenüber 0,25 für LanguageTool. Luna war nicht schneller: Der warme p50-Wert betrug 7,97 Sekunden gegenüber 0,72 Sekunden für LanguageTool, also etwa das 11,1-Fache. Segment- und Span-Protokolle wurden deshalb nicht aktiviert. Ein zukünftiges Profil wird nur dann als schneller bezeichnet, wenn beide gepaarten p50-/p95-Latenzgatter bestehen; andernfalls entscheidet die maximale sichere Qualität über das Luna-Profil.

Die selbstenthaltende Einzeldatei liegt anschließend unter:

`src\CodexLanguageFix\bin\Release\net8.0-windows\win-x64\publish\CodexLanguageFix.exe`

## Unterstützte Anwendungen

- OpenAI Codex Desktop für Windows
- Antigravity für Windows

Die Erkennung verwendet zugängliche UI-Strukturen und keine fest codierten Bildschirmkoordinaten.

## Rechtlicher Status

Der Quellcode wird öffentlich bereitgestellt. Es wurde noch keine Open-Source-Lizenz erteilt. Ohne eine ausdrückliche Lizenz gelten die gesetzlichen Urheberrechtsbestimmungen.
