# Desktop-Laufzeit und automatische Luna-Auswahl – RC8

## Umsetzung

Codex Language Fix löst die registrierte Windows-Paketfamilie `OpenAI.Codex_2p2nqsd0c76g0` über `GetPackagesByPackageFamily` und `GetPackagePathByFullName` auf und verwendet deren `app/resources/codex.exe`. Es gibt keine PATH-Suche und keinen WinGet-Fallback. Die Auflösung benötigt keinen laufenden Desktop-Prozess. Paketwechsel lassen aktive Korrekturen auslaufen, starten den eigenen App Server neu und verwerfen vorbereitete Threads.

Beim Serverstart werden das ChatGPT-Konto und alle Seiten von `model/list` mit `includeHidden=true` geprüft. Die höchste numerische Luna-Version mit ausdrücklich angebotenen Fähigkeiten `low` und `priority` wird gewählt. Fehlt eine Fähigkeit, wird die neueste passende ältere Version gewählt; ohne passende Version bleibt die Korrektur gesperrt. Die Auswahl bleibt für die Laufzeit erhalten und wird bei Laufzeit- oder Kontokontextwechsel neu ermittelt. Eine anfängliche Kontobenachrichtigung erlaubt genau eine vollständige Wiederholung der Konto- und Katalogabfrage; Inferenz wird nicht automatisch wiederholt.

Das unveränderliche Profil wird ausdrücklich an sämtliche Korrekturpfade weitergereicht und vor dem Ergebnis-Cache aufgelöst. Modell, Denkstufe und Service-Tier gehören zum Cache-Schlüssel und zur Ausführungsmetadaten-Ausgabe. Vorbereitete Threads werden nur mit vollständig identischem Profil wiederverwendet. Die private Katalogmanipulation ist entfernt; die gemeinsame Codex-Konfiguration bleibt erhalten.

Innere Modelldaten enthalten Unicode und Markdown-Zeichen direkt als gültiges JSON. Notwendige Escapes für Anführungszeichen, Backslashes und Steuerzeichen bleiben erhalten; der äußere RPC-Serializer bleibt unverändert. Der vorhandene Entwicklerprompt und sämtliche Ausgabe-, Platzhalter-, Struktur- und Injektionsgatter bleiben unverändert.

## Verifikation

- 494 Tests der vollständigen Standardsuite bestanden, einschließlich Paketfindung, fehlender Installation, Paketwechsel, Pagination, numerischem Versionsvergleich, Capability-Auswahl, Kontokontextwechsel, Cache-Isolation, parallelen Profilen und veralteten vorbereiteten Threads. Bericht: `tests/CodexLanguageFix.Tests/TestResults/desktop-runtime-low-unit-final.trx`.
- Live-Auswahl: `gpt-6-luna / low / priority` über die Desktop-CLI `0.159.2`.
- Vier Live-Testgruppen bestanden: deutsche Korrektur mit technischen Stellen, mehrere Fehler bei unverändertem Layout, zwölf deutsche und englische Formate mit korrekten Kontrolltexten sowie sechs wiederholte Korrekturen ohne Werkzeugprozesse. Bericht: `tests/CodexLanguageFix.Tests/TestResults/desktop-runtime-low-readable-live.trx`.
- Die ursprüngliche JSON-Codierung lieferte beim künstlichen Blockzitat sporadisch eine NUL-Modellantwort. Das Schutzgatter wies sie ab. Mit lesbaren inneren Modelldaten bestanden fünf direkte Diagnoseaufrufe und anschließend alle zwölf Live-Formate. Dies ist kein allgemeiner Nachweis, dass das Modell niemals ungültige Antworten erzeugt.

Die vollständige Live-Qualifikation mit allen 128 Korpusfällen und einer Wiederholung bestand. Insgesamt wurden 144 von 144 geplanten Messungen ausgeführt: 128 Luna-Korpusfälle und je acht gepaarte Luna-/LanguageTool-Fälle. Es gab keine Anfragefehler, alle 32 Korpuskontrollen blieben exakt und alle 53 sicherheitskritischen Luna-Messungen bestanden. Luna korrigierte 88 der 96 Fehlerfälle exakt; der Korpus-F0,5-Wert betrug 0,9402. Der gesamte Lauf dauerte rund 7 Minuten 48 Sekunden. Bericht: `benchmark-results/desktop-runtime-low-readable-2026-09-30/CodexLanguageFix-Luna-Benchmark-v3-20260930T144708984Z-70f824ab52404379b314f2db59772af4.json`.

Im kleinen gepaarten Vergleich mit acht Fällen betrug F0,5 für Luna 0,75 und für LanguageTool 0,3125. Luna war langsamer: p50 2,70 Sekunden gegenüber 0,30 Sekunden, etwa das Neunfache. Dies ist ein begrenzter Qualitätsvergleich; das Qualitäts- und Geschwindigkeitsgatter gemeinsam bestand nicht, und eine Überlegenheit bei der Geschwindigkeit wird nicht behauptet. Die Release-Qualifikation verlangt die unveränderten Sicherheitsgatter, keinen Geschwindigkeitsgewinn gegenüber LanguageTool. Im gesamten 128er-Korpus lag Luna-p50 bei 3,17 Sekunden und p95 bei 5,27 Sekunden.

Fehlgeschlagene Benchmark-Zellen werden nun vor dem Abschlussgatter als vollständiger Bericht gespeichert.

## Lokale Auslieferung

- Der selbstenthaltende Release-Build `1.3.1-rc.8` wurde am bestehenden, eindeutig identifizierten Installationsort installiert. Kandidat und installierte EXE besitzen SHA256 `B95F403683A6C7E63C99830053F46568370AB9603960EEF52817F6C453E3267B`.
- Die vorherige RC7-EXE und der vorhandene private Modellkatalog wurden unter `artifacts/desktop-runtime-low/backup-rc7-20260930-170101` gesichert und vor dem Austausch geprüft. Der aktive private Katalog wurde anschließend entfernt.
- Der gestartete Helfer meldete `application_started` und `luna_warmup_completed`; sein eigener App Server lief nachweislich aus dem registrierten Desktop-Paket. Die zum Installationszeitpunkt vorhandene globale Konfiguration und die Anbietereinstellung blieben erhalten.
- Vor der Deinstallation war kein weiterer WinGet-CLI-Aufrufer aktiv. Die reguläre, benutzerspezifische `OpenAI.Codex`-Installation `0.146.1` wurde über WinGet entfernt; Paketabfrage, EXE und WinGet-Alias bestätigen ihre Abwesenheit. Die Desktop-Installation bleibt vorhanden.
- Nach der WinGet-Entfernung bestanden erneut die Live-Korrekturprüfung mit technischen Stellen und sechs wiederholte Ressourcenprüfungen ohne Werkzeugprozesse. Bericht: `tests/CodexLanguageFix.Tests/TestResults/desktop-runtime-low-after-winget-removal.trx`.

Die Live-Prüfungen verwenden ausschließlich synthetische Texte. Ein echter Aa-Klick in einem Benutzercomposer und sämtliche UI-/Monitorvarianten wurden bei dieser Laufzeitänderung nicht erneut geprüft. Historische Speicherzahlen anderer Profile werden nicht auf RC8 übertragen; künftige automatische Modellwechsel behalten die vorhandenen Ausgabeprüfungen bei, sind aber durch diesen datierten Lauf nicht vorab qualifiziert.
