@echo off
setlocal
set "CFG=%LOCALAPPDATA%\AutoInputTester\settings.json"
if exist "%CFG%" (
  del /q "%CFG%"
  echo Einstellungen geloescht. Beim naechsten Start werden die V5-Standardwerte verwendet.
) else (
  echo Keine gespeicherten Einstellungen gefunden.
)
pause
