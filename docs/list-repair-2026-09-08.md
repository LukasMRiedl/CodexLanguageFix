# Reparatur wiederholter Fundstellen in Listen

## Belegte Ursache

RC3 scheiterte bei mehreren Stichpunkten mit
`selection_preflight_failed_not_attempted`, obwohl Luna erfolgreich antwortete.
Der Fehler ließ sich mit drei identischen echten Listeneinträgen reproduzieren:
`Das ist ein fehler.`. Der erste Eintrag funktionierte; der zweite scheiterte.

Die schreibgeschützte Diagnose zeigte bei der übergreifenden UIA-Suche nach `f`
die Position 37 statt 36 und einen falschen Treffertext. Beim dritten Eintrag
trat dieselbe Verschiebung auf. Die Suche im jeweiligen nativen Textknoten
lieferte dagegen exakt die Positionen 14, 36 und 58. Ein weiterer Test zeigte,
dass auch das Suchbereichsende begrenzt werden muss, damit Satzzeichen am
Zeilenende nicht mit einem erzeugten Absatztrenner zurückgegeben werden.

## Reparatur in RC4

Die Suche beginnt und endet jetzt in den exakt zum Ziel passenden Textknoten.
Anfang und Ende dürfen in unterschiedlichen Inline-Textknoten liegen. Jeder
Anker wird gegen den vollständigen Dokumentpräfix und seinen exakten Inhalt
geprüft. Es werden keine vermuteten Umbruchlängen von Offsets abgezogen.

Treffertext, Position, Dokument, Fokus, Editoridentität und die tatsächliche
Auswahl werden weiterhin vollständig geprüft. Schreib- und Rückgängiglogik,
Strukturschutz und Luna-Produktionsprofil bleiben unverändert.

## Prüfungen

- 329 Offline-Tests bestanden, Build ohne Warnungen oder Fehler.
- 16 echte native Hin-/Rückwege mit drei Stichpunkten bestanden: erste,
  zweite, dritte und alle Fundstellen; wiederholte Buchstaben innerhalb einer
  Zeile; Einfügen am Zeilenende; längere Ersetzungen.
- Sechs echte native Hin-/Rückwege mit fünf Stichpunkten bestanden: jeder
  einzelne Eintrag und alle gemeinsam.
- Die Tests vergleichen den vollständigen Text einschließlich Listenzeichen
  und Umbrüchen nach Korrektur und Rückgängig. Keine Chatnachricht wurde gesendet.

Berichte: `artifacts/list-repair/offline-list-repair.trx`,
`three-bullets-bounded-search.trx`, `five-bullets-six-roundtrips.trx`.
Fehlgeschlagene Zwischenberichte bleiben als Reproduktionsnachweis erhalten.

Lokale Installation und echter Luna-Klicktest werden nach Abschluss ergänzt.
Die weiterhin offenen Oberflächenprüfungen aus dem
[Formatbericht](format-repair-2026-09-08.md) bleiben davon unberührt.
