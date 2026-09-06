# Astral Cache Scanner

Windows-Desktop-Scanner für den geprüften Project-Astral-Client. Nur lesender Prozesszugriff, keine Eingabeautomation.

Version 1.0.0: Live-Erkennung, Richtungsoverlay, persistente Fundliste, JSON-/Lua-Export und Token-Gutschrift als ergänzendes Lootsignal.

Start: ZIP entpacken, AstralScanner.exe öffnen, beim ersten Start WoW-Ordner wählen, dann Scan starten. Benötigt Windows mit .NET Framework 4.x. Funddateien bleiben unter %LOCALAPPDATA%\AstralScanner.

Die Anzeige verwendet Weltkoordinaten in Yards; gespeicherte Orte sind keine Bestätigung aktuell vorhandener Kisten.

Build: build.ps1. Tests: --self-test sowie --memory-test, --database-test und --reward-test mit einem separaten Testordner.
