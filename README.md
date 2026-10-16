# Bot-Typing (Auto Input Tester V6)

Tippt Übungstexte auf **edu.typewriter.at** (Typewriter EDU). Primär über **Browser-DOM** (exakt), OCR nur als Fallback.

## Schnellstart (ohne Build)

1. `starten-edge-typewriter.bat` **oder** `starten-chrome-typewriter.bat`
2. Auf edu.typewriter.at einloggen und Lektion starten
3. `publish\AutoInputTester.exe` starten (vorher `build.bat` / `fertigen-zum-teilen.bat`)
4. Browser fokussieren → **Ctrl+Shift+F9** starten → **Ctrl+Shift+F11** stoppen

Siehe auch `SO-STARTEN.txt` und `TEILEN.txt`.

Fertiges Paket für andere PCs (kein .NET nötig): `fertigen-zum-teilen.bat`

## Bauen

Voraussetzung: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (Windows x64)

```bat
build.bat
```

EXE: `publish\AutoInputTester.exe` (self-contained, läuft auf frischen PCs ohne SDK)

## Hotkeys (Standard)

| Aktion | Hotkey |
|--------|--------|
| Bereich (OCR-Fallback) | Ctrl+Shift+S |
| Start | Ctrl+Shift+F9 |
| Pause | Ctrl+Shift+F10 |
| Stop | Ctrl+Shift+F11 |

## Hinweise

- Browser **nur über die .bat** starten (Debugging-Port 9222)
- Edge und Chrome nicht gleichzeitig mit der .bat nutzen
- Persönliche Settings/Logs liegen lokal unter `%LocalAppData%\AutoInputTester` und sind **nicht** im Repo

## Lizenz

MIT – siehe `LICENSE`
