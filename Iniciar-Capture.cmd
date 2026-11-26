@echo off
powershell.exe -NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0src\Capture.ps1"
if errorlevel 1 pause
