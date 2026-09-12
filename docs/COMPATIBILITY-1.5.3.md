# Client-Kompatibilität in 1.5.3

Stand: 12. September 2026.

Das Clientupdate wurde zuvor allein wegen der geänderten vollständigen SHA-256-Prüfsumme abgelehnt. Die elf bisher geprüften Getter-Sequenzen sind in der neuen Datei unverändert. Das neue konkrete Clientprofil bleibt zusätzlich zur bisherigen Freigabeliste unterstützt.

## Eng begrenzte automatische Erkennung

Für das geprüfte Profil vom 12. September berechnet ClientCompatibility einen normalisierten SHA-256-Fingerabdruck. Ausschließlich COFF-Zeitstempel, PE-Prüfsumme und Nutzdaten der schreibgeschützten, nicht ausführbaren .rsrc-Sektion werden normalisiert. Dateilänge, alle anderen Headerinformationen, Sektionseinteilung, Code, initialisierte Daten und angehängte Bytes bleiben Bestandteil des Vergleichs. Eine Ressourcenänderung mit geänderter Dateigröße oder geändertem Layout wird nicht automatisch akzeptiert.

Vor der Normalisierung werden Dateigröße (maximal 32 MiB), PE32/x86, ImageBase, Sektionsgrenzen, Überlappungen, Ressourcenrechte sowie Entry-Point-/Datenverzeichnisüberschneidungen geprüft. Der Fingerabdruck ist ein Kompatibilitätsmerkmal, keine Herstellersignatur und kein Beweis für die Unverändertheit des laufenden Prozesses. Die bestehenden Live-Getter-, Objekt-, GUID-, Koordinaten- und Kontextprüfungen bleiben aktiv. Es gibt keine freie Suche nach vermeintlich passenden Offsets und keine allgemeine Freigabe unbekannter WoW-Clients.

Die Diagnose unterscheidet bekannte Clientdateien von kompatiblen Profilvarianten. Änderungen außerhalb der eng begrenzten Normalisierung erfordern weiterhin eine erneute Prüfung und gegebenenfalls ein Scanner-Update.

## Prüfung

- Neuer Client SHA-256: CC685C8EA7B84300A99B8BE0FAE84CB82287A85C14C6A84F4CA04042EC629B65.
- Normalisiertes Profil: 78908DDA484A6AAA61A1EDA10B6E13A7683C1600DD24215974FF3C0F01A894B1.
- Elf statische Getter-Sequenzen unverändert.
- Zehn rein lesende Live-Scans: jeweils 19 GameObjects, keine unlesbaren GameObjects, Spielerposition, Richtung, Region und Kontext vorhanden, Loot-/Walletlesung gültig. Keine Astral Cache geladen; kein echter Fund-/Loot-Praxistest.
- Gegenprüfungen mit isolierten Dateikopien: unbekannte Gesamtprüfsumme durch Zeitstempel/PE-Prüfsumme/Ressourceninhalt akzeptiert; Code-, Daten-, Entry-Point- und angehängte Byteänderungen abgelehnt; ungültige PE-Datei und ausführbare Ressourcen abgelehnt.
- Sieben Testsuiten bestanden: Selbsttest, Overlay, Einrichtung/Kompatibilität, gespeicherte Funde, Belohnungen, Desktopfunktionen und Fundort-Datenbank.
- Keine Änderungen am Spielprozess, keine Spielaktionen; lokale installierte Scanner-App nicht ersetzt.
