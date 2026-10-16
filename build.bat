@echo off
setlocal EnableExtensions
cd /d "%~dp0"

echo ======================================
echo   Auto Input Tester V6 - Build
echo ======================================
echo Ordner: %cd%
echo.

REM Guard: nested duplicate from old ZIPs breaks the build with CS0101.
if exist "%~dp0AutoInputTester_FIXED_V6\AutoInputTester.csproj" (
  echo WARNUNG: Alte Doppelkopie gefunden und wird entfernt ...
  rmdir /s /q "%~dp0AutoInputTester_FIXED_V6"
)

dotnet --version >nul 2>&1
if errorlevel 1 (
  echo FEHLER: dotnet wurde nicht gefunden.
  echo.
  echo Bitte .NET 8 SDK fuer Windows x64 installieren:
  echo https://dotnet.microsoft.com/download/dotnet/8.0
  echo Danach dieses Fenster schliessen und build.bat erneut starten.
  pause
  exit /b 1
)

for /f "tokens=1 delims=." %%A in ('dotnet --version') do set DOTNET_MAJOR=%%A
if not "%DOTNET_MAJOR%"=="8" (
  echo.
  echo HINWEIS: Gefundene SDK-Version:
  dotnet --version
  echo Empfohlen ist .NET 8 SDK. Der Build wird trotzdem versucht ...
  echo.
)

echo Alte Build-Dateien werden entfernt ...
if exist publish rmdir /s /q publish
if exist publish_fix rmdir /s /q publish_fix
if exist publish_new rmdir /s /q publish_new
if exist publish_oe rmdir /s /q publish_oe
if exist publish_oe2 rmdir /s /q publish_oe2
if exist bin rmdir /s /q bin
if exist obj rmdir /s /q obj

echo.
echo Erstelle einzelne Windows-x64 EXE ...
dotnet publish AutoInputTester.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
if errorlevel 1 (
  echo.
  echo BUILD FEHLGESCHLAGEN.
  echo.
  echo Haeufige Ursachen auf einem anderen PC:
  echo  1. .NET 8 SDK fehlt  -^> https://dotnet.microsoft.com/download/dotnet/8.0
  echo  2. Windows Desktop / WPF Workload fehlt
  echo  3. Ordner liegt in OneDrive/Cloud und Dateien sind nur online
  echo  4. Antivirus blockiert den Build kurzzeitig -^> erneut versuchen
  echo.
  pause
  exit /b 1
)

if not exist "%~dp0publish\AutoInputTester.exe" (
  echo FEHLER: publish\AutoInputTester.exe wurde nicht erzeugt.
  pause
  exit /b 1
)

echo.
echo FERTIG:
echo %~dp0publish\AutoInputTester.exe
echo.
echo Tipp zum Weitergeben: Nur diesen Projektordner OHNE bin\ obj\ publish\
echo Oder noch einfacher: nur die fertige publish\AutoInputTester.exe teilen.
start "" "%~dp0publish"
pause
