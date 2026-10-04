# Codex Language Fix

[English](README.md) · **Deutsch**

Eine eigenständige Windows-App für die Ein-Klick-Korrektur von Prompts in der OpenAI-Codex-Desktop-App, Antigravity und Hermes. Als Anbieter stehen die kostenlose öffentliche LanguageTool-API und Luna über die offizielle Codex-ChatGPT-OAuth-Sitzung zur Verfügung. Die App benötigt weder ein Codex-Plugin noch eine Browsererweiterung, einen OpenAI-API-Schlüssel, zusätzliche API-Abrechnung oder einen eigenen Server.

> Dieses Projekt ist unabhängig und nicht mit OpenAI, Google oder LanguageTool verbunden.

Der aktuelle Quellstand ist **Release-Kandidat 1.3.1-rc.10**. Er wählt das neueste Luna-Modell aus dem Katalog der installierten Codex-Laufzeit, das die niedrige Denkstufe mit Fast Mode (`priority`) unterstützt. Das Produktionsprofil `low` bestand mit `gpt-6-luna` die Live-Qualifikation mit 128 Korpusfällen und unveränderten Sicherheitsgattern; Nachweise und Grenzen stehen im [Laufzeitbericht](docs/desktop-runtime-2026-09-30.md). Der frühere Speichervergleich für RC7 ergab rund 49 % weniger privaten Speicher und 43 % weniger Working Set; das angestrebte **80-%-Ziel ist nicht erreicht**. Nachweise und Grenzen stehen im [Speicherbericht](docs/ram-optimization-2026-09-13.md). Dieser Entwicklungsstand ist nicht als stabiler Release veröffentlicht.

## Funktionen

- Ein unaufdringlicher `Aa`-Knopf erscheint dynamisch in der aktuellen Eingabeleiste.
- Ein Klick prüft Rechtschreibung, Grammatik, Zeichensetzung und Stil.
- LanguageTool bleibt der Standardanbieter. OpenAI Luna lässt sich im Infobereich auswählen. Die Produktion fordert das Profil `baseline / low / full-v1` mit Fast Mode (`priority`) auf dem neuesten passenden Modell aus dem Katalog der installierten Codex-Laufzeit an. Dieses Profil wurde am 30. September 2026 mit `gpt-6-luna` live geprüft. Experimentelle Profile werden niemals automatisch aktiviert.
- Luna nutzt den offiziellen Codex App Server und dessen verwalteten ChatGPT-OAuth-Ablauf. Die App liest oder speichert niemals OAuth-Tokens.
- Deutsch und Englisch werden automatisch erkannt; bevorzugt werden `de-DE` und `en-US`.
- Die gesamte Oberfläche ist auf Deutsch und Englisch verfügbar. Standardmäßig folgt sie der Windows-Anzeigesprache und lässt sich jederzeit über das Menü im Infobereich umschalten.
- Markdown-Codeblöcke, Inline-Code, URLs, E-Mail-Adressen, Pfade, Befehlsoptionen und typische Codebezeichner bleiben unverändert.
- Die Zwischenablage wird weder gelesen noch verändert.
- Der Prompt wird nur nach einem ausdrücklichen Klick übertragen.
- Absätze, Leerzeilen, Einrückungen, Listenzeichen, Checkboxen und Markdown-Zeilenumbrüche werden vor dem Einsetzen geprüft. Unsichere Luna-Antworten werden verworfen, schädliche LanguageTool-Einzelkorrekturen ausgelassen.
- Änderungen werden über exakt geprüfte Textbereiche eingesetzt, ohne Tippanimation oder erneute Eingabe des gesamten Felds. Rückgängig ist nur im ursprünglichen Editor mit unverändertem Korrekturergebnis möglich.
- Der kompakte Knopf erscheint nur auf einer geprüften freien Fläche am eindeutig zugeordneten Composer. Bei unklarer Zuordnung oder Platzmangel wird er ausgeblendet. Die Positionierung berücksichtigt die Skalierung des Zielmonitors und negative Bildschirmkoordinaten.
- Änderungen am Ausgangstext, Editor- oder Anbieterwechsel sowie Deaktivierung brechen laufende Anfragen ab. Nach unklaren Teilschreibfehlern gibt es keinen blinden Volltextersatz. Eigene verifizierte Änderungen werden nach Möglichkeit sicher zurückgenommen; es öffnet sich kein Wiederherstellungsfenster.
- Kein Hintergrunddienst und keine automatische Updatefunktion.

Die erweiterte Aa-Felderkennung und Platzierung wird im [Felderdiagnosebericht](docs/aa-coverage-2026-10-04.md) dokumentiert. Hauptchat-Korrektur und exaktes Rückgängig wurden in Codex und Hermes live geprüft, ebenso reale Monitorwechsel zwischen 175 % und 200 %. Dieser Kandidat ist lokal installiert; zusätzliche, nicht erreichbare Feldfamilien werden im Bericht ausdrücklich als ungeprüft ausgewiesen.

## Installation

1. Lade `CodexLanguageFix-win-x64.zip` aus dem [aktuellen Release](https://github.com/LukasMRiedl/CodexLanguageFix/releases/latest) herunter.
2. Entpacke die ZIP-Datei in einen dauerhaft verfügbaren Ordner.
3. Starte `CodexLanguageFix.exe`.
4. Bestätige beim ersten Start den Hinweis, dass Prompts nur nach einem Klick an den ausgewählten Anbieter übertragen werden.

Die App benötigt keine Administratorrechte. Über das Symbol im Infobereich lassen sich der Korrekturanbieter auswählen, OpenAI verbinden, der ausgewählte Anbieter testen, die App pausieren oder beenden und der automatische Windows-Start ändern.

LanguageTool funktioniert ohne zusätzliche Software. Der optionale Luna-Anbieter setzt die offizielle Codex-Desktop-Installation und ein in Codex verfügbares ChatGPT-Konto voraus; Codex Desktop kann während der Nutzung geschlossen bleiben. Beim Start des App Servers wählt die App das neueste im offiziellen Runtime-Katalog angebotene Luna-Modell, das die niedrige Denkstufe und Fast Mode (`priority`) unterstützt. Sie verwendet diese Auswahl für die Laufzeit und ermittelt sie nach einem Laufzeit- oder Kontokontext-Reset neu, damit bei Korrekturen keine wiederholte Modelllisten-Abfrage anfällt. Ist kein kompatibles Modell verfügbar, bleibt der Prompt unverändert; Codex Language Fix wechselt niemals still in einen anderen Modus oder Anbieter.

Die veröffentlichte EXE ist derzeit nicht codesigniert. Windows kann deshalb beim ersten Start einen Sicherheitshinweis anzeigen.

## Datenschutz

Erst nach einem Klick sendet die App den aktuellen Prompt an den ausgewählten Anbieter. LanguageTool-Anfragen gehen per HTTPS an `https://api.languagetool.org/v2/check`. Luna verwendet ChatGPT-OAuth über den offiziellen Codex App Server; einen API-Schlüssel-Pfad gibt es nicht. Beim Start des App Servers wählt die App das neueste im offiziellen Runtime-Katalog angebotene Luna-Modell, das die niedrige Denkstufe und Fast Mode (`priority`) unterstützt, und verwendet dieses Profil für die Laufzeit weiter. Nach einem Laufzeit- oder Kontokontext-Reset ermittelt sie das Profil neu, sodass Korrekturen keine wiederholte Modelllisten-Abfrage auslösen. Die Produktion fordert `baseline / low / full-v1` an. Jede Korrektur nutzt einen ephemeren Thread mit einer schreibgeschützten Sandbox ohne Netzwerk und ausdrücklichem Werkzeugverbot. Der App Server und ein leerer Einmal-Thread dürfen ohne Composer-Inhalt vorbereitet werden; während des Tippens startet weder eine Korrektur noch ein lokaler Vorabentwurf. Segment- und Span-Protokolle sowie `medium` bleiben deaktiviert. Technische Abschnitte werden vor der Übertragung an Luna lokal durch zufällige Platzhalter ersetzt und erst nach strenger Validierung wiederhergestellt.

Prompttext, korrigierter Text, Anbieterantworten, OAuth-Tokens, E-Mail-Adressen und Kontodaten werden in Produktionsprotokollen niemals aufgezeichnet. Bis zu 64 bereits validierte Luna-Ergebnisse werden ausschließlich im Arbeitsspeicher zwischengespeichert und beim Beenden verworfen. Lokale Diagnoseprotokolle enthalten ausschließlich technische Metadaten wie Anbieter, Zeitpunkt, Dauer, Statuscode, Zeichen- und Änderungsanzahl und werden nach sieben Tagen entfernt. Die ausdrücklich aktivierte Oberflächendiagnose kann ausschließlich vorab verifizierte künstliche Testdaten ausgeben.

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

Der externe Qualitäts- und Geschwindigkeitsbenchmark ist bewusst opt-in und setzt die offizielle Codex-Desktop-Installation mit ChatGPT-OAuth voraus; Codex Desktop kann geschlossen bleiben. Das Produktionsqualifikationsskript misst ausschließlich `baseline / low / full-v1` mit dem neuesten im offiziellen Runtime-Katalog angebotenen Luna-Modell, das die niedrige Denkstufe und Fast Mode (`priority`) unterstützt. Der [Lauf vom 30. September 2026](docs/desktop-runtime-2026-09-30.md) bestand mit `gpt-6-luna` alle Sicherheitsgatter; Luna war im gepaarten Vergleich genauer, aber langsamer als LanguageTool. Der Schema-v3-Bericht enthält das ausgewählte Modell, aber keine Rohtexte. Explorative Matrixeinstellungen werden an derselben Laufzeitauswahl geprüft; nicht unterstützte Denkstufen werden ohne Modellwechsel abgewiesen.

```powershell
.\scripts\run-full-luna-qualification.ps1
```

Der historische Qualifikationslauf vom 20. August 2026 maß das damals fest vorgegebene Modell `gpt-5.6-luna` mit `baseline / none / full-v1`. Das Profil bestand sämtliche Sicherheitsgatter und erreichte im finalen gepaarten Vergleich einen F0,5-Wert von 0,75 gegenüber 0,25 für LanguageTool. Luna war nicht schneller: Der warme p50-Wert betrug 7,97 Sekunden gegenüber 0,72 Sekunden für LanguageTool, also etwa das 11,1-Fache. Segment- und Span-Protokolle wurden deshalb nicht aktiviert. Diese Werte beschreiben diesen datierten Lauf und legen keine feste Modellauswahl der aktuellen App fest.

Die selbstenthaltende Einzeldatei liegt anschließend unter:

`src\CodexLanguageFix\bin\Release\net8.0-windows\win-x64\publish\CodexLanguageFix.exe`

## Unterstützte Anwendungen

- OpenAI Codex Desktop für Windows
- Antigravity für Windows
- Hermes für Windows

Die Erkennung verwendet zugängliche UI-Strukturen und keine fest codierten Bildschirmkoordinaten.

## Rechtlicher Status

Der Quellcode wird öffentlich bereitgestellt. Es wurde noch keine Open-Source-Lizenz erteilt. Ohne eine ausdrückliche Lizenz gelten die gesetzlichen Urheberrechtsbestimmungen.
