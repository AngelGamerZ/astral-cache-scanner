# Astral Scanner 1.2.0 – neue Oberfläche

Der Scanner wurde mit einem Team aus UI-Planung, Usability-Feedback, Test und unabhängiger QA überarbeitet.

## Was sich geändert hat

- Dunkle Oberfläche mit klarer Seitennavigation, kompakter Statuskarte und sichtbarer Ersteinrichtung.
- **Übersicht:** Aktuelle Treffer, offene Kisten und gesammelte Fundorte auf einen Blick. Standardmäßig werden nur Kisten angezeigt.
- **Meine Kisten:** Tabelle mit Zustand, Sichtungsregion, Karte und letzter Sichtung. Ausgewählte Kisten zeigen ihre vollständigen Weltkoordinaten. „Als gelootet markieren“ und „Als offen markieren“ sind nur bei passender Auswahl aktiv.
- **Fundorte:** Historische Sammlung mit Regions-/Karten-/Serversuche, Import und JSON-/Lua-Export. Die Auswahl bleibt bei Aktualisierungen erhalten.
- **Diagnose:** Debug-Log, Text-Export und Diagnoseaufnahme an einem Ort. „Live folgen“ kann zum Lesen abgeschaltet werden; im Hintergrund wird weiter protokolliert.
- **Einstellungen:** Spielordner, Overlayposition, Ton und GitHub-Updates. Technische Dateiquellen und die Anzeige aller Spielobjekte stehen unter erweiterter Diagnose.
- Bildschirmbegrenzung und scrollbare Einstellungen; Abstände und Schriftgrößen sind für Windows-Skalierung angepasst.

## Bestehende Funktionen

Funde und historische Datenbank bleiben erhalten. Der Scan startet weiterhin nur auf Knopfdruck nach bestätigter Ordnerwahl. Näheprüfung: Eine Kiste, die innerhalb von 10 Yards eine Sekunde lang in vollständigen Scans fehlt, gilt als gelootet. Der Fundort bleibt historisch gespeichert. Das Overlay arbeitet weiterhin ohne Eingabeautomation.

Die Regionsangabe beschreibt den Spielerstandort bei der Sichtung. Weltkoordinaten und Karten-ID bleiben das räumliche Bezugssystem; die Sichtungsregion ist an Regionsgrenzen keine exakte Grenzbestimmung der Kiste.

## Test und Abnahme

Bestanden: 20 Layoutszenarien (fünf Ansichten, zwei Fenstergrößen, 100 % und 175 % Skalierung), zwei Prüfungen mit gefüllten Fundlisten, Auswahl-/Aktionszustände, Regionssuche und Debug-Pause. Zusätzlich bestehen Pfadeinrichtung, Speicher-/Datenbank-/Loot-/Overlay-Regressionstests. Die QA-Prüfung führte zu Korrekturen bei Speicherwarnungen, Auswahlstabilität, Pfadbreite und lesbaren deaktivierten Buttons.

Diese Abnahme prüft die Oberfläche und bestehende Programmlogik. Sie ersetzt keinen erneuten echten Loot-Praxistest auf jedem Rechner.

Download: https://angelgamerz.github.io/astral-cache-scanner/

Releases: https://github.com/AngelGamerZ/astral-cache-scanner/releases
