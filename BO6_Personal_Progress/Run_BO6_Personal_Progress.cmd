@echo off
setlocal
set "BO6_PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if exist "%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe" set "BO6_PS=%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
"%BO6_PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0BO6_Personal_Progress.ps1" -GameRoot "%~dp0.."
if errorlevel 1 pause
endlocal
