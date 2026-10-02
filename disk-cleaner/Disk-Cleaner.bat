@echo off
title Disk Cleaner
rem Double-click this file to start Disk Cleaner.
rem It asks for administrator rights so Windows folders can be cleaned too.

net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -Command "try { Start-Process -FilePath '%~f0' -Verb RunAs -ErrorAction Stop; exit 0 } catch { exit 1 }"
    if not errorlevel 1 exit /b
    echo Administrator rights were not given - running with limited cleaning.
    echo.
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Disk-Cleaner.ps1" %*
