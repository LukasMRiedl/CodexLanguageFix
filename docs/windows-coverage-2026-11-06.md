# Windows-Felder und nativer Absturz am 6. November 2026

Status: Lokaler Fix installiert. Korrektur und Rückgängig sind in Codex, Windows-Editor und WhatsApp Web vom Nutzer bestätigt. Die Abdeckung aller Windows-Anwendungen bleibt **partial**.

## Live-Befund

Die laufende Installation unter `%LOCALAPPDATA%\Programs\CodexLanguageFix\Browser-20261106-Only` wurde mit `--autostart --browser-only` gestartet. Der Autostart enthielt dieselben Argumente; das Produktionsprotokoll meldete `browser_only_started`. Die normale Codex-Installation wurde anhand ihres Prozesspfads korrekt erkannt.

Nach Entfernung von `--browser-only` und Explorer-Neustart derselben unveränderten EXE bestätigte der Nutzer, dass Aa in Codex erscheint. Während einer anschließenden Korrektur beendete sich der Prozess jedoch mit `System.AccessViolationException` in `UiaCoreApi.RawTextRange_GetText`, aufgerufen aus der Auswahlprüfung in `NativeEditTarget.Replace`. Windows protokollierte am 6. November um 15:13:41 Uhr MEZ Ereignis 1026 (.NET Runtime) und um 15:13:42 Uhr Ereignis 1000 (Application Error). Der nachfolgende Nutzerbericht zum verschwundenen Knopf passt zu diesem nachgewiesenen Prozessende.

Der Stack unterscheidet die fünf Textbereichslesevorgänge der Auswahlprüfung nicht. Ein konkreter Fehler der Bereichslebensdauer oder des Chromium-Providers ist damit nicht bewiesen. Ein ähnlicher Fehler ist im [Microsoft-UI-Automation-Projekt](https://github.com/microsoft/Microsoft-UI-UIAutomation/issues/100) gemeldet; dieser Bericht beweist keine identische Ursache auf diesem Rechner.

Der Browser-only-Autostart wurde anschließend aus dem gesicherten Zustand wiederhergestellt und mit derselben EXE erneut gestartet. Der fehlerhafte Desktopmodus ist damit derzeit nicht dauerhaft aktiviert.

## Änderungen am Quellstand

- Die aktive Felderkennung pollt nach einem vorübergehend fehlenden UIA-Snapshot weiter. Fehlender Fokus und nicht verfügbare Felder verbergen weiterhin Aa und deaktivieren das Kürzel; geöffnete Einstellungen und Pause stoppen das Polling.
- Zusätzliche normale `Edit`-Felder können über ihr schreibbares `ValuePattern` erkannt werden. Passwort-, Read-only-, Fokus-, Metadaten- und Codeausschlüsse bleiben bestehen. Die reine Klasse eines unbekannten TextPattern-Editors genügt nicht.
- Die Auswahlprüfung erhält textfreie Phasenmarker für Dokument, Präfix, Endpräfix, Auswahl und Suffix. Diese Lesungen sind auf die Ausgangstextlänge plus ein Zeichen begrenzt; längere Ergebnisse werden weiterhin durch den exakten Vergleich verworfen. Die Sicherheitsprüfungen werden nicht entfernt.
- Die native UI-Automation wird in einen getrennten Prozess verlagert. Ziel ist die Begrenzung eines nativen Absturzes auf diesen Prozess. Dies ersetzt nicht den noch ausstehenden Nachweis einer erfolgreichen Korrektur im betroffenen echten Feld.

## Prüfung und Grenzen

Vor der Prozessisolation bestanden 746 automatisierte Release-Tests. Die opt-in Live-Tests führen ohne ihre jeweiligen Umgebungsvariablen keine Live-Prüfung aus; diese Anzahl ist deshalb kein Nachweis für 746 geprüfte Windows-Felder.

Der abschließende Quellstand besteht 753 automatisierte Release-Tests (`windows-coverage-final.trx`). Sieben davon prüfen die neue Prozessgrenze: echter Headless-Handshake ohne Feldzugriff, stabile Editorzuordnung, maximale Unicode-Nachrichten, Prozessabbruch, Timeout, ungültige Antwort und Abbruch während des Beendens. Der Release-Build erzeugt keine Warnungen oder Fehler. Ein unabhängiges Review fand in diesen geprüften Pfaden keinen konkreten verbleibenden Fehler.

Der Kandidat `artifacts/coverage-20261106/release-isolated/CodexLanguageFix.exe` wurde vorübergehend über Explorer gestartet; Haupt- und Textfeldprozess sind getrennt nachgelesen. Der bestehende Autostart bleibt während der Abnahme im gesicherten Browser-only-Modus. Die produktive Installation wurde durch diesen Start nicht ersetzt. SHA-256 des laufenden Kandidaten: `797EDCE5A36681606C4ADC5F844B27CAD0136B1DD2F0E1885F95DA33D0C15112`.

### Ergebnis des isolierten Live-Tests

Der Nutzer meldete erneut, dass nach dem Prüfen das Symbol verschwindet. Windows bestätigte mehrere native Abstürze des Textfeldprozesses, während der Hauptprozess mit PID 8528 weiterlief. Das Protokoll meldete jeweils `composer_write_rejected_worker_unavailable_selection_selected`; der Textfeldprozess wurde für die nächste Erkennung neu gestartet. Die Prozessgrenze hat damit auch im echten Fehlerfall den Hauptprozess geschützt. Eine erfolgreiche Korrektur war damit weiterhin nicht erreicht.

Die Phasenmarker grenzen den Fehler jetzt auf `GetText` des direkt von `GetSelection` gelieferten Bereichs ein. Der nächste Kandidat übernimmt die tatsächlichen Auswahlendpunkte in einen Klon des Dokumentbereichs und liest diesen. Dokument, Präfix, Endpräfix, ausgewählter Text und Suffix werden unverändert exakt geprüft. Ein unabhängiges Review fand darin keine Abschwächung der Auswahlprüfung. Der erneut konfliktfreie Testlauf besteht 753 Prüfungen (`windows-selection-document-range-clean.trx`); ein Zwischenlauf hatte sechs Fehler 1409, weil der noch laufende Kandidat das echte globale Testkürzel belegte. Diese sechs Fehler sind kein unveränderter Baseline-Befund und wurden nach Beenden des Kandidaten nicht reproduziert.

Der Auswahl-Kandidat läuft vorübergehend unter `artifacts/coverage-20261106/release-selection-range/CodexLanguageFix.exe`, SHA-256 `BC723C5457B2D220008CB0F35F5C7F70E5F740E40543363462748D90256B277F`. Hauptprozess 28032 und Textfeldprozess 29356 wurden getrennt nachgelesen. Der Autostart bleibt während der Abnahme unverändert im Browser-only-Modus.

### Bestätigter Auswahl-Fix und Vorbereitung

Der Nutzer bestätigte anschließend die echte Korrektur samt Rückgängig in Codex und danach ebenfalls Aa, Korrektur und Rückgängig im Windows-Editor. Das Produktionsprotokoll bestätigt erfolgreiche `composer_write_completed`- und `composer_undo_completed`-Ereignisse nach dem gezielten Auswahl-Fix.

Die gespeicherte Einstellung `prepareLunaCorrections` ist weiterhin `true`. Am 6. November um 15:36:55 Uhr MEZ begann eine Vorbereitung mit 13 Zeichen, um 15:36:57 Uhr folgten `preparation_completed`, `preparation_adopted`, `correction_provider_completed` mit `cache_hit` und ein verifizierter Schreibabschluss. Der Klick erfolgte vor Abschluss der Vorbereitung und wartete noch etwa 1,29 Sekunden auf den Anbieter; die gesamte Korrektur dauerte etwa 1,56 Sekunden. Das ist ein Funktionsnachweis der Übernahme, kein kontrollierter Geschwindigkeitsvergleich. Beim Weiterschreiben wurden andere vorbereitete Zwischenstände wie vorgesehen abgebrochen.

Ein zusätzliches Review von Browser-Bridge, Native Host und Erweiterung fand keinen konkreten Sicherheits- oder Schreibfehler. Die Prüfung der bestehenden Browserfunktion mit diesem Kandidaten ist separat angefragt.

### WhatsApp-Mehrfehlerkorrektur

Der Nutzer meldete aus `Das ist ein Tset. Und ncoh ein Fheler.` das Teilresultat `Das ist ein Tset. Und ncoh ein Fhehler.`. Das entspricht exakt dem ersten, rückwärts angewandten Einfügeschritt des bisherigen mehrteiligen DOM-Plans. Der neue Test `browser-extension/tests/whatsapp-managed.cjs` reproduzierte diesen Zustand mit einem verwalteten Editor, der nach dem ersten Eingabebefehl seinen Textknoten ersetzt und weitere Befehle derselben Transaktion ablehnt.

Der eindeutig erkannte WhatsApp-Composer erhält nun eine zusammenhängende Änderung innerhalb eines vorhandenen Textknotens. Änderungen über Formatierungsgrenzen werden vor dem Schreiben abgelehnt; andere Webeditoren behalten ihren bisherigen Pfad. Der Regressionstest besteht alle drei Korrekturen und exaktes Undo mit jeweils einem Schreibbefehl. Zehn Kernprüfungen, Browser-DOM-Matrix, Wiederverbindungs- und Dokumenttypprüfungen bestehen ebenfalls. Die vier echten Luna-Mehrfehlerfälle bestehen zusätzlich (`multi-error-live-enforced.trx`); ein vorheriger Lauf ohne wirksam gesetzte Opt-in-Variable war kein Live-Nachweis, ein Versuch im eingeschränkten Dateisystem konnte die offizielle Runtime nicht finden. Erst der explizit aktivierte erhöhte Lauf gilt als bestanden.

Der geladene Chrome-Erweiterungspfad wurde als `C:\Private Projects\Textkorrektur-App\browser-extension` nachgelesen. Nach Neuladen der Erweiterung und von WhatsApp Web bestätigte der Nutzer alle drei Korrekturen und das exakte Rückgängig. Der synthetische Test wird dadurch nicht als Test der echten Lexical-Bibliothek ausgegeben; die produktive Bestätigung ist ein eigener Nachweis.

Die native Desktopsteuerung konnte den Windows-Editor auch nach erneuter Auswahl nicht bedienen (`foreground window did not report a process id`). Sie wurde danach nicht weiter verwendet. Der Nutzer bestätigte nur die wieder sichtbare Codex-Schaltfläche; eine erfolgreiche native Korrektur samt Undo ist für diesen Lauf nicht bestätigt.

Passwort- und sensible Felder, technische Eingaben, nicht zugängliche Editoren und erhöhte Windows-Oberflächen sind keine pauschal unterstützten Felder. Die [Windows-Sicherheitsgrenzen für UI Automation](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-securityoverview) bleiben erhalten. Auch die vorhandene Chrome-Erweiterung belegt keine Unterstützung jedes Browsers oder jeder Webanwendung.

Die bestehende Installation und Einstellungen sind als ZIP mit sieben nachgelesenen SHA-256-Dateihashes unter `artifacts/coverage-20261106/installation-backup-20261106-151235` gesichert. Diagnoseereignisse, Testberichte und Start-Readback liegen unter `artifacts/coverage-20261106`. Der Readback ist ausdrücklich als nach dem Absturz zurückgerollt markiert.

## Dauerhafte lokale Installation

Nach den erfolgreichen Prüfungen wurde derselbe bestätigte Desktop-Build unter `%LOCALAPPDATA%\Programs\CodexLanguageFix\CodexLanguageFix.exe` installiert. Sein SHA-256 stimmt mit dem geprüften Kandidaten überein: `BC723C5457B2D220008CB0F35F5C7F70E5F740E40543363462748D90256B277F`. Die Versionsmetadaten lauten weiterhin 1.5.2.0; daraus wird kein vollständiger stabiler Release 1.6.0 abgeleitet.

Hauptprozess 34740 wurde über Explorer gestartet; Textfeldprozess 2720 gehört zu diesem Hauptprozess. Der Autostart lautet auf die kanonische EXE mit `--autostart`, ohne `--browser-only`. Die gespeicherten Einstellungen sind per SHA-256 unverändert, einschließlich aktivierter Vorbereitung. Die vorhandene Native-Host-Registrierung wurde nicht ersetzt. Das Installations-Readback liegt unter `artifacts/coverage-20261106/installed-readback.json`.

Die früher dokumentierten breiteren Oberflächen- und Monitorprüfungen bleiben offen. Die lokale Installation behebt die nachgewiesenen Fehler in den geprüften Pfaden; sie ist keine pauschale Abnahme aller Windows-Felder. Die bereits vorher vorhandenen Änderungen an den Speichermesswerkzeugen gehören nicht zu diesem Fix und werden nicht in dessen Commit einbezogen.
