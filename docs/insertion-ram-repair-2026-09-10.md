# RC6: Einsetzen und Speicher

## Änderungen

- Originaltext-Wiederherstellungsfenster samt Aufrufern und Übersetzungen entfernt. Verifizierte eigene Änderungen werden weiterhin sicher zurückgenommen; neue Nutzereingaben werden nicht überschrieben.
- Reale RC5-Protokolle zeigen überwiegend `readback_mismatch`. Bisherige Bestätigung nach SendInput: sechs Leseversuche im Abstand von 25 ms. Nun maximal zwei Sekunden rein lesendes Warten auf ordinal exakten Zieltext, mit Editoridentitätsprüfung vor und nach jedem Lesen. Kein erneutes Senden der Korrektur und kein Unterdrücken echter Fehler.
- Eine noch ausstehende eigene Änderung bleibt bis zur Rücknahme zuordenbar, wenn die erste Rücklesung veraltet ist. Keine doppelte Rücknahme und keine pauschale Annahme, dass ein fehlgeschlagener Schreibaufruf erfolgreich war.
- UIA-Suche materialisiert nur aktivierte, sichtbare, fokussierbare Kandidaten unterstützter Steuerelementtypen; die vollständige Editorprüfung bleibt erhalten.
- Der eigene App Server deaktiviert Plugins pro Prozess und alle konfigurierten MCP-Server pro Korrekturthread. Globale Konfiguration und OAuth bleiben unverändert. Nur Servernamen, keine Konfigurationswerte, werden für die Overrides behalten. Parallele Anfragen warten auf die vollständig initialisierte Verbindung.

## Prüfungen

Release-Build ohne Warnungen/Fehler; 402 Offline-Tests bestanden (`artifacts/insertion-ram-repair/offline-rc6-final.trx`). Neue Tests decken verzögerte Bestätigung, Timeout, Fokusverlust, neue Nutzereingaben, spätes Rücklesen und MCP-Deaktivierung ab.

Je zehn native Erstversuch-Korrektur-/Rückgängig-Durchläufe eines Mehrfehlerabsatzes und einer Liste mit neun Fehlern bestanden, ohne Schreibwiederholung (`native-first-attempt-ten-rc6.trx`, `native-first-attempt-ten-bullets-rc6.trx`).

Sechs echte cachefreie Luna-Korrekturen mit anschließendem Vorwärmen bestanden. Bei jeder Messung null Werkzeug-Hilfsprozesse, nur ein Konsolenhost. Testserver nach sechs Runden: 59 MiB privater Speicher. Berichte unter `artifacts/ram-repair/resource-live.trx` und `resource-*.json`.

## Speichermessung und Grenzen

Alte laufende RC5: 24 eigene Prozesse, zusammen 1.090 MiB Working Set und 1.526 MiB privater Speicher; darunter 21 Node-Helfer. Die Anzahl sank während der Beobachtung von 27 auf 21: Nachweis unnötiger Werkzeuginitialisierung, kein Beweis eines dauerhaften Lecks. Die alte App allein belegte 269 MiB Working Set und 999 MiB privat (`artifacts/ram-repair/before.json`).

Ein frischer Prozess ist nicht mit einer lange laufenden Installation gleichzusetzen. Kein künstliches Working-Set-Trimmen oder erzwungener GC; keine globale Deaktivierung fremder Werkzeuge. Langzeitverhalten über mehrere Tage bleibt offen. Sicherheitsbedingte Abbrüche sind weiterhin möglich, insbesondere bei Fokuswechsel oder veränderter Eingabe. Historische Editdialoge, andere Hosts und sämtliche DPI-Varianten wurden nicht erneut abgenommen.

## Installierter Stand

- Version `1.3.1-rc.6+cafe9698fd93e1c8a69408496f22044f32c49ea4`.
- Paket und Installation SHA256: `009B7996360ADD6DD55833249F7E1155BBD669735400FA632C6ADED584C84076`.
- Installationspfad `C:\Users\lukas\.codex\.chatgpt-projects\g-p-6a60c6d22cb88191be272153223ad7f6\dist\CodexLanguageFix\CodexLanguageFix.exe`; gestarteter Prozess 38232.
- RC5-Backup: `artifacts/insertion-ram-repair/installed-backup-1.3.1-rc.5.exe`. Beim Austausch wurde ausschließlich der geprüfte alte App-Prozess einschließlich seiner eigenen Kinder beendet.
- Echte installierte Aa-/Luna-Korrektur der Liste mit neun Fehlern beim ersten Klick, anschließend vollständiges Rückgängigmachen bestanden: `artifacts/insertion-ram-repair/installed-luna-bullets-rc6.trx`. Nur eigener Testentwurf, anschließend entfernt; keine Nachricht abgesendet.
- Erste Installationsmessung: drei Prozesse (App, Server, Konsolenhost), null Node-Helfer; 388 MiB Working Set / 211 MiB privat insgesamt. Kein kontrollierter Langzeitvergleich mit der alten Installation.
- Nach Aa-/Undo-Test weiterhin drei Prozesse und null Node-Helfer; insgesamt 460 MiB Working Set / 271 MiB privat. App privat 240 MiB, Server privat 28 MiB (`artifacts/ram-repair/after.json`). Der Anstieg gegenüber der ersten Messung erlaubt noch keine Aussage zur Langzeitstabilität. Belegt ist die Eliminierung der unnötigen Werkzeugprozesse.
