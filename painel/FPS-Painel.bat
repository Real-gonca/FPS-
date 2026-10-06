@echo off
:: ============================================================================
::  FPS PAINEL - Mouse + Teclado + Sensi   (Blood Strike e Free Fire no emulador)
::  Versao: 1.0  |  Windows 10/11  |  Executar como Administrador
::
::  O QUE ESTE PAINEL FAZ DE VERDADE
::   - Desliga a aceleracao do mouse no Windows: causa numero 1 de sensi
::     "instavel", porque o mesmo movimento da mao vira um giro diferente a
::     cada vez que voce move mais rapido.
::   - Coloca o ponteiro em 1:1, valor 10, e o teclado na resposta mais rapida.
::   - Corta fontes de atraso e engasgo: Game DVR, throttle de rede, plano de
::     energia, agendamento de GPU e prioridade do processo do emulador.
::   - Via ADB: ajusta a sensibilidade do mouse DENTRO do Android do emulador
::     [pointer_speed], desliga animacoes e libera a taxa de atualizacao.
::   - Mede e converte a sua sensi real em cm por volta de 360 graus,
::     inclusive de Free Fire para Blood Strike.
::
::  O QUE ELE NAO FAZ
::   - Ele NAO altera a sensibilidade dentro do jogo. Esse numero fica no
::     perfil da sua conta, dentro do Android do emulador, e so pode ser
::     digitado na tela de Settings do proprio jogo. O painel calcula e mostra
::     o valor exato que voce deve digitar.
::
::  Tudo e reversivel: a opcao [2] faz backup do registro antes de mudar.
:: ============================================================================
chcp 65001 >nul
setlocal EnableExtensions EnableDelayedExpansion
title FPS Painel - Mouse, Teclado e Sensi  [Blood Strike + Free Fire]
mode con cols=100 lines=38 >nul 2>&1

set "PVER=1.0"
set "ROOT=%~dp0"
set "BKROOT=%ROOT%backups"
set "PF86=%ProgramFiles(x86)%"
set "ADB="
set "DEV="
set "PERFIL=nenhum"
set "REBOOT=0"
set "LASTBK=-"
if not exist "%BKROOT%" mkdir "%BKROOT%" >nul 2>&1
if exist "%BKROOT%\ultimo.txt" set /p LASTBK=<"%BKROOT%\ultimo.txt"

net session >nul 2>&1
if errorlevel 1 goto elevar
goto menu

:: ---------------------------------------------------------------------------
:elevar
echo.
echo  Este painel precisa de privilegios de ADMINISTRADOR para mexer em
echo  HKLM, plano de energia e prioridade de processo.
echo.
echo  Abrindo uma nova janela com elevacao... aguarde.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs -WorkingDirectory '%~dp0'" >nul 2>&1
if errorlevel 1 echo  [ERRO] Nao foi possivel elevar. Clique com o botao direito - Executar como administrador.
echo.
pause
exit /b 1

:: ===========================================================================
::  MENU PRINCIPAL
:: ===========================================================================
:menu
cls
echo  ======================================================================
echo    FPS PAINEL  v%PVER%   -   MOUSE, TECLADO E SENSI NO EMULADOR
echo    Blood Strike  -  Free Fire
echo  ======================================================================
echo    Perfil aplicado nesta sessao ......... %PERFIL%
echo    Ultimo backup ........................ %LASTBK%
echo  ----------------------------------------------------------------------
echo    [ 1 ] APLICAR TUDO  -  backup + mouse + teclado + sistema + emulador
echo    [ 2 ] Backup do estado atual  [fazer antes de qualquer coisa]
echo    [ 3 ] Diagnostico  -  o que esta ativo agora
echo  ----------------------------------------------------------------------
echo    [ 4 ] Otimizar MOUSE       -  aceleracao off, ponteiro 1:1
echo    [ 5 ] Otimizar TECLADO     -  repeticao e resposta mais rapidas
echo    [ 6 ] Sistema / latencia   -  Game DVR, energia, rede, GPU
echo    [ 7 ] Emulador via ADB     -  sensi do mouse, animacoes, Hz
echo    [ 8 ] Boost do emulador    -  prioridade de processo
echo  ----------------------------------------------------------------------
echo    [ 9 ] PERFIS PRONTOS       -  RUSH / EQUILIBRADO / PRECISAO
echo    [10 ] SENSIBILIDADE        -  calibracao, conversao e calculadora
echo    [11 ] Guia de sensi        -  onde mexer no jogo e no emulador
echo  ----------------------------------------------------------------------
echo    [12 ] Avancado             -  GPU dedicada, fullscreen, fila de entrada
echo    [13 ] Restaurar padrao / desfazer
echo    [ 0 ] Sair
echo  ----------------------------------------------------------------------
set "OP="
set /p "OP=  Escolha uma opcao: "
if not defined OP goto menu
if "%OP%"=="1" goto aplicar_tudo
if "%OP%"=="2" goto backup
if "%OP%"=="3" goto diag
if "%OP%"=="4" goto opt_mouse
if "%OP%"=="5" goto opt_teclado
if "%OP%"=="6" goto opt_sistema
if "%OP%"=="7" goto opt_adb
if "%OP%"=="8" goto boost_proc
if "%OP%"=="9" goto perfis
if "%OP%"=="10" goto sensi
if "%OP%"=="11" goto guia
if "%OP%"=="12" goto avancado
if "%OP%"=="13" goto restaurar
if "%OP%"=="0" goto fim
goto menu

:: ===========================================================================
:aplicar_tudo
cls
echo  [1] APLICAR TUDO
echo  ======================================================================
echo  Sera feito: backup - mouse - teclado - sistema - emulador via ADB.
echo  A sensi DENTRO do jogo continua sendo sua: o painel mostra os valores
echo  sugeridos no final, para voce digitar na tela de Settings do jogo.
echo.
set "C="
set /p "C=  Continuar? [S/N]: "
if /i not "%C%"=="S" goto menu
call :backup_run
call :opt_mouse_run
call :opt_teclado_run
call :opt_sistema_run
call :adb_detectar
if defined ADB (call :opt_adb_run) else (echo  [--] Emulador: adb nao encontrado, etapa ADB pulada. Veja a opcao [7].)
echo.
echo  ----------------------------------------------------------------------
echo  Feito. Proximos passos:
echo    1. Reinicie o Windows  -  obrigatorio para mouse e teclado.
echo    2. Reabra o emulador e o jogo.
echo    3. No Blood Strike: Settings - Sensitivity - Acceleration = FIXED SPEED.
echo    4. Va na opcao [10] e calibre a sensi real em cm por volta.
echo  ----------------------------------------------------------------------
set "PERFIL=completo"
call :aviso_reboot
pause
goto menu

:: ===========================================================================
::  BACKUP
:: ===========================================================================
:backup
cls
echo  [2] BACKUP DO ESTADO ATUAL
echo  ======================================================================
call :backup_run
echo.
pause
goto menu

:backup_run
set "TS="
for /f "usebackq delims=" %%T in (`powershell -NoProfile -Command "Get-Date -Format yyyyMMdd-HHmmss" 2^>nul`) do set "TS=%%T"
if not defined TS set "TS=manual"
set "BKF=%BKROOT%\%TS%"
mkdir "%BKF%" >nul 2>&1
if not exist "%BKF%" (
  echo  [ERRO] Nao consegui criar a pasta %BKF%
  goto :eof
)
reg export "HKCU\Control Panel\Mouse" "%BKF%\01-mouse.reg" /y >nul 2>&1
reg export "HKCU\Control Panel\Keyboard" "%BKF%\02-teclado.reg" /y >nul 2>&1
reg export "HKCU\Control Panel\Desktop" "%BKF%\03-desktop.reg" /y >nul 2>&1
reg export "HKCU\System\GameConfigStore" "%BKF%\04-gameconfigstore.reg" /y >nul 2>&1
reg export "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR" "%BKF%\05-gamedvr.reg" /y >nul 2>&1
reg export "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" "%BKF%\06-mmcss.reg" /y >nul 2>&1
reg export "HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl" "%BKF%\07-prioridade.reg" /y >nul 2>&1
reg export "HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers" "%BKF%\08-graficos.reg" /y >nul 2>&1
reg export "HKCU\Software\Microsoft\DirectX\UserGpuPreferences" "%BKF%\09-gpu-pref.reg" /y >nul 2>&1
reg export "HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers" "%BKF%\10-compat.reg" /y >nul 2>&1
powercfg /getactivescheme > "%BKF%\plano-energia.txt" 2>&1
echo %TS%> "%BKROOT%\ultimo.txt"
set "LASTBK=%TS%"
echo  [OK] Registro e plano de energia salvos em:
echo       %BKF%
call :adb_detectar
if defined DEV (
  "%ADB%" -s !DEV! shell settings get system pointer_speed > "%BKF%\adb-pointer_speed.txt" 2>&1
  echo  [OK] pointer_speed atual do Android salvo junto.
)
goto :eof

:: ===========================================================================
::  DIAGNOSTICO
:: ===========================================================================
:diag
cls
echo  [3] DIAGNOSTICO - VALORES ATUAIS
echo  ======================================================================
echo  MOUSE E TECLADO  [Windows]
call :regget "HKCU\Control Panel\Mouse" "MouseSpeed"
set "D1=!RG!"
call :regget "HKCU\Control Panel\Mouse" "MouseThreshold1"
set "D2=!RG!"
call :regget "HKCU\Control Panel\Mouse" "MouseThreshold2"
set "D3=!RG!"
call :regget "HKCU\Control Panel\Mouse" "MouseSensitivity"
set "D4=!RG!"
call :regget "HKCU\Control Panel\Keyboard" "KeyboardDelay"
set "D5=!RG!"
call :regget "HKCU\Control Panel\Keyboard" "KeyboardSpeed"
set "D6=!RG!"
call :regget "HKCU\Control Panel\Desktop" "LowLevelHooksTimeout"
set "D7=!RG!"
echo    Aceleracao do ponteiro, Speed/T1/T2 ...... !D1! / !D2! / !D3!
if "!D1!"=="0" (echo        - desligada, sensi consistente) else (echo        - LIGADA, esta causando sensi instavel)
echo    Velocidade do ponteiro, 1 a 20 ........... !D4!
if "!D4!"=="10" (echo        - 1:1, correto) else (echo        - fora do 1:1, o Windows esta escalando seus counts)
echo    Teclado, atraso 0-3 / repeticao 0-31 ..... !D5! / !D6!
echo    LowLevelHooksTimeout ..................... !D7!
echo.
echo  SISTEMA
call :regget "HKCU\System\GameConfigStore" "GameDVR_Enabled"
set "D8=!RG!"
call :regget "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" "NetworkThrottlingIndex"
set "D9=!RG!"
call :regget "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" "SystemResponsiveness"
set "D10=!RG!"
call :regget "HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl" "Win32PrioritySeparation"
set "D11=!RG!"
call :regget "HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers" "HwSchMode"
set "D12=!RG!"
echo    Game DVR / captura em segundo plano ...... !D8!    alvo: 0
echo    Throttle de rede, MMCSS .................. !D9!    alvo: 0xffffffff
echo    SystemResponsiveness ..................... !D10!   alvo: 10
echo    Win32PrioritySeparation .................. !D11!
echo    Agendamento de GPU por hardware .......... !D12!   alvo: 2
echo    Plano de energia ativo:
powercfg /getactivescheme 2>nul
echo.
echo  EMULADOR  [Android via ADB]
call :adb_detectar
if not defined ADB (
  echo    adb ........ nao encontrado. Use a opcao [7] para apontar o caminho.
  echo    dispositivo  n/d
) else (
  echo    adb ........ %ADB%
  if not defined DEV (
    echo    dispositivo  NENHUM conectado. Abra o emulador e ative o ADB nele.
  ) else (
    echo    dispositivo  !DEV!
    set "A1=-"
    set "A2=-"
    set "A3=-"
    for /f "usebackq delims=" %%R in (`"%ADB%" -s !DEV! shell settings get system pointer_speed 2^>nul`) do set "A1=%%R"
    for /f "usebackq delims=" %%R in (`"%ADB%" -s !DEV! shell settings get system peak_refresh_rate 2^>nul`) do set "A2=%%R"
    for /f "usebackq delims=" %%R in (`"%ADB%" -s !DEV! shell settings get global window_animation_scale 2^>nul`) do set "A3=%%R"
    echo    pointer_speed do Android, -7 a 7 ......... !A1!
    echo    peak_refresh_rate ........................ !A2!
    echo    window_animation_scale ................... !A3!    alvo: 0
  )
)
echo  ----------------------------------------------------------------------
echo  Reinicie o Windows depois de aplicar: mouse e teclado do registro so
echo  valem apos logoff ou reboot.
echo  ----------------------------------------------------------------------
echo.
pause
goto menu

:regget
:: %1 = chave   %2 = valor   -> resultado em RG
set "RG=-"
for /f "tokens=2,*" %%A in ('reg query "%~1" /v "%~2" 2^>nul ^| findstr /i /c:"%~2"') do set "RG=%%B"
goto :eof

:: ===========================================================================
::  MOUSE
:: ===========================================================================
:opt_mouse
cls
echo  [4] OTIMIZAR MOUSE
echo  ======================================================================
echo    1 - Modo JOGO: aceleracao off + ponteiro 1:1  [recomendado]
echo    2 - Definir a velocidade do ponteiro manualmente, 1 a 20
echo    3 - Explicar o que cada valor faz
echo    0 - Voltar
echo  ----------------------------------------------------------------------
set "OM="
set /p "OM=  Opcao: "
if "%OM%"=="1" (call :opt_mouse_run & pause & goto opt_mouse)
if "%OM%"=="2" goto mouse_speed_manual
if "%OM%"=="3" goto mouse_info
if "%OM%"=="0" goto menu
goto opt_mouse

:opt_mouse_run
echo.
echo  -- Mouse: aceleracao desligada e ponteiro 1:1 --
call :regset "HKCU\Control Panel\Mouse" "MouseSpeed" REG_SZ "0"
call :regset "HKCU\Control Panel\Mouse" "MouseThreshold1" REG_SZ "0"
call :regset "HKCU\Control Panel\Mouse" "MouseThreshold2" REG_SZ "0"
call :regset "HKCU\Control Panel\Mouse" "MouseSensitivity" REG_SZ "10"
call :regset "HKCU\Control Panel\Mouse" "MouseHoverTime" REG_SZ "10"
set "REBOOT=1"
echo  [OK] Aceleracao desligada e ponteiro em 1:1, valor 10.
echo       Vale para o Windows inteiro. Jogos com raw input ignoram isso, mas
echo       em emulador a mira costuma passar pelo cursor - aqui faz diferenca.
goto :eof

:mouse_speed_manual
cls
echo  Velocidade do ponteiro do Windows, de 1 a 20. O valor 10 e o ponto 1:1,
echo  sem escalonamento. Para jogo o certo e 10: qualquer outro valor multiplica
echo  ou divide seus counts e quebra a memoria muscular.
set "NS="
set /p "NS=  Valor de 1 a 20: "
if not defined NS (pause & goto opt_mouse)
echo !NS!| findstr /r "^[1-9][0-9]*$" >nul 2>&1
if errorlevel 1 (echo  [ERRO] Digite um numero inteiro. & pause & goto opt_mouse)
if !NS! GTR 20 (echo  [ERRO] O maximo e 20. & pause & goto opt_mouse)
call :regset "HKCU\Control Panel\Mouse" "MouseSensitivity" REG_SZ "%NS%"
set "REBOOT=1"
echo  [OK] MouseSensitivity = %NS%. Reinicie para aplicar.
pause
goto opt_mouse

:mouse_info
cls
echo  MouseSpeed + MouseThreshold1 + MouseThreshold2 formam o "Aprimorar
echo  precisao do ponteiro". Ligados, o Windows acelera o cursor conforme a
echo  velocidade da sua mao: o mesmo movimento fisico vira um giro diferente a
echo  cada vez. Em FPS isso e exatamente o que faz a sensi parecer doida.
echo.
echo  MouseSensitivity = 10 e o ponto neutro do Windows, o 6 de 11 na interface
echo  grafica. Abaixo de 10 ele divide seus counts, acima ele multiplica. Nos
echo  dois casos o movimento deixa de ser 1:1 e o arredondamento introduz erro.
echo.
echo  Importante: jogos com raw input ignoram tudo isso. Em emulador a mira
echo  normalmente passa pelo cursor do Windows, entao aqui FAZ diferenca.
echo.
pause
goto opt_mouse

:: ===========================================================================
::  TECLADO
:: ===========================================================================
:opt_teclado
cls
echo  [5] OTIMIZAR TECLADO
echo  ======================================================================
echo    1 - Resposta maxima: repeticao rapida e atraso minimo [recomendado]
echo    2 - Voltar o teclado ao padrao do Windows
echo    3 - Explicar
echo    0 - Voltar
echo  ----------------------------------------------------------------------
set "OT="
set /p "OT=  Opcao: "
if "%OT%"=="1" (call :opt_teclado_run & pause & goto opt_teclado)
if "%OT%"=="2" (call :teclado_padrao & pause & goto opt_teclado)
if "%OT%"=="3" goto teclado_info
if "%OT%"=="0" goto menu
goto opt_teclado

:opt_teclado_run
echo.
echo  -- Teclado: repeticao rapida, atraso minimo --
call :regset "HKCU\Control Panel\Keyboard" "KeyboardDelay" REG_SZ "0"
call :regset "HKCU\Control Panel\Keyboard" "KeyboardSpeed" REG_SZ "31"
set "REBOOT=1"
echo  [OK] Teclado otimizado. Segurar tecla repete mais rapido, o que ajuda em
echo       movimento continuo e strafe. Vale apos logoff ou reboot.
goto :eof

:teclado_padrao
call :regset "HKCU\Control Panel\Keyboard" "KeyboardDelay" REG_SZ "1"
call :regset "HKCU\Control Panel\Keyboard" "KeyboardSpeed" REG_SZ "31"
set "REBOOT=1"
echo  [OK] Teclado de volta ao padrao do Windows.
goto :eof

:teclado_info
cls
echo  KeyboardDelay = quanto tempo ate comecar a repetir. 0 e o minimo, 3 o max.
echo  KeyboardSpeed = velocidade da repeticao. 31 e a mais rapida.
echo.
echo  NAO confunda com input lag de jogo nem com ping: isso melhora a resposta
echo  de tecla SEGURADA, nao o tempo do primeiro clique. O primeiro clique
echo  depende do USB, do polling rate do teclado e do proprio jogo.
echo.
pause
goto opt_teclado

:: ===========================================================================
::  SISTEMA / LATENCIA
:: ===========================================================================
:opt_sistema
cls
echo  [6] SISTEMA / LATENCIA
echo  ======================================================================
echo    1 - Aplicar tudo desta secao [recomendado]
echo    2 - Plano de energia: Desempenho Maximo
echo    3 - Desligar Game DVR e Game Bar
echo    4 - Multimedia: throttle de rede e SystemResponsiveness
echo    5 - Agendamento de GPU por hardware
echo    6 - Explicar
echo    0 - Voltar
echo  ----------------------------------------------------------------------
set "OS="
set /p "OS=  Opcao: "
if "%OS%"=="1" (call :opt_sistema_run & pause & goto opt_sistema)
if "%OS%"=="2" (call :plano_maximo_run & pause & goto opt_sistema)
if "%OS%"=="3" (call :gamedvr_off_run & pause & goto opt_sistema)
if "%OS%"=="4" (call :mmcss_run & pause & goto opt_sistema)
if "%OS%"=="5" (call :hags_on_run & pause & goto opt_sistema)
if "%OS%"=="6" goto sistema_info
if "%OS%"=="0" goto menu
goto opt_sistema

:opt_sistema_run
echo.
echo  -- Sistema / latencia --
call :plano_maximo_run
call :gamedvr_off_run
call :mmcss_run
call :hags_on_run
echo  [OK] Secao de sistema aplicada.
goto :eof

:plano_maximo_run
:: O GUID aparece na saida de powercfg /list em qualquer idioma, entao a
:: deteccao abaixo nao depende de o Windows estar em portugues ou ingles.
powercfg /list 2>nul | findstr /i "e9a42b02-d5df-448d-aa00-03f14749eb61" >nul 2>&1
if not errorlevel 1 (
  powercfg -setactive e9a42b02-d5df-448d-aa00-03f14749eb61 >nul 2>&1
  echo  [OK] Plano Desempenho Maximo ativado.
  goto :eof
)
set "ULT="
for /f "usebackq delims=" %%G in (`powershell -NoProfile -Command "$o = powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61 2>$null | Out-String; if ($o -match '([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})') { $Matches[1] }" 2^>nul`) do set "ULT=%%G"
if defined ULT (
  powercfg -setactive !ULT! >nul 2>&1
  echo  [OK] Plano Desempenho Maximo criado e ativado: !ULT!
  goto :eof
)
powercfg -setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c >nul 2>&1
if not errorlevel 1 (
  echo  [OK] Plano Alto Desempenho ativado.
  goto :eof
)
echo  [AVISO] Nao consegui trocar o plano de energia. Faca manual em powercfg.cpl
goto :eof

:gamedvr_off_run
call :regset "HKCU\System\GameConfigStore" "GameDVR_Enabled" REG_DWORD "0"
call :regset "HKCU\System\GameConfigStore" "GameDVR_FSEBehaviorMode" REG_DWORD "2"
call :regset "HKCU\System\GameConfigStore" "GameDVR_HonorUserFSEBehaviorMode" REG_DWORD "1"
call :regset "HKCU\System\GameConfigStore" "GameDVR_DXGIHonorFSEWindowsCompatible" REG_DWORD "1"
call :regset "HKCU\System\GameConfigStore" "GameDVR_EFSEFeatureFlags" REG_DWORD "0"
call :regset "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR" "AppCaptureEnabled" REG_DWORD "0"
reg add "HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR" /v AllowGameDVR /t REG_DWORD /d 0 /f >nul 2>&1
set "REBOOT=1"
echo  [OK] Game DVR e captura em segundo plano desligados.
goto :eof

:mmcss_run
call :regset "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" "NetworkThrottlingIndex" REG_DWORD "4294967295"
call :regset "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" "SystemResponsiveness" REG_DWORD "10"
set "REBOOT=1"
echo  [OK] Throttle multimedia liberado e SystemResponsiveness em 10.
goto :eof

:hags_on_run
call :regset "HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers" "HwSchMode" REG_DWORD "2"
set "REBOOT=1"
echo  [OK] Agendamento de GPU por hardware habilitado. Se o emulador ficar
echo       instavel, volte este valor para 1 na opcao [13].
goto :eof

:sistema_info
cls
echo  Plano de energia ....... evita que a CPU reduza o clock no meio da
echo                           partida, que e o que causa pico de input lag.
echo  Game DVR ............... a gravacao em segundo plano do Game Bar rouba
echo                           frames e causa stutter: a sensi "trava".
echo  NetworkThrottling ...... o Windows limita trafego de rede quando ha audio
echo                           multimedia; liberar evita microengasgo de pacote.
echo  SystemResponsiveness ... reserva de CPU para tarefas de fundo. Em 10 sobra
echo                           mais CPU para o jogo sem matar Discord e voz.
echo  HAGS ................... tira o agendamento de GPU do Windows. Ajuda em
echo                           alguns setups e piora em outros: teste.
echo.
pause
goto opt_sistema

:: ===========================================================================
::  EMULADOR / ADB
:: ===========================================================================
:opt_adb
cls
echo  [7] EMULADOR VIA ADB
echo  ======================================================================
call :adb_detectar
if defined ADB (echo    adb ......... %ADB%) else (echo    adb ......... NAO ENCONTRADO)
if defined DEV (echo    dispositivo . %DEV%) else (echo    dispositivo . nenhum conectado)
echo  ----------------------------------------------------------------------
echo    1 - Procurar e conectar o emulador de novo
echo    2 - Informar o caminho do adb manualmente
echo    3 - APLICAR otimizacao do emulador: sensi, animacoes e Hz
echo    4 - Ajustar so o pointer_speed do Android, de -7 a 7
echo    5 - Mostrar os valores atuais do Android
echo    6 - Reiniciar o servidor adb, quando o emulador some
echo    0 - Voltar
echo  ----------------------------------------------------------------------
set "OA="
set /p "OA=  Opcao: "
if "%OA%"=="1" (call :adb_conectar & pause & goto opt_adb)
if "%OA%"=="2" goto adb_manual
if "%OA%"=="3" (call :opt_adb_run & pause & goto opt_adb)
if "%OA%"=="4" goto adb_pointer
if "%OA%"=="5" (call :adb_status & pause & goto opt_adb)
if "%OA%"=="6" (call :adb_restart & pause & goto opt_adb)
if "%OA%"=="0" goto menu
goto opt_adb

:adb_manual
cls
echo  Cole o caminho completo do adb do seu emulador. Exemplos:
echo    BlueStacks 5 : C:\Program Files\BlueStacks_nxt\HD-Adb.exe
echo    LDPlayer 9   : C:\LDPlayer\LDPlayer9\adb.exe
echo    MuMu 12      : C:\Program Files\Netease\MuMuPlayer-12.0\shell\adb.exe
echo    Nox          : C:\Program Files (x86)\Nox\bin\nox_adb.exe
echo.
set "NP="
set /p "NP=  Caminho: "
if not defined NP (pause & goto opt_adb)
set "NP=!NP:"=!"
if not exist "!NP!" (echo  [ERRO] Arquivo nao existe: !NP! & pause & goto opt_adb)
set "ADB=!NP!"
echo  [OK] adb definido: !ADB!
call :adb_conectar
pause
goto opt_adb

:opt_adb_run
echo.
echo  -- Emulador via ADB --
if not defined ADB (
  echo  [ERRO] adb nao definido. Use a opcao [2] deste menu.
  goto :eof
)
call :adb_conectar
if not defined DEV (
  echo  [ERRO] Nenhum dispositivo conectado. Abra o emulador, ative o ADB nas
  echo         configuracoes dele e tente de novo.
  goto :eof
)
set "PSV=2"
if /i "%PERFIL%"=="rush" set "PSV=4"
if /i "%PERFIL%"=="precisao" set "PSV=0"
call :adb_put system pointer_speed %PSV%
call :adb_put system peak_refresh_rate 120
call :adb_put system min_refresh_rate 120
call :adb_put global window_animation_scale 0
call :adb_put global transition_animation_scale 0
call :adb_put global animator_duration_scale 0
call :adb_put secure show_touches 0
call :adb_put system accelerometer_rotation 0
echo  [OK] Emulador otimizado. pointer_speed = %PSV%.
echo       Se a tela ficar dura demais, use a opcao [4] e baixe esse valor.
echo       Em Android antigo o peak_refresh_rate pode nao existir: o painel
echo       apenas reporta e segue.
goto :eof

:adb_pointer
cls
echo  pointer_speed do Android: -7 e o mais lento, 7 o mais rapido, 0 o padrao.
echo  Ele muda a velocidade do CURSOR do mouse dentro do Android. Se o seu
echo  emulador usa mira com mouse travado, a mira pode nao usar esse valor -
echo  nesse caso mexa na sensi do mapeamento de teclas do emulador e na sensi
echo  do jogo.
set "PV="
set /p "PV=  Valor de -7 a 7: "
if not defined PV (pause & goto opt_adb)
set "PV=!PV:,=.!"
echo !PV!| findstr /r "^[0-9][0-9]*$ ^-[0-9][0-9]*$" >nul 2>&1
if errorlevel 1 (echo  [ERRO] Numero invalido. & pause & goto opt_adb)
if !PV! LSS -7 (echo  [ERRO] O minimo e -7. & pause & goto opt_adb)
if !PV! GTR 7 (echo  [ERRO] O maximo e 7. & pause & goto opt_adb)
call :adb_conectar
if not defined DEV (echo  [ERRO] Nenhum dispositivo. & pause & goto opt_adb)
call :adb_put system pointer_speed !PV!
pause
goto opt_adb

:adb_status
call :adb_conectar
if not defined DEV (
  echo  [ERRO] Nenhum dispositivo conectado.
  goto :eof
)
echo.
call :adb_show system pointer_speed
call :adb_show system peak_refresh_rate
call :adb_show system min_refresh_rate
call :adb_show global window_animation_scale
call :adb_show global transition_animation_scale
call :adb_show global animator_duration_scale
call :adb_show secure show_touches
call :adb_show system accelerometer_rotation
echo.
goto :eof

:adb_show
:: %1 = namespace  %2 = chave
set "SV=-"
for /f "usebackq delims=" %%R in (`"%ADB%" -s %DEV% shell settings get %1 %2 2^>nul`) do set "SV=%%R"
echo    %1 %2 = !SV!
goto :eof

:adb_restart
if not defined ADB (
  echo  [ERRO] adb nao definido.
  goto :eof
)
"%ADB%" kill-server >nul 2>&1
"%ADB%" start-server >nul 2>&1
echo  [OK] Servidor adb reiniciado.
call :adb_conectar
goto :eof

:adb_put
:: %1 = namespace  %2 = chave  %3 = valor
"%ADB%" -s %DEV% shell settings put %1 %2 %3 >nul 2>&1
if errorlevel 1 (echo    [--] %1 %2 : nao aceito neste Android) else (echo    [OK] %1 %2 = %3)
goto :eof

:adb_detectar
if defined ADB if exist "%ADB%" goto adb_dev
set "ADB="
for /f "delims=" %%A in ('where adb 2^>nul') do if not defined ADB set "ADB=%%A"
for %%A in (
  "%ProgramFiles%\BlueStacks_nxt\HD-Adb.exe"
  "%ProgramFiles%\BlueStacks\HD-Adb.exe"
  "%ProgramFiles%\LDPlayer\LDPlayer9\adb.exe"
  "%PF86%\LDPlayer\LDPlayer9\adb.exe"
  "C:\LDPlayer\LDPlayer9\adb.exe"
  "%ProgramFiles%\Netease\MuMuPlayer-12.0\shell\adb.exe"
  "%ProgramFiles%\Netease\MuMu Player 12\shell\adb.exe"
  "%ProgramFiles%\Nox\bin\nox_adb.exe"
  "%PF86%\Nox\bin\nox_adb.exe"
  "%ProgramFiles%\Microvirt\Microvirt\adb.exe"
  "%ProgramFiles%\Tencent\MobileGamePCShared\adb.exe"
  "%ProgramFiles%\Gameloop\AndroidEmulator\adb.exe"
) do if not defined ADB if exist "%%~A" set "ADB=%%~A"
:adb_dev
set "DEV="
if not defined ADB goto :eof
for /f "usebackq tokens=1,2" %%A in (`"%ADB%" devices 2^>nul`) do if /i "%%B"=="device" if not defined DEV set "DEV=%%A"
if defined DEV goto :eof
for %%P in (5555 5556 5557 5559 5561 5565 5575 5585 5595 5554 62001 21503 7555 16384 16416 16448 16480) do if not defined DEV (
  "%ADB%" connect 127.0.0.1:%%P >nul 2>&1
  for /f "usebackq tokens=1,2" %%A in (`"%ADB%" devices 2^>nul`) do if /i "%%B"=="device" if not defined DEV set "DEV=%%A"
)
goto :eof

:adb_conectar
call :adb_detectar
if defined DEV (echo  [OK] Conectado: !DEV!) else (echo  [--] Nenhum dispositivo Android conectado.)
goto :eof

:: ===========================================================================
::  BOOST DE PROCESSO
:: ===========================================================================
:boost_proc
cls
echo  [8] BOOST DO EMULADOR
echo  ======================================================================
echo  Coloca os processos do emulador em prioridade ALTA. Reduz o engasgo quando
echo  Discord, navegador ou antivirus disputam CPU - que e quando a sensi parece
echo  pesar no meio do tiro.
echo.
echo  AVISO: prioridade alta demais pode picotar o audio. Se o som travar,
echo         reinicie o emulador.
echo.
set "BP="
set /p "BP=  Aplicar agora? [S/N]: "
if /i not "%BP%"=="S" goto menu
powershell -NoProfile -Command "$n='HD-Player','dnplayer','Ld9BoxHeadless','LdVBoxHeadless','Nox','NoxVMHandle','MuMuPlayer','MuMuVMMHeadless','NemuHeadless','aow_exe'; $c=0; Get-Process | Where-Object { $n -contains $_.ProcessName } | ForEach-Object { try { $_.PriorityClass='High'; $c++ } catch {} }; if ($c -eq 0) { 'nenhum processo de emulador em execucao' } else { 'prioridade Alta aplicada em ' + $c + ' processos' }"
echo.
echo  Dica: isso precisa ser refeito toda vez que voce abrir o emulador.
pause
goto menu

:: ===========================================================================
::  PERFIS
:: ===========================================================================
:perfis
set "PSUG="
cls
echo  [9] PERFIS PRONTOS
echo  ======================================================================
echo    [1] RUSH        - giro rapido, combate de perto, SMG e shotgun
echo                      pointer_speed 4 e tudo no maximo
echo    [2] EQUILIBRADO - recomendado para ranked, pointer_speed 2
echo    [3] PRECISAO    - controle de recuo e mira de longe, pointer_speed 0
echo    [0] Voltar
echo  ----------------------------------------------------------------------
echo  Os tres desligam aceleracao, animacoes e Game DVR. O que muda entre eles
echo  e a velocidade do cursor no Android e a sugestao de sensi do jogo.
echo  ----------------------------------------------------------------------
set "PF="
set /p "PF=  Perfil: "
if "%PF%"=="1" (set "PERFIL=rush" & set "PSUG=4")
if "%PF%"=="2" (set "PERFIL=equilibrado" & set "PSUG=2")
if "%PF%"=="3" (set "PERFIL=precisao" & set "PSUG=0")
if "%PF%"=="0" goto menu
if not defined PSUG goto perfis
goto perfis_run

:perfis_run
call :backup_run
call :opt_mouse_run
call :opt_teclado_run
call :opt_sistema_run
call :adb_detectar
if defined DEV (
  call :adb_put system pointer_speed %PSUG%
  call :adb_put system peak_refresh_rate 120
  call :adb_put system min_refresh_rate 120
  call :adb_put global window_animation_scale 0
  call :adb_put global transition_animation_scale 0
  call :adb_put global animator_duration_scale 0
  call :adb_put secure show_touches 0
)
echo.
echo  ----------------------------------------------------------------------
echo    PERFIL %PERFIL% APLICADO  -  pointer_speed do Android = %PSUG%
echo  ----------------------------------------------------------------------
echo    Ponto de partida sugerido para digitar DENTRO do jogo:
echo.
if /i "%PERFIL%"=="rush" (
  echo      Free Fire    : Geral 95 - Red Dot 90 - 2x 85 - 4x 75 - Mira 55
  echo      Blood Strike : Camera 80 - Disparo 65 - Red Dot 75 - 4x 55 - 8x 35
)
if /i "%PERFIL%"=="equilibrado" (
  echo      Free Fire    : Geral 90 - Red Dot 85 - 2x 80 - 4x 70 - Mira 50
  echo      Blood Strike : Camera 70 - Disparo 58 - Red Dot 68 - 4x 48 - 8x 30
)
if /i "%PERFIL%"=="precisao" (
  echo      Free Fire    : Geral 82 - Red Dot 78 - 2x 72 - 4x 62 - Mira 45
  echo      Blood Strike : Camera 60 - Disparo 50 - Red Dot 58 - 4x 42 - 8x 26
)
echo      Nos dois jogos: Aceleracao em FIXED SPEED antes de qualquer slider.
echo.
echo    Isso e ponto de partida, nao numero magico. Calibre na opcao [10]
echo    medindo o giro de 360 graus e ajuste de 2 em 2 pontos ate travar.
echo  ----------------------------------------------------------------------
call :aviso_reboot
pause
goto menu

:: ===========================================================================
::  SENSIBILIDADE - CALIBRACAO E CONVERSAO
:: ===========================================================================
:sensi
cls
echo  [10] SENSIBILIDADE
echo  ======================================================================
echo    [1] Medir cm/360 no jogo A  [calibracao real]
echo    [2] Medir cm/360 no jogo B e informar a sensi usada
echo    [3] Igualar a sensi do jogo B a do jogo A
echo    [4] Ajuste fino: deixar X porcento mais rapida ou mais lenta
echo    [5] eDPI: DPI vezes sensi do jogo
echo    [6] Como medir certo
echo    [0] Voltar
echo  ----------------------------------------------------------------------
if defined CM_A echo    cm por volta no jogo A ....... %CM_A% cm
if defined CM_B echo    cm por volta no jogo B ....... %CM_B% cm
if defined SB   echo    sensi atual do jogo B ........ %SB%
echo  ----------------------------------------------------------------------
set "SN="
set /p "SN=  Opcao: "
if "%SN%"=="1" goto sensi_medir_a
if "%SN%"=="2" goto sensi_medir_b
if "%SN%"=="3" goto sensi_igualar
if "%SN%"=="4" goto sensi_fino
if "%SN%"=="5" goto sensi_edpi
if "%SN%"=="6" goto sensi_como
if "%SN%"=="0" goto menu
goto sensi

:sensi_medir_a
cls
echo  No JOGO A, entre no treino, olhe para um ponto fixo, gire exatamente uma
echo  volta completa de 360 graus e mea no mousepad quantos centimetros o mouse
echo  andou. Use ponto e nao virgula. Exemplo: 24.5
set "CA="
set /p "CA=  cm do giro de 360 no jogo A: "
if not defined CA (pause & goto sensi)
set "CA=!CA:,=.!"
echo !CA!| findstr /r "^[0-9][0-9.]*$" >nul 2>&1
if errorlevel 1 (echo  [ERRO] Use apenas numeros com ponto. & pause & goto sensi)
set "CM_A=!CA!"
echo  [OK] Jogo A = !CM_A! cm por volta.
pause
goto sensi

:sensi_medir_b
cls
echo  Faca o mesmo no JOGO B, usando a sensi que voce ja joga hoje.
set "CB="
set /p "CB=  cm do giro de 360 no jogo B: "
if not defined CB (pause & goto sensi)
set "CB=!CB:,=.!"
echo !CB!| findstr /r "^[0-9][0-9.]*$" >nul 2>&1
if errorlevel 1 (echo  [ERRO] Use apenas numeros com ponto. & pause & goto sensi)
set "CM_B=!CB!"
echo  [OK] Jogo B = !CM_B! cm por volta.
echo.
set "SB2="
set /p "SB2=  Qual a sensi atual do jogo B usada nessa medida? "
if defined SB2 (
  set "SB2=!SB2:,=.!"
  echo !SB2!| findstr /r "^[0-9][0-9.]*$" >nul 2>&1
  if errorlevel 1 (echo  [ERRO] Numero invalido, sensi nao salva.) else (set "SB=!SB2!" & echo  [OK] sensi do jogo B = !SB2!)
)
pause
goto sensi

:sensi_igualar
if not defined CM_A (echo  [ERRO] Falta medir o jogo A na opcao [1]. & pause & goto sensi)
if not defined CM_B (echo  [ERRO] Falta medir o jogo B na opcao [2]. & pause & goto sensi)
if not defined SB   (echo  [ERRO] Falta a sensi atual do jogo B na opcao [2]. & pause & goto sensi)
cls
echo  Convertendo a sensi do jogo B para ficar igual ao jogo A...
set "NOVA="
for /f "usebackq delims=" %%R in (`powershell -NoProfile -Command "$v=[math]::Round(%SB%*(%CM_B%/%CM_A%),3); $v.ToString([Globalization.CultureInfo]::InvariantCulture)" 2^>nul`) do set "NOVA=%%R"
if not defined NOVA (echo  [ERRO] Falha no calculo. O PowerShell esta disponivel? & pause & goto sensi)
echo  ----------------------------------------------------------------------
echo    Jogo A .................. %CM_A% cm por volta
echo    Jogo B .................. %CM_B% cm por volta com sensi %SB%
echo    NOVA SENSI DO JOGO B  =   !NOVA!
echo  ----------------------------------------------------------------------
echo    Digite !NOVA! no jogo B, refaca a medida e confira se chegou perto de
echo    %CM_A% cm. Se passar, ajuste fino na opcao [4].
echo.
echo    Depois de mudar, jogue 3 partidas de treino sem mexer de novo: memoria
echo    muscular so trava com repeticao.
pause
goto sensi

:sensi_fino
cls
echo  Ajuste fino: quanto voce quer mudar a sensi atual?
set "PCT="
set /p "PCT=  Porcentagem, use -10 para deixar 10 porcento mais lenta: "
if not defined PCT (pause & goto sensi)
set "PCT=!PCT:,=.!"
echo !PCT!| findstr /r "^[0-9][0-9.]*$ ^-[0-9][0-9.]*$" >nul 2>&1
if errorlevel 1 (echo  [ERRO] Numero invalido. & pause & goto sensi)
set "ATUAL="
set /p "ATUAL=  Sensi atual: "
if not defined ATUAL (pause & goto sensi)
set "ATUAL=!ATUAL:,=.!"
echo !ATUAL!| findstr /r "^[0-9][0-9.]*$" >nul 2>&1
if errorlevel 1 (echo  [ERRO] Numero invalido. & pause & goto sensi)
set "NOVA2="
for /f "usebackq delims=" %%R in (`powershell -NoProfile -Command "$v=[math]::Round(!ATUAL!*(1+!PCT!/100),3); $v.ToString([Globalization.CultureInfo]::InvariantCulture)" 2^>nul`) do set "NOVA2=%%R"
if not defined NOVA2 (echo  [ERRO] Falha no calculo. & pause & goto sensi)
echo  ----------------------------------------------------------------------
echo    !ATUAL! com !PCT! porcento  =  !NOVA2!
echo  ----------------------------------------------------------------------
pause
goto sensi

:sensi_edpi
cls
echo  eDPI e o DPI do mouse multiplicado pela sensibilidade do jogo. Serve para
echo  comparar setups dentro do MESMO jogo.
set "DPI="
set /p "DPI=  DPI do seu mouse: "
if not defined DPI (pause & goto sensi)
echo !DPI!| findstr /r "^[0-9][0-9]*$" >nul 2>&1
if errorlevel 1 (echo  [ERRO] Numero invalido. & pause & goto sensi)
set "SG="
set /p "SG=  Sensibilidade dentro do jogo: "
if not defined SG (pause & goto sensi)
set "SG=!SG:,=.!"
echo !SG!| findstr /r "^[0-9][0-9.]*$" >nul 2>&1
if errorlevel 1 (echo  [ERRO] Numero invalido. & pause & goto sensi)
set "EDPI="
for /f "usebackq delims=" %%R in (`powershell -NoProfile -Command "$v=[math]::Round(!DPI!*!SG!,2); $v.ToString([Globalization.CultureInfo]::InvariantCulture)" 2^>nul`) do set "EDPI=%%R"
echo  ----------------------------------------------------------------------
echo    !DPI! DPI  x  !SG!  =  !EDPI! eDPI
echo  ----------------------------------------------------------------------
echo    ATENCAO: a escala do Free Fire, de 0 a 100, nao e a mesma do Blood
echo    Strike, e nenhuma delas e a escala de Valorant ou CS. Por isso a
echo    conversao de verdade entre jogos e a medicao em cm por volta.
pause
goto sensi

:sensi_como
cls
echo  COMO MEDIR CERTO
echo  ----------------------------------------------------------------------
echo   1. Entre no treino e fique em pe, parado, sem pular.
echo   2. Escolha um objeto longe como referencia inicial.
echo   3. Ande com o mouse na horizontal ate dar uma volta completa e voltar a
echo      olhar exatamente para o mesmo objeto.
echo   4. Marque com fita no mousepad onde comecou e onde parou. Depois mea.
echo   5. Repita 3 vezes e use a media. Se variar mais de 1 cm, ainda existe
echo      aceleracao ligada em algum lugar.
echo.
echo  VARIACAO GRANDE ENTRE MEDIDAS = ACELERACAO LIGADA
echo    Rode a opcao [4] deste painel para desligar no Windows, coloque a
echo    Acceleration do Blood Strike em FIXED SPEED e desligue qualquer
echo    suavizacao de mira do emulador.
echo.
pause
goto sensi

:: ===========================================================================
::  GUIA
:: ===========================================================================
:guia
cls
echo  [11] ONDE MEXER DENTRO DO JOGO E DO EMULADOR
echo  ======================================================================
echo   A sensi que voce sente e a soma de 3 camadas. O painel controla a 1.
echo.
echo   CAMADA 1 - WINDOWS  [este painel]
echo     Aceleracao off, ponteiro 1:1, sem Game DVR. Faz a sensi ser SEMPRE a
echo     mesma para o mesmo movimento de mao.
echo.
echo   CAMADA 2 - EMULADOR
echo     pointer_speed do Android ..................... opcao [7] deste painel
echo     Sensi do mapeamento de teclas / mira com mouse .. editor de controles
echo       BlueStacks : Game controls, na lateral - Aim sensitivity
echo       LDPlayer   : Configuracoes de teclas - mira
echo       MuMu       : Key mapping - sensibilidade da mira
echo     FPS alto e ESTAVEL: 60 ou 90 fixos. FPS oscilando faz a sensi parecer
echo     dura em alguns momentos e leve em outros, mesmo sem mudar nada.
echo.
echo   CAMADA 3 - JOGO
echo     Free Fire    : Settings - Sensitivity. Geral, Red Dot, 2x, 4x, Sniper e
echo                    Free Look. Desative qualquer ajuste automatico de mira.
echo     Blood Strike : Settings - Sensitivity. Coloque ACCELERATION em
echo                    FIXED SPEED antes de mexer em qualquer slider: com
echo                    aceleracao dinamica nenhum valor fica estavel.
echo                    Deixe Global Sensitivity Sync em OFF enquanto calibra.
echo.
echo   ORDEM QUE FUNCIONA
echo     1 painel, camada 1  -  2 emulador, camada 2  -  3 jogo, camada 3
echo     Nunca mexa nas tres ao mesmo tempo, senao voce nao descobre qual
echo     mudou o que.
echo.
pause
goto menu

:: ===========================================================================
::  AVANCADO
:: ===========================================================================
:avancado
cls
echo  [12] AVANCADO
echo  ======================================================================
echo    [1] Forcar GPU dedicada para o emulador, notebook com 2 GPUs
echo    [2] Desligar otimizacao de tela inteira do executavel do emulador
echo    [3] Fila de entrada de mouse e teclado  [LEIA antes]
echo    [4] LowLevelHooksTimeout  [LEIA antes]
echo    [0] Voltar
echo  ----------------------------------------------------------------------
set "AV="
set /p "AV=  Opcao: "
if "%AV%"=="1" goto gpu_pref
if "%AV%"=="2" goto fso
if "%AV%"=="3" goto queue_info
if "%AV%"=="4" goto hooks
if "%AV%"=="0" goto menu
goto avancado

:gpu_pref
cls
echo  Cole o caminho completo do executavel principal do emulador. Exemplo:
echo    C:\Program Files\BlueStacks_nxt\HD-Player.exe
set "EXE="
set /p "EXE=  Caminho: "
if not defined EXE (pause & goto avancado)
set "EXE=!EXE:"=!"
if not exist "!EXE!" (echo  [ERRO] Nao existe: !EXE! & pause & goto avancado)
reg add "HKCU\Software\Microsoft\DirectX\UserGpuPreferences" /v "!EXE!" /t REG_SZ /d "GpuPreference=2;" /f >nul 2>&1
if errorlevel 1 (echo  [ERRO] Falha ao gravar.) else (echo  [OK] !EXE! marcado para usar a GPU de alto desempenho.)
pause
goto avancado

:fso
cls
set "EXE2="
set /p "EXE2=  Caminho do executavel do emulador: "
if not defined EXE2 (pause & goto avancado)
set "EXE2=!EXE2:"=!"
if not exist "!EXE2!" (echo  [ERRO] Nao existe: !EXE2! & pause & goto avancado)
reg add "HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers" /v "!EXE2!" /t REG_SZ /d "~ DISABLEDXMAXIMIZEDWINDOWEDMODE" /f >nul 2>&1
if errorlevel 1 (echo  [ERRO] Falha ao gravar.) else (echo  [OK] Otimizacao de tela inteira desligada para !EXE2!.)
pause
goto avancado

:queue_info
cls
echo  FILA DE ENTRADA: MouseDataQueueSize e KeyboardDataQueueSize
echo  ----------------------------------------------------------------------
echo  Voce vai ver em muito guia por ai a dica "baixe para 8 ou 16 que reduz
echo  input lag". Isso NAO e verdade. Esses valores definem o tamanho do buffer
echo  de eventos do driver mouclass e kbdclass, e o padrao do Windows e 100.
echo.
echo  Diminuir o buffer nao acelera nada: ele comeca a DESCARTAR eventos. Da a
echo  sensacao de resposta mais seca, mas na pratica causa perda de clique e
echo  queda de quadros. Por isso este painel NAO altera esse valor.
echo.
echo  Se ainda assim quiser testar, os caminhos sao:
echo    HKLM\SYSTEM\CurrentControlSet\Services\mouclass\Parameters
echo    HKLM\SYSTEM\CurrentControlSet\Services\kbdclass\Parameters
echo  E volte para 100 se sentir qualquer perda de clique.
echo.
pause
goto avancado

:hooks
cls
echo  LowLevelHooksTimeout define quanto tempo o Windows espera um gancho de
echo  teclado ou mouse de terceiros responder antes de ignora-lo. Reduzir ajuda
echo  quando ha software de macro ou RGB segurando o input, e pode quebrar esses
echo  programas. Padrao do Windows: o valor nao existe e o sistema usa o interno.
echo.
set "HK="
set /p "HK=  Valor em ms, por exemplo 100, ou 0 para remover: "
if not defined HK (pause & goto avancado)
echo !HK!| findstr /r "^[0-9][0-9]*$" >nul 2>&1
if errorlevel 1 (echo  [ERRO] Numero invalido. & pause & goto avancado)
if "!HK!"=="0" (
  reg delete "HKCU\Control Panel\Desktop" /v LowLevelHooksTimeout /f >nul 2>&1
  echo  [OK] Valor removido, volta ao padrao do Windows.
) else (
  call :regset "HKCU\Control Panel\Desktop" "LowLevelHooksTimeout" REG_DWORD "!HK!"
)
set "REBOOT=1"
pause
goto avancado

:: ===========================================================================
::  RESTAURAR
:: ===========================================================================
:restaurar
cls
echo  [13] RESTAURAR
echo  ======================================================================
echo    [1] Importar o ultimo backup feito pelo painel  [mais fiel]
echo    [2] Importar outro backup da pasta
echo    [3] Voltar aos padroes do Windows sem usar backup
echo    [0] Voltar
echo  ----------------------------------------------------------------------
set "RS="
set /p "RS=  Opcao: "
if "%RS%"=="1" goto restaurar_ultimo
if "%RS%"=="2" goto restaurar_escolher
if "%RS%"=="3" goto restaurar_padrao
if "%RS%"=="0" goto menu
goto restaurar

:restaurar_ultimo
if not exist "%BKROOT%\ultimo.txt" (echo  [ERRO] Nenhum backup registrado. & pause & goto restaurar)
set /p ULTBK=<"%BKROOT%\ultimo.txt"
set "BKF=%BKROOT%\!ULTBK!"
if not exist "!BKF!" (echo  [ERRO] Pasta nao encontrada: !BKF! & pause & goto restaurar)
call :restaurar_importar
pause
goto menu

:restaurar_escolher
echo.
echo  Pastas disponiveis em %BKROOT% :
dir /b /ad "%BKROOT%" 2>nul
set "BKN="
set /p "BKN=  Digite o nome da pasta: "
if not defined BKN (pause & goto restaurar)
set "BKF=%BKROOT%\!BKN!"
if not exist "!BKF!" (echo  [ERRO] Nao existe. & pause & goto restaurar)
call :restaurar_importar
pause
goto menu

:restaurar_importar
echo.
set "N=0"
for %%F in ("!BKF!\*.reg") do (
  reg import "%%F" >nul 2>&1
  if not errorlevel 1 set /a N+=1
)
echo  [OK] !N! arquivo de registro importado de !BKF!
call :adb_detectar
if exist "!BKF!\adb-pointer_speed.txt" if defined DEV (
  set /p OLDPS=<"!BKF!\adb-pointer_speed.txt"
  "%ADB%" -s !DEV! shell settings put system pointer_speed !OLDPS! >nul 2>&1
  echo  [OK] pointer_speed do Android devolvido para !OLDPS!
)
if exist "!BKF!\plano-energia.txt" echo  [i] O plano de energia anterior esta em plano-energia.txt
set "REBOOT=1"
call :aviso_reboot
goto :eof

:restaurar_padrao
cls
echo  Vai aplicar os valores padrao do Windows sem usar backup.
set "RP="
set /p "RP=  Confirmar? [S/N]: "
if /i not "%RP%"=="S" (pause & goto restaurar)
call :regset "HKCU\Control Panel\Mouse" "MouseSensitivity" REG_SZ "10"
call :regset "HKCU\Control Panel\Mouse" "MouseSpeed" REG_SZ "1"
call :regset "HKCU\Control Panel\Mouse" "MouseThreshold1" REG_SZ "6"
call :regset "HKCU\Control Panel\Mouse" "MouseThreshold2" REG_SZ "10"
call :regset "HKCU\Control Panel\Keyboard" "KeyboardDelay" REG_SZ "1"
call :regset "HKCU\Control Panel\Keyboard" "KeyboardSpeed" REG_SZ "31"
call :regset "HKCU\System\GameConfigStore" "GameDVR_Enabled" REG_DWORD "1"
call :regset "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR" "AppCaptureEnabled" REG_DWORD "1"
reg delete "HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR" /v AllowGameDVR /f >nul 2>&1
call :regset "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" "NetworkThrottlingIndex" REG_DWORD "10"
call :regset "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" "SystemResponsiveness" REG_DWORD "20"
call :regset "HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl" "Win32PrioritySeparation" REG_DWORD "2"
reg delete "HKCU\Control Panel\Desktop" /v LowLevelHooksTimeout /f >nul 2>&1
powercfg -setactive 381b4222-f694-41f0-9685-ff5bb260df2e >nul 2>&1
echo  [OK] Padroes do Windows restaurados.
set "PERFIL=padrao"
call :aviso_reboot
pause
goto menu

:: ===========================================================================
::  SUBROTINAS
:: ===========================================================================
:regset
:: %1 = chave  %2 = valor  %3 = tipo  %4 = dado
reg add "%~1" /v "%~2" /t %~3 /d "%~4" /f >nul 2>&1
if errorlevel 1 (echo    [ERRO] %~2) else (echo    [OK]   %~2 = %~4)
goto :eof

:aviso_reboot
if not "%REBOOT%"=="1" goto :eof
echo.
echo  ======================================================================
echo    REINICIE O WINDOWS - as mudancas de mouse, teclado e sistema so valem
echo    depois de logoff ou reboot.
echo  ======================================================================
set "REBOOT=0"
goto :eof

:: ===========================================================================
:fim
echo.
if "%REBOOT%"=="1" echo  Lembrete: reinicie o Windows para aplicar as mudancas.
echo  Ate a proxima.
echo.
endlocal
exit /b 0
