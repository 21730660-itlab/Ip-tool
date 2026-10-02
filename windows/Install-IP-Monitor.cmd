@echo off
rem IP Monitor for Windows - double-click to install (current user, no admin rights needed)
title IP Monitor setup
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
echo.
pause
