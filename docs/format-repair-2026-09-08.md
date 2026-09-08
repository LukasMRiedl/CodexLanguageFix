# Format- und Composer-Reparatur: Prüfstand vom 8. September 2026

Historischer Prüfstand: Release-Kandidat 1.3.1-rc.2. Die lokale Installation wurde anschließend durch [RC3 mit reparierter Auswahlprüfung](selection-repair-2026-09-08.md) ersetzt. Dieser Bericht bewahrt die damaligen Ergebnisse und offenen Abnahmepunkte; eine vollständige stabile Abnahme oder GitHub-Veröffentlichung wird nicht behauptet.

## Implementiert

- Gemeinsamer Strukturschutz für Luna und LanguageTool: Zeilenenden, Leerzeilen, Einrückung, Listen-/Markdownpräfixe, Checkboxen und abschließender Leerraum.
- Verifizierte rückwärts angewandte Textbereichsänderungen statt Volltext-Tippanimation. Die native Auswahl wird über `FindText` und den exakt geprüften vorangehenden Text zugeordnet, statt UIA-Zeicheneinheiten als Zeichenoffsets zu behandeln. Verifizierte Auswahlabfragen warten auf die tatsächliche Editorreaktion. Technische Spannen, Unicode-Grenzen und eingebettete Objekte bleiben geschützt.
- Auswahlprüfung vor dem ersten Schreiben, Identitäts- und Textprüfung vor jedem Schritt, vollständiges Rücklesen und eingeschränkte Rücknahme ausschließlich eigener verifizierter Änderungen.
- Originaltext im lokalen Wiederherstellungsfenster bei unklaren Teilschreibfehlern. Keine automatische Volltextwiederholung und keine Zwischenablagenutzung.
- Editorgebundenes Rückgängig, Abbruch bei Kontext-/Text-/Anbieterwechsel und Deaktivierung, konstant 28 DIP großer Knopf mit separatem Status und aktualisierter Monitorposition. Schwebende Eingabeflächen werden innerhalb ihres Hostfensters zugeordnet; ohne eindeutig freie Position wird der Knopf ausgeblendet.
- Overlay und eigenes Status-Popup verwenden No-Activate-Schutz, damit echte Mausklicks den Fokus im Hosteditor belassen.
- Wiederverwendung ausschließlich einer bereits vorhandenen gültigen app-eigenen Luna-Katalogkopie, falls der externe Katalogeintrag entfällt. Das Produktionsprofil bleibt `baseline / none / full-v1`, Modell `gpt-5.6-luna`, Tier `priority`.

## Automatisch verifiziert

- Aktueller expliziter Offline-Lauf mit ausgeschlossenen Live-Einstiegspunkten: 229 Tests bestanden, Build ohne Warnungen oder Fehler. Bericht: `artifacts/format-repair/final-offline-20260908/offline-final.trx`.
- Echter Luna-Produktions-Smoke-Test mit OAuth, technischer Erhaltung und unverändertem Profil: bestanden.
- Zwölf feste Formatfälle je echtem Anbieter (24 Korrekturen insgesamt): sämtliche exakten Ausgaben und Strukturprüfungen bestanden. Abschließender echter Formatlauf rund 74 Sekunden; Bericht: `artifacts/format-repair/providers-format-interactive.trx`. Kein Composer wurde durch diesen Providerlauf beschrieben.
- Ein vorheriger isolierter Agentenlauf hatte weder eine verwendbare OAuth-Sitzung noch den nötigen Netzwerkzugriff und ist keine gültige Providermessung. Der abschließende Lauf mit freigegebenem Zugriff bestand alle 24 Korrekturen.
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

## Lokale Installation verifiziert

Die tatsächliche Installation wurde auf `1.3.1-rc.2` aktualisiert. Sie stammt aus Commit `150e3f7396baa50767d051e56d418c66d46da79f`; die Produktversion enthält `1.3.1-rc.2+` und diesen Commit-Hash.

- Installierte Datei: `C:\Users\lukas\.codex\.chatgpt-projects\g-p-6a60c6d22cb88191be272153223ad7f6\dist\CodexLanguageFix\CodexLanguageFix.exe`.
- Die EXE im Paket und die installierte EXE sind hashgleich: SHA-256 `BA745291950D8013E886A9AD61CA495B71A67F07E84DAD9901E05B5C84224A49`.
- Die vorherige Version 1.3.0 wurde unter `artifacts/format-repair/installed-backup-1.3.0.exe` gesichert; SHA-256 `1993B89749B9F1B135CA2C01080C07883E02D190D9AAF6579030049281559683`.
- Ein echter Klick-Smoke-Test der installierten RC2-Version bestand; rund drei Sekunden. Bericht: `artifacts/format-repair/installed-rc2-real-click-smoke.trx`.

Diese lokale Aktualisierung ist abgeschlossen. Die nachstehenden Einschränkungen und die ausstehende Veröffentlichung bleiben davon unberührt.

## Noch offen

- Editdialoge echter bereits abgesendeter historischer Nachrichten sind noch nicht geprüft; die dafür notwendige Freigabe wurde angefragt.
- Reale Monitorwechsel und gemischte DPI-Werte sind noch nicht abgenommen.
- In einer engen Work-Ansicht mit überlagerndem Gesprächsverlauf kann die konservative Kollisionsprüfung den Knopf weiterhin ausblenden. Damit ist die Positionierung über alle schmalen und breiten Ansichten noch nicht vollständig abgenommen.
- Die reale Platzierung des separaten Status-Popups an Bildschirmrändern ist noch zu prüfen. Windows kann das Popup dort versetzen.
- UIA unterscheidet leere Eingabefelder mit Platzhaltertext noch nicht in allen Fällen zuverlässig; der `Aa`-Knopf kann dort sichtbar bleiben.
- RC2 ist noch nicht auf GitHub veröffentlicht.

Vor einem stabilen Release müssen die verbleibenden Oberflächenprüfungen abgeschlossen oder ausdrücklich als ungeprüft ausgewiesen werden. Nach einer Veröffentlichung sind die GitHub-Datei und der Download gegen den installierten Build abzugleichen. Eine vollständige stabile Abnahme wird weiterhin nicht behauptet.
