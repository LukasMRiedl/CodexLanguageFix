# Sofortkorrektur und Luna-Vorbereitung, 13. Oktober 2026

Die finale Geschwindigkeits- und Qualitätsabnahme ist bestanden. Im realen Codex-Composer sank die Wartezeit vom echten AltGr-Komma bis zum verifizierten Einfügen im Median um 1.562 ms beziehungsweise 34,5 %. Alle 60 Korrekturen entsprachen dem erwarteten Text; Rückgängig und anschließendes Entfernen wurden jeweils exakt bestätigt. Es wurde keine Nachricht abgesendet.

| Messung | Baseline | Kandidat |
| --- | ---: | ---: |
| Median | 4.529,78 ms | 2.967,37 ms |
| p95 | 6.639,47 ms | 5.666,15 ms |
| Mittelwert | 4.631,54 ms | 3.485,58 ms |
| Exakte Ergebnisse | 30/30 | 30/30 |
| Fehler oder Abbrüche | 0 | 0 |

Der vollständige [Messdatensatz](shortcut-speed-2026-10-13.json) enthält alle Einzelmessungen ohne Textinhalte. Baseline war Commit `a5b91873bc58e7c637f42c95b573ef74f7daeba5`. Beide Varianten verwendeten `gpt-6-luna / low / priority` mit unverändertem Prompt und Protokoll, aufgewärmter Laufzeit und ausgeschaltetem Ergebniscache. 30 Paare liefen in wechselnder Reihenfolge, jeweils zehn mit kurzen, mittleren und langen synthetischen Texten. Zwischen letzter Eingabe und Shortcut lagen höchstens 300 ms. Kein Ergebnis wurde aus der Bewertung entfernt.

Die Abnahme gilt für den zusammengefassten Durchlauf auf dieser Installation. Einzelne Modellantworten schwanken weiterhin: Beim langen Text war der höchste Kandidatenwert 9.541 ms gegenüber 7.188 ms der Baseline; bei zehn Messungen ist dies zugleich das p95 dieser Teilgruppe. Das bessere Gesamt-p95 verspricht daher keine schnellere einzelne Anfrage.

## Änderung und Qualitätsprüfung

Der Korrekturpfad erfasst Editoridentität, Text, Fokus, Bearbeitbarkeit und Editor-Geometrie frisch, ohne erneut Toolbar-, Hindernis- und Overlay-Platzierungsdaten zu suchen. Die Overlay-Aktualisierung ermittelt diese Daten weiterhin. Auswahlprüfung, Schutz technischer Stellen, exakter Readback, Rollback, Rückgängig und Wartefristen wurden erhalten.

Ein vorausgehender vollständiger Durchlauf erfüllte bereits das Geschwindigkeitsziel, scheiterte aber an zwei Luna-Antworten, die bei langen Texten je eine unverändert wiederholte Satzfolge ausließen. Er wurde nicht als bestandene Abnahme gewertet. Der finale Kandidat erkennt eine verringerte Anzahl noch vorhandener, unverändert wiederholter Satzfragmente und wiederholt die Anfrage höchstens einmal unter demselben Abbruchsignal und Zeitlimit. Wiederholter Verstoß wird abgewiesen und nicht gecacht. Dies ist eine gezielte Prüfung dieses beobachteten Fehlers, keine allgemeine Garantie gegen jede semantische Auslassung. Prompt, Modell und Grundkorrektur bleiben unverändert; zusätzliche Anfragen können Kontingent verbrauchen.

Die abschließende automatisierte Suite bestand mit 660 Tests und ohne Fehler. Kontrollierte Zeit und simulierte Provider prüfen Pausenfrist, fortlaufendes Schreiben, Doppelanfragen, Übernahme, verspätete Antworten, Fokus-/Anbieterwechsel, Fehler, Abschalten, Rückgängig und Vorrang manueller Korrekturen. Tests belegen insbesondere, dass Vorbereitung keinen Schreibzugriff auslöst und LanguageTool keine automatische Anfrage erhält. Da der Prompt unverändert blieb, war der bedingte erneute 128-Fälle-Promptvergleich nicht erforderlich.

## Vorbereitung separat geprüft

Die optionale Vorbereitung startet nach 1,2 Sekunden beobachteter Textstabilität; das bestehende 750-ms-Polling kann dies verzögern. Sie ist standardmäßig ausgeschaltet und auf Luna beschränkt. Die drei Live-Szenarien mit dem finalen Kandidaten bestanden einschließlich exaktem Einfügen, Rückgängig und Entfernen:

| Szenario | Verhalten | Shortcut bis Einfügen |
| --- | --- | ---: |
| Zwei Sekunden Pause | Laufende Vorbereitung übernommen, anschließend Cache-Treffer | 3.927 ms |
| Fünf Sekunden Pause | Fertige Vorbereitung übernommen, Cache-Treffer | 249 ms |
| Weiterschreiben während Vorbereitung | Alte Anfrage abgebrochen, neue bewusste Anfrage ohne Übernahme | 3.336 ms |

Vor dem bewussten Korrekturbefehl fand kein Einfügen statt. Die Zwei-Sekunden-Pause genügte in dieser Messung nicht zum Abschluss der Modellanfrage. Diese Szenarien ersetzen nicht den Nachweis für den Sofort-Shortcut. Automatisch übertragene ungesendete Zwischenstände und Kontingentverbrauch werden in Erststarttext, Datenschutz und beiden READMEs erläutert.

## Lokale Auslieferung

Version 1.5.0 wurde nach bestandener Abnahme als eigenständige Windows-x64-EXE gebaut. Der vorhandene Autostartpfad bestätigte den Installationsort; EXE und Einstellungen wurden vor dem Austausch gesichert. Die installierte EXE stimmt per SHA-256 mit dem Release-Build überein (`19F6E33882BEB6F9D1C1D471FEC83BBBA90C7AFE8B7539A2796B58D3952B7BFB`). Der unabhängige Launcher bestätigte Explorer als Elternprozess. Für diese Installation wurde `prepareLunaCorrections` aktiviert; der Standard für andere Installationen bleibt `false`.

Der anschließende Opt-in-Test gegen den tatsächlich installierten Prozess bestand: Er trug ausschließlich den synthetischen Text in den fokussierten Composer ein, betätigte echtes AltGr-Komma, bestätigte das erwartete Ergebnis, klickte Rückgängig im eigenen Overlay und bestätigte anschließend Originaltext und vollständige Bereinigung. Der erste Versuch des neuen Prüfprogramms brach vor einer Eingabe wegen der Leerfeld-Darstellung mit abschließendem Zeilenumbruch ab; der Prüfer wurde an dieselbe eng begrenzte Placeholder-Prüfung wie der SpeedProbe angepasst. Die Anwendung selbst erforderte dafür keine Änderung.
