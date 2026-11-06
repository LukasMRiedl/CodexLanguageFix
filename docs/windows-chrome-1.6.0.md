# Windows- und Chrome-Erweiterung: Abnahmestand

Dieser Bericht hält den früheren Entwicklungs- und Abnahmestand fest. Die spätere lokale Reparatur, ihre bestätigten Feldprüfungen und verbleibenden Abdeckungsgrenzen stehen im [Prüfbericht vom 6. November 2026](windows-coverage-2026-11-06.md). Die nachfolgenden Sperren und Kandidatenpfade beschreiben den jeweiligen historischen Stand.

## Verbindliche Entscheidungen

- Codex bleibt warm; Tempo hat Vorrang. Unter 50 MB ist kein Abnahmekriterium.
- Am 4. November 2026 hat der Nutzer den RAM-Vorher-Nachher-Vergleich ausdrücklich gestrichen („Mach keinen Vergleich.“). Es werden keine solchen Vergleichsläufe durchgeführt; eine Speicherreduzierung wird nicht als nachgewiesen ausgegeben.
- Automatische Vorbereitung nach 1,2 Sekunden gilt auch für geeignete neue Felder. Einsetzen nur durch Aa oder Shortcut.
- Erste Live-Abnahme: Codex, Hermes, Windows-Editor, Chrome-Formulare, Gmail und WhatsApp Web.
- Keine Windows-Benachrichtigungen. Vorhandene Einstellungen und Anmeldung erhalten.

## Freigabegatter

| Prüfung | Status |
|---|---|
| Vollständige Release-Tests nach Integration | 740 bestanden; final-no-flicker.trx; Chrome-Änderungen anschließend separat geprüft |
| Browser-DOM- und Native-Messaging-Integration | 10 Kernprüfungen, DOM-/WhatsApp-/Dokumenttyp-Matrix und MV3/NativeHost/Test-Pipe/Fakeprovider-Rundlauf bestanden; echte WhatsApp-Korrektur durch Nutzer bestätigt |
| Echter Aa-/Shortcut-/Undo-Ablauf je Pflichtanwendung | offen |
| Zoom, DPI, mehrere Monitore, Überdeckung | offen |
| RAM-Vorher-Nachher-Vergleiche | auf ausdrücklichen Nutzerwunsch entfallen; Speicherreduzierung nicht nachgewiesen |
| Vergleich warmer Korrekturlatenz | offen |
| Gesicherte Installation mit Hash- und Prozessprüfung | gesperrt bis Abnahme |
| Commit und Push | gesperrt bis Abnahme |

Die bisher gestartete Messung `artifacts/coverage-1.6.0/observational-baseline.json` beobachtet die bestehende Benutzersitzung. Sie ist kein kontrollierter Vorher-Nachher-Nachweis. Die ursprünglich vorgesehenen Speichergatter sind inzwischen auf ausdrücklichen Nutzerwunsch entfallen. Die als `baseline-1.5.2.trx` benannte Testdatei wurde bereits während der Änderungen gebaut und ist ebenfalls kein unveränderter Baseline-Nachweis.

Erfolgreiche Offline-Tests sind kein Ersatz für tatsächlich ausgeführte Live-Tests. Nicht erreichbare Anwendungen, Browserseiten oder Monitorvarianten bleiben ausdrücklich offen. Es wird kein Testnutzertext abgesendet und keine Sicherheitseinstellung umgangen.

## Diagnose der vorhandenen Installation, 4. November 2026

Die laufende Version 1.5.2 wurde 30 Sekunden mit Microsoft dotnet-counters 8.0.547301 beobachtet. Der Working Set lag bei 546,94–548,57 MB (dezimal), der verwaltete Heap bei 0,32–2,63 MB und GC-Commit bei konstant 16,92 MB. In diesem Fenster gab es 12 zusätzliche UIA- und Layout-Erfassungen. Das grenzt den großen Speicheranteil auf außerhalb des verwalteten Heaps ein; es beweist weder eine konkrete native Leckquelle noch die Wirksamkeit des Kandidaten. Der Diagnoseleser war an die beobachtete Sitzung angeschlossen; diese Messung darf nicht als unbeeinflusste RAM-Abnahme verwendet werden.

Rohdaten: `artifacts/coverage-1.6.0/installed-runtime-counters.json`. Es wurden ausschließlich technische Zähler erfasst; kein Speicherabbild und keine Textinhalte.

## Zusätzliche Prüfungen und offene Live-Befunde

- Zusätzliches schreibgeschütztes Review: Credential-/Code-Ausschlüsse, Shadow-DOM-Labels und Originalerhalt bei fehlgeschlagenem Readback korrigiert und nachgeprüft; keine verbleibenden konkreten Reviewblocker im damaligen Stand.
- NativeHost-Prozesstest mit richtiger/falscher Origin, echtem stdio-Framing und isolierter Test-Pipe bestanden. Die tatsächliche Chrome-Erweiterung bestand den Rundlauf in Chromium for Testing 151; produktives Chrome lud eine Erweiterung per Test-Startparameter nicht. Das ist kein Nachweis eines produktiven Installationsfehlers.
- Temporäre Testregistrierung `com.codexlanguagefix.chrome.test` und ihr Manifest entfernt und nachgelesen; Produktionsregistrierung unverändert.
- Antigravity auf ausdrücklichen Nutzerwunsch aus der Pflichtabnahme genommen. Hermes-Aufruf über Computer Use endete durch abgelaufene App-Freigabe; keine Anmeldung automatisiert.
- Separater Testkandidat wurde nach Explorer-Start anstelle des laufenden alten Prozesses ausgeführt; Installationsdatei, Autostart und gespeicherte Einstellungen wurden nicht ersetzt. Vor Abschluss der Sitzung muss der bisherige Installationsprozess wiederhergestellt werden, solange die Freigabe offen ist.
- Notepad: allgemeines Feld erkannt, Positionierungsstatus `placed`, automatische echte Codex-Vorbereitung des künstlichen 17-Zeichen-Texts bestanden. Einsetzen, Aa-Klick und Undo sind noch offen. Das automatisierte gespeicherte Kürzel erzeugte noch keinen Korrekturaufruf; hohe Ereignis-/Layoutfrequenz wird untersucht. Kein vollständiger Notepad-Rundlauf behauptet.
- Beobachtende 30-Minuten-Messung enthielt 1.800 Proben, ist wegen `sampling-deadline-missed` unvollständig und kein akzeptierter Vorher-Nachher-Vergleich.

## Fortsetzung: Native-Host-Vorbereitung

Der geprüfte Produktions-NativeHost wurde separat unter `artifacts/coverage-1.6.0/native-host` gebaut und für die eigene feste Erweiterungs-ID benutzerbezogen registriert. Registrierung und Ablehnung einer fremden Origin wurden nachgelesen/geprüft. Die Desktop-Installation und ihr Autostart bleiben unverändert; der No-Flicker-Testkandidat läuft weiterhin separat. Noch keine Freigabe als dauerhaft installierter Release.

Die Browsersteuerung beendete sich automatisch, weil sie die aktuelle Chrome-Adresse nicht sicher bestimmen konnte. Dadurch konnte sie die entpackte Erweiterung nicht über die Chrome-Oberfläche laden und die anschließenden produktiven Browserprüfungen nicht ausführen. Die Sperre wurde nicht umgangen. Der konkrete erforderliche Nutzerschritt ist das Laden von `browser-extension` über die bereits geöffnete Chrome-Erweiterungsseite. RAM-Dauervergleich und übrige Live-Gatter bleiben offen.

## WhatsApp-Erkennung und Wiederverbindung

Am tatsächlich geöffneten WhatsApp-Web-Composer wurde ausschließlich lesend festgestellt: Erweiterung aktiv, Aa wegen fehlenden Außenplatzes verborgen. Das aktuelle Nachrichtenfeld ist rund 22 Pixel hoch und besitzt die Merkmale `data-testid=conversation-compose-box-input`, `data-lexical-editor=true`, `contenteditable=true`, `role=textbox` innerhalb eines etwa 64 Pixel hohen Footers. Die Emoji-/Senden-Schaltflächen belegen die möglichen Außenpositionen.

Der neue WhatsApp-Adapter nutzt ausschließlich diese nachgewiesene Feldfamilie auf `web.whatsapp.com`. Er sucht eine freie Innenposition anhand sichtbarer Textrechtecke und Bedienelemente mit 4 Pixeln Abstand. Es wird kein CSS-Padding der fremden App verändert; vollständig belegte Zeilen bleiben geschützt. Außerdem werden getrennte Verbindungen nicht mehr als erfolgreiche Sendung behandelt: expliziter Klick oder erneuter Fokus darf genau einen zeitlich begrenzten Handshake auslösen. Das eigene Aa-Element verwendet nun ein offenes, weiterhin stilisoliertes Shadow Root für semantische Bedienung und Tests.

Prüfung: 10 Node-Tests, vollständige isolierte Browser-DOM-Matrix und neuer isolierter WhatsApp-/Wiederverbindungstest bestanden. Der neue Test bildet die tatsächlich gemessene Feld-/Footer-Geometrie nach und prüft freien 28-Pixel-Platz, Schutz von Text/Schaltflächen, Korrektur, Undo, fehlgeschlagenes Senden, Wiederverbindung ohne Schleife und Handshake-Zeitlimit. Dies ersetzt nicht die noch nötige Prüfung im angemeldeten WhatsApp-Tab nach dem Neuladen der aktualisierten Erweiterung. Der vorhandene Nutzerentwurf wurde bei der Diagnose nicht geändert oder abgesendet.

Die native Chrome-Steuerung beendete sich erneut wegen nicht sicher bestimmbarer Browseradresse. Weitere Browserprüfungen müssen die funktionierende DOM-Steuerung verwenden; der Native-Chrome-Zugriff wird nicht erneut versucht. Die neue Skriptfassung ist auf Platte; Chrome muss die entpackte Erweiterung und anschließend den WhatsApp-Tab neu laden.

## Startfehler bei SVG-/XML-Dokumenten

Der gemeldete Fehler `Cannot set properties of undefined (setting cssText)` wurde in echten SVG-/XML-Dokumenten reproduziert: Ein über `document.createElement` erzeugtes gewöhnliches XML-Element besitzt dort kein HTML-`style`-Objekt. Die Erweiterung startet nun ausschließlich bei einer HTML-Dokumentwurzel. Ihre Oberfläche verwendet explizit den HTML-Namensraum und DOM-Konstruktion statt HTML-Stringparsing. HTML und XHTML bleiben unterstützt; eigenständige SVG-/XML-Dokumente werden vor Overlay-, Ereignis- und Verbindungsaufbau übersprungen.

Neue Prüfung `browser-extension/tests/document-startup.cjs`: Korrektur und exaktes Rückgängig auf HTML und XHTML; kein Overlay, keine Verbindung und kein Fehler auf SVG/XML. Zusätzlich 10 Kernprüfungen, bestehende Browsermatrix sowie WhatsApp-/Wiederverbindungstest nach der Änderung erneut bestanden. Die tatsächliche Dokumentart des Iris-Unterdokuments wurde aus dem Screenshot allein nicht als live geprüft ausgegeben. Chrome muss die geänderte entpackte Erweiterung einmal neu laden.

## Prüfung nach erneutem Laden

Die aktuelle Erweiterung zeigte auf der lokalen künstlichen Testseite einen zugänglichen Aa-Knopf. Ein eigener kurzlebiger Testtab wurde in ein eigenes Chrome-Testfenster verschoben und danach geschlossen; bestehende Benutzertabs und der WhatsApp-Entwurf blieben unverändert.

Der Verbindungsversuch meldete `Native host has exited`. Zur Unterscheidung wurde der laufende Helper mit einer textfreien Statusanfrage geprüft: Exitcode 0, kein stderr, keine Antwort, kein Absturz. Gleichzeitig war Word als Vordergrundprozess festgestellt worden. Die vorhandene serverseitige Chrome-Fokusprüfung beendet solche Hintergrundanfragen absichtlich. Diese Prüfung wurde nicht entfernt oder abgeschwächt. Der Versuch ist deshalb kein bestandener aktiver Chrome-/Codex-Korrekturtest und kein nachgewiesener Helper-Absturz.

Eine physische WhatsApp-Bestätigung im aktiven Chrome-Nachrichtenfeld ist angefragt. Die reduzierten DOM-Auswertungen des Browser-Steuerungswerkzeugs unterstützen nicht alle normalen Browser-DOM-Eigenschaften (beispielsweise `document.hasFocus()`); daraus werden keine Aussagen über den tatsächlichen Erweiterungsquellstand oder einen Produktfehler abgeleitet. Die weitere Browsersteuerung verwendet ausschließlich den funktionierenden DOM-Zugriff, keine wiederholten nativen Chrome-Zugriffe.

## Bestätigte WhatsApp-Korrektur

Nach dem erneuten Laden der Erweiterung und des Tabs bestätigte der Nutzer ausdrücklich: „Aa erscheint und die Korrektur funktioniert“. Damit ist der echte Aa-Korrekturpfad im angemeldeten WhatsApp Web bestätigt. Exaktes Undo im angemeldeten Konto sowie die übrigen Pflichtanwendungen werden dadurch nicht automatisch als geprüft gewertet; Undo im nachgebildeten WhatsApp-Aufbau ist separat automatisiert bestanden.

Die App läuft weiterhin aus `artifacts/coverage-1.6.0/candidate-no-flicker`; die EXE der bisherigen Installation und ihr Autostart wurden nicht ersetzt. Die Versionsmetadaten des Testkandidaten lauten weiterhin 1.5.2.0. Der Native Host ist benutzerbezogen registriert und die entpackte Chrome-Erweiterung wurde durch den Nutzer geladen. Eine vollständig installierte und abgenommene Version 1.6.0 wird daraus nicht abgeleitet.

Vor dem ausdrücklichen Verzicht auf RAM-Vergleiche wurde ein Testfenster angefragt, weil eine Umschaltung zwischen alter und neuer App nötig gewesen wäre. Es wurde kein solcher Vergleichslauf durchgeführt oder als erfolgreich ausgegeben.

## Verzicht auf RAM-Vergleiche

Der Nutzer beantwortete die Frage nach dem mehrstündigen RAM-Testfenster mit „Mach keinen Vergleich.“. Die RAM-Vergleichsläufe entfallen damit als Freigabegatter; es wird weder zwischen alter und neuer App für diese Messungen umgeschaltet noch ein Speichergewinn behauptet. Die übrigen Funktions- und Installationsprüfungen bleiben bestehen.
