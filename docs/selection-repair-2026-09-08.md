# Reparatur der wiederholten Schreibablehnung

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
(`offline-final.trx`). Die neuen Tests decken auch falsche Auswahlpositionen,
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
Die lokale Installation und der anschließende echte Klicklauf stehen noch aus.
Lokale Einzelberichte liegen unter `artifacts/selection-repair/`.
Die offenen Oberflächenbereiche aus dem [vorherigen Prüfbericht](format-repair-2026-09-08.md)
bleiben ausdrücklich ungeprüft; diese Reparatur ist keine vollständige stabile
Abnahme aller Appansichten.
