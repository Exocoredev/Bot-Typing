@echo off
setlocal EnableExtensions
cd /d "%~dp0"

echo ======================================
echo   Fertiges Paket fuer frische PCs
echo   EXE + BATs - kein .NET noetig
echo ======================================
echo.

if not exist "%~dp0publish\AutoInputTester.exe" (
  echo publish\AutoInputTester.exe fehlt - baue jetzt ...
  call "%~dp0build.bat"
  if not exist "%~dp0publish\AutoInputTester.exe" (
    echo FEHLER: Build hat keine EXE erzeugt.
    pause
    exit /b 1
  )
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0_make_share.ps1"
if errorlevel 1 (
  echo FEHLER beim Packen.
  pause
  exit /b 1
)

echo.
start "" "%USERPROFILE%\Desktop\AutoInputTester_FERTIG"
pause
exit /b 0
