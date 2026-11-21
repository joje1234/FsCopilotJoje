@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Configure startup.ps1" -Mode Install
set "FSC_STARTUP_EXIT=%ERRORLEVEL%"
if not "%FSC_STARTUP_EXIT%"=="0" echo Startup setup failed. Read the message above; the helper was not started.
pause
endlocal & exit /b %FSC_STARTUP_EXIT%
