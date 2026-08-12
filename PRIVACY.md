# Datenschutz

Codex Language Fix verarbeitet Text ausschließlich nach einem ausdrücklichen Klick auf den `Aa`-Knopf.

## Übertragene Daten

Der aktuelle Prompt wird per HTTPS an die öffentliche LanguageTool-Schnittstelle unter `https://api.languagetool.org/v2/check` gesendet. Für die Verarbeitung gelten die Datenschutzbedingungen des LanguageTool-Betreibers.

## Nicht übertragene Daten

- Ohne Klick wird kein Prompt übertragen.
- Die Zwischenablage wird weder gelesen noch verändert.
- Die App überträgt keine Zugangsdaten oder API-Schlüssel.
- Technische Bereiche wie Code, URLs und Pfade werden vor Änderungen geschützt, befinden sich jedoch weiterhin im an LanguageTool gesendeten Gesamttext.

## Lokale Protokolle

Prompttexte, korrigierte Texte und API-Antworten werden nicht protokolliert. Diagnoseprotokolle enthalten ausschließlich Zeitpunkt, Vorgangsart, Dauer, HTTP-Statuscode, Zeichenanzahl und Trefferanzahl. Diese Dateien werden nach sieben Tagen gelöscht.

## Deinstallation

Beende die App über das Symbol im Infobereich und lösche anschließend den Programmordner. Einstellungen und Diagnoseprotokolle liegen unter `%LOCALAPPDATA%\CodexLanguageFix` und können separat gelöscht werden.
