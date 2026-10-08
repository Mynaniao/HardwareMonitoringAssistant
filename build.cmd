@echo off
rem One-click build wrapper (ASCII only, so no code-page issues).
rem Output: dist\HardwareMonitor.exe
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
pause
