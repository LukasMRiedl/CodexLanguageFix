# Format- und Composer-Reparatur: Prüfstand vom 8. September 2026

Status: Release-Kandidat 1.3.1-rc.1, noch keine vollständige Oberflächenabnahme.

## Implementiert

- Gemeinsamer Strukturschutz für Luna und LanguageTool: Zeilenenden, Leerzeilen, Einrückung, Listen-/Markdownpräfixe, Checkboxen und abschließender Leerraum.
- Verifizierte rückwärts angewandte Textbereichsänderungen statt Volltext-Tippanimation. Technische Spannen, Unicode-Grenzen und eingebettete Objekte bleiben geschützt.
- Auswahlprüfung vor dem ersten Schreiben, Identitäts- und Textprüfung vor jedem Schritt, vollständiges Rücklesen und eingeschränkte Rücknahme ausschließlich eigener verifizierter Änderungen.
- Originaltext im lokalen Wiederherstellungsfenster bei unklaren Teilschreibfehlern. Keine automatische Volltextwiederholung und keine Zwischenablagenutzung.
- Editorgebundenes Rückgängig, Abbruch bei Kontext-/Text-/Anbieterwechsel und Deaktivierung, kompakter Knopf mit separatem Status und aktualisierter Monitorposition.
- Wiederverwendung ausschließlich einer bereits vorhandenen gültigen app-eigenen Luna-Katalogkopie, falls der externe Katalogeintrag entfällt. Das Produktionsprofil bleibt `baseline / none / full-v1`, Modell `gpt-5.6-luna`, Tier `priority`.

## Automatisch verifiziert

- Vollständiger lokaler Standardtestlauf: 200 grüne Tests einschließlich vier deaktivierter Opt-in-Einstiegspunkte; keine externen Anfragen in diesem Lauf.
- Abschließender expliziter Offline-Lauf mit ausgeschlossenen Live-Einstiegspunkten: 196 Tests bestanden, Build ohne Warnungen oder Fehler.
- Echter Luna-Produktions-Smoke-Test mit OAuth, technischer Erhaltung und unverändertem Profil: bestanden.
- Zwölf feste Formatfälle je echtem Anbieter (24 Korrekturen insgesamt): sämtliche exakten Ausgaben und Strukturprüfungen bestanden. Gesamter Formatlauf rund 73 Sekunden. Kein Composer wurde durch diesen Providerlauf beschrieben.
- Fälle: LF-/CRLF-Absätze, Stichpunkte, verschachtelte Listen, Nummerierung, Checkboxen, Markdown-Zeilenumbrüche, technische Spannen sowie vier unveränderte Kontrollen mit Unicode, Zitat, Code und abschließenden Leerzeilen.
- Künstliche Schreibziele prüfen Auswahlausfall, Fehler vor/nach Schreibschritten, Ausnahme nach bereits erfolgtem Schreiben, Fokusverlust, Nutzereingaben, falsche Editoridentität, Rücknahme und Texte bis 20.000 Zeichen.
- Geometrieprüfungen decken belegte Werkzeugleisten, Platzmangel, negative Monitorpositionen und Skalierungen von 100 bis 200 Prozent ab. WPF-Tests prüfen konstante Knopfgröße, separate Statusanzeige und getrennte Aktionen ohne Fokusübernahme.

Die ursprünglichen mehrdeutigen Rechtschreibfälle scheiterten an LanguageTools erster Ersatzempfehlung, nicht am Strukturschutz. Sie wurden für die isolierte Formatprüfung durch zuvor live bestätigte eindeutige Großschreibungs-/Artikelfehler ersetzt. Die exakten Orakel wurden nicht gelockert. Dies ist kein allgemeiner Qualitätsbenchmark und kein Nachweis einer Geschwindigkeitsüberlegenheit Lunas.

Eine versuchte Übernahme des globalen Modellcaches wurde wegen eines realen Schemafehlers der installierten Laufzeit verworfen und vollständig entfernt. Stattdessen wurde die bestehende app-eigene Katalogkopie isoliert im echten Smoke- und Formatlauf erfolgreich geprüft. Globale Codex-Einstellungen wurden nicht geändert.

Lokale maschinenlesbare Ergebnisse liegen unter `artifacts/format-repair/` (nicht eingecheckt). Sie enthalten keine Prompt- oder Antworttexte.

## Noch nicht abgenommen

Die echten Chat-, Work- und Plan-Eingabefelder, schmale/breite Fenster mit Seitenleisten, Monitorwechsel und gemischte DPI-Werte sind nach der Reparatur **ungeprüft**. Die verfügbare Oberflächenautomatisierung darf die ChatGPT-Desktop-Oberfläche nicht bedienen. Antigravity wurde geöffnet; Fensterwechsel und erkannte Nutzereingaben unterbrachen die Prüfung, bevor Testtext eingesetzt wurde. Korrektur und Rückgängig in Antigravity sind ebenfalls **ungeprüft**.

Die kollisionsfreie Geometrie des dauerhaften Knopfs ist offline geprüft. Windows kann das separate Status-Popup an Bildschirmrändern versetzen; dessen reale Platzierung benötigt ebenfalls die manuelle Abnahme.

Vor einem stabilen Release und dem abschließenden Ersatz der Installation sind diese Oberflächenprüfungen mit Korrektur, Kontrolltext und Rückgängig zu bestätigen. Ein nicht verfügbarer Prüfpunkt darf nicht als bestanden ausgewiesen werden. Danach folgen Versions-/Hashabgleich von installierter und veröffentlichter Datei sowie Prüfung des GitHub-Downloads.
