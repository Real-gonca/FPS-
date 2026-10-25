@echo off
rem ============================================================================
rem  HL OPTIMIZER - simulacao (marca: HL)
rem  Arquivo: HL-Simular.cmd
rem  Roda o perfil EQUILIBRADO em modo DryRun: mostra tudo que SERIA feito,
rem  sem alterar nada no sistema. Use isto antes de aplicar de verdade.
rem ============================================================================
setlocal EnableExtensions
title HL Optimizer - Simulacao (DryRun)
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
echo   HL OPTIMIZER - MODO SIMULACAO (nada sera alterado)
echo  ============================================================
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0HL-Optimizer.ps1" -DryRun -Profile Balanced -EmulatorFocus -NonInteractive
set "RC=%errorlevel%"

echo.
echo  Simulacao concluida. Veja o log em _logs (arquivo *_dryrun.log).
echo.
pause
endlocal & exit /b %RC%
