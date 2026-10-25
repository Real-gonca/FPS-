@echo off
rem ============================================================================
rem  HL OPTIMIZER - launcher (marca: HL)
rem  Arquivo: HL-Optimizer.cmd
rem  Eleva privilegio e abre o painel principal do HL-Optimizer.ps1
rem ============================================================================
setlocal EnableExtensions
title HL Optimizer v2.0.0 - FPS / Games / Emulators
cd /d "%~dp0"

rem --- checa privilegio de administrador (fltmc falha sem admin) --------------
fltmc >nul 2>&1
if not %errorlevel%==0 (
    echo.
    echo  HL OPTIMIZER - solicitando privilegios de administrador...
    echo.
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -WorkingDirectory '%~dp0' -Verb RunAs"
    exit /b 0
)

rem --- garante que o .ps1 esta ao lado deste .cmd -----------------------------
if not exist "%~dp0HL-Optimizer.ps1" (
    echo.
    echo  [X] HL-Optimizer.ps1 nao encontrado nesta pasta.
    echo      Mantenha os arquivos do HL Optimizer juntos.
    echo.
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0HL-Optimizer.ps1" %*
set "RC=%errorlevel%"

if not "%RC%"=="0" (
    echo.
    echo  HL Optimizer terminou com codigo %RC%.
    echo  1=erro  2=cancelado  3=sem admin  4=lista de protecao  5=falha de backup
    echo.
    pause
)
endlocal & exit /b %RC%
