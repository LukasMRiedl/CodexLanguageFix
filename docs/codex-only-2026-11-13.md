# Codex-Beschränkung und RAM-Prüfung vom 13. November 2026

## Umfang und Stand

Version **1.5.4** beschränkt die Textkorrektur auf die OpenAI-Codex-Desktop-App für Windows. Die zuvor entwickelten Pfade für allgemeine Windows-Anwendungen, Antigravity, Hermes und Browser sind deaktiviert. Einstellungen, Vorbereitung und Tastenkürzel werden beibehalten. Der Autostart der alten breiten Version ist bis zur vollständigen Abnahme vorübergehend deaktiviert.

**Zwischenstand am Abend:** Der geprüfte Kandidat läuft unabhängig über Explorer aus dem Artefaktverzeichnis. Der neue Dauertest wurde durch Standby ungültig. Der Nutzer hat einen weiteren zweistündigen Lauf für heute abgelehnt. Die kanonische Installation wurde noch nicht ersetzt; Installation, Commit und Push stehen bis zur vollständigen Abnahme aus.

## Änderungen

- Die Prozessidentifikation akzeptiert nur die bereits unterstützten Codex-Installationspfade. Die allgemeine Windows-Rückfallkennung wurde entfernt. Unbekannte und nicht lesbare Prozesse bleiben ausgeschlossen. Die frischen Identitäts-, Text-, Fokus- und Auswahlprüfungen vor Schreibzugriffen bleiben erhalten.
- Die Desktop-App erzeugt keine Browser-Bridge mehr und öffnet deren Named Pipe nicht. Die Chrome-Erweiterung wurde vom Nutzer ausgeschaltet; sein Screenshot zeigt den deaktivierten Schalter. Die eindeutig zugehörige Native-Messaging-Registrierung wurde nach Sicherung entfernt. Manifest und Hostdateien bleiben erhalten.
- Der isolierte UIA-Worker verzweigt bereits im Programmeinstieg vor WPF. Der reale Headless-Test prüft Ping, sauberes Ende und die Abwesenheit von `PresentationFramework`/`wpfgfx`.
- Nur der eigene App-Server erhält `thread_unload_delay_secs=0`. Abgeschlossene und abgebrochene Korrekturen nutzen die vorhandene Abmeldung im `finally`; der nächste vorbereitete Thread bleibt erhalten. Der tatsächliche Konfigurationswert und die geladenen Threads wurden per RPC gelesen. Die globale Codex-Konfiguration bleibt unberührt.
- Layoutcache und letzte Schreibidentität halten reine Daten statt vollständiger nativer Editorreferenzen. Die Editoridentität und Arrays werden kopiert. Frische Capture-/Refresh-/Replace-Prüfungen und der bisherige begrenzte Worker-Cache bleiben bestehen.

Die umfangreichere zweiphasige UIA-Geometrieänderung und ein versuchsweiser Weak-Reference-Cache wurden nicht übernommen. Für sie lag kein ausreichender isolierter Nutzenbeleg vor. Es gibt keine neue Abhängigkeit, keinen Codex-Fork, kein Speichertrimmen, keine erzwungene GC und keine regelmäßigen Prozessneustarts als Optimierung.

## Ursache und nachgewiesener Nutzen

Die anfangs lange laufende Installation 1.5.2 belegte in einer Momentaufnahme rund 329,8 MB privaten residenten RAM über Hauptprozess, Worker und eigenen Codex-Server. Ein zusätzlicher Konsolenhelfer fehlte noch in dieser ersten Aufstellung. Der UIA-Worker allein belegte 250,7 MB, bei etwa 14 MB verwaltetem Heap, 23,3 MB GC-Commit und 516 MB privatem Commit. Der überwiegende Anteil lag außerhalb des verwalteten Heaps. Das grenzt die Suche ein, beweist aber noch keinen konkreten nativen Allokationsstack.

Eine konkrete native Allokationsursache bleibt teilweise offen: Ein prozessnamengebundenes WPR-Heapprofil wurde von der automatischen Freigabeprüfung abgewiesen und nicht gestartet. Der anschließend auf einen künstlichen eigenen Worker begrenzte PID-Ansatz scheiterte an Windows-Profilingprivilegien. Dauerhafte systemweite Heaptracing-Einstellungen wurden nicht eingeschaltet.

Ein getrennter Vergleich isolierte den Workerstart: Beide Versionen wurden frisch mit einem künstlichen Ping initialisiert und nach 30 Sekunden Aufwärmen je 1.800 Sekunden mit 100-ms-Abtastung gemessen. Beide Berichte enthalten vollständig 18.000 Messpunkte und dieselbe eingebettete .NET-Version 8.0.33.

| Messgröße, dezimale MB | Alter Worker 1.5.2 | Worker mit neuem Startpfad |
| --- | ---: | ---: |
| Privater Commit, Median | 14,07 | 8,92 |
| Privater Commit, p95 | 14,17 | 8,99 |
| Privates Working Set, p95 | 9,99 | 6,23 |
| Privates Working Set, Maximum | 10,01 | 6,26 |

Die Commit-Reduktion beträgt im Median 5,15 MB. Die viel stärkere residente Medianänderung enthält natürliche Windows-Auslagerung und wird nicht als Allokationseinsparung ausgegeben. Dieser Test belegt allein den Startpfad des Leerlauf-Workers, keine prozentuale Gesamtersparnis bei aktiver Nutzung.

Der frühere zweistündige Zwischenkandidat mit breiter Anwendungsunterstützung erreichte 217,8 MB Mittelwert, 253,4 MB p95 und 272,2 MB Spitze bei +36,3 MB Änderung des Halbzeitmedians. Dieser Kandidat wurde beendet und nicht installiert. Seine Werte sind kein kontrolliertes Vergleichspaar zur jetzt eingeschränkten Fassung. Ein früher isolierter Test erreichte allein für die Codex-Laufzeit 54,8 MB; das beweist keine feste Untergrenze jeder Konfiguration.

## Messmethode und Dauertest

Das Ziel beträgt **weniger als 50.000.000 Bytes privates Working Set über alle eigenen Prozesse**. Hauptprozess, UIA-Worker und eigene Codex-Kinder werden summiert. Die Native-Host-Option berücksichtigt zusätzlich exakt nach Abbildpfad zugeordnete Chrome-Hosts samt Nachkommen, ohne doppelte Zählung; bei deaktiviertem Browser sind keine solchen Hosts zu erwarten.

Schema 2 trennt privaten residenten RAM, gesamtes Working Set und privaten Commit. Abgetastet wird alle 100 ms anhand monotoner Fristen. Mittelwert, Median, nearest-rank-p95, Maximum, Halbzeitwachstum sowie größte Abtastlücke und Verspätung werden ausgewiesen. Ein nicht lesbarer lebender Prozess, ein beendeter Hauptprozess oder eine Verspätung über 500 ms macht den Lauf unvollständig. Alte Berichte ohne privaten residenten RAM sind kein Beleg für das 50-MB-Ziel. Einzelheiten: [Messwerkzeuge](../scripts/memory-measurement.md).

Der Nutzer arbeitet parallel. Der zweistündige Test war deshalb als **natürliche Desktopnutzung** vorgesehen, kein kontrolliertes Paar mit identischer Reihenfolge von Hintergrundbetrieb, unverändertem Feld, langem Verlauf und 20.000-Zeichen-Entwurf. An die App-Prozesse wurden währenddessen keine Heap-/GC-Diagnosewerkzeuge angehängt. Eine vorübergehende `SYSTEM_REQUIRED`-Anforderung war aktiv; sie hat den tatsächlichen modernen Standby nicht verhindert. Unvollständige frühere Läufe bleiben getrennt und werden nicht zusammengesetzt.

Der Lauf begann um 18:50 Uhr. Windows protokollierte modernen Standby von etwa 19:33 bis 21:23 Uhr, zuletzt mit Aufweckgrund `Lid`. Der Collector lief anschließend weiter, um die ursprünglich geplante Stichprobenzahl nachzuholen. Dieser Ablauf kann keine lückenlose 100-ms-Abnahme belegen. Ausschließlich der eindeutig identifizierte, ungültige Messprozess wurde beendet; die App blieb unverändert in Betrieb. Ein technischer Abbruchnachweis wurde gespeichert, keine erfundenen oder zusammengesetzten RAM-Messreihen. Zur Ursachenklärung wurde nur der eigene Messprozess per Stackleser untersucht, kein App-Prozess. Die Standbyanforderung endet mit dem Messprozess.

Der Collector bricht jetzt bei einer verpassten Frist sofort ab und schreibt die bis dahin erfassten Messpunkte als unvollständigen Bericht. Der reale Regressionstest mit künstlich verspätetem Sleep bestätigt Exitcode 2, `sampling-deadline-missed` und genau einen erhaltenen Messpunkt. Ein neuer Dauertest wurde nach der Antwort „Heute nicht mehr“ nicht gestartet.

**Ergebnis der endgültigen Fassung:** Eine gültige zweistündige Messung steht aus. Das 50-MB-Ziel ist nicht als erreicht belegt. Eine einzelne schreibgeschützte PSAPI-Ablesung um 19:16 Uhr ergab:

| Komponente | Privates Working Set, dezimale MB |
| --- | ---: |
| Hauptprozess | 50,18 |
| UIA-Worker | 85,97 |
| Eigene Codex-Laufzeit | 27,41 |
| Konsolenhelfer | 0,15 |
| **Summe** | **163,70** |

Das ist weder ein Dauertestmittelwert noch ein kontrollierter Reduktionsnachweis. Der UIA-Worker und die WPF-Oberfläche begrenzen in dieser Ablesung den Verbrauch am stärksten. Eine genaue native Allokationsursache oder eine feste technische Speicheruntergrenze ist damit nicht bewiesen.

## Funktionsprüfungen

| Prüfung | Ergebnis |
| --- | --- |
| Vollständiger abschließender automatisierter Release-Lauf | **756 bestanden**, nach Entfernung von fünf überholten Fremdhost-Livetests und Ergänzung des echten Fremdprozess-Regressionstests; Opt-in-Methoden ohne Aktivierung sind keine Live-Nachweise |
| Abschließender Build nach Entfernung dieser Methoden | Ohne Warnungen und Fehler |
| Gezielte Host-, Layoutcache- und Prozessgrenzen-Prüfungen | 46 bestanden |
| Eigene Prozessschranke sowie Codex-/Nicht-Codex-Pfade | Codex erkannt; Windows-Editor, Chrome, Edge, Hermes, Antigravity und fremde ChatGPT-Pfade abgewiesen |
| Regression des entfernten Windows-Fallbacks | 35 Hosttests bestanden, einschließlich eines echten, separat gestarteten Nicht-Codex-Prozesses |
| Explizit aktivierte Luna-Ressourcentests | Zwei bestanden: sechs exakte künstliche Korrekturen, gelesener Wert 0, nur ein vorbereiteter Thread; tatsächlich gestarteter Turn abgebrochen, anschließend exakte Korrektur |
| Reale Codex-Korrektur per `AltGr+,` und exaktes Undo | Bestanden am unabhängig gestarteten Kandidaten; 19 künstliche Zeichen, 1.844 ms bis zur Korrektur, vollständige Bereinigung bestätigt |
| Browser-Pipe | Verbindungsversuch mit 300-ms-Timeout scheitert erwartungsgemäß; keine Daten gesendet |
| Chrome-Erweiterung | Vom Nutzer deaktiviert, Screenshot bestätigt |
| Abschließende RAM-Werkzeugtests | Bestanden, einschließlich des absichtlich verspäteten Collectors mit sofortigem unvollständigem Bericht |

Der erste Desktop-Prüfversuch brach vor dem Schreiben wegen eines nicht leeren fokussierten Feldes ab. Nach ausdrücklichem Freihalten des Feldes bestand derselbe abgesicherte Test. Es wurden keine fremden Entwürfe überschrieben oder Chatnachrichten gesendet. Die 20.000-Zeichen-IPC-Grenze ist automatisiert abgedeckt; ein neuer kontrollierter realer 20.000-Zeichen-Desktopvergleich ist damit nicht belegt. Die früheren Windows-/Browser-Korrekturprüfungen gehören zur historischen Entwicklung und sind keine aktuelle Unterstützung. Die bestehende Codex-Pfaderkennung ist keine kryptografische Prüfung der Programmidentität; die Umstellung erhält diese Erkennung und entfernt die zusätzlichen Hosts.

Der reale Native-Host-Churn-Test deckte außerdem einen Messwerkzeugfehler auf: Eine Abbildpfadabfrage konnte während des Prozessendes bereits mit Fehler 5 scheitern, bevor der Prozesshandle signalisierte. Nur in diesem Fehlerpfad wartet der Collector nun höchstens 50 ms auf das bestätigte Ende. Ein weiterhin lebender, nicht lesbarer Prozess bleibt fatal. Zwei vollständige anschließende Werkzeugprüfungen bestanden, einschließlich echter 100-ms-Abtastung, Prozessumfang mit gleichnamigem Fremdhost und kurzlebigen Hosts mit Exitcode 259.

## Sicherung und Installation

Die bestehende kanonische Installation 1.5.2 wurde eindeutig bestimmt und samt Einstellungen, Native Host, Manifest sowie älteren vorhandenen Unterversionen gesichert. Das vollständige ZIP enthält acht Dateien; alle Hashes wurden nach erneutem Entpacken geprüft. SHA-256 des Archivs:

`44726F771886B0EE75BC63BD7B5AAFD8B53A5ADDA28CBC7E379777EBDA1F4E3A`

Der unabhängig gestartete Codex-Kandidat 1.5.4 hat SHA-256:

`89D0A051BAC75F699FABBE9EF1270D6A30D2F709EFCFB108A6E2B4A8DFF337ED`

Die kanonische EXE ist weiterhin Version 1.5.2. Ihr Autostarteintrag zeigte genau auf diese alte breite Fassung. Damit sie die Beschränkung nach einem Windows-Neustart nicht wieder aufhebt, wurde nur dieser Eintrag gesichert, zurückgelesen und vorübergehend entfernt. SHA-256 des separaten Autostartnachweises:

`D27676B6086EC05F5D5B0CE94CC565DFBBE376735B2BFFCB834D07158620D07E`

Die laufende Codex-Fassung bleibt davon unberührt. Nach einem Windows-Neustart startet die Textkorrektur bis zur abschließenden Installation nicht automatisch. Die Einstellung `startWithWindows=true` und alle anderen Einstellungen bleiben gespeichert. Der Einstellungsdateihash ist weiterhin `D4FBCF530E6F038ACF0670FE4666885F979E27BD8C5D70D9D61890A673E60FED`.

Für die Fortsetzung fehlen der neue vollständige zweistündige Dauertest, seine Auswertung und die Prüfung der anschließend ersetzten kanonischen Installation. Vor dem Ersetzen werden aktueller Einstellungsstand und Sicherung erneut geprüft; ersetzt wird nur die Haupt-EXE. Die entfernte Browserregistrierung wird nicht wiederhergestellt. Der beibehaltene Autostartwunsch soll erst mit der geprüften Codex-Fassung wieder wirksam werden. Installation und Veröffentlichung erfolgen erst nach erfolgreichem Abschluss der relevanten Prüfungen. Der isolierte Branch heißt `codex/codex-only-20261113`; der übrige RAM-Arbeitsbaum bleibt getrennt erhalten.
