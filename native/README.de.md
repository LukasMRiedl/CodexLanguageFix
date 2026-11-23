# Native Textkorrektur

Dieser Entwicklungszweig portiert den Codex-only-Referenzstand 1.5.4 nach C++20.
Das Produkt besteht aus einer Win32-EXE. Ihr Modus `--composer-worker <PID>` führt
UI Automation isoliert auf einem MTA-Thread aus. Der offizielle Codex App Server
bleibt ein eigener Kindprozess und verwendet die vorhandene Codex-Anmeldung.
Browser und allgemeine Windows-Textfelder sind deaktiviert.

Unterstützt werden außerdem Startprompts, der Chat, Freitextfelder für
Lernaufgaben und Side Questions in der Desktop-Version von LearnHarness 0.7.0.
Die Korrektur funktioniert über die Aa-Schaltfläche oder das eingestellte
Tastenkürzel. Sie verwendet wie in Codex die bestehende Codex-Anmeldung.
In LearnHarness werden Korrekturen ausschließlich manuell ausgelöst. Enthält
das Eingabefeld einen eingebetteten Datei- oder Ordneranhang oder ein Zitat,
korrigiere den Text vor dem Einfügen dieses Elements.

Die C#-Quellen unter `src/` und deren Tests bleiben als Vergleichsreferenz erhalten.
Sie gehören nicht zum nativen Produktpaket. Auch die Programme unter `tests/`
werden nicht installiert.

## Bauen und prüfen

Voraussetzungen sind Visual Studio Build Tools 2022 mit MSVC v143, Windows SDK
einschließlich Windows ICU und CMake. `nlohmann/json` 3.12.0 liegt mit seiner
MIT-Lizenz im Repository. CMake prüft den SHA-256 des offiziellen Einzelheaders.
Es werden keine Abhängigkeiten während des Builds nachgeladen.

```powershell
.\scripts\build-native.ps1
```

Der Aufruf baut und prüft die nativen Ziele. Das erzeugte Paket unter
`dist/native-win-x64` enthält ausschließlich die EXE und Lizenzhinweise.
Ein erfolgreich gebautes Paket ersetzt keine Ressourcen- oder Live-Abnahme.

Der differenzielle Vergleich verwendet das unveränderte Verhalten der C#-Referenz:

```powershell
dotnet build native/tests/reference/NativeReference.csproj -c Release
python native/tests/run_differential.py `
  --native native/build/Release/native_differential.exe `
  --reference native/tests/reference/bin/Release/net8.0-windows/win-x64/CodexLanguageFix.Tests.exe `
  --report benchmark-results/native-differential.json
python native/tests/test_worker_protocol.py `
  --exe native/build/Release/CodexLanguageFix.exe `
  --report benchmark-results/native-worker-protocol.json
```

Der Kernvergleich deckt Schutzbereiche, Platzhalterwiederherstellung, Textstruktur,
Layoutwiederherstellung, Edit- und Rückwärtspläne, Wiederholungen, Cache-Schlüssel
und UTF-16-Graphemgrenzen ab. Der Prozess-Protokolltest erfasst keine echten Editoren.
Für UI- und Vordergrundtests werden ausschließlich künstliche Entwürfe verwendet.

## Schreiben und Speicher

Vor dem Schreiben werden Prozess, Fenster, Runtime-ID, Fokus, Ausgangstext und
Auswahl erneut geprüft. Änderungen werden rückgelesen. Ein unklarer Teilschreibzustand
wird nicht automatisch wiederholt. Undo ist an den unveränderten korrigierten Text
und die ursprüngliche Editoridentität gebunden.

Historische Tokens und Layoutcaches enthalten reine Daten. Layoutprüfungen lesen
Identität und Geometrie frisch. Ein dokumentweiter Fallback wird als vollständige
Suche gezählt und erhält keinen vermeintlich lokalen Geometriecache. Diagnose-APIs
liefern numerische Zähler ohne Texte, Kontodaten oder Tokens. Diagnoseinstrumente
müssen vor der abschließenden RAM-Abnahme abgekoppelt werden.

Die Graphemsegmentierung entspricht den Regeln der .NET-8-Referenz und fragt
Unicode-Eigenschaften über die Windows-ICU-C-API ab. Texte werden nicht normalisiert.
Quelle und Lizenz stehen in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
