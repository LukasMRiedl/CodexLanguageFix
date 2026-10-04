# Aa-Felderdiagnose vom 4. Oktober 2026

## Umfang und Freigabestatus

Geprüfter Quellstand und lokale Installation: 1.3.1-rc.10.
Der Release-Build für win-x64 wurde erfolgreich erstellt. Die eindeutig über
laufenden Prozess und HKCU-Autostart identifizierte Installation wurde als EXE
unter `artifacts/aa-coverage/installed-backup-1.3.1-rc.8.exe` gesichert und ihr
SHA-256 mit der bisherigen Installation abgeglichen. Nach bestandenen Prüfungen
wurde die eindeutig über Autostart bestimmte EXE durch den Release-Build ersetzt.
Der installierte SHA-256 entspricht dem geprüften Kandidaten; der Autostartpfad
bleibt derselbe. Die Sicherung der vorherigen RC8 bleibt lokal erhalten.
Die zusätzlich installierte RC9 wurde vor der abschließenden RC10-Ergänzung ebenfalls
gesichert (`installed-backup-1.3.1-rc.9.exe`).
Die zusätzlichen Hermes-Formulare sind nach der nachträglichen Nutzerentscheidung
kein verpflichtender Abnahmeumfang. Hermes-Hauptchat und Nachrichtenbearbeitung
bleiben im Umfang. Bestehende Nachrichten wurden nicht geändert oder abgesendet;
die Prüfung verwendet ausschließlich eigene ungesendete Testentwürfe.

Quellcodefunde und automatisierte Geometrietests werden getrennt von tatsächlich
geprüften Oberflächen und echten Aa-Klicks ausgewiesen.

## Bestätigte Ursachen und Änderung

Ohne erkannte Werkzeugleiste war das Editorrechteck zugleich verfügbare Fläche und
Hindernis. Damit war keine Position möglich. Das vorhandene Tagesprotokoll enthält
24 Übergänge zu `overlay_no_safe_space`; diese historischen Einträge unterscheiden
die Hosts nicht und sind keine Anzahl der einzelnen Prüfintervalle.

Die bisherige Erkennung erfasst Codex-ProseMirror und Hermes-Composer anhand ihrer
CSS-Klassen, jedoch keine gewöhnlichen Prosa-Eingaben. Die Änderung ergänzt eine
enge Zuordnung über nachgewiesene IDs und exakte englische/deutsche Feldbeschriftungen.
Schreibgeschützte, unsichtbare, deaktivierte, Passwort- und technische Felder werden
ausgeschlossen. Ein fokussiertes ungeeignetes Feld verhindert die Umleitung auf
einen anderen Editor. Bei mehreren gleichwertigen sichtbaren Composern bleibt die
Zuordnung verborgen. Die geprüfte Runtime-Identität bleibt beim Schreiben und
Rückgängig maßgeblich.

Die vorhandene Werkzeugleistenposition hat Vorrang. Verifizierte Fenster-/Dokument-
und Dialoggrenzen ermöglichen zusätzliche Positionen rechts, links, unterhalb und
oberhalb des Felds. Der Knopf bleibt 28 DIP groß, der Abstand beträgt 4 DIP. Der
Monitorarbeitsbereich begrenzt jede Position zusätzlich. Nahe Textbereiche, andere
Felder, Bilder und Bedienelemente werden als Hindernisse berücksichtigt. Geometrie
wird beim periodischen Poll neu erfasst; ein kurzzeitiger Cache gilt höchstens 50 ms.

Die weitere Platzierungsprüfung zeigte fehlendes `PerMonitorV2` im eigenen
Anwendungsmanifest. Der Release-Kandidat deklariert nun diese DPI-Awareness;
Die Skalierung wird am eigenen, auf den Zielmonitor verschobenen HWND mit
`GetDpiForWindow` ermittelt. Live-Tests verwenden ebenfalls einen expliziten
PerMonitorV2-Thread, damit UIA-
und Win32-Rechtecke auf gemischt skalierten Monitoren physisch vergleichbar sind.
Der bevorzugte Werkzeugleistenanker bleibt auf seiner Zeile; freie Stellen werden
nach Entfernung zum Anker gesucht. Ohne Anker folgt zuerst die Fußzeile. Seitliche
Ersatzpositionen bleiben unmittelbar an der Feldspanne, statt entfernte freie
Fensterflächen zu verwenden. Text und Steuerelemente erhalten ebenfalls 4 DIP Abstand.

Die abschließende Nutzeranweisung ergänzt ausschließlich für Codex eine Position
oben rechts innerhalb des Eingaberahmens, falls die Werkzeugleiste keinen sicheren
Platz bietet. Dafür werden aktuelle sichtbare Textrechtecke über TextPattern erfasst;
bei fehlender oder ungültiger Textgeometrie bleibt diese Position gesperrt. Der
gesamte Eingaberahmen ist maßgeblich, damit auch ein kurzer einzeiliger Editor
berücksichtigt werden kann. Tatsächlicher Text und andere Bedienelemente bleiben
geschützt. Hermes behält die zuvor geprüfte Platzierungsfolge.

Diagnoseeinträge unterscheiden Host, Kategorie sowie Erkennungs- und Platzierungsgrund.
Sie enthalten keine Texte, Kontodaten oder Anbieterantworten. Anbieterwahl,
Strukturschutz, native Schreibtransaktionen und verifiziertes Rückgängig bleiben bestehen.
Es wurde keine Abhängigkeit hinzugefügt.

## Oberflächeninventar

| Feldfamilie | Nachweis | Ergebnis / verbleibende Prüfung |
|---|---|---|
| Codex bestehender Chat, schmale Eingabe, zusätzlicher Planeditor sichtbar | UIA und eigener Testentwurf | Erkennung, sichere Werkzeugleistenposition, frische Geometrie und echte Aa-Korrektur mit vollständigem Rücklesen und exaktem Rückgängig für Satz sowie Absätze/Liste bestanden |
| Codex neue Chats, Work, Plan-Eingabe, schwebende/zusätzliche Fenster | bisheriger ProseMirror-Pfad | Gesonderte Oberflächenabnahme offen; keine Behauptung einer Live-Prüfung |
| Codex Nachrichtenbearbeitung | noch keine eigene Oberflächenprüfung | Konkret ungeprüft |
| Codex persönliche Anweisungen und „Mehr über dich“ | installierte Webview-Assets, IDs `chatgpt-custom-instructions`, `chatgpt-personalization-more-about-you` | Klassifikator geprüft; die erreichbare Codex-Personalisierung verlinkt AGENTS.md statt eines Eingabefelds. Work-Felder nicht erreichbar und nicht live geprüft |
| Codex Projektanweisungen | installierter Projektsettings-Asset, Beschriftung „Custom instructions“ | Zuordnung per exaktem Kontext; Live-Abnahme offen |
| Codex Freitextfeedback | installierter Feedback-Asset, ID `artifact-feedback-details` | Klassifikator geprüft; Live-Abnahme offen |
| Codex Rückfragen | `request-panel-8b25f2820c9c.js`: textarea mit dynamischer ID, Beschriftung und Platzhalter; MCP-Elicitation-Callsite belegt | Keine pauschale Freigabe dynamischer Felder; allgemeine Agenten-Rückfragen konkret ungeprüft |
| Codex Automationsanweisungen | `automation-dialog-a13cf1632745.js`: Rich-Composer, Beschriftung „Prompt“, Platzhalter „Describe what ChatGPT should do“ | Rich-Composer-Pfad im Source belegt; UIA-Identität und Live-Abnahme offen |
| Hermes Hauptchat | UIA, eigener Testentwurf, 200 % Skalierung | Erkennung, freie Position, Geometrieerneuerung und echte Aa-Korrektur mit vollständigem Rücklesen und exaktem Rückgängig für Satz sowie Absätze/Liste bestanden |
| Hermes leerer Hauptchat | eigenes Fixture vollständig entfernt; schreibgeschützte UIA-Diagnose | CSS-Platzhalter als 22 Zeichen über beide Lesemethoden bestätigt; mit Guard Live-Prüfung bestanden: `ambiguous_placeholder`, kein Snapshot/Korrekturziel |
| Hermes Nachrichtenbearbeitung | tatsächlich geöffneter Editor und UIA-Metadaten | Zusätzliche Klasse `ui-prompt-input-editor__input` bestätigt und erkannt; TextPattern-Schreibbarkeit und vollständiges Lesen bestätigt. Bestehende Nachricht unverändert abgebrochen; Korrektur/Rückgängig hier nicht live ausgeführt |
| Hermes zusätzliche Composer und Gruppenchat | installierter Source | Keine gesonderte erreichbare Oberfläche geprüft |
| Hermes Botbeschreibung | eigenes ungespeichertes Dialogfixture, danach abgebrochen | Prosa-Zuordnung per benachbarter Beschriftung, ValuePattern, Dialoggrenzen und freie Position bestanden; nach Nutzerentscheidung keine verpflichtende weitere Abnahme |
| Hermes Rückfragen, Projektideen, Kriterien, Automations-/Webhook-Prompts, SOUL-/Profil-/Avatar-/Kanban-Felder | installierter Source und enge Beschriftungs-/ID-Zuordnung teilweise vorhanden | Nach Nutzerentscheidung kein verpflichtender Abnahmeumfang; nicht als live geprüft ausgewiesen |
| Suche, Namen, Titel, Pfade, URLs, Zugangsdaten, Befehle, Code, JSON, technische Konfiguration | negative Klassifikatorfälle | Bewusst ausgeschlossen |

## Prüfnachweise

- 578 automatische Tests ohne opt-in Live-Tests bestanden. Darunter neue Fälle für
  Seitenplatzierung ohne Werkzeugleiste, einseitige Belegung, begrenzte Flächen,
  fehlenden Platz, Skalierungen von 100–200 % und negative Koordinaten.
- Echte Aa-Roundtrips für eigenen Satz und Absatz-/Listenentwurf in beiden Hauptchats
  bestanden: Korrektur, vollständiges Rücklesen und exaktes Rückgängig.
- Externer Monitor: tatsächlich 3840 × 2160 bei 175 %, Arbeitsbereich mit negativem
  Ursprung (-528, -2160). Hermes mit schmalem und maximiertem Fenster geprüft.
  Physischer Aa-Knopf 49 × 49 Pixel; das entspricht 28 DIP bei 175 %.
- Realer Codex-Fensterwechsel vom externen Monitor auf den primären Monitor bei
  200 % und zurück auf 175 %: echte Aa-Korrektur und exaktes Rückgängig in beiden
  Positionen bestanden. Größe 56 × 56 beziehungsweise 49 × 49 physische Pixel;
  Fenstergrenzen und Abstand zu Text/Steuerelementen am tatsächlichen Knopf geprüft.
  Der Kandidat besteht den gleichen Ablauf erneut ohne Appneustart; die tatsächlich
  installierte EXE besteht ihn ebenfalls.
- Installierter Hermes-Hauptchat: Aa-Korrektur und exaktes Rückgängig des eigenen
  Satzes auf dem externen Monitor ebenfalls bestanden.
- Die neue Codex-Eckposition wurde am RC10-Kandidaten und erneut an der installierten
  RC10-EXE ausschließlich lesend geprüft: genaue obere rechte Position im Rahmen,
  28/4-DIP-Maße, Fenstergrenzen und Abstand zu sichtbarem Text/Bedienelementen bestanden.
  Da inzwischen ein Nutzerentwurf sichtbar war, wurde dort kein Korrekturklick
  ausgeführt. Die zuvor beschriebenen vollständigen Aa-Roundtrips gehören zu RC9;
  Korrektur-, Struktur- und Rückgängigverfahren wurden für RC10 nicht geändert.
- Eigener Hermes-Testentwurf entfernt; Leerfeld-Ausblendung der installierten
  Version erneut bestanden. Codex wird nach der Abnahme nicht mehr verändert,
  sobald in der Oberfläche ein anderer Entwurf sichtbar ist.
- Zwei frühere Wechselversuche nach den Fenstermenü-/Verschieben-Versuchen brachen
  an der Fokus-/Identitätsprüfung ab. Auch ein Rückkehrtest brach ab. Die Ursache
  dieses Zustands ist nicht belegt; nach Neustart bestanden die Wechsel und ihre
  Wiederholung. Die Schutzprüfung wurde nicht gelockert. Zusätzliche Fokusdiagnosen
  enthalten ausschließlich Hostgleichheit, Fokusgleichheit und UIA-Typnummer.
- Hermes Hauptchat: Capture etwa 136 ms, anschließender Poll etwa 90 ms.
  Codex Hauptchat: Capture etwa 242 ms, anschließender Poll etwa 140 ms.
  Ein separater kalter Codex-Capture dauerte etwa 1322 ms. Das 750-ms-Intervall
  garantiert daher ohne weitere Laufzeitprüfung keine vollständige Ende-zu-Ende-
  Reaktionszeit von höchstens 750 ms bei kalter UIA-Initialisierung.
- Originale opt-in TRX-Ergebnisse einschließlich früherer fehlgeschlagener Versuche
  liegen lokal unter `artifacts/aa-coverage/`
  (nicht zur Veröffentlichung vorgesehen). Die Leerfeld-Metadaten protokollieren
  ausschließlich Längen, Typen, Identitäten und Rechtecke.

Nicht nachgewiesen: sämtliche zusätzlichen Codex-Feldfamilien, sämtliche
Zoom-/Dialog-/Scrollkombinationen sowie vollständige mehrzeilige Live-Korrekturen
pro zusätzlicher Prosa-Familie. Dynamische Rückfragefelder bleiben bei unbekanntem
Kontext ausgeschlossen. Diese Oberflächen dürfen nicht als vollständig geprüft gelten.

Die deutsche/englische Hermes-Oberfläche verwendet quellgebundene statische Hinweise.
UIA liefert weder über ValuePattern, TextPattern noch über die Schreibbarkeitsattribute
der Textkinder einen verlässlichen Unterschied zwischen CSS-Pseudotext und Nutzertext.
Die exakte Hinweis-Liste wird daher als `ambiguous_placeholder` abgewiesen. Ein echter
Entwurf mit exakt denselben Worten bleibt ebenfalls verborgen. Andere Hermes-Sprachen
wurden nicht abgesichert oder live geprüft. Dies ist keine inhaltsbasierte Vermutung
über beliebige Entwürfe und kein Beleg einer zuverlässigen Leerheitserkennung.

## Bestehende Lösungen

Die vorhandene UIA-/Korrekturlösung sowie die bereits integrierten Anbieter erfüllen
die native Schreib- und Rückgängigprüfung. Die offiziellen LanguageTool-Angebote
liefern keinen hier nachgewiesenen vollständigen Ersatz für die verlangte Kombination
aus Codex-/Hermes-Feldzuordnung, freier Aa-Platzierung und verifiziertem Rückgängig.
Siehe [Windows-Angebot](https://languagetool.org/windows) und
[offizielle unterstützte Anwendungen](https://help.languagetool.org/hc/en-us/articles/39254502620183-Which-websites-and-applications-are-supported-by-LanguageTool).
