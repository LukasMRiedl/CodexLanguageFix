# Unabhängiger Desktop-Start vom 10. Oktober 2026

## Befund

Die installierte Version 1.4.1 lief zu Beginn nicht. Nach dem letzten Diagnoseeintrag vom 9. Oktober um 22:44:40 Uhr gab es keinen regulären Exit-, Sitzungsende- oder Fehler-Eintrag. Windows war seit dem 7. Oktober durchgehend gestartet; Codex wurde am 9. Oktober um 23:34 Uhr neu gestartet. Der HKCU-Autostart war aktiv, wird jedoch erst bei einer neuen Windows-Anmeldung ausgeführt.

Der bisherige manuelle Start über `Start-Process` aus dem Agententerminal erzeugte einen Kindprozess des Terminals mit Windows-Job-Mitgliedschaft. Der abgefragte Terminaljob hatte die Limits `0x2800`, darunter `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. Windows beendet damit zugehörige Prozesse beim Schließen des letzten Job-Handles. Das erklärt den fehlenden normalen App-Exit bei diesem nachgewiesenen Startfehler. Der exakte historische Zeitpunkt der Prozessbeendigung wurde nicht aufgezeichnet.

Eine globale Prüfung auf irgendeine Job-Mitgliedschaft ist hier ungeeignet: Auch Explorer gehört auf diesem Rechner zu einem Windows-Job. Ein Austritt nur aus dem innersten Job reicht bei verschachtelten Jobs ebenfalls nicht aus. Entscheidend ist die Unabhängigkeit vom konkreten Starterjob.

## Reparatur

`scripts/start-independent.ps1` verwendet das Desktop-Shell-Objekt von Windows Explorer zum Start und prüft anschließend Prozesspfad sowie Explorer als Parent. Das vorhandene Windows-Verfahren benötigt keine zusätzliche Abhängigkeit. Der Anzeigeparameter aktiviert kein fremdes Fenster und erzwingt kein verstecktes erstes Appfenster. Der bereits richtige Windows-Autostart bleibt bestehen.

## Nachweis

`scripts/test-independent-start.ps1` erzeugt einen eigenen Job mit `KILL_ON_JOB_CLOSE`, startet darin einen normalen Kontrollprozess und über das reparierte Startskript die echte installierte App. Mit dem konkreten Job-Handle wird geprüft: Der Kontrollprozess gehört zum Testjob, die App nicht. Nach dem Schließen dieses Jobs sind Teststarter und Kontrollprozess beendet; die App läuft weiter, reagiert und hat den erwarteten EXE-Pfad. Dieser Test bestand. Der App-Prozess hatte Explorer als Parent und protokollierte anschließend `application_started` sowie `luna_warmup_completed`.

Der Release-Build `1.4.1+cd73e2f29b0a7bbc28cb829a8965ef9b8e7d889a` wurde nach Sicherung der bisherigen Installation installiert. Build und installierte EXE haben denselben SHA-256-Wert `306ED8EF442A5F840D7CFB0890F1908B52AE53928DDCE90B7DDEF93018677BB3`. Der vollständige Job-Ende-Test bestand auch mit dieser Installation. Die Startschutzprüfungen für eine bereits laufende App und einen Nicht-EXE-Pfad bestanden ebenfalls.

Es wurden keine Nutzerentwürfe verändert oder Nachrichten abgesendet. Der tatsächliche Codex-Prozess und die Windows-Sitzung mussten für den Test nicht beendet werden. Ein neuer Aa-Klicktest war nicht Teil dieser Prüfung des Startverfahrens; die Korrekturimplementierung wurde nicht verändert.

## Windows-Verträge

- [Job Objects und KILL_ON_JOB_CLOSE](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)
- [Start durch den Explorer-Prozess](https://learn.microsoft.com/en-us/windows/win32/shell/samples-execinexplorer)
- [IShellWindows.FindWindowSW](https://learn.microsoft.com/en-us/windows/win32/api/exdisp/nf-exdisp-ishellwindows-findwindowsw)
- [Shell.ShellExecute](https://learn.microsoft.com/en-us/windows/win32/shell/shell-shellexecute)
