# Codex Language Fix

[English](README.md) · **Deutsch**

**Native Vorschau 2.1.5 · Windows x64**

Korrigiere Entwürfe direkt in **Codex Desktop** und **LearnHarness**. Über **Aa** oder ein Tastenkürzel korrigierst du Rechtschreibung, Grammatik und Zeichensetzung. Die App nutzt die offizielle Codex-Laufzeit und deine bestehende Codex-Anmeldung.

| App | Unterstützte Felder |
| --- | --- |
| **Codex Desktop für Windows** | Chat-Eingabe und ausgewählte Freitextfelder |
| **LearnHarness Desktop 0.7.0** | Startprompts, Chat, freie Übungsantworten und Side Questions in der regulären Windows-Installation |

## App verwenden

1. Klicke in einen unterstützten Entwurf und wähle **Aa** oder drücke <kbd>AltGr</kbd>+<kbd>,</kbd> (auch <kbd>Strg</kbd>+<kbd>Alt</kbd>+<kbd>,</kbd>).
2. Prüfe die Korrektur im selben Feld. Du entscheidest, wann du den Entwurf absendest.
3. Über **Rückgängig** stellst du den Entwurf wieder her, solange der korrigierte Text unverändert ist.

Korrigiere LearnHarness-Entwürfe vor dem Einfügen eingebetteter Anhänge oder Zitate. Tastenkürzel und Autostart kannst du über **Einstellungen** im Windows-Infobereich ändern.

## Vorschau bauen

Die native Vorschau ist als Quellcode verfügbar. Zum Bauen brauchst du **Visual Studio Build Tools 2022** mit **Desktopentwicklung mit C++**, das Windows SDK und CMake. Für die Korrektur brauchst du Codex Desktop und ein in Codex angemeldetes ChatGPT-Konto.

```powershell
git clone --branch codex/native-ram-20261114 https://github.com/LukasMRiedl/CodexLanguageFix.git
Set-Location CodexLanguageFix
.\scripts\build-native.ps1
```

Das Skript baut und prüft die App und schreibt das portable Paket nach `dist\native-win-x64`. Starte dort `CodexLanguageFix.exe`. Das frühere fertige Paket findest du auf der [Release-Seite von v1.4.0](https://github.com/LukasMRiedl/CodexLanguageFix/releases/tag/v1.4.0).

## Datenschutz und Entwicklung

LearnHarness-Entwürfe werden nur auf Anforderung an Codex gesendet. Für Codex Desktop gibt es eine optionale automatische Vorbereitung stabiler Entwürfe. Die App schützt erkannte technische Bestandteile vor der Übertragung und protokolliert keine Entwürfe oder Korrekturergebnisse. Details stehen in den [Datenschutzhinweisen](PRIVACY.md) (Englisch).

Die native App verwendet C++20, Win32 und Windows UI Automation. Die C#-Implementierung dient als Vergleich für die Tests. Siehe [Build- und Testanleitung](native/README.de.md) und [Hinweise zu Drittsoftware](native/THIRD_PARTY_NOTICES.md).

[Fehler melden](https://github.com/LukasMRiedl/CodexLanguageFix/issues/new) · [Sicherheitsrichtlinie](SECURITY.md)

Codex Language Fix ist ein unabhängiges Projekt. Der Projektquellcode ist öffentlich einsehbar und unterliegt dem Urheberrecht.
