# Astral Scanner

Astral Scanner ist eine portable Windows-App für Project Astral / WoW 3.3.5a. Sie erkennt geladene Objekte namens **Astral Cache**, merkt sich ihre Fundorte und zeigt mit einem Overlay Richtung und Entfernung zur nächsten offenen Kiste an. Der Scanner arbeitet lesend; er bewegt den Charakter nicht und führt keine Klicks oder Loot-Aktionen aus.

**Aktuelle Version: 1.5.3** · Windows · .NET Framework 4.5 oder neuer · ZIP ohne Installation

[Windows-Version 1.5.3 herunterladen](https://github.com/AngelGamerZ/astral-cache-scanner/releases/download/v1.5.3/AstralScanner-v1.5.3.zip) · [Neuestes Release & Änderungen](https://github.com/AngelGamerZ/astral-cache-scanner/releases/latest) · [Online-Anleitung](https://angelgamerz.github.io/astral-cache-scanner/)

## Schnellstart

1. ZIP herunterladen und vollständig in einen eigenen Ordner entpacken.
2. **AstralScanner.exe** öffnen. Beim ersten Start den Spielordner auswählen, der die **Wow.exe** enthält. Ohne bestätigten, passenden Spielordner lässt sich der Scan nicht starten.
3. Project Astral aus diesem Ordner starten und mit einem Charakter einloggen. Es sollte genau ein passender WoW-Prozess laufen.
4. Im Scanner **Scan starten** drücken. Der Scan startet immer manuell.
5. In die Nähe einer Astral Cache gehen. Sobald der Client sie geladen hat, erscheint sie in der Übersicht und wird gespeichert.
6. Für den Richtungspfeil unter **Einstellungen** das **Ingame-Overlay** einschalten und zurück ins Spiel wechseln.

Der Scanner kann minimiert weiterlaufen. **Stoppen** beendet den Scan und blendet das Overlay aus; gespeicherte Funde bleiben erhalten. Beim vollständigen Schließen endet auch das Overlay.

## Voraussetzungen und Erkennung

Version 1.5.3 unterstützt die geprüfte Wow.exe des Client-Updates vom 12. September 2026. Die beiden zuvor geprüften Clientdateien bleiben unterstützt.

- Windows mit .NET Framework 4.5 oder neuer und dem unterstützten Project-Astral-Client auf Basis von WoW 3.3.5a, Build 12340.
- Der Scanner akzeptiert bekannte Clientdateien. Für das Profil vom 12. September erkennt er außerdem Änderungen ausschließlich am PE-Buildzeitstempel, an der PE-Prüfsumme oder am Inhalt des schreibgeschützten Ressourcenbereichs automatisch, solange Dateigröße, Layout und alle übrigen Dateibytes unverändert bleiben.
- Änderungen an Programmcode, Daten, Speicherlayout oder anderen Dateibereichen verlangen weiterhin ein geprüftes Scanner-Profil. Andere 3.3.5a-Clients sind nicht automatisch kompatibel. Die Lesefunktionen und gelesenen Objekte werden zusätzlich im laufenden Spiel geprüft. Die Diagnose nennt, ob die Datei bekannt ist oder über das kompatible Profil akzeptiert wurde.
- Das Overlay benötigt den Fenstermodus oder maximierten Fenstermodus. Exklusives Vollbild wird nicht unterstützt.
- Erkannte Kisten müssen vom Spielclient geladen sein. Der Scanner durchsucht nicht die gesamte Welt nach noch unbekannten Spawns.

Die App liest Clientdaten ohne Speicheränderungen, Injection oder Eingabeautomation. Daraus folgt keine Freigabe durch den Serverbetreiber; maßgeblich sind dessen Regeln.

## Die fünf Bereiche

| Bereich | Das findest du dort |
| --- | --- |
| **Übersicht** | Aktuell sichtbare Kisten, Anzahl offener Kisten und gesammelter Fundorte. |
| **Meine Kisten** | Persönliche Funde mit Zustand, Sichtungsregion, Karte und letzter Sichtung. Eine Auswahl zeigt die Weltkoordinaten und ermöglicht manuelles Markieren. |
| **Fundorte** | Historische Fundort-Datenbank mit Suche nach Region, Karte oder Server sowie Import und Export. |
| **Diagnose** | Debug-Log, Log-Export, Protokollordner und Diagnoseaufnahme. |
| **Einstellungen** | Spielordner, Overlay, Overlayposition, Alarmton, Fenster im Vordergrund und GitHub-Updates. Weitere Optionen sind durch Scrollen erreichbar. |

## Overlay und Richtung

Ab Version 1.5.2 ist der Hintergrund vollständig transparent, ohne Rahmen. Nur Schrift, Pfeil und Entfernung bleiben sichtbar. Normale Klicks gehen durch das Overlay. Zum Verschieben Shift halten und direkt an der Schrift oder am Pfeil ziehen; leere transparente Flächen bleiben durchlässig.

Ab Version 1.5.1 zeigt das kompakte Overlay ausschließlich Überschrift, Richtungspfeil und Entfernung. Zusätzliche Statuszeilen und Fußtexte entfallen. Die Zielmarkierung ersetzt den Pfeil bei gleicher horizontaler Position; eine angezeigte Restentfernung kann dann einen Höhenunterschied bedeuten. Sie bestätigt keinen Lootabschluss.

**Astral Cache Found** bedeutet: Die Kiste ist aktuell geladen. **Astral Cache Saved** bedeutet: Der Scanner führt dich zu einem früher gespeicherten, noch offenen Fundort. Ein gespeicherter Ort garantiert nicht, dass die Kiste dort noch steht.

Der Pfeil zeigt relativ zur **Blickrichtung des Charakters**, nicht zur frei gedrehten Kamera. Die Entfernung wird in **Yards** berechnet und berücksichtigt die Höhe, wenn sie verfügbar ist. Der Pfeil zeigt die direkte Richtung; Gelände, Hindernisse und Laufwege werden nicht berechnet. Bei mehreren passenden offenen Kisten wird das nächste Ziel gewählt.

Das Overlay erscheint nur, wenn WoW im Vordergrund ist und gültige aktuelle Daten vorliegen. Bei Spielwechsel, Lesefehlern oder veralteten Daten wird es ausgeblendet. Auf Transportmitteln können Richtung und Entfernung pausieren, weil dort andere Koordinaten gelten. Die Position des Infofensters lässt sich in den Einstellungen ändern. **Zum freien Verschieben Shift gedrückt halten und das Overlay mit der linken Maustaste ziehen.** Die Position wird gespeichert und an die Größe des Spielfensters angepasst. Ohne Verschieben bleibt das Overlay durchklickbar. Unter **Einstellungen → Overlayposition zurücksetzen** geht es zurück zur Standardposition.

## Kisten speichern und als gelootet erkennen

Jede erkannte Kiste wird automatisch unter **Meine Kisten** gespeichert. Beim Vorbeifliegen bleibt der letzte bekannte Ort erhalten, auch wenn der Client die Kiste anschließend nicht mehr lädt.

| Zustand | Bedeutung |
| --- | --- |
| **Sichtbar** | Die Kiste wird aktuell vom Client gemeldet. |
| **Gespeichert** | Ein offener Fund ist bekannt, wird aber gerade nicht geladen. |
| **Gelootet** | Der Fund wurde automatisch oder manuell als erledigt markiert. |

Die automatische Näheprüfung markiert einen Fund als gelootet, wenn der Spieler **höchstens 10 Yards** von seinen Koordinaten entfernt ist und die Kiste **mindestens eine Sekunde** lang in vollständigen, gültigen Scans fehlt. Für diese Prüfung müssen auch die Höhen beider Positionen bekannt sein; es zählt der räumliche Abstand. Tokens sind dafür nicht erforderlich. Lesefehler, ungeeignete Koordinaten oder unterbrochene Scans verhindern diese Näheentscheidung.

Das ist eine Erledigungsannahme: Auch eine von jemand anderem gelootete oder verschwundene Kiste kann so abgeschlossen werden. Ebenso kann ein bereits leerer gespeicherter Fundort beim späteren Besuch erledigt werden. Außerhalb der Nähegrenze reicht das Verschwinden beim Weiterfliegen dafür nicht aus.

Zum Korrigieren unter **Meine Kisten** eine Zeile auswählen und **Als gelootet markieren** oder **Als offen markieren** drücken. Mit **Gelootete anzeigen** werden erledigte Einträge sichtbar. Der historische Fundort bleibt in der Datenbank erhalten. Es gibt keine Vorhersage der Respawnzeit.

## Regionen und Koordinaten

Gespeichert werden **Server, Karten-ID, Objekt-ID und Weltkoordinaten X/Y sowie Z, sofern verfügbar**, dazu erste und letzte Sichtung. Bei neuen passenden Sichtungen kommen Region und Unterregion hinzu.

Die Koordinaten sind Weltkoordinaten in Yards, keine regionalen Kartenprozente von 0 bis 100. Die Karten-ID gehört deshalb immer zum räumlichen Bezug. **Sichtungsregion** bezeichnet die Region des Spielers beim Erkennen; direkt an einer Regionsgrenze muss sie nicht exakt der Region der Kiste entsprechen.

Bei alten Funden ohne Regionsdaten steht **Nicht erfasst**. Fehlende Regionen werden nicht aus X/Y geraten. Für die dauerhafte Navigation und die austauschbare Datenbank werden nur bestätigte Weltkarten unterstützt: Östliche Königreiche, Kalimdor, Scherbenwelt und Nordend. Instanzen oder unbekannte Custom-Karten sind dafür nicht allgemein freigegeben.

## Fundorte importieren, zusammenführen und exportieren

**Meine Kisten** enthält deinen persönlichen offenen oder erledigten Zustand. **Fundorte** ist die historische Sammlung möglicher Spawnorte. Ein importierter Fundort erzeugt deshalb keinen Live-Treffer und kein neues offenes Overlay-Ziel.

### Mit anderen teilen

1. **Fundorte** öffnen und **JSON exportieren** wählen.
2. Speicherort auswählen. Exportiert wird die gesamte Sammlung, auch wenn gerade ein Suchfilter aktiv ist.
3. Die JSON-Datei weitergeben. Sie enthält Fundorte, keine Charakter-GUIDs, lokalen Dateipfade oder persönlichen Loot-Zustände.

### Sammlungen zusammenführen

1. Unter **Fundorte** auf **Importieren** klicken.
2. Eine oder mehrere zuvor exportierte JSON-Dateien auswählen.
3. Die App prüft die Dateien und führt passende Orte zusammen. Erneuter Import derselben Daten erzeugt keine zusätzlichen identischen Einträge.

Als gleich gelten Orte mit passendem Server, gleicher Karte und Objekt-ID sowie denselben auf eine Nachkommastelle gerundeten Koordinaten. Unterschiedliche oder fehlende Höhen können getrennte Einträge ergeben. Eine ungültige Importdatei wird mit einer Fehlermeldung abgewiesen; der geprüfte Import wird dann nicht teilweise übernommen.

### Für ein späteres Addon

**Für Addon exportieren** erzeugt eine Lua-Datentabelle namens **AstralCacheLocations**. Das ist noch kein installierbares Addon. Eine spätere Integration muss Karten-ID, Weltkoordinaten und gegebenenfalls die Umrechnung in Addon-Kartenkoordinaten berücksichtigen. Zum Austausch zwischen Scannern immer JSON verwenden; der Scanner importiert keine Lua-Dateien.

## Speicherort und Sicherung

Alle persönlichen Daten liegen getrennt vom Programm im Windows-Benutzerordner **%LOCALAPPDATA%\AstralScanner**. Den Pfad mit **Windows + R** öffnen.

| Datei oder Ordner | Inhalt |
| --- | --- |
| **overlay-position.json** | Frei gewählte Overlayposition. |
| **settings.json** | Bestätigter Spielordner. |
| **finds.json** | Persönliche Kisten und ihr Erledigungszustand. |
| **locations.json** | Historische Fundort-Datenbank. |
| **.bak-Dateien** | Vorheriger erfolgreich gespeicherter Stand der jeweiligen Datendatei. |
| **Logs** | Protokolle und gespeicherte Diagnoseaufnahmen. |

Für eine vollständige Sicherung den Scanner schließen und den gesamten Ordner kopieren. Ein JSON-Export der Fundorte sichert die Ortsammlung, aber nicht deine persönlichen offenen oder gelooteten Kisten. Beim normalen Programmupdate bleiben die Daten in diesem Benutzerordner erhalten.

## Updates installieren

Beim Programmstart und über **Einstellungen → Auf Updates prüfen** wird das neueste stabile GitHub-Release geprüft. Ab **Version 1.4.0** erscheint bei einer neuen Version oben im Scannerfenster ein animierter Updatehinweis. Ohne Bestätigung werden keine Update-Dateien heruntergeladen oder installiert.

1. Den Updatehinweis anklicken und die Frage **Update installieren?** mit **Ja** bestätigen.
2. Der Scanner lädt das ZIP-Paket und prüft Größe, SHA-256-Prüfsumme und Programmversion.
3. Persönliche Daten werden gespeichert. Der Scanner schließt sich regulär, ein Helfer ersetzt die Programmdateien und startet die App neu.
4. Den **Scan starten**-Knopf anschließend selbst drücken.

Mit **×** kannst du den Hinweis schließen. Nach ungefähr zehn Sekunden fährt er auch von selbst nach oben aus dem Fenster. Das Update bleibt unter **Einstellungen → Update installieren** erreichbar. Diese Aktion kann auch ohne gespeichertes Angebot erneut nach einer neuen Version suchen; nur während einer laufenden Prüfung oder Installation ist sie gesperrt. **Nein** in der Rückfrage lässt die bisherige Version weiterlaufen.

Die Dateien **AstralScanner.exe** und **README.md** werden direkt im vorhandenen Programmordner ersetzt. Ein ZIP-Unterordner **AstralScanner** wird aufgelöst; es entsteht kein zusätzlicher Unterordner. Persönliche Daten unter **%LOCALAPPDATA%\AstralScanner** bleiben erhalten. Alte Programmdateien werden unter **.astral-update-backup-…** gesichert; bei einem Fehler beim Austausch wird zurückgesetzt. Das Updateprotokoll liegt unter **Logs\updater.log** im persönlichen Datenordner.

Für das Update sind eine GitHub-Verbindung und Schreibrechte im Programmordner nötig. Bei Netzwerkproblemen oder einem fehlerhaften Paket bleibt die bisherige Version verfügbar. Über **Downloads öffnen** ist weiterhin eine manuelle Installation möglich: Scanner schließen und die Dateien der neuen ZIP in den bestehenden Programmordner kopieren.

**Ältere Versionen:** Bis einschließlich 1.2.1 ist ein einmaliger manueller Umstieg nötig. Versionen 1.3.x installieren Updates noch automatisch ohne Rückfrage; der neue Bestätigungsablauf gilt ab 1.4.0.

## Debug-Log und Hilfe bei Problemen

Den Scan laufen lassen, das Problem nachstellen und möglichst zeitnah unter **Diagnose → Debug-Log exportieren** eine Textdatei speichern. Versionsnummer, kurze Fehlerbeschreibung und die Situation im Spiel helfen bei der Auswertung.

**Live folgen** pausiert nur die Anzeige zum Lesen; im Hintergrund wird weiter protokolliert. Der exportierbare Sitzungsverlauf ist begrenzt, deshalb nach einem Fehler zeitnah exportieren. **Protokollordner öffnen** führt zu den Logdateien. **Diagnoseaufnahme speichern** legt zusätzlich eine Momentaufnahme der aktuellen Scandaten ab, sofern welche vorliegen.

Logs vor dem Weitergeben prüfen: Trotz teilweiser Maskierung können Dateipfade, Spielinformationen und Koordinaten enthalten sein. Eine Diagnoseaufnahme ersetzt keinen Fundort-Export.

| Problem | Was du prüfen kannst |
| --- | --- |
| **Scan starten ist gesperrt** | Den Ordner mit der passenden Wow.exe wählen. Nach einem Client-Update kann das unterstützte Profil nicht mehr passen. |
| **Kein passender Prozess** | Genau einen WoW-Client aus dem gewählten Ordner starten und einloggen. |
| **Keine Kiste erkannt** | Scan starten und nahe genug herangehen, damit der Client ein Objekt namens Astral Cache lädt. Historische Fundorte sind keine aktuellen Spawn-Meldungen. |
| **Kein Overlay** | Overlay einschalten, WoW in den Vordergrund holen und Fenstermodus verwenden. Der Scanner muss laufen und gültige Zieldaten haben. |
| **Pfeil wirkt verdreht** | Der Pfeil folgt der Charakterausrichtung. Eine unabhängig gedrehte Kamera verändert diesen Bezug nicht. |
| **Kiste bleibt nach Loot offen** | Innerhalb von 10 Yards kurz am Fundort bleiben und mindestens eine Sekunde gültige Scans abwarten. Bei Bedarf manuell markieren und das Debug-Log exportieren. |
| **Region fehlt** | Bei alten Funden wurde sie möglicherweise noch nicht erfasst. X/Y allein reichen nicht für eine sichere Ergänzung. |
| **Importierter Ort erscheint nicht im Overlay** | Importe ergänzen die historische Datenbank, nicht die persönlichen aktiven Funde. |
| **Speichern oder Import schlägt fehl** | Fehlermeldung und Log sichern. Vor Änderungen den Datenordner kopieren; bestehende Dateien nicht vorschnell löschen. |
| **Updateprüfung schlägt fehl** | GitHub-Verbindung prüfen oder das neueste Release direkt öffnen. |

## Grenzen und Projektlinks

Der Scanner ist ein Prototyp mit geprüftem Clientprofil. Oberflächen- und Logiktests ersetzen keinen Loot-Praxistest auf jedem Rechner. Historische Fundorte bestätigen keinen aktuellen Spawn, und die automatische Erledigung ist kein Beweis, wer eine Kiste gelootet hat.

[Alle Releases](https://github.com/AngelGamerZ/astral-cache-scanner/releases) · [Quellcode](https://github.com/AngelGamerZ/astral-cache-scanner) · [Fehler melden](https://github.com/AngelGamerZ/astral-cache-scanner/issues)

Die Downloadseite und das Repository sind öffentlich. Die Seite bittet Suchmaschinen, sie nicht zu indexieren; ein direkt weitergegebener Link ist kein Zugriffsschutz.

Ab Version 1.5.0 darf pro Windows-Benutzer und Sitzung nur eine reguläre Scannerinstanz laufen, damit persönliche Daten nicht gegenseitig überschrieben werden. [Auditbericht 1.5.0](docs/AUDIT-1.5.0.md)
