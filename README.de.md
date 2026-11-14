# Codex Language Fix

[English](README.md) · **Deutsch**

Der aktuelle Entwicklungszweig implementiert die auf Codex beschränkte Textkorrektur nativ mit C++20 und Win32. Er verwendet weiterhin den offiziellen Codex App Server und die vorhandene Codex-Anmeldung. Version 2.0.0 ist lokal installiert; Korrektur, exaktes Undo, die überarbeitete Darstellung, automatisierte native Prüfungen und 3.945 differenzielle Vergleichsfälle sind geprüft. Die vollständige Langzeit- und Ressourcenabnahme wurde auf ausdrücklichen Wunsch zugunsten der Beobachtung im Alltag zurückgestellt. Eine kurze Ablesung ersetzt keinen Nachweis des dauerhaften 50-MB-Ziels. C# bleibt ausschließlich Testreferenz und gehört nicht zum nativen Produktpaket. Die [Entwicklungsnotizen](native/README.de.md) beschreiben Build und Prüfungen.

Der [Abnahmestand](docs/native-port-2026-11-14.md) trennt die bereits bestandenen Prüfungen von den verbleibenden Freigabesperren und Messgrenzen.

Das Projekt ist eine eigenständige Windows-App für die Ein-Klick-Korrektur von Prompts in der OpenAI-Codex-Desktop-App. Die Referenzimplementierung nutzt Codex mit dessen offizieller ChatGPT-OAuth-Sitzung und dem bestehenden Luna-Modellprofil. Sie benötigt weder ein Codex-Plugin noch einen OpenAI-API-Schlüssel, zusätzliche API-Abrechnung oder einen eigenen Server.

> Dieses Projekt ist unabhängig und nicht mit OpenAI oder Google verbunden.

Der C#-Referenzstand im Repository ist **Version 1.5.4**. Er wählt das neueste Luna-Modell aus dem Katalog der installierten Codex-Laufzeit, das die niedrige Denkstufe mit Fast Mode (`priority`) unterstützt. Das Produktionsprofil `low` bestand mit `gpt-6-luna` die Live-Qualifikation mit 128 Korpusfällen und unveränderten Sicherheitsgattern; Nachweise und Grenzen stehen im [Laufzeitbericht](docs/desktop-runtime-2026-09-30.md). Der frühere Speichervergleich für RC7 ergab rund 49 % weniger privaten Speicher und 43 % weniger Working Set; das angestrebte **80-%-Ziel ist nicht erreicht**. Nachweise und Grenzen stehen im [Speicherbericht](docs/ram-optimization-2026-09-13.md). Diese Berichte beziehen sich auf die C#-Referenz und datierte Läufe, nicht auf eine native Abnahme. Version 1.4.0 ist der stabile GitHub-Release.

Die C#-Referenz 1.5.4 startet den isolierten UI-Automations-Worker vor WPF, gibt ungenutzte eigene Codex-Sitzungen zeitnah frei und hält in Geometriecache und Schreibidentität keine nativen Editorreferenzen. Der [Prüfbericht](docs/codex-only-2026-11-13.md) dokumentiert diese Änderungen und die Grenzen des 50-MB-Ziels; er belegt weder native Speicherwerte noch Funktionsparität.

Version 1.5.0 bestand 30 gepaarte Live-Vergleiche beim sofortigen Shortcut: Die Medianwartezeit sank von 4,53 auf 2,97 Sekunden (34,5 %), bei 60 exakten Korrekturen und besserem Gesamt-p95. Diese Version war lokal mit aktivierter optionaler Luna-Vorbereitung installiert. Methode und Grenzen stehen im [Geschwindigkeitsbericht](docs/shortcut-speed-2026-10-13.md).

Die Codex-only-Umstellung der C#-Referenz erhält Tastenkürzel und Autostart. Die automatische Vorbereitung bleibt bei bisherigen Codex-Nutzern entsprechend ihrer Einstellung erhalten. Bei alten LanguageTool-Einstellungen sowie fehlendem oder ungültigem Anbieterwert wird sie ausgeschaltet. Neu gespeicherte Einstellungen verwenden Schemaversion 2, damit eine ausdrücklich gewählte Vorbereitung nach Neustarts erhalten bleibt.

## C#-Referenz: Funktionen und Bedienung

Die folgenden Angaben zu Funktionen, Installation und Datenschutz beschreiben den C#-Referenzstand. Sie belegen keine Funktionsparität oder Live-Funktion des nativen Ports.

- Die App zeigt keine automatischen Windows-Benachrichtigungen an. Korrekturstatus und Rückgängig bleiben am Aa-Knopf sichtbar; die letzte Meldung ist unter **Infobereich → Einstellungen…** nachlesbar.
- Unter **Einstellungen… → Tastenkürzel aufnehmen** kannst du eine eigene Kombination erfassen und speichern. Sie gilt ohne Neustart und bleibt nach dem Beenden erhalten. Belegte Kombinationen werden beim Speichern abgelehnt. Erlaubt sind Strg und/oder Alt mit einer weiteren Taste, optional Umschalt, sowie Funktionstasten, optional mit Umschalt; F12 und Windows-Kombinationen sind ausgeschlossen. AltGr entspricht technisch Strg+Alt und kann je nach gewählter Taste mit normalen Sonderzeichen kollidieren. **Standard wiederherstellen** setzt die Auswahl auf Strg+Alt+Komma zurück.

- Ein unaufdringlicher `Aa`-Knopf erscheint dynamisch in der aktuellen Eingabeleiste. Klicke darauf oder nutze dein gewähltes Tastenkürzel (Vorgabe: `AltGr+,` / `Strg+Alt+,`), um den Prompt zu korrigieren. Das Tastenkürzel ist nur bei aktivierter App und einem fokussierten, nichtleeren, unterstützten Eingabefeld belegt. Gedrückthalten startet keine weiteren Anfragen.
- Ein Klick prüft Rechtschreibung, Grammatik, Zeichensetzung und Stil.
- Codex ist der einzige Korrekturanbieter. Die Produktion fordert das Profil `baseline / low / full-v1` mit Fast Mode (`priority`) auf dem neuesten passenden Modell aus dem Katalog der installierten Codex-Laufzeit an. Dieses Profil wurde am 30. September 2026 mit `gpt-6-luna` live geprüft. Experimentelle Profile werden niemals automatisch aktiviert.
- Luna nutzt den offiziellen Codex App Server und dessen verwalteten ChatGPT-OAuth-Ablauf. Die App liest oder speichert niemals OAuth-Tokens.
- Deutsch und Englisch werden automatisch erkannt; bevorzugt werden `de-DE` und `en-US`.
- Die gesamte Oberfläche ist auf Deutsch und Englisch verfügbar. Standardmäßig folgt sie der Windows-Anzeigesprache und lässt sich jederzeit über das Menü im Infobereich umschalten.
- Markdown-Codeblöcke, Inline-Code, URLs, E-Mail-Adressen, Pfade, Befehlsoptionen und typische Codebezeichner bleiben unverändert.
- Die Zwischenablage wird weder gelesen noch verändert.
- Standardmäßig wird kein Prompt automatisch übertragen. Wenn du im Infobereich „Codex-Korrekturen vorbereiten“ aktivierst, sendet die App den Prompt nach 1,2 Sekunden beobachteter Textstabilität an Luna, um einen Korrekturentwurf vorzubereiten. Den Entwurf fügst du manuell ein; er wird nicht automatisch eingesetzt. Das kann auf die Nutzungslimits deines verbundenen ChatGPT-Kontos angerechnet werden.
- Absätze, Leerzeilen, Einrückungen, Listenzeichen, Checkboxen und Markdown-Zeilenumbrüche werden vor dem Einsetzen geprüft. Unsichere Codex-Antworten werden verworfen.
- Änderungen werden über exakt geprüfte Textbereiche eingesetzt, ohne Tippanimation oder erneute Eingabe des gesamten Felds. Rückgängig ist nur im ursprünglichen Editor mit unverändertem Korrekturergebnis möglich.
- Der kompakte Knopf erscheint nur auf einer geprüften freien Fläche am eindeutig zugeordneten Composer. Bei unklarer Zuordnung oder Platzmangel wird er ausgeblendet. Die Positionierung berücksichtigt die Skalierung des Zielmonitors und negative Bildschirmkoordinaten.
- Änderungen am Ausgangstext, Editorwechsel sowie Deaktivierung brechen laufende Anfragen ab. Nach unklaren Teilschreibfehlern gibt es keinen blinden Volltextersatz. Eigene verifizierte Änderungen werden nach Möglichkeit sicher zurückgenommen; es öffnet sich kein Wiederherstellungsfenster.
- Kein Hintergrunddienst und keine automatische Updatefunktion.

Der [Felderdiagnosebericht](docs/aa-coverage-2026-10-04.md) hält frühere Prüfungen fest. Hauptchat-Korrektur und exaktes Rückgängig wurden damals in Codex und Hermes live geprüft, ebenso reale Monitorwechsel zwischen 175 % und 200 %. Version 1.4.0 war bei jener Prüfung lokal installiert. Diese historischen Befunde beschreiben keine Hermes-Unterstützung in Version 1.5.4; zusätzliche, nicht erreichbare Feldfamilien sind im Bericht ausdrücklich als ungeprüft ausgewiesen.

## C#-Referenz: Installation

Der Downloadlink führt zum stabilen C#-Release (Version 1.4.0), nicht zum C#-Referenzstand 1.5.4 im Repository, den die Bedienungs- und Datenschutzhinweise beschreiben.

1. Lade `CodexLanguageFix-win-x64.zip` aus dem [aktuellen Release](https://github.com/LukasMRiedl/CodexLanguageFix/releases/latest) herunter.
2. Entpacke die ZIP-Datei in einen dauerhaft verfügbaren Ordner.
3. Starte `CodexLanguageFix.exe`.
4. Bestätige beim ersten Start den Datenschutzhinweis. Die automatische Luna-Vorbereitung ist standardmäßig ausgeschaltet. Wenn sie aktiviert ist, wird ein unterstützter Prompt nach 1,2 Sekunden beobachteter Textstabilität automatisch an Luna gesendet. Den fertigen Entwurf fügst du manuell ein.

Die App benötigt keine Administratorrechte. Über das Symbol im Infobereich lassen sich Codex verbinden, die Codex-Verbindung prüfen, die App pausieren oder beenden und der automatische Windows-Start ändern.

Die Codex-Korrektur setzt die offizielle Codex-Desktop-Installation und ein in Codex verfügbares ChatGPT-Konto voraus; Codex Desktop kann während der Nutzung geschlossen bleiben. Beim Start des App Servers wählt die App das neueste im offiziellen Runtime-Katalog angebotene Luna-Modell, das die niedrige Denkstufe und Fast Mode (`priority`) unterstützt. Sie verwendet diese Auswahl für die Laufzeit und ermittelt sie nach einem Laufzeit- oder Kontokontext-Reset neu, damit bei Korrekturen keine wiederholte Modelllisten-Abfrage anfällt. Ist kein kompatibles Modell verfügbar, bleibt der Prompt unverändert; Codex Language Fix wechselt niemals still in einen anderen Modus oder Anbieter.

Die veröffentlichte EXE ist derzeit nicht codesigniert. Windows kann deshalb beim ersten Start einen Sicherheitshinweis anzeigen.

## C#-Referenz: Datenschutz

Bei aktivierter Vorbereitung können auch ungesendete Zwischenstände während einer Schreibpause übertragen werden. Ein Abbruch beim Weiterschreiben kann eine bereits erfolgte Übertragung nicht zurücknehmen.

Die aktive App überträgt den aktuellen, nichtleeren Prompt aus einem unterstützten Eingabefeld an Codex, wenn du den Korrekturknopf anklickst oder dein konfiguriertes Tastenkürzel drückst (Vorgabe: `AltGr+,` / `Strg+Alt+,`). Zusätzlich kann im Infobereich „Codex-Korrekturen vorbereiten“ aktiviert werden; standardmäßig ist die Option ausgeschaltet. Ist sie aktiviert, sendet die App den Prompt nach 1,2 Sekunden beobachteter Textstabilität automatisch an Luna, um einen Korrekturentwurf vorzubereiten. Der Entwurf wird nicht automatisch eingesetzt, sondern muss manuell eingefügt werden. Diese Anfragen können auf die Nutzungslimits des mit Codex verbundenen ChatGPT-Kontos angerechnet werden. Luna verwendet ChatGPT-OAuth über den offiziellen Codex App Server; einen API-Schlüssel-Pfad gibt es nicht. Beim Start des App Servers wählt die App das neueste im offiziellen Runtime-Katalog angebotene Luna-Modell, das die niedrige Denkstufe und Fast Mode (`priority`) unterstützt, und verwendet dieses Profil für die Laufzeit weiter. Nach einem Laufzeit- oder Kontokontext-Reset ermittelt sie das Profil neu, sodass Korrekturen keine wiederholte Modelllisten-Abfrage auslösen. Die Produktion fordert `baseline / low / full-v1` an. Jede Korrektur nutzt einen ephemeren Thread mit einer schreibgeschützten Sandbox ohne Netzwerk und ausdrücklichem Werkzeugverbot. Der App Server und ein leerer Einmal-Thread dürfen ohne Composer-Inhalt vorbereitet werden. Segment- und Span-Protokolle sowie `medium` bleiben deaktiviert. Technische Abschnitte werden vor der Übertragung an Luna lokal durch zufällige Platzhalter ersetzt und erst nach strenger Validierung wiederhergestellt.

Prompttext, korrigierter Text, Anbieterantworten, OAuth-Tokens, E-Mail-Adressen und Kontodaten werden in Produktionsprotokollen niemals aufgezeichnet. Bis zu 64 bereits validierte Luna-Ergebnisse werden ausschließlich im Arbeitsspeicher zwischengespeichert und beim Beenden verworfen. Lokale Diagnoseprotokolle enthalten ausschließlich technische Metadaten wie Anbieter, Zeitpunkt, Dauer, Statuscode, Zeichen- und Änderungsanzahl und werden nach sieben Tagen entfernt. Die ausdrücklich aktivierte Oberflächendiagnose kann ausschließlich vorab verifizierte künstliche Testdaten ausgeben.

Weitere Einzelheiten stehen in [PRIVACY.md](PRIVACY.md).

## C#-Referenz: Anbietergrenzen

Codex akzeptiert höchstens 20.000 Zeichen pro Korrektur. Die App erzeugt keine Warteschlange. Die automatische Luna-Vorbereitung ist optional und standardmäßig ausgeschaltet; ihre Anfragen können auf die Nutzungslimits des mit Codex verbundenen ChatGPT-Kontos angerechnet werden.

## Nativer Build und automatisierte Prüfungen

Verwende unter Windows das native Buildskript mit Visual Studio Build Tools 2022, MSVC v143, Windows SDK und CMake:

```powershell
.\scripts\build-native.ps1
```

Das Skript baut und prüft die C++20-/Win32-Ziele und erstellt ein Paket unter `dist/native-win-x64`. Der Build allein installiert oder veröffentlicht nichts und bestätigt keine Ressourcenabnahme. Die [Entwicklungsnotizen](native/README.de.md) enthalten die genauen Befehle für den differenziellen Vergleich und das Worker-Protokoll sowie deren Grenzen. Die lokalen Live-Prüfungen und noch offenen Langzeitnachweise stehen im [Abnahmestand](docs/native-port-2026-11-14.md).

Das C#-Projekt bleibt als Testreferenz erhalten. Seine .NET-Tests und Qualifikationswerkzeuge bauen oder veröffentlichen das native Produkt nicht. Historische Messungen für C# stehen in den jeweils datierten Berichten.

### C#-Referenztests und historische Qualifikation

Für diese Prüfungen der C#-Referenz ist das .NET 8 SDK unter Windows erforderlich. Der externe Qualitäts- und Geschwindigkeitsbenchmark ist bewusst optional und setzt die offizielle Codex-Desktop-Installation samt ChatGPT-OAuth voraus; Codex Desktop kann geschlossen bleiben. Das Produktionsqualifikationsskript misst `baseline / low / full-v1` mit dem neuesten im Runtime-Katalog angebotenen Luna-Modell, das die niedrige Denkstufe und Fast Mode (`priority`) unterstützt. Der [Lauf vom 30. September 2026](docs/desktop-runtime-2026-09-30.md) bestand mit `gpt-6-luna` alle Sicherheitsgatter; Luna war im gepaarten Vergleich genauer, aber langsamer als LanguageTool. Die Berichte enthalten keine Rohtexte.

```powershell
.\scripts\run-full-luna-qualification.ps1
```

Der frühere Qualifikationslauf vom 20. August 2026 maß das damals fest vorgegebene Modell `gpt-5.6-luna` mit `baseline / none / full-v1`. Das Profil bestand sämtliche Sicherheitsgatter und erreichte im finalen gepaarten Vergleich einen F0,5-Wert von 0,75 gegenüber 0,25 für LanguageTool. Luna war nicht schneller: Der warme p50-Wert betrug 7,97 Sekunden gegenüber 0,72 Sekunden für LanguageTool, also etwa das 11,1-Fache. Diese Werte sind historische Messungen der C#-Referenz, keine nativen Ergebnisse.

Die selbstenthaltende EXE der C#-Referenz lag unter `src\CodexLanguageFix\bin\Release\net8.0-windows\win-x64\publish\CodexLanguageFix.exe`. Ihre Prozessdiagnose und die optionalen unabhängigen Startprüfungen stehen im [Prozessbericht](docs/process-lifetime-2026-10-10.md). Dies ist nicht das Buildverfahren für die native App.

## C#-Referenz: unterstützte Anwendungen

- OpenAI Codex Desktop für Windows

Die C#-Referenz und die native App verarbeiten nur unterstützte Eingabefelder innerhalb von Codex Desktop. Andere Windows-Anwendungen, Antigravity, Hermes und Browser werden nicht unterstützt.

## Rechtlicher Status

Der Quellcode wird öffentlich bereitgestellt. Es wurde noch keine Open-Source-Lizenz erteilt. Ohne eine ausdrückliche Lizenz gelten die gesetzlichen Urheberrechtsbestimmungen.
