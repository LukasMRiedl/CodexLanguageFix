# RAM-Optimierung: Entwicklung und Abnahme

## Status

Das Ziel von mindestens 80 % weniger privatem Speicher **und** Working Set ist nicht erreicht. Die geprüfte Teilverbesserung ist zusammen mit der Hermes-Unterstützung Bestandteil von RC7 und wurde auf ausdrücklichen Wunsch lokal installiert; sie wird nicht als erfülltes RAM-Ziel oder stabiler Release ausgegeben.

## Änderungen

- Fremde Vordergrundprozesse werden über `QueryFullProcessImageNameW` geprüft, bevor UI-Automation beginnt. Die Erkennung benötigt keine Enumeration geladener Module mehr; erlaubte Hosts und Pfadregeln bleiben erhalten.
- Hintergrundprüfungen lesen Text, Editoridentität, Fenster, Rechteck und DPI frisch. Nur die Layoutzuordnung des aktuellen Editors wird wiederverwendet. Text-/Editor-/DPI-Änderungen sowie Fokus-, Fenster- und Layoutmeldungen verwerfen sie. Fehlerhafte oder nicht auflösbare Layouts werden im nächsten Takt erneut geprüft.
- Sichtbare Toolbar-Elemente werden mit einem kurzlebigen UIA-CacheRequest und gebündelten Eigenschaften gesucht. Ein nachgewiesen zugehöriger Composer-Container begrenzt die Suche. Wo dieser fehlt, bleibt die bisherige dokumentweite Zuordnung erhalten, um vorhandene Hostlayouts nicht ohne Live-Nachweis auszuschließen.
- Native Ereignisse werden auf 50-ms-Gruppen zusammengefasst; ohne Ereignisse läuft dafür kein Timer. Die 750-ms-Prüfung auf Nutzereingaben bleibt erhalten. Vor Einsetzen und Rücknahme werden vollständige frische Erfassungen verwendet.
- Die Ladeanimation läuft ausschließlich während einer sichtbaren Korrektur; Ausblenden, Leerlauf und Schließen entfernen ihre Animationsuhr.
- Die Release-Einzeldatei wird ohne interne Assembly-Kompression gebaut. .NET muss dadurch die Assemblies nicht mehr in anonyme beschreibbare Mappings dekomprimieren. Diese bestehende Buildoption benötigt keine neue Laufzeitabhängigkeit; die EXE wächst von 68,38 auf 154,46 MiB. [Microsoft-Dokumentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview#compress-assemblies-in-single-file-apps), [Runtime-Implementierung](https://github.com/dotnet/runtime/blob/v8.0.0/src/coreclr/vm/peimagelayout.cpp#L627-L667).
- `CodexLanguageFix-Memory` stellt bei angeschlossenem Diagnoseleser EventCounters für verwalteten Heap, GC-Commit, kumulierte Allokationen, UIA-Erfassungen und Layout-Erfassungen bereit. Ohne Leser erfolgt keine periodische Diagnoseabfrage. Es werden keine Texte oder Kontodaten erfasst.

## Bisherige Prüfungen

- Release-Testsuite: 470 erfolgreiche Offline-Testfälle. Opt-in-Livetests werden dadurch allein nicht als live ausgeführt gewertet.
- Neue Tests prüfen Layoutverwerfung, Editor-/Fenster-/DPI-Wechsel, Textänderungen, native Hostpfadregeln, Ereignisbündelung und Dispose-Zeitpunkte sowie den Animationslebenszyklus.
- Sechs echte cachefreie Luna-Korrekturen mit erneutem Vorwärmen bestanden. Keine Werkzeug-Hilfsprozesse; jeweils ein Konsolenhost. Serverprivatspeicher nach sechs Korrekturen rund 57,5 MiB. Dieser Test prüft weder App-Langzeitverbrauch noch eine aktive 80-%-Reduktion.
- Der LanguageTool-Livetest mit synthetischem Text bestand ebenfalls; Inline-Code blieb erhalten.
- Die lokal installierte RC7 erkannte den Hermes-Hauptcomposer, platzierte das Overlay und setzte bestätigte Luna-Korrekturen nach exaktem Readback erfolgreich ein. Referenzchips und weitere Hermes-Layoutvarianten wurden nicht vollständig abgenommen.
- Eine frühere explizite Live-Geometrieprüfung fand keinen erreichbaren Composer (`other_host`). Lange Unterhaltungen, 20.000-Zeichen-Entwürfe, zusätzliche Hostlayouts und DPI-Varianten bleiben am echten Editor offen.
- Die Vergleichsskripte prüfen vier unabhängige 80-%-Grenzen, unvollständige Läufe und nicht vergleichbare Metadaten. Ein echter Collector-Smoke prüft zusätzlich die Zeitsteuerung. Zu früh zurückkehrende Windows-Schlafaufrufe werden durch erneutes Prüfen der tatsächlichen Deadline behandelt; Messgrenzen werden nicht gelockert.

## Ausgangslage und Grenzen

Die lang laufende installierte RC6 belegte vor dem Neustart rund 2.022 MiB privaten Speicher und 159,5 MiB Working Set einschließlich eigener Hilfsprozesse. Die separat angeschlossene .NET-Diagnose zeigte nur etwa 34 MB GC-Commit in der App. Der große Speicheranteil liegt außerhalb des GC-Heaps; ein konkretes natives Leck ist dadurch noch nicht identifiziert.

Der Quellstand `10dcf01c3b7e4b322bd16ce1763520410b0d5223` und die installierte EXE (SHA256 `009B7996360ADD6DD55833249F7E1155BBD669735400FA632C6ADED584C84076`) wurden unter `artifacts/ram-optimization/baseline` gesichert. Ein sauberer Vergleichsbuild wurde separat aus dem Quellarchiv gebaut. Lange laufende und frisch gestartete Prozesse werden nicht als Prozentnachweis gegeneinander gerechnet.

Kurze Vergleichsläufe sind explorativ: 30 Sekunden Aufwärmen, 30 Messpunkte im Sekundentakt, gleicher Rechner, gleiche Hostversion, .NET 8.0.28 und selbstenthaltende Release-Einzeldatei. Es war kein Composer erreichbar. Diagnosezähler waren angeschlossen; diese Läufe ersetzen keine unbeeinflusste Abnahmemessung. Die ersten Kurzberichte mit frühen Zeitpunkten bleiben als `initial-short-*` erhalten und werden nicht als gültige Vergleichsläufe verwendet.

Die vollständige Matrix mit drei gepaarten 30-Minuten-Läufen und einem gepaarten 24-Stunden-Test je Hintergrundszenario wurde noch nicht ausgeführt. Anleitung und Einschränkungen stehen in [der Messanleitung](../scripts/memory-measurement.md). Kein künstliches Working-Set-Leeren, keine erzwungene GC und keine periodischen Neustarts sind Bestandteil der Optimierung.

## Gültig getakteter Kurzvergleich

Gemessen wurde jeweils der gesamte eigene Prozessbaum mit drei Prozessen. Die funktionalen Optimierungen mit weiterhin komprimierter EXE allein erreichten nur 3,7 % weniger privaten Speicher und 4,2 % weniger Working Set im Median. Mit abgeschalteter EXE-Kompression ergaben sich folgende Werte:

| Messgröße | Vergleichsbuild | Optimierter Build | Reduktion | 80-%-Grenze |
|---|---:|---:|---:|---|
| Privater Speicher, Median | 169,625 MiB | 86,969 MiB | 48,729 % | verfehlt |
| Privater Speicher, p95 | 170,418 MiB | 87,090 MiB | 48,896 % | verfehlt |
| Working Set, Median | 328,551 MiB | 186,713 MiB | 43,171 % | verfehlt |
| Working Set, p95 | 329,406 MiB | 186,809 MiB | 43,289 % | verfehlt |

Die Rohmessungen und negativen Ergebnisse stehen unter `artifacts/ram-optimization/{baseline,candidate,uncompressed}/short-memory.json`, `short-comparison.json` und `uncompressed-comparison.json`. Beim unkomprimierten Testbuild meldeten die Diagnosezähler null UIA- und Layout-Erfassungen: Die Wirkung des Layoutcaches am aktiven Editor wurde mit diesem Szenario ausdrücklich nicht gemessen. Die damalige unveränderte Installation wurde nach den Versuchen wieder gestartet; ihr Neustart war kein Optimierungsergebnis. RC7 wurde später nach den Offline-Prüfungen ausdrücklich installiert.

Der ursprüngliche Maßnahmenplan reicht mit diesen Messergebnissen nicht für eine zugesicherte 80-%-Reduktion. Offen bleiben die Untersuchung des nativen Langzeitwachstums und die vollständige Live-/Dauerabnahme. Die getesteten Änderungen werden daher trotz der ausdrücklich gewünschten RC7-Installation nicht als erfülltes RAM-Ziel veröffentlicht.
