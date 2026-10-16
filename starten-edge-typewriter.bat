@echo off
setlocal EnableExtensions
cd /d "%~dp0"

echo ======================================
echo   Edge mit DOM-Debugging starten
echo   edu.typewriter.at (Typewriter EDU)
echo ======================================
echo.
echo Kein .NET / keine Installation noetig.
echo Auto Input Tester liest den Text aus der Seite.
echo.

set "PORT=9222"
set "URL=https://edu.typewriter.at/index.php?r=user/overview"
set "PROFILE=%LOCALAPPDATA%\AutoInputTester\EdgeDomProfile"

if not exist "%PROFILE%" mkdir "%PROFILE%"

set "EDGE="

REM 1) App Paths (zuverlaessig auf frischen PCs)
for /f "tokens=2*" %%A in ('reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe" /ve 2^>nul') do set "EDGE=%%B"
if not defined EDGE for /f "tokens=2*" %%A in ('reg query "HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe" /ve 2^>nul') do set "EDGE=%%B"

REM 2) Standard-Pfade
if not defined EDGE if exist "%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe" set "EDGE=%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"
if not defined EDGE if exist "%ProgramFiles%\Microsoft\Edge\Application\msedge.exe" set "EDGE=%ProgramFiles%\Microsoft\Edge\Application\msedge.exe"
if not defined EDGE if exist "%LocalAppData%\Microsoft\Edge\Application\msedge.exe" set "EDGE=%LocalAppData%\Microsoft\Edge\Application\msedge.exe"

set "CHROME="
if exist "%ProgramFiles%\Google\Chrome\Application\chrome.exe" set "CHROME=%ProgramFiles%\Google\Chrome\Application\chrome.exe"
if not defined CHROME if exist "%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe" set "CHROME=%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe"
if not defined CHROME if exist "%LocalAppData%\Google\Chrome\Application\chrome.exe" set "CHROME=%LocalAppData%\Google\Chrome\Application\chrome.exe"

if defined EDGE (
  echo Starte Microsoft Edge ...
  echo %EDGE%
  REM --remote-allow-origins=* damit AutoInputTester den DOM lesen kann.
  start "" "%EDGE%" --remote-debugging-port=%PORT% --remote-allow-origins=* --user-data-dir="%PROFILE%" --no-first-run --no-default-browser-check "%URL%"
  goto :done
)

if defined CHROME (
  echo Edge nicht gefunden – Fallback Chrome ...
  start "" "%CHROME%" --remote-debugging-port=%PORT% --remote-allow-origins=* --user-data-dir="%LOCALAPPDATA%\AutoInputTester\ChromeDomProfile" --no-first-run --no-default-browser-check "%URL%"
  goto :done
)

echo FEHLER: Weder Edge noch Chrome gefunden.
echo Windows 10/11 sollte Edge mitbringen. Sonst Chrome installieren
echo und starten-chrome-typewriter.bat nutzen.
pause
exit /b 1

:done
echo.
echo Browser laeuft mit Port %PORT%.
echo Seite: edu.typewriter.at
echo.
echo Danach:
echo   1. Einloggen / Lektion starten
echo   2. AutoInputTester.exe starten
echo   3. Browser fokussieren, Ctrl+Shift+F9
echo.
echo Siehe auch: SO-STARTEN.txt
echo.
pause
