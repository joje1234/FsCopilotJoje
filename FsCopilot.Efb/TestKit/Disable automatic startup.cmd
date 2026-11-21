@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Configure startup.ps1" -Mode Remove
set "FSC_STARTUP_EXIT=%ERRORLEVEL%"
pause
endlocal & exit /b %FSC_STARTUP_EXIT%
