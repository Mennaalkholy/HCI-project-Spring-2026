@echo off
REM Launcher from hci2 repo root (forwards to phase 3 hci\start-play.ps1).
setlocal
set "PS_EXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%PS_EXE%" set "PS_EXE=powershell.exe"
"%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\..\..\..\start-play.ps1" %*
exit /b %ERRORLEVEL%
