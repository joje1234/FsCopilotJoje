@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Collect test logs.ps1"
set "FSC_TEST_COLLECT_EXIT=%ERRORLEVEL%"
echo.
pause
endlocal & exit /b %FSC_TEST_COLLECT_EXIT%
