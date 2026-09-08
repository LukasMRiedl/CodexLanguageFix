# Format- und Composer-Reparatur: Prüfstand vom 8. September 2026

Status: Release-Kandidat 1.3.1-rc.2, mehrere echte Oberflächenabläufe bestanden; noch keine vollständige Abnahme oder Auslieferung.

## Implementiert

- Gemeinsamer Strukturschutz für Luna und LanguageTool: Zeilenenden, Leerzeilen, Einrückung, Listen-/Markdownpräfixe, Checkboxen und abschließender Leerraum.
- Verifizierte rückwärts angewandte Textbereichsänderungen statt Volltext-Tippanimation. Die native Auswahl wird über `FindText` und den exakt geprüften vorangehenden Text zugeordnet, statt UIA-Zeicheneinheiten als Zeichenoffsets zu behandeln. Verifizierte Auswahlabfragen warten auf die tatsächliche Editorreaktion. Technische Spannen, Unicode-Grenzen und eingebettete Objekte bleiben geschützt.
- Auswahlprüfung vor dem ersten Schreiben, Identitäts- und Textprüfung vor jedem Schritt, vollständiges Rücklesen und eingeschränkte Rücknahme ausschließlich eigener verifizierter Änderungen.
- Originaltext im lokalen Wiederherstellungsfenster bei unklaren Teilschreibfehlern. Keine automatische Volltextwiederholung und keine Zwischenablagenutzung.
- Editorgebundenes Rückgängig, Abbruch bei Kontext-/Text-/Anbieterwechsel und Deaktivierung, konstant 28 DIP großer Knopf mit separatem Status und aktualisierter Monitorposition. Schwebende Eingabeflächen werden innerhalb ihres Hostfensters zugeordnet; ohne eindeutig freie Position wird der Knopf ausgeblendet.
- Overlay und eigenes Status-Popup verwenden No-Activate-Schutz, damit echte Mausklicks den Fokus im Hosteditor belassen.
- Wiederverwendung ausschließlich einer bereits vorhandenen gültigen app-eigenen Luna-Katalogkopie, falls der externe Katalogeintrag entfällt. Das Produktionsprofil bleibt `baseline / none / full-v1`, Modell `gpt-5.6-luna`, Tier `priority`.

## Automatisch verifiziert

- Aktueller expliziter Offline-Lauf mit ausgeschlossenen Live-Einstiegspunkten: 229 Tests bestanden, Build ohne Warnungen oder Fehler. Bericht: `test-results/offline-noactivate-20260908/offline.trx`.
- Echter Luna-Produktions-Smoke-Test mit OAuth, technischer Erhaltung und unverändertem Profil: bestanden.
- Zwölf feste Formatfälle je echtem Anbieter (24 Korrekturen insgesamt): sämtliche exakten Ausgaben und Strukturprüfungen bestanden. Gesamter Formatlauf rund 73 Sekunden. Kein Composer wurde durch diesen Providerlauf beschrieben.
- Ein erneuter abschließender Providerlauf ist noch in Prüfung; dessen Ergebnis wird erst nach Abschluss übernommen.
- Fälle: LF-/CRLF-Absätze, Stichpunkte, verschachtelte Listen, Nummerierung, Checkboxen, Markdown-Zeilenumbrüche, technische Spannen sowie vier unveränderte Kontrollen mit Unicode, Zitat, Code und abschließenden Leerzeilen.
- Künstliche Schreibziele prüfen Auswahlausfall, Fehler vor/nach Schreibschritten, Ausnahme nach bereits erfolgtem Schreiben, Fokusverlust, Nutzereingaben, falsche Editoridentität, Rücknahme und Texte bis 20.000 Zeichen.
- Geometrieprüfungen decken belegte Werkzeugleisten, Platzmangel, negative Monitorpositionen und Skalierungen von 100 bis 200 Prozent ab. WPF-Tests prüfen konstante Knopfgröße, separate Statusanzeige und getrennte Aktionen ohne Fokusübernahme.

Die ursprünglichen mehrdeutigen Rechtschreibfälle scheiterten an LanguageTools erster Ersatzempfehlung, nicht am Strukturschutz. Sie wurden für die isolierte Formatprüfung durch zuvor live bestätigte eindeutige Großschreibungs-/Artikelfehler ersetzt. Die exakten Orakel wurden nicht gelockert. Dies ist kein allgemeiner Qualitätsbenchmark und kein Nachweis einer Geschwindigkeitsüberlegenheit Lunas.

Eine versuchte Übernahme des globalen Modellcaches wurde wegen eines realen Schemafehlers der installierten Laufzeit verworfen und vollständig entfernt. Stattdessen wurde die bestehende app-eigene Katalogkopie isoliert im echten Smoke- und Formatlauf erfolgreich geprüft. Globale Codex-Einstellungen wurden nicht geändert.

Lokale maschinenlesbare Ergebnisse liegen unter `artifacts/format-repair/` und `test-results/` (nicht eingecheckt). Produktionsprotokolle enthalten keine Prompt- oder Antworttexte. Die ausdrücklich aktivierte Oberflächendiagnose darf ausschließlich zuvor verifizierte künstliche Testdaten ausgeben; echte Nutzerentwürfe werden dadurch nicht protokolliert.

## In der echten Oberfläche verifiziert

Die Prüfungen verwenden eigene, nicht abgesendete synthetische Entwürfe. Die anspruchsvollere Textvorlage enthält einen vom Editor erzeugten echten `•`-Listenmarker, Absatzabstand, Emoji und eine Beispiel-E-Mail-Adresse. Rücklesen und Wiederherstellung werden über den vollständigen UIA-Text exakt geprüft.

- Nativer Rich-List-Roundtrip: bestanden. Bericht: `artifacts/format-repair/desktop-normal-rich-list-findtext.trx`.
- Normaler Chat: vollständiger Ablauf mit `Aa`, echter Luna-Korrektur und Rückgängig bestanden; Testlauf rund sechs Sekunden. Bericht: `artifacts/format-repair/desktop-normal-rich-list-luna.trx`.
- Work-Eingabe: vollständiger Ablauf mit `Aa`, echter Luna-Korrektur und Rückgängig bestanden; Testlauf rund zehn Sekunden. Bericht: `artifacts/format-repair/desktop-work-final-range.trx`.

- RC-Testinstanz `ui15`, echter Mausklick in Antigravity: Korrektur und Rückgängig mit exakt wiederhergestelltem Original bestanden; rund vier Sekunden. Bericht: `artifacts/format-repair/desktop-antigravity-real-click.trx`.
- RC-Testinstanz `ui15`, echter Mausklick in der Plan-Ansicht: Korrektur und Rückgängig mit exakt wiederhergestelltem Original bestanden; rund acht Sekunden. Bericht: `artifacts/format-repair/desktop-plan-real-click.trx`.
- RC-Testinstanz `ui15`, echter Mausklick im normalen Chat mit Liste: Korrektur und Rückgängig mit exakt wiederhergestelltem Original bestanden; rund vier Sekunden. Bericht: `artifacts/format-repair/desktop-normal-list-real-click.trx`.

Die Laufzeiten beziehen sich auf diese konkreten Oberflächentests und sind kein allgemeiner Anbieter-Latenzvergleich. Die genannten Berichte belegen die getesteten Ansichten und Abläufe, keine vollständige Abnahme aller Appbereiche.

## Befunde aus den Zwischenläufen

Die ersten Oberflächenläufe deckten fehlende semantische Werkzeugleistencontainer, fälschlich als Hindernis behandelte Dokument-Vorfahrflächen und die zu große ursprüngliche 36-DIP-Knopfgröße auf. Eindeutig benachbarte Werkzeugleisten, gezielt bereinigte Containerhindernisse und der 28-DIP-Knopf beheben diese Fälle; gemessene Rechtecke wurden als Regressionstests übernommen.

Weitere vorübergehende Schreibablehnungen entstanden durch die Abbildung nativer UIA-Zeicheneinheiten auf Textoffsets in strukturierten Editoren sowie verzögert sichtbare Textauswahlen. Die aktuelle Auswahlzuordnung mit `FindText`, exakter Präfixprüfung und verifizierten Auswahlabfragen wurde anschließend mit den oben genannten erfolgreichen Rich-List-, Chat- und Work-Läufen geprüft. Ein früherer durch Aufgabenwechsel unterbrochener Rückgängig-Lauf ist ebenfalls durch die erfolgreichen vollständigen Abläufe ergänzt. Diese historischen Fehlläufe sind keine aktuellen Blocker der inzwischen bestandenen Szenarien.

Weitere UIA-Invoke-Fehlläufe hatten eine diagnostisch belegte Ursache im Prüfinstrument: Der programmatische Aufruf aktivierte das eigene Overlay-HWND und veränderte dadurch den Fokus vor der Editorprüfung. Die nachfolgenden Tests mit echten Mausklicks bestanden einschließlich exakter Wiederherstellung. Diese Instrumentenfehler werden nicht als Fehlschlag der inzwischen erfolgreich geprüften Klickabläufe gewertet.

## Noch offen

- Editdialoge echter bereits abgesendeter historischer Nachrichten sind noch nicht geprüft; die dafür notwendige Freigabe wurde angefragt.
- Reale Monitorwechsel und gemischte DPI-Werte sind noch nicht abgenommen.
- In einer engen Work-Ansicht mit überlagerndem Gesprächsverlauf kann die konservative Kollisionsprüfung den Knopf weiterhin ausblenden. Damit ist die Positionierung über alle schmalen und breiten Ansichten noch nicht vollständig abgenommen.
- Die reale Platzierung des separaten Status-Popups an Bildschirmrändern ist noch zu prüfen. Windows kann das Popup dort versetzen.
- UIA unterscheidet leere Eingabefelder mit Platzhaltertext noch nicht in allen Fällen zuverlässig; der `Aa`-Knopf kann dort sichtbar bleiben.
- Die aktuelle lokale RC-Testinstanz ist `ui15`. Die Aktualisierung der ursprünglichen Installation auf RC2 mit Sicherung ist vorgesehen, aber noch nicht als erfolgt verifiziert. Die GitHub-Veröffentlichung ist weiterhin ausstehend und blockiert.

Vor einem stabilen Release und dem abschließenden Ersatz der Installation müssen die verbleibenden Oberflächenprüfungen abgeschlossen oder ausdrücklich als ungeprüft ausgewiesen werden. Anschließend sind Versions- und Hashabgleich zwischen installierter und veröffentlichter Datei sowie der GitHub-Download zu prüfen. Die Reparatur ist derzeit nicht als vollständig ausgeliefert abgenommen.
