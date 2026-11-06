# Chrome-Erweiterung

Manifest-V3-Oberfläche für die lokale Codex-Textkorrektur. Die Erweiterung verwendet den Native-Messaging-Host `com.codexlanguagefix.chrome`; sie enthält weder Anmeldetokens noch eine eigene Modellanbindung. Die feste Erweiterungs-ID lautet `pacghgdaoldcojhkbglolhbggdfjbgfb`.

## Installation nach bestandener Abnahme

Den Ordner über `chrome://extensions` → Entwicklermodus → „Entpackte Erweiterung laden“ auswählen. Zuvor muss der zugehörige geprüfte Native Host für das Benutzerkonto registriert sein. Interne Chrome-Seiten, Inkognito und lokale Dateien werden nicht unterstützt. Browserberechtigungen können die Ausführung auf einzelnen Seiten einschränken.

## Verhalten

Nur das fokussierte, geeignete Feld wird verarbeitet. Die Desktop-App liefert Pausezustand, Vorbereitungseinstellung und Tastenkürzel. Passwörter, erkennbare Anmeldedaten, Zahlungsdaten und technische Felder sind ausgeschlossen. Die automatische Vorbereitung sendet bei aktivierter Einstellung nach 1,2 Sekunden stabiler Eingabe; Einsetzen erfolgt erst per Aa oder Shortcut. Browserreservierte Tastenkürzel können vom Browser abgefangen werden.

Textbereiche werden vor jedem Einsetzen erneut geprüft. Bei Contenteditable werden minimale Änderungen innerhalb vorhandener Textknoten verwendet, damit umgebende Formatierung erhalten bleibt. Ein Änderungsteil, der über mehrere Textknoten oder strukturelle Grenzen hinweg reicht, wird vor dem Schreiben abgelehnt. Beliebige komplexe Webeditoren sind daher nicht pauschal unterstützt. Wenn ein Editor den eingesetzten Text eigenständig verändert und das Rücklesen fehlschlägt, bleiben Original und beobachteter Zustand für ein ausdrücklich angefordertes Rückgängig erhalten. Das Original kann zusätzlich per Klick direkt am Aa-Bereich eingeblendet werden; es wird nicht protokolliert oder automatisch in die Zwischenablage geschrieben. Veränderte Texte oder verlorener Fokus verwerfen Antworten; Rückgängig gilt ausschließlich für den noch unveränderten korrigierten Text.

## Prüfungen

- `node --test browser-extension/tests/core.test.cjs`: reine Klassifikator-, Differenz- und Platzierungsprüfungen.
- `node browser-extension/tests/browser.cjs`: isolierter echter Chrome mit künstlichen Feldern und nachgebildeter Desktop-Verbindung. Prüft Textarea, formatierten mehrzeiligen Contenteditable, Rückgängig, Passwortausschluss, dynamisches Shadow DOM, entfernte Felder, Eingabekomposition, CSS-Zoom, Frames, Überdeckung und Originaltext-Recovery. Fokusverlust wird ausdrücklich über ein synthetisches Blur-Ereignis geprüft; ein echter sichtbarer Tabwechsel ist damit nicht abgenommen. Dies ist kein echter Codex- oder Gmail-/WhatsApp-Nachweis.
- `node browser-extension/tests/native-browser.cjs`: echte entpackte Erweiterung mit testexklusivem Native Host `com.codexlanguagefix.chrome.test` und kurzlebiger Fake-Provider-Pipe. Erfordert separat gestartete .NET-Fixture und temporäre Testhostregistrierung. Verändert keine produktive Installation.

Die Browsertests verwenden Playwright als reine Testlaufzeit. `PLAYWRIGHT_MODULE` kann auf ein bereits installiertes Playwright-Modul zeigen; `CHROME_PATH` auf eine vorhandene Chrome-Binärdatei. Die Tests starten isolierte Headless-Browser und senden keine Nachrichten oder realen Benutzerdaten.


## WhatsApp-Platzierung und Wiederverbindung

Für `web.whatsapp.com` wird der verifizierte Lexical-Composer `data-testid="conversation-compose-box-input"` innerhalb des `footer` erkannt. Wenn das nur etwa 22 Pixel hohe Textfeld keinen äußeren Platz für Aa bietet, wird eine freie Stelle im Composer gesucht. Tatsächliche Textrechtecke und Bedienelemente bleiben mit vier Pixeln Abstand geschützt; bei vollständig belegter Zeile bleibt Aa verborgen. Es werden keine Seitenabstände oder WhatsApp-Styles verändert.

Bei unterbrochener Desktop-Verbindung bleibt Aa bedienbar. Ein ausdrücklicher Korrekturversuch oder erneuter Feldfokus verbindet einmal neu und wartet auf die aktuelle Desktop-Einstellung. Bleibt die Antwort fünf Sekunden aus, endet der Versuch mit einer Statusmeldung; es gibt keine Wiederverbindungsschleife. Nachrichten ohne aktive Verbindung erzeugen keinen hängenden Korrekturzustand.

`node browser-extension/tests/whatsapp-reconnect.cjs` prüft die schreibgeschützt ermittelte WhatsApp-Geometrie als isoliertes HTML-Fixture mit nachgebildeter Desktop-Verbindung: freie Innenposition, Text-/Button-Abstände, Korrektur und Undo, belegte Zeile, Verbindungsabbruch, fehlgeschlagenes Senden, einmalige Wiederverbindung und ausbleibender Handshake. Das ist kein Nachweis einer Korrektur im angemeldeten WhatsApp-Konto.

Der erkannte WhatsApp-Lexical-Composer erhält pro Korrektur beziehungsweise Rückgängig einen zusammenhängenden `insertText`-Befehl. Der vollständige Änderungsbereich muss in einem vorhandenen Textknoten liegen; andernfalls wird vor dem Schreiben abgebrochen. Andere Contenteditable-Felder behalten ihre bisherigen minimalen Einzeländerungen. Dadurch bleiben vorhandene Formatierungsgrenzen geschützt.

`node browser-extension/tests/whatsapp-managed.cjs` bildet einen verwalteten Editor nach, der nach dem ersten Eingabebefehl seinen Textknoten ersetzt und weitere Befehle derselben Eingabetransaktion ablehnt. Vor der Reparatur erzeugte der Test aus `Das ist ein Tset. Und ncoh ein Fheler.` exakt den gemeldeten Teilzustand `Das ist ein Tset. Und ncoh ein Fhehler.` statt `Das ist ein Test. Und noch ein Fehler.` Nach der Reparatur prüft er alle drei Korrekturen mit einem Klick, das exakte Original nach einem Rückgängig-Klick und die Ablehnung eines Änderungsbereichs über mehrere Formatierungsknoten ohne Schreibbefehl. Dies ist eine gezielte Reproduktion des beobachteten Fehlermusters mit einem nachgebildeten verwalteten Lebenszyklus, kein Test der echten Lexical-Bibliothek oder des angemeldeten WhatsApp-Kontos.

### Dokumenttypen

Die Oberfläche wird mit HTML-Elementen im expliziten HTML-Namensraum aufgebaut. Dadurch funktioniert der Start auch auf XHTML-Seiten, ohne vom HTML-Fragmentparser abhängig zu sein. Eigenständige SVG-/XML-Dokumente ohne HTML-Dokumentwurzel erhalten keine Textkorrektur-Oberfläche und öffnen keine Native-Messaging-Verbindung.

`node browser-extension/tests/document-startup.cjs` prüft tatsächliche HTML-/XHTML-Dokumente mit Korrektur und Rückgängig sowie SVG-/XML-Dokumente ohne Startfehler oder Verbindung. Die Dokumenttypen werden durch echte HTTP-Content-Types des isolierten Browsertests erzeugt.
