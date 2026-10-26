# Tastenkürzel und Rückmeldungen in Version 1.5.1

Prüfung am 26./27. Oktober 2026 unter Windows.

## Verhalten

Rechtsklick auf das Tray-Icon → Einstellungen… → Tastenkürzel aufnehmen → Speichern. Der Dialog zeigt auch die letzte Statusmeldung. Die Vorgabe bleibt AltGr+, beziehungsweise Strg+Alt+,. AltGr wird als Strg+Alt behandelt.

Automatische Windows-Benachrichtigungen wurden vollständig aus dem Anwendungscode entfernt. Korrektur, Fehler und Rückgängig bleiben am Aa-Knopf erreichbar. Verbindungsfehler sind auch im Einstellungsdialog nachlesbar. Einstellungen und erforderliche Anmeldedialoge öffnen sich weiterhin auf Anforderung.

Die Aufnahme verwendet WPF-Tastenereignisse im geöffneten Dialog. Die vorhandene RegisterHotKey-Registrierung prüft Konflikte. Es gibt keine neue Bibliothek, keinen globalen Tastaturhook und keine zusätzliche Hintergrundschleife.

## Prüfungen

- Release-Suite: 706 Tests bestanden, keine Fehler. Externe und interaktive Tests bleiben wie bisher ausdrücklich opt-in; der Gesamtlauf allein belegt ihre Ausführung nicht.
- Zusätzlich ausdrücklich ausgeführte native Aufnahmetests: AltGr+K und Strg+Umschalt+J, beide bestanden. Geprüft wurden echte Tastensignale an das eigene sichtbare Einstellungsfenster, Anzeige, Speichern, Freigabe der Modifikatoren und Wiederherstellung des ursprünglichen Vordergrunds.
- Zusätzlich nativer AltGr-Hotkeytest: bestanden; einmaliges Auslösen und Freigabe der Modifikatoren geprüft.
- Integrationsprüfung mit künstlichem Text: neues Kürzel speichern und neu laden, Korrektur über den Hotkey-Handler auslösen und Rückgängig anwenden. Hierbei wird ein Testanbieter verwendet; keine neue Live-Anbieterprüfung behauptet.
- Speicherung einschließlich alter Einstellungsdateien, ungültiger Kombinationen, Registrierungskonflikten und Schreibfehlern geprüft. Fokusverlust, Pause und Beenden geben den Hotkey frei.
- Deutsch/Englisch, konfigurierte Hilfetexte und Statusanzeige geprüft; Einstellungsdialog zusätzlich gerendert und visuell kontrolliert.
- Quellcodesuche: keine Aufrufe von ShowBalloonTip, ShowMessage, MessageBox oder ToastNotification in src.
- Die anfänglichen nativen Testfehler betrafen die Vordergrundaktivierung des Testfensters. Der Test wartet nun nach ContentRendered auf das sichtbare eigene Fenster und sendet ausschließlich bei bestätigtem Vordergrund. Der abschließende Lauf besteht.

Lokale TRX-Nachweise liegen unter test-results/release-1.5.1: release-1.5.1.trx, native-settings.trx und native-altgr.trx.

## Installation

Der selbstständige Release-Build 1.5.1.0 wurde nach den Prüfungen installiert. Die bisherige EXE und Einstellungen wurden vorher unter artifacts/installation-backups/1.5.0-20261026-235957 gesichert. Der Start erfolgte über den bestehenden Explorer-Startweg; Prozesspfad und Explorer-Elternprozess wurden nachgelesen. Die vorhandene Einstellungsdatei blieb anhand ihrer Prüfsumme unverändert.

Installationspfad: `%LOCALAPPDATA%\Programs\CodexLanguageFix\CodexLanguageFix.exe`.

SHA-256 des installierten Builds:

```text
AE77CCFB83CCFCEC223B7F303C622D27354AFA96386B1C6F9F6F467F6962AAFD
```

Der lokale Installationsnachweis liegt in artifacts/release-1.5.1-installation.json. Der Windows-Anmeldestart wurde nicht durch eine Abmeldung geprüft; der bestehende Autostarteintrag bleibt erhalten.