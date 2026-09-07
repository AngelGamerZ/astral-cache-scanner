# Team-Audit – Astral Scanner 1.5.0

Stand: 7. September 2026. Vier Rollen: Koordination/Implementierung, Update-Review, Overlay/Usability und unabhängige QA. Geprüft wurden Updateablauf, Oberfläche, Overlay, Fundspeicherung und wichtige Regressionen. Dies ist kein Nachweis vollständiger Fehlerfreiheit.

## Ergebnisse und Korrekturen

- **Updateaktion:** Der gemeldete Fehler betrifft nach Nutzerangabe Version 1.3. Der genaue ursprüngliche Auslöser lässt sich ohne Log dieses Rechners nicht sicher bestimmen. In der neuen Version erfolgt die Versionsentscheidung anhand strukturierter Daten statt Anzeigetext. „Update installieren“ prüft bei fehlendem Angebot selbst erneut. Beide Updateaktionen sind nur während laufender Arbeit gesperrt; Fehler geben sie wieder frei. Die Bestätigung bleibt erforderlich. Tests decken Angebote bei versteckten Einstellungen, Bannerablauf, X sowie aktuelle/ältere/ungültige Releases ab.
- **Overlaybedienung:** Shift + linke Maustaste verschiebt das Overlay. Während des Ziehens bleibt WoW im Vordergrund; sonst ist das Overlay durchklickbar. Die Position wird relativ zum verfügbaren Spielbereich gespeichert, bei Größenänderungen angepasst und auf den sichtbaren Bereich begrenzt. Rücksetzen ist in den Einstellungen möglich.
- **Parallelbetrieb:** Zwei reguläre Scannerinstanzen konnten ihre vollständigen Datenschnappschüsse gegenseitig überschreiben. Der Verlust wurde isoliert reproduziert. Eine benutzerbezogene Startsperre verhindert künftig mehrere reguläre Instanzen in derselben Windows-Sitzung. Tests und Update-Helfer sind ausgenommen.
- **Neustart nach abgeschlossenem Fund:** Bestätigtes Verschwinden wird beim automatischen Abschluss sofort gespeichert. Dadurch kann dieselbe Objektkennung nach dem Neustart wieder als neuer Fund erscheinen. Manuell abgeschlossene, noch sichtbare Kisten bleiben unterdrückt. Die neuen Tests prüfen beide Fälle. Bereits vorhandene alte Datensätze ohne diese Bestätigung werden nicht rückwirkend geraten.
- **Testdaten:** UI-Testprotokolle und Overlayeinstellungen werden bei isolierten Tests getrennt von persönlichen Daten abgelegt.

## Validierung

Build sowie UI-, Desktop-/Updatezustands-, Speicher-, Token-, Datenbank-, Update-Helfer- und Overlaytests bestanden. Der Update-Helfertest umfasst echten Prozesswechsel, EXE-Austausch, Neustart und Zurücksetzen bei gesperrter Datei. Die UI-Tests prüfen 100 % und 175 % Skalierung. Unabhängiges abschließendes Review fand keine Veröffentlichungshindernisse.

## Offene Grenzen

- Ein echtes Shift-Ziehen mit Maus im laufenden Spiel wurde nicht automatisiert ausgeführt. Windows-Eingabestile, Fokusvorgaben, Positionsberechnung und Speicherung sind geprüft; der manuelle Praxistest bleibt nötig.
- Das Overlay bleibt in dieser Version gleich groß. Freies Platzieren reduziert Überdeckung, eine kompakte Darstellung ist noch nicht enthalten.
- Ein bereits defekter Updater in Version 1.3 kann nicht durch eine noch nicht installierte Korrektur repariert werden. Falls dessen Update scheitert: Scanner schließen, aktuelles ZIP manuell herunterladen und dessen Programmdateien ersetzen. Persönliche Daten bleiben separat gespeichert.
- Keine Änderung an der zuletzt beibehaltenen Token-/Loot-Erkennungsregel; der zuvor abgebrochene Umbau wurde nicht wieder aufgenommen.
