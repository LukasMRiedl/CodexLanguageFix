# Reparatur der wiederholten Schreibablehnung

Historischer Prüfstand RC3. Die spätere [Listenreparatur in RC4](list-repair-2026-09-08.md)
ergänzt diesen Fix und ersetzt die nachstehend dokumentierte lokale Installation.

## Ursache und Änderung

Die installierte RC2 meldete nach erfolgreichen Luna-Antworten wiederholt
`write_not_verified_selection_end_mismatch`. Der Fehler wurde im normalen Chat
mit eigenen, nicht abgesendeten Testeingaben reproduziert.

Chromium liefert bei derselben Textauswahl unterschiedliche UIA-Endpunktanker.
Ein reiner Endpunktvergleich lehnte deshalb passende Auswahlen ab. Ein weiterer
echter Test mit abschließendem Zeilenumbruch zeigte, dass die direkte Auswahl
eines UIA-Bereichsendes außerdem den Umbruch mit erfassen kann.

RC3 setzt deshalb nur einen zuvor verifizierten Startcaret. Die eigentliche
Auswahl entsteht über Auswahltasten und wird vor dem Schreiben vollständig
geprüft: unverändertes Dokument, exakter Präfix, Auswahltext, Endpräfix und Suffix.
Die Graphemzahl plant ausschließlich die Tastenbewegung; sie ersetzt niemals
die Prüfung des tatsächlichen Ergebnisses. Einfügungen und Löschungen erhalten
innerhalb desselben ungeschützten Textblocks ein vollständiges Nachbar-Graphem,
damit auch Rückgängig ohne mehrdeutige leere Auswahl auskommt.

Editoridentität, Fokus, unveränderter Ausgangstext und vollständiges Rücklesen
bleiben verpflichtend. Es gibt keine Umbruchnormalisierung, Zwischenablage,
Volltextwiederholung oder Aufhebung der Strukturprüfung. Ohne sicheren lokalen
Anker, etwa unmittelbar hinter einem geschützten Codefragment ohne freien
Nachbartext, wird der Änderungsplan weiterhin abgelehnt.

Das Luna-Produktionsprofil und die Anbieterwahl wurden nicht geändert.

## Prüfung und Auslieferung

Build: keine Warnungen oder Fehler. 306 Offline-Tests bestanden
(`offline-final.trx`, nach Installation erneut `offline-after-install.trx`). Die neuen Tests decken auch falsche Auswahlpositionen,
identische Textstellen, CRLF, Unicode-Grapheme, Modifier und partielle
Auswahltasten-Batches ab.

Echte native Tests im normalen Chat, jeweils mit exaktem Rückgängig:

- Elf Schreibfälle mit echtem abschließendem Umbruch bestanden
  (`anchored-keyboard-real-newline.trx`).
- Neun Schreibfälle mit zwei echten abschließenden Umbrüchen bestanden
  (`anchored-keyboard-two-newlines.trx`).
- Absatz mit echter formatierter Liste, Emoji und geschützter Beispieladresse
  bestanden (`anchored-keyboard-rich-list.trx`).

Die fehlgeschlagenen Zwischenberichte bleiben zur Ursachenklärung erhalten;
sie sind nicht die Ergebnisse des abschließenden reparierten Standes.
Die lokale Installation wurde auf `1.3.1-rc.3` aktualisiert und neu gestartet.
Der Paketstand stammt aus Commit `be129f9c438aec2d658aced6106ea05a937ee776`.

- Installierte Datei:
  `C:\Users\lukas\.codex\.chatgpt-projects\g-p-6a60c6d22cb88191be272153223ad7f6\dist\CodexLanguageFix\CodexLanguageFix.exe`.
- Paket: `artifacts/selection-repair/release-1.3.1-rc.3/CodexLanguageFix.exe`.
- Paket und installierte Datei sind identisch: SHA-256
  `F6E94A8512FD5B3A0EE1EE755D8A2DA4A9D20B33425598D49C8FBD7F8F1D0D28`.
- Die vorherige RC2 ist unter
  `artifacts/selection-repair/installed-backup-1.3.1-rc.2.exe` gesichert;
  SHA-256 `BA745291950D8013E886A9AD61CA495B71A67F07E84DAD9901E05B5C84224A49`.
- Echter Mausklick auf `Aa` der installierten RC3, Luna-Korrektur der eigenen
  Absatz-/Listenfixture und exaktes Rückgängig: bestanden, rund fünf Sekunden
  für den gesamten Test (`installed-rc3-luna-rich-list.trx`). Das Produktionslog
  bestätigt `composer_write_completed` und `composer_undo_completed`.

Der Test sendete keine Chatnachricht ab und veränderte keine fremden Entwürfe.
GitHub wurde in dieser Reparatur nicht veröffentlicht; die lokale Installation
ist aktualisiert, nicht jedoch ein öffentlicher Release.
Lokale Einzelberichte liegen unter `artifacts/selection-repair/`.
Die offenen Oberflächenbereiche aus dem [vorherigen Prüfbericht](format-repair-2026-09-08.md)
bleiben ausdrücklich ungeprüft; diese Reparatur ist keine vollständige stabile
Abnahme aller Appansichten.
