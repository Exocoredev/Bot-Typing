@echo off
setlocal EnableExtensions
cd /d "%~dp0"

echo ======================================
echo   Sauberes ZIP zum Weitergeben
echo ======================================
echo.

set "OUTDIR=%TEMP%\AutoInputTester_V6_Share"
set "ZIP=%USERPROFILE%\Desktop\AutoInputTester_FIXED_V6_SHARE.zip"

if exist "%OUTDIR%" rmdir /s /q "%OUTDIR%"
mkdir "%OUTDIR%" || goto :fail

echo Kopiere Quellcode ohne bin/obj/publish ...
robocopy "%cd%" "%OUTDIR%" /E /XD bin obj publish .vs AutoInputTester_FIXED_V6 /XF *.user *.suo /NFL /NDL /NJH /NJS /nc /ns /np
if errorlevel 8 goto :fail

if exist "%ZIP%" del /f /q "%ZIP%"

echo Erzeuge ZIP auf dem Desktop ...
powershell -NoProfile -Command "Compress-Archive -Path '%OUTDIR%\*' -DestinationPath '%ZIP%' -Force"
if errorlevel 1 goto :fail

rmdir /s /q "%OUTDIR%"

echo.
echo FERTIG:
echo %ZIP%
echo.
echo Dieses ZIP auf den anderen PC kopieren, entpacken, build.bat starten.
echo Noch einfacher: nur publish\AutoInputTester.exe weitergeben.
echo.
pause
exit /b 0

:fail
echo.
echo FEHLER beim Packen.
pause
exit /b 1
