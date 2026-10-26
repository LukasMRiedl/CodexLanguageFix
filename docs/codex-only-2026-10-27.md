# Codex als einziger Korrekturanbieter – Version 1.5.2

Stand: 27. Oktober 2026.

## Änderung

Die aktive App verwendet ausschließlich den bestehenden Codex App Server mit ChatGPT-Anmeldung und dem unveränderten Luna-Modellprofil. LanguageTool-Client, Anbieter, eigene Regeln/HTTP-Anfragen, Anbieterauswahl und LanguageTool-Fallback sind entfernt. Aktive Benchmark-Werkzeuge verwenden ebenfalls nur Codex. Historische Vergleichsberichte und die historischen Korpusbeispiele bleiben erhalten.

Im Tray stehen „Mit Codex verbinden“ und „Codex-Verbindung prüfen“ direkt zur Verfügung. Tastenkürzel-Einstellungen, Pause, Autostart, Aa und Rückgängig bleiben bestehen. Automatische Windows-Benachrichtigungen bleiben entfernt. Fehler lassen den Text unverändert und bleiben im Einstellungsstatus erreichbar.

## Einstellungen

Die Anbieterwahl ist aus AppSettings entfernt. Alte Einstellungen mit correctionProvider=1 erhalten die bisherige Vorbereitungseinstellung. Bei 0, fehlendem oder ungültigem Anbieterwert wird die Vorbereitung ausgeschaltet. So startet keine zuvor inaktive automatische Textübertragung. Neu gespeicherte Einstellungen tragen settingsVersion=2; dadurch bleibt eine ausdrücklich gewählte Vorbereitung nach Neustarts erhalten, obwohl kein Anbieterfeld mehr gespeichert wird. Tastenkürzel und übrige Einstellungen bleiben erhalten.

## Nachweise

- Gesamte Release-Suite: 700 Tests bestanden, keine Fehler. Wie bisher laufen externe und native Tests nur mit ausdrücklichem Opt-in; der Gesamtlauf allein belegt ihre Ausführung nicht.
- Zusätzlich echter Codex-Test mit ChatGPT-Anmeldung und künstlichem Composer: bestanden. Knopf- und Shortcut-Befehl wurden mit echter Codex-Korrektur sowie jeweils anschließendem Rückgängig geprüft. Es erfolgte keine Eingabe in Benutzerprogramme. Der Test bestätigt low/priority des ausgewählten Laufzeitprofils.
- Nach der abschließenden Menütextanpassung: 19 Lokalisierungs-/Tray-Tests bestanden.
- SpeedProbe in Release gebaut: keine Fehler oder Warnungen.
- Legacy-Einstellungen, neue Speicherung/Reload, deaktivierte Vorbereitung, Korrekturfehler ohne Schreibzugriff und erreichbarer Fehlerstatus geprüft.
- Quellcodesuche in src und scripts: keine LanguageTool-Referenzen oder Endpunkte mehr. Die verbleibenden Testreferenzen prüfen Migration und das Entfernen alter Oberflächentexte.
- Unabhängige Codeprüfung von Laufzeit, Migration und Benchmarks: keine Blocker.

Lokale TRX-Dateien: test-results/release-1.5.2/release-1.5.2.trx, codex-only-live-smoke.trx und codex-only-ui.trx. Der vollständige Live-Korpusbenchmark wurde für diese Umstellung nicht erneut ausgeführt; die Codex-Modell-/Protokoll-/Schutzlogik wurde nicht geändert.

## Installation

Die bisherige Installation 1.5.1 und ihre Einstellungen sind unter artifacts/installation-backups/1.5.1-20261027-001520 gesichert. Der neue selbstständige Build wurde über den vorhandenen Explorer-Startweg gestartet. Prozesspfad und Explorer als Elternprozess wurden geprüft; der Helfer läuft nach Ende des Installationsprozesses weiter. Die bestehende Einstellungsdatei blieb anhand ihrer Prüfsumme unverändert.

Installationspfad: `%LOCALAPPDATA%\Programs\CodexLanguageFix\CodexLanguageFix.exe`.

Version: 1.5.2.0. SHA-256:

```text
A2DE2073CAC0F7F66983F880FF7C409012EA6FC8D3D980F7FF1CC8C7760FA3F2
```

Lokaler Installationsnachweis: artifacts/release-1.5.2-installation.json. Ein Windows-Anmeldestart wurde nicht durch Abmeldung geprüft; der bestehende Autostarteintrag blieb unverändert.