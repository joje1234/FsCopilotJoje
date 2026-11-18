@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Check setup.ps1"
set "FSC_TEST_CHECK_EXIT=%ERRORLEVEL%"
echo.
pause
endlocal & exit /b %FSC_TEST_CHECK_EXIT%
