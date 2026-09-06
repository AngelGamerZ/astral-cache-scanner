# Astral Scanner 1.1.0

- **Erledigung am Fundort:** Wenn der Spieler höchstens 10 Yards (3D) vom gespeicherten Ort entfernt ist und die Kiste eine Sekunde lang in vollständigen Scans fehlt, gilt sie als gelootet. Tokens sind nicht erforderlich. Lesefehler, Transport, Kartenwechsel und Abfragelücken unterbrechen die Prüfung. Der Ort bleibt in der historischen Datenbank. Auch eine anderweitig verschwundene Kiste gilt nach dieser Regel als erledigt; das ist kein persönlicher Lootnachweis.
- **Updates:** Die App prüft beim Start das aktuelle stabile GitHub-Release. Im Reiter „Updates“ kann die Prüfung wiederholt und die Downloadseite geöffnet werden. Updates werden manuell heruntergeladen und entpackt; persönliche Daten liegen weiterhin unter `%LOCALAPPDATA%\AstralScanner`.
- **Debug-Log:** Ein eigener Reiter zeigt ein begrenztes Sitzungsprotokoll. „Debug-Log exportieren“ speichert es als Textdatei. Darin stehen Version, Scanzustand, Näheprüfung und Fehlermeldungen. Benutzerprofilpfad und Charakter-GUID im Kartenkontext werden ersetzt. Spiel-/Exportpfade und Fundorte können enthalten sein; vor Weitergabe prüfen. Zugangstokens werden nicht protokolliert.
- **Regionen:** `region`, `subregion` und `regionSource` ergänzen JSON- und Lua-Exporte. Der Reader liest die aktuelle Region und das Untergebiet des Spielers zum Sichtungszeitpunkt. Deshalb heißt dies in der Oberfläche „Sichtungsregion“. An Regionsgrenzen kann die Kiste auf der anderen Seite stehen. Weltkoordinaten in Yards und Weltkarten-ID bleiben das eindeutige räumliche Bezugssystem; es sind keine Prozentkoordinaten einer Zonenkarte. Alte Daten ohne Region bleiben importierbar und werden als unbekannt angezeigt.

Downloadseite: https://angelgamerz.github.io/astral-cache-scanner/

Releases: https://github.com/AngelGamerZ/astral-cache-scanner/releases

Die Downloadseite ist öffentlich. `noindex` und `robots.txt` sind Hinweise an Suchmaschinen, kein Passwortschutz.

Die Angaben zu verpflichtenden Token-Gutschriften in älteren Dokumentationsabschnitten beschreiben die vorherige Version. Für 1.1.0 genügt die oben beschriebene Näheprüfung. Ein vollständiger Test mit einer echten gelooteten Kiste bei einem betroffenen Nutzer steht noch aus.
