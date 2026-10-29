@echo off
:: BlueBoost — Otimizador de PC fraco para BlueStacks 5
:: Clique duplo neste arquivo para abrir a ferramenta.
title BlueBoost
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0BlueBoost.ps1"
if errorlevel 1 (
    echo.
    echo Ocorreu um erro ao iniciar o BlueBoost.
    pause
)
