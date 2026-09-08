# Mehrfehlerkorrekturen – RC5

## Nachgewiesene Ablehnungsursachen

Die gegenüber 1.3.0 hinzugefügte Strukturprüfung wies auch harmlose Änderungen an Rand-Leerraum und LF/CRLF zurück. Nun werden ursprüngliche Leerzeilen, Einrückungen, Randabstände und Zeilentrenner anhand gleicher Inhaltszeilenzahl und identischer Strukturmarker lokal wiederhergestellt. Fehlende Inhaltszeilen, veränderte Stichpunkte und technische Inhalte bleiben Fehler. Alle Luna-Protokolle verwenden diese Prüfung vor dem Cache; die Cache-Version wurde erhöht.

Der Schreibplan klassifizierte technische Spannen im Zieltext erneut. Ein angehängter Punkt hinter URL oder CLI-Option wurde dadurch Teil eines vermeintlich veränderten geschützten Bereichs. Nun werden ausschließlich ursprüngliche technische Spannen exakt und eindeutig in Originalreihenfolge zugeordnet. Einfügungen ohne freien Textanker navigieren vom geprüften benachbarten Zeichen zur nochmals geprüften Einfügeposition. Es wird kein geschützter Nachbar neu eingegeben. Rückgängig invertiert den erfolgreich angewandten Plan ausschließlich für denselben Editor und den exakten Gegenweg.

## Verifikation

- Release-Build: keine Warnungen oder Fehler; 387 Offline-Tests bestanden (`artifacts/list-repair/offline-rc5-final.trx`).
- Vier echte Luna-Anfragen mit mehreren eindeutigen Fehlern: Randabstände, mehrere Stichpunkte, URL/CLI und CRLF. Alle Korrektur-, Struktur-, Schutz- und Profilprüfungen bestanden (`artifacts/format-repair/luna-multiple-errors-20260908-233031/luna-multiple-errors.trx`).
- Echte native Korrektur mit exaktem Rückweg im ChatGPT-Aufgabencomposer: Satzpunkt hinter URL, fünf Fehler in einem Absatz und neun Fehler in drei identischen Stichpunkten. Berichte `native-protected-url-rc5.trx`, `native-multiple-errors-rc5.trx`, `native-multiple-errors-bullets-rc5.trx` unter `artifacts/list-repair`.
- Nur eigene synthetische Entwürfe; keine Nachrichten abgesendet und keine Zwischenablage verwendet.

Das Produktionsprofil bleibt `baseline / none / full-v1`, OAuth-only. Keine neue Promptoptimierung. Nicht alle historischen generischen Fehlermeldungen lassen sich ohne die absichtlich nicht protokollierten Ausgangstexte diesen Ursachen eindeutig zuordnen. Historische Editdialoge und sämtliche Monitor-/Positionierungsvarianten wurden in dieser Reparatur nicht erneut geprüft.

## Lokale Auslieferung

Die Installations- und abschließenden Overlay-Prüfdaten werden nach dem Paketabgleich ergänzt. GitHub-Veröffentlichung ist nicht Bestandteil dieses lokalen RC5-Nachweises.
