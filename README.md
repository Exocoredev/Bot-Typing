# Auto Input Tester V6

V6 behebt den Fehler, dass V5 nach dem einmalig erkannten sichtbaren Text "Fertig" meldete.

## Neu in V6

- **Live OCR waehrend des Tippens**
- neu rechts auftauchender / nachscrollender Text wird automatisch nachgeladen
- OCR laeuft im Hintergrund und soll den Browserfokus nicht wegnehmen
- bereits bekannte Zeichen werden ueber einen Text-Overlap abgeglichen
- nur der neu erschienene Teil wird an den internen Puffer angehaengt
- wenn der Puffer leer wird, wartet V6 auf weitere OCR-Daten statt sofort zu stoppen
- erst nach mehreren leeren Scans wird wirklich "Fertig" gemeldet
- Live-OCR Werte sind in den Einstellungen anpassbar
- Log enthaelt OCR-Durchlaeufe, nachgeladene Zeichen und Ausrichtungsfehler

## Bauen

1. ZIP entpacken (siehe auch `TEILEN.txt`).
2. Auf dem Ziel-PC muss das **.NET 8 SDK** installiert sein.
3. Optional einmal `reset_settings.bat` starten, damit keine alten V5-Einstellungen stoeren.
4. `build.bat` starten.
5. Danach liegt die EXE unter:

`publish\AutoInputTester.exe`

Zum Weitergeben am einfachsten: `packen-zum-teilen.bat` ausfuehren
oder nur `publish\AutoInputTester.exe` kopieren (kein Build auf dem anderen PC).

## Empfohlene Einstellungen fuer eine laufende Typewriter-artige Zeile

- Live OCR: **AN**
- OCR-Modus: **Nur schwarzer / noch nicht geschriebener Text**
- Restpuffer: **45 Zeichen**
- OCR-Abfrageintervall: **220 ms**
- Leere Scans bis Fertig: **10**
- Leerzeichen: **AN**
- Zeilenumbrueche: **AUS**
- Fokusverlust: **Fokus automatisch zurueckholen**

## Ablauf

1. `AutoInputTester.exe` starten. Sie liegt nur im Tray.
2. `Ctrl+Shift+S` druecken.
3. Den Bereich markieren, in dem die laufende Textzeile sichtbar ist. Der Bereich sollte breit genug sein, damit rechts immer neuer Text sichtbar wird.
4. OCR-Vorschau kontrollieren und uebernehmen.
5. Im Zielfenster die eigentliche Aufgabe starten / das Eingabefeld aktivieren.
6. `Ctrl+Shift+F9` **einmal** druecken und komplett loslassen.
7. V6 liest direkt vor dem Tippen den Bereich nochmals still ein und tippt los.
8. Wenn Text nach rechts nachgeladen wird, liest V6 ihn automatisch nach.
9. `Ctrl+Shift+F10` = Pause / Fortsetzen.
10. `Ctrl+Alt+Esc` = sofort stoppen.

## Erster Test

Zum Testen zuerst:

- Fehlerquote: `0 %`
- Schwankung: `0 %`
- Live OCR: `AN`

Wenn das Durchlaufen der kompletten Aufgabe funktioniert, Fehler und Schwankung wieder aktivieren.

## Wichtig

Im Live-Modus wird die OCR-Vorschau beim Start **nicht blind weiterverwendet**. V6 macht beim Start eine frische, unsichtbare OCR-Aufnahme des gespeicherten Bereichs. Dadurch wird kein alter Text getippt, wenn sich die Webseite zwischen Auswahl und Start veraendert hat.
