# Nativer Umbau: Implementierung und offene Abnahme

Stand: 14. November 2026. Version **2.0.0 ist lokal installiert**. Nach bestandenen
Funktionsprüfungen hat der Nutzer ausdrücklich die sofortige Installation und
eine Beobachtung des Verbrauchs im Alltag anstelle der noch offenen langen
Vergleichsläufe gewählt. Ein dauerhaftes Erreichen des RAM-Ziels ist damit
weiterhin nicht nachgewiesen.

## Referenz und Trennung

Die Referenz ist der geprüfte Codex-only-Stand 1.5.4. Quellstand, Binärdatei,
Einstellungen und Nachweise wurden getrennt vom umfangreicheren früheren
RAM-Entwurf gesichert. Das schreibgeschützte Referenzarchiv enthält 185 Dateien;
alle extrahierten Dateien wurden gegen ihre gespeicherten SHA-256-Werte geprüft.
Archiv-SHA-256:
`66D795DEC73A88766AFAF26F05572ACDDEC51B0690E1B95EDE2B2BDF0F9E1DB8`.

Die native Umsetzung liegt auf `codex/native-ram-20261114`. Die C#-Quellen und
Tests bleiben als Vergleichsreferenz erhalten und werden nicht mit dem nativen
Produkt ausgeliefert. Browseranbindung und allgemeine Windows-Unterstützung
bleiben deaktiviert.

## Implementierte Architektur

- Eine C++20-EXE stellt Win32-Oberfläche, Tray, Einstellungen und Koordinator
  bereit. Derselbe Programmstand startet den isolierten UIA-Worker über
  `--composer-worker <PID>`. Die produktive EXE hängt ausschließlich von
  Windows-DLLs ab; sie lädt keine eigene .NET-Laufzeit.
- Der Worker betreibt UIA auf einem MTA-Thread. Ereignisse setzen getrennte
  Änderungsmarkierungen für Fokus, Text und Layout. COM-Referenzen werden über
  RAII freigegeben. Die bis zu 16 historischen Editor-Tokens enthalten
  Identitätsdaten, keine historischen Editorreferenzen oder Textkopien.
- Der 750-ms-Kontrollpfad bleibt erhalten. Lokale Geometrieprüfungen verwenden
  Daten statt dauerhaft gehaltener Baumobjekte. Dokumentweite Fallbacks zählen
  als vollständige Layoutsuche und werden nicht als lokaler Cachetreffer verbucht.
- Vor Schreiben und Undo werden Zielidentität, Prozess, Fenster, Fokus,
  Ausgangstext und Auswahl frisch geprüft. Rücklesen bestätigt die Änderung.
  Unsichere Teilschreibvorgänge werden nicht automatisch wiederholt.
- Schutzbereiche, Textstruktur, Edit-Pläne, Rückwärtspläne, Vorbereitung,
  Abbruch und Cache sind portiert. UTF-16-Offsets und die Grenze von 20.000
  Codeeinheiten bleiben erhalten. Graphemregeln entsprechen der .NET-8-Referenz;
  Unicode-Eigenschaften stammen aus Windows ICU. Texte werden nicht normalisiert.
- JSON-RPC verwendet weiterhin den offiziellen Codex App Server und dessen
  vorhandene Anmeldung. Modellwahl, low/priority-Profil, Zeitlimits, Abbruch,
  Abmeldung und ein vorbereiteter Folgethread bleiben erhalten. Der exakte
  Entwicklerprompt einschließlich seiner CRLF-Zeilenumbrüche wurde mit der
  kompilierten Referenz abgeglichen.
- Das interne Worker-Protokoll ist Version 2: längenpräfixiertes UTF-8-JSON,
  vier Byte Little Endian, höchstens 256 KiB. Ein produktiver Mischbetrieb mit
  Version 1 ist nicht vorgesehen. Die Einstellungen behalten Schema 2.

## Bereits geprüfte Funktionen

| Prüfung | Ergebnis und Aussagegrenze |
| --- | --- |
| Beibehaltene C#-Tests | 756 Tests bestanden; Referenzprüfung |
| Differenzieller Kern- und Einstellungsvergleich | 3.945 identische Ausgaben für C# und C++; darunter Unicode, geschützte Bereiche, Struktur, Edit-/Rückwärtspläne, Cache-Schlüssel, lange Eingaben und 21 gezielte Einstellungsfälle |
| Native Core-Tests | Bestanden, einschließlich UTF-16-/Graphem- und Schreibtransaktionsfällen |
| Native Koordinatortests | 15 Szenarien bestanden; darunter Abbruch, Fokus-/Textwechsel, sofortiges Ausblenden bei blockierter Erfassung, Vorbereitung, Teilschreiben und Herunterfahren |
| Nachgebildeter App Server | Bestanden: Protokoll, Cache, Wiederholung, Abbruch bei blockierter Eingabe, frühe/späte Ereignisse, gleichzeitige Aufrufe und Handle-Isolation |
| Worker-Protokoll | 11 Prüfungen bestanden; unter anderem Version 1, ungültige Frames, veraltete Tokens und überlange Schreibanforderungen sicher abgewiesen |
| Explizit aktivierter Live-Backendtest | Sechs künstliche Korrekturfälle, Cache, bestätigter Abbruch und Korrektur nach Abbruch bestanden; kein UI-Schreibnachweis |
| Reale native Codex-Korrektur und exaktes Undo | Bestanden mit künstlichem Text: Shortcut korrigiert exakt 15 zu 16 UTF-16-Einheiten; der Nutzer betätigt das native Undo, ein unabhängiger TextPattern-Readback bestätigt den ursprünglichen Text mit exakt 15 Einheiten |

Der Live-Backendtest verwendete `gpt-6-luna/low/priority/full-v1`. Nach der
Bereinigung wurden lokal null aktive Turns, Turn-Zustände, offene RPC-Anfragen,
ausstehende Turnstarts und Wartungsaufträge sowie ein vorbereiteter Thread
beobachtet. Die Zahl tatsächlich geladener Threads im Server wurde dabei nicht
abgefragt und wird daraus nicht abgeleitet.

Der reale Schreibtest verwendete den nativen Build mit SHA-256
`7C252D4076E0AB4A382402EBF0A8A1A288C974E7FC74C7D61A3E372039F2653F`.
Die Rückleseprobe akzeptiert ausschließlich exakt bekannte künstliche Testtexte;
sie protokolliert nur Variante, Länge, Fokus, gültige Identität und Geometrie.
Nach dem Test war der Composer sichtbar leer. Die Referenz wurde mit unveränderten
Einstellungen wieder gestartet. Anschließend wurde die Overlaydarstellung
überarbeitet und im echten Codex-Fenster erneut geprüft: kompakte gerundete
Statusfläche, zentriertes Aa, getrennte Undo-Aktion und DPI-skalierte Schrift.
Auch die Korrektur und die ergänzte Runtime-ID-Prüfung bestanden dabei.
Der unveränderte so geprüfte Build ist installiert; SHA-256:
`F1989E32E6A56D9B600D31DEF60E77CBEE26827F70FCE1630C1854DBEF5C7D3B`.

## RAM-Nachweise und Grenzen

Der vollständige zweistündige Referenzlauf umfasst 72.000 Messpunkte bei 100 ms:
185.990.045 Bytes privates Working Set im Mittel, 246.079.488 Bytes p95 und
253.915.136 Bytes Spitze über die eigenen Prozesse. Der private Commit zeigt
zusätzlich rund 106 MB Halbzeitwachstum. Es handelt sich um natürliche parallele
Nutzung und nicht um einen kontrollierten Vergleich aktiver Szenarien.

Drei vollständige gepaarte 1.800-Sekunden-Läufe prüfen ausschließlich den
Worker-Ping-Leerlauf: je Build und Paar 18.000 Messpunkte nach 120 Sekunden
Aufwärmen. Die mediane Paaränderung beträgt −74,09 % beim privaten Working Set
und −72,62 % beim privaten Commit. Diese Läufe verwenden einen früheren gepinnten
nativen Prototyp, nicht die installierte Fassung. Sie erfassen keine Editorabfragen,
keine Hauptoberfläche und keinen Codex App Server und ersetzen weder die aktive
30-%-Abnahme noch das Gesamtziel unter 50.000.000 Bytes.

Nach dem künstlichen Korrekturtest zeigte der spätere Installationsbuild in einer
separaten fünfsekündigen Kurzablesung 30,6 MB privates Working Set über vier eigene
Prozesse, bei ausgeschalteter Vorbereitung. Nach der Installation mit den
unveränderten Einstellungen einschließlich eingeschalteter Vorbereitung zeigte
eine weitere fünfsekündige Ausgangsablesung im Hintergrund 26,75 MB im Mittel,
26,78 MB Spitze und 36,30 MB mittleren privaten Commit. Beide Kurzablesungen sind
vollständig mit 100 ms abgetastet. Sie sind weder Langzeitnachweise noch ein
kontrolliertes aktives Vergleichspaar zur Referenz.

Das vereinbarte Planungsziel für aktive Komponentenvergleiche sind mindestens
30 % weniger Median und p95 für Hauptprozess plus Worker gegenüber der
Codex-only-Referenz 1.5.4, jeweils bei identischen synthetischen Abläufen in
mindestens drei gepaarten 1.800-Sekunden-Läufen. Commit und Spitzen dürfen keine
verdeckte Verschlechterung zeigen. Dieses Ziel ist noch nicht nachgewiesen.

Speichertrimmung, erzwungene GC und regelmäßige Prozessneustarts sind keine
Optimierungsnachweise. Diagnosezähler werden vor der abschließenden
Gesamtabnahme abgekoppelt.

## Installation und zurückgestellte Qualifikation

Die kanonische EXE wurde nach frischem Backup atomar ersetzt und unabhängig über
den interaktiven Explorer gestartet. Hauptprozess und isolierter Worker wurden
am Installationspfad bestätigt. Der gespeicherte Autostartwunsch zeigt wieder auf
die kanonische native EXE. Der Einstellungsdateihash ist unverändert. Browser und
Native-Host-Registrierung bleiben deaktiviert; ältere deaktivierte Browserdateien
wurden erhalten. Das neue Paket enthält nur die native EXE und Lizenzhinweise.

Das frische Installationsbackup enthält acht Archivdateien einschließlich
Einstellungen und Integrationszustand. Alle Einträge wurden aus dem ZIP erneut
gehasht und mit dem Manifest verglichen. SHA-256 des Archivs:
`66541778C045A2DF90724445B978908E1CA4702D706A420E61D7F5406FFA2C0B`.

Der Nutzer entschied anschließend zur laufenden Arbeit ausdrücklich:
„Implementiere die Software jetzt und wir schauen einfach, wie der RAM verbrauch
heute Abend ist.“ Die Installation erfolgt daher mit folgenden benannten Grenzen:

- Die vollständige Matrix aus Einstellungen, Sprachwahl, Screenreader-Interaktion,
  DPI-/Monitorwechseln und längeren realen Unicode-Entwürfen ist nicht live abgenommen.
- Die drei gepaarten aktiven Komponentenläufe und die lokale Latenzabnahme
  gegenüber der Referenz stehen aus. Ein reiner Kern- oder Leerlauftest ersetzt sie nicht.
- Die zweite Diagnoserunde setzt vollständige native Funktionsparität voraus.
  Weitere Optimierungskandidaten werden erst anhand ihrer Messungen bewertet.
- Der zweistündige native Gesamtlauf mit lückenloser 100-ms-Abtastung fehlt noch.

Die tatsächliche Abendablesung bleibt eine spätere Beobachtung der natürlichen
Nutzung. Sie wird nicht vorweggenommen oder als kontrollierte Abnahme ausgegeben.
