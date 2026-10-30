# bsboost — DLL de desempenho para BlueStacks 5 / Free Fire

DLL nativa **x64** (C, Win32 puro) + CLI + script de configuração, para deixar o
Free Fire mais leve dentro do BlueStacks 5.

> **O que esta DLL não faz:** ela não é injetada no `HD-Player.exe`, não hooka
> `d3d11.dll`, não patcharia memória do jogo e não escreve nada dentro da pasta do
> emulador. Ela recebe um `HANDLE` de processo e usa **API pública do Windows**
> (`SetPriorityClass`, `SetProcessAffinityMask`, `SetProcessInformation`,
> `SetProcessDefaultCpuSets`, `RegSetValueExW`) para ajustar o processo **por fora**.

---

## Por que não injeção

Três motivos, em ordem de importância prática:

1. **Não acelera.** O FPS do Free Fire no BlueStacks é limitado por GPU, virtualização
   (VT-x/AMD-V + WHPX), resolução da instância, núcleos/RAM alocados e limite de FPS
   do próprio emulador. Hookar `Present()` ou `QueryPerformanceCounter` dentro de um
   processo não aumenta um único frame — na melhor das hipóteses remove o vsync, o que
   já é uma opção do BlueStacks (`enable_vsync=0`).
2. **Anti-cheat.** Injeção de módulo + hooks de IAT/inline é exatamente a assinatura que
   anti-cheat de jogo competitivo procura. O ganho mensurável é zero; o risco de ban
   não é.
3. **É o vetor de malware nº 1** disfarçado de "FPS booster". Buildar e assinar uma
   DLL desse tipo treina todo mundo a confiar no formato errado.

O que **de fato** decide o desempenho é o conjunto abaixo — e é o que este
diretório implementa.

---

## Conteúdo

| Arquivo | O que é |
|---|---|
| `bsboost.h` | API pública (`__stdcall`, exportada pelo `.def`, ABI versionada) |
| `bsboost.c` | Implementação: prioridade, afinidade, CPU Sets, EcoQoS, memória, GPU pref |
| `bsboost.def` | Lista de exports (`bsboost.dll`) |
| `host/bsboostctl.c` | CLI: `apply`, `watch`, `restore`, `status`, `diag`, `gpu-pref` |
| `Optimize-BlueStacks.ps1` | Configura `bluestacks.conf` + preferência de GPU + plano de energia, com backup/dry-run/rollback |
| `build.ps1` | Compila com MSVC (`cl.exe`) sem precisar de CMake |
| `CMakeLists.txt` | Alternativa de build |

---

## O que a DLL aplica

| Ajuste | API | Por que ajuda |
|---|---|---|
| `ABOVE_NORMAL_PRIORITY_CLASS` | `SetPriorityClass` | O emulador compete com navegador/overlays; `HIGH` costuma piorar (starva o compositor e o áudio) |
| Afinidade explícita | `SetProcessAffinityMask` | Evita que o processo migre para núcleos E-core lentos |
| CPU Sets nos P-cores | `SetProcessDefaultCpuSets` | Em CPU híbrida (Intel 12ª geração+), prende as threads do render no núcleo rápido |
| Desliga EcoQoS | `SetProcessInformation(ProcessPowerThrottling)` | Sem isso o Windows rebaixa o processo para baixo consumo quando a janela não está em foco — queda direta de FPS |
| Prioridade de memória | `SetProcessInformation(ProcessMemoryPriority)` | Reduz trim de working set durante stutter de textura |
| GPU de alto desempenho | `HKCU\...\DirectX\UserGpuPreferences` | Em notebook com iGPU+dGPU, força o `HD-Player.exe` na dedicada |

O modo `BsBoostApplySelf` existe para o caso em que **o próprio processo** carrega a
DLL (`LoadLibrary`) — aí ele pode chamar `timeBeginPeriod(1)` e subir a prioridade da
thread. Isso é o processo ajustando a si mesmo, não código estrangeiro entrando nele.

---

## Build

### Windows (MSVC x64)

```powershell
cd tools\bsboost
powershell -ExecutionPolicy Bypass -File .\build.ps1
# artefatos em tools\bsboost\bin\
```

Ou com CMake:

```powershell
cmake -S . -B build -G "Visual Studio 17 2022" -A x64
cmake --build build --config Release
```

### Verificação cruzada em Linux (CI)

O repositório tem um script que compila a DLL e o CLI com `zig cc`
(`x86_64-windows-gnu`) para pegar erro de compilação/link e warnings sem precisar de
Windows. Útil como smoke test rápido:

```bash
./verify-linux.sh
```

O workflow pronto está em `ci/bsboost-workflow.yml`. Copie para
`.github/workflows/bsboost.yml` com uma conta que tenha permissão de `workflows`
(o token do agente desta sessão não tem, por isso o arquivo fica fora do
`.github/`).

---

## Uso

```powershell
# 1) O que vai mudar (não escreve nada)
.\Optimize-BlueStacks.ps1 -DryRun

# 2) Aplicar (fecha o BlueStacks antes; ele relê o conf ao iniciar)
.\Optimize-BlueStacks.ps1 -Preset balanced

# 3) Emulador com 5 GB de RAM
.\Optimize-BlueStacks.ps1 -RamGb 5
#    ou, em MB:  .\Optimize-BlueStacks.ps1 -RamMb 4096
#    todas as instâncias, sem mexer no resto:  .\Optimize-BlueStacks.ps1 -RamGb 5 -Instance Rvc64

# 4) Subir a prioridade do emulador já aberto e vigiar reaberturas
.\bsboostctl.exe apply
.\bsboostctl.exe watch --interval 2000

# 5) Conferir / desfazer
.\bsboostctl.exe status
.\bsboostctl.exe restore
.\Optimize-BlueStacks.ps1 -Rollback
```

Presets do script:

| Preset | Resolução | DPI | FPS | Uso |
|---|---|---|---|---|
| `low` | 960×540 | 200 | ≤30 | PC fraco, 4–8 GB, iGPU |
| `balanced` | 1280×720 | 240 | ≤60 | padrão — melhor relação nitidez/carga |
| `quality` | 1600×900 | 240 | ≤60 | GPU dedicada com folga |

O script só altera **chaves que já existem** no `bluestacks.conf` — nada de inventar
nome de opção. Ele guarda backup do arquivo inteiro em
`%ProgramData%\BsBoost\backup\` e um `state.json` com cada mudança, para rollback exato.

### RAM da instância (5 GB, por exemplo)

A RAM da VM **não** é ajustável pela DLL: ela é um parâmetro do hipervisor lido na
inicialização da instância (`bst.instance.<Nome>.ram`, em MB). Por isso o caminho é o
script, e o BlueStacks precisa ser **fechado por completo** e reaberto.

```powershell
.\Optimize-BlueStacks.ps1 -RamGb 5 -DryRun   # mostra 4096 -> 5120
.\Optimize-BlueStacks.ps1 -RamGb 5          # aplica
```

Detalhe que importa: a UI do BlueStacks 5 limita a RAM da instância a **4096 MB**;
acima disso o único caminho é o `bluestacks.conf` — que é exatamente o que este script
faz. O valor é em **MB**, sem sufixo (`ram="5120"`).

Guardrails embutidos:

| Situação | O que o script faz |
|---|---|
| 5 GB num PC de 16 GB | aplica direto (sobra ~11 GB para o host) |
| valor maior que `RAM total − 2 GB` | reduz e avisa — o Windows precisa respirar |
| valor abaixo de 2 GB | sobe para 2048 MB (mínimo do BlueStacks) |
| acima de 6 GB com host < 16 GB | avisa sobre swap |
| chave `ram` ausente no conf | não inventa nome de opção; lista as chaves de memória encontradas e manda usar *Configurações → Avançado → Memória* |

Depois de aplicar, o arquivo é relido do disco e cada chave alterada é conferida
(`verificado: bst.instance.Rvc64.ram = 5120`).

---

## O que o script NÃO faz (e por quê)

- **Não troca o renderer sozinho.** DirectX × Vulkan × OpenGL depende do driver da sua
  GPU. Trocar no escuro pode piorar. Faça no UI: *Settings → Graphics*.
- **Não mexe em Hyper-V/WHPX.** Desligar Hyper-V "para ganhar FPS" quebra WSL, Docker e
  o próprio WHPX que o BlueStacks usa. Se o objetivo é desempenho, mantenha ligado.
- **Não mexe em `MMCSS`, `TimerResolution` global, `LargeSystemCache`.** Mitos que, na
  melhor das hipóteses, não fazem nada.
- **Não instala overlay, não abre porta, não faz telemetria.** Nenhuma chamada de rede
  no código.

---

## Se ainda estiver pesado

Nessa ordem, do maior ganho para o menor:

1. **VT-x/AMD-V ligado na BIOS** e *Virtualization* habilitada no BlueStacks. Sem isso
   nada mais importa.
2. **Resolução da instância** — 1280×720 em vez de 1920×1080 costuma dobrar o FPS.
3. **Renderer**: teste DirectX e Vulkan no seu driver; fique com o mais estável.
4. **Núcleos/RAM** da instância: 4 vCPU / 4 GB é o ponto doce. Mais que isso rouba do host.
5. **In-game**: sombras OFF, anti-aliasing OFF, qualidade baixa/média, limite de FPS igual
   ao refresh do monitor.
6. **Fechar** navegador, Discord com overlay e OBS. Com 8 GB, isso é swap garantido.

---

## Aviso

Ferramenta de configuração, não de modificação de jogo. Ela não altera o Free Fire, não
dá vantagem injusta e não interfere no anti-cheat — e é assim que deve continuar. Se
algum dia alguém pedir para "só injetar uma DLL no processo", a resposta é a mesma deste
README: não acelera, e é o formato que o anti-cheat pune.
