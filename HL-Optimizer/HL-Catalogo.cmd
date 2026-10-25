@echo off
rem ============================================================================
rem  HL OPTIMIZER - catalogo de tweaks (marca: HL)
rem  Arquivo: HL-Catalogo.cmd
rem  Lista os 51 tweaks com o estado atual (aplicado / pendente) e sai.
rem ============================================================================
setlocal EnableExtensions
title HL Optimizer v2.0.0 - Catalogo de tweaks
cd /d "%~dp0"

fltmc >nul 2>&1
if not %errorlevel%==0 (
    echo.
    echo  HL OPTIMIZER - solicitando privilegios de administrador...
    echo.
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -WorkingDirectory '%~dp0' -Verb RunAs"
    exit /b 0
)

if not exist "%~dp0HL-Optimizer.ps1" (
    echo.
    echo  [X] HL-Optimizer.ps1 nao encontrado nesta pasta.
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0HL-Optimizer.ps1" -ListTweaks -NoPanel
echo.
pause
endlocal
