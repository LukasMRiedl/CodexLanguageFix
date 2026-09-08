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
  Nach Installation erneut bestanden (`offline-rc4-after-install.trx`).
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

## Lokale Installation bestätigt

RC4 wurde installiert und neu gestartet. Paket und laufende Installation
stammen aus Commit `613fc7314897f557a104ff34b0bbe6b214e78ccd`.

- Installationsdatei:
  `C:\Users\lukas\.codex\.chatgpt-projects\g-p-6a60c6d22cb88191be272153223ad7f6\dist\CodexLanguageFix\CodexLanguageFix.exe`.
- Paket: `artifacts/list-repair/release-1.3.1-rc.4/CodexLanguageFix.exe`.
- Beide Dateien haben SHA-256
  `08DD354642EAFBE3D0734E7679DA62427DE9CAE464870DC0ED171B8A3BB1A3DF`.
- RC3 ist unter `artifacts/list-repair/installed-backup-1.3.1-rc.3.exe`
  gesichert, SHA-256
  `F6E94A8512FD5B3A0EE1EE755D8A2DA4A9D20B33425598D49C8FBD7F8F1D0D28`.
- Echter Mausklick auf `Aa` der installierten RC4, Luna-Korrektur aller fünf
  Teststichpunkte und exaktes Rückgängig: bestanden, rund acht Sekunden für
  den gesamten Test (`installed-rc4-five-bullets-luna.trx`). Das Produktionslog
  bestätigt erfolgreiches Schreiben und Rückgängigmachen.

Die lokale Installation ist aktualisiert; ein GitHub-Release wurde nicht
veröffentlicht. Es wurden keine fremden Entwürfe geändert und keine
Chatnachrichten abgesendet.

Die weiterhin offenen Oberflächenprüfungen aus dem
[Formatbericht](format-repair-2026-09-08.md) bleiben davon unberührt.
