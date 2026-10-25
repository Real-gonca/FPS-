@echo off
rem ============================================================================
rem  HL OPTIMIZER - reverter (marca: HL)
rem  Arquivo: HL-Reverter.cmd
rem  Restaura o estado anterior a partir do backup mais recente em _HL-Backup
rem ============================================================================
setlocal EnableExtensions
title HL Optimizer - Reverter backup
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

echo.
echo  ============================================================
echo   HL OPTIMIZER - REVERTER
echo   Restaura servicos, registro e plano de energia do backup.
echo  ============================================================
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0HL-Optimizer.ps1" -Revert -NoPanel %*
set "RC=%errorlevel%"

echo.
pause
endlocal & exit /b %RC%
