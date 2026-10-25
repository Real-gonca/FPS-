# HL Optimizer

**Marca / proprietária deste arquivo: HL**
**Versão:** 1.0.0 · build 2026.10 · `FPS / Games / Emulators`
**Motor:** Windows PowerShell 5.1 nativo — offline, sem downloads, sem módulos externos. **62 funções**, 11 módulos.

Kit de otimização para **Windows 10/11 x64** focado em FPS médio, **1% low (stutter)** e tempo de carregamento em **jogos** e **emuladores** (Android: BlueStacks 5, LDPlayer 9, MuMu 12, MEmu · Console: PCSX2, Dolphin, RPCS3, Ryujinx).

> Filosofia: **inventaria → mostra → aplica → verifica → registra → reverte**.
> Nada de "debloat" cego, nada de desativar o Defender inteiro, nada de HPET/timers mágicos.

---

## 1. Arquivos

| Arquivo | Para que serve |
|---|---|
| `HL-Optimizer.ps1` | Script principal. **Painel de início** + 9 módulos. |
| `HL-Optimizer.cmd` | Duplo clique: pede admin (UAC) e abre o **painel**. |
| `HL-Reverter.cmd` | Duplo clique: **restaura** o backup mais recente. |
| `HL-Simular.cmd` | Duplo clique: **DryRun** do perfil Equilibrado (não altera nada). |
| `_logs\` | Logs e relatórios (criado automaticamente). |
| `_HL-Backup\` | Backups `state.json` / `state.csv` (criado automaticamente). |

Mantenha os 4 arquivos **na mesma pasta**.

---

## 2. Como usar

### Jeito seguro (recomendado)

1. Duplo clique em **`HL-Simular.cmd`** → leia o que *seria* feito (nada é alterado).
2. Duplo clique em **`HL-Optimizer.cmd`** → o painel abre com o estado do seu PC.
3. Escolha **[1] Diagnóstico** primeiro, veja o gargalo provável.
4. Escolha **[3] Perfil Equilibrado**. Confirme.
5. Feche o jogo/emulador e reinicie se o resumo disser que é necessário.
6. Não gostou? **`HL-Reverter.cmd`** devolve tudo.

### Linha de comando

```powershell
.\HL-Optimizer.ps1                              # painel interativo
.\HL-Optimizer.ps1 -DryRun                      # simula (não altera)
.\HL-Optimizer.ps1 -Profile Safe -NonInteractive
.\HL-Optimizer.ps1 -Profile Balanced -EmulatorFocus
.\HL-Optimizer.ps1 -Profile Extreme -IncludeExperimental
.\HL-Optimizer.ps1 -Revert                      # desfaz o último backup
.\HL-Optimizer.ps1 -Revert -BackupPath ".\_HL-Backup\2026-10-25_14-32-07"
```

Parâmetros: `-Profile Safe|Balanced|Extreme` · `-EmulatorFocus` · `-IncludeExperimental` · `-Revert` · `-BackupPath` · `-DryRun` · `-NonInteractive` · `-SkipRestorePoint` · `-NoPanel` · `-LogDir`.

---

## 3. O painel de início (branding)

Ao abrir, o HL Optimizer mostra o painel da marca com **badges de saúde do sistema**, em verde/amarelo/vermelho:

```
╔══════════════════════════════════════════════════════════════════════════╗
║                        H  L     O  P  T  I  M  I  Z  E  R                ║
║              FPS / Games / Emulators   |   v1.0.0 build 2026.10          ║
╟──────────────────────────────────────────────────────────────────────────╢
║  Host: SEU-PC  |  User: voce                                             ║
║  OS  : Windows 11 Pro  |  Build: 22631                                   ║
╟──────────────────────────────────────────────────────────────────────────╢
║  ▪ Virtualizacao (VT-x/SVM) ..... LIGADA                                 ║
║  ▪ Hyper-V / hypervisor ......... DESLIGADO                              ║
║  ▪ VBS / HVCI (Isolamento) ...... ATIVO (-5..15% CPU)                    ║
║  ▪ Game DVR / Capturas .......... ATIVO (-5..15%)                        ║
║  ▪ Plano de energia ............. Equilibrado                            ║
║  ▪ Disco do sistema (C:) ........ SSD                                    ║
║  ▪ Emuladores detectados ........ BlueStacks 5                           ║
║  ▪ Exclusoes no Defender ........ nenhuma                                ║
╟──────────────────────────────────────────────────────────────────────────╢
║  Backup .......... nenhum backup ainda                                   ║
║  Sessao .......... 2026-10-25_14-32-07   |   DryRun: nao                ║
╚══════════════════════════════════════════════════════════════════════════╝
```

---

## 4. Menu

| Opção | O que faz |
|---|---|
| `[1]` Diagnóstico | CPU/RAM/GPU, VT-x, Hyper-V, VBS, Game DVR, HAGS, plano de energia, SSD/HDD, emuladores, exclusões, carga atual (10 s) e top 8 processos. Salva relatório. |
| `[2]` Perfil **Seguro** | Plano Alto desempenho, PCIe ASPM off, USB selective suspend off, Game DVR/Capturas off, telemetria (DiagTrack/dmwappushservice/WerSvc), `VisualFXSetting=2`, `Win32PrioritySeparation=38`. |
| `[3]` Perfil **Equilibrado** | Tudo do Seguro + SysMain (só em SSD), WSearch, Spooler (só sem impressora), Bluetooth (só sem dispositivo), serviços Xbox (só sem Game Pass), serviços de fabricante que causam stutter, limite do Delivery Optimization. |
| `[4]` Perfil **Agressivo** | Equilibrado + hypervisor off (**só se** WSL/Docker/WSA não estiverem em uso, senão recusa), detecta VBS e instrui, HAGS (teste-e-compare), experimental opcional. |
| `[5]` **Foco Emuladores** | Exclusões no Defender, `GpuPreference=2` no .exe do emulador, diagnóstico específico e **checklist** de configuração interna. |
| `[6]` **Sessão de jogo** | Boost temporário: pausa serviços de fabricante, plano Alto desempenho, prioridade `High` no jogo; **restaura tudo ao fechar**. |
| `[7]` Benchmark rápido | 20 amostras de CPU, fila de disco e RAM livre → CSV (não é FPS; para FPS use CapFrameX/PresentMon). |
| `[8]` Self-check | Verifica admin, cmdlets, pastas e ferramentas antes de aplicar. |
| `[9]` **Reverter** | Restaura o backup (serviços, registro, energia, bcdedit, exclusões). |
| `[T]` Limpeza | Temporários do usuário/Windows e cache de shader (NVIDIA/AMD/DirectX) — **pede confirmação** por pasta e mostra o MB liberado. |
| `[L]` / `[B]` | Abre as pastas de logs / backups. |
| `[P]` | Lista do que o HL Optimizer **não** faz (placebos). |
| `[A]` | Sobre / ajuda de linha de comando. |
| `[0]` | Sair. |

---

## 5. Segurança e reversão

- **Backup antes de cada escrita:** `_HL-Backup\<data>\state.json` (+ `state.csv`) com tipo, alvo, valor **antes**, valor **depois**, ganho, risco e dica de reversão.
- **Verificação sempre:** cada mudança é relida; o log mostra `antes -> depois [OK]` ou `[FALHOU]`.
- **Valores criados são removidos** na reversão (não ficam órfãos no registro).
- **Lista de proteção** (`NeverTouch`): `RpcSs`, `DcomLaunch`, `PlugPlay`, `Power`, `AudioSrv`, `AudioEndpointBuilder`, `Dwm`, `EventLog`, `Winmgmt`, `Themes`, `CryptSvc`, `BFE`, `mpssvc`, `Dhcp`, `Dnscache`, `NlaSvc`, `WinDefend`… Se alguém tentar tocar nelas, o script **aborta com código 4**.
- **Ponto de restauração** criado antes de aplicar (se a Proteção do Sistema estiver ativa).
- **Nunca** reinicia o PC sozinho, **nunca** baixa nada, **nunca** desinstala programas.
- **Idempotente:** rodar duas vezes mostra "já aplicado" em vez de refazer.

Códigos de saída: `0` ok · `1` erro · `2` cancelado · `3` sem admin · `4` lista de proteção · `5` falha de backup.

---

## 6. Ordem real de impacto (o que dá FPS de verdade)

| # | Item | Ganho típico |
|---|---|---|
| 1 | VT-x/SVM na BIOS + Hyper-V desligado | **10–100%+** em emulador |
| 2 | Isolamento de núcleo (VBS/HVCI) desligado | 5–15% CPU |
| 3 | Game Bar / Game DVR (gravação em 2º plano) | 5–15% |
| 4 | Config interna do emulador (resolução, núcleos, renderer) | 20–200% |
| 5 | Plano de energia + GPU em desempenho máximo | 3–10% |
| 6 | Overlays (Discord/NVIDIA/Steam/RTSS) | 3–10% |
| 7 | Exclusões no antivírus | 2–10% |
| 8 | Serviços de fabricante (Nahimic, Armoury, iCUE) | 2–6% |
| 9 | SysMain / WSearch / Delivery Optimization | 0–3% |
| 10 | Telemetria, Spooler, Xbox, Bluetooth | ~0% |

O perfil `Safe`/`Balanced` cobre 3, 5, 7, 8, 9. Os itens **1, 2 e 4 dependem de você** (BIOS, UI de Segurança do Windows e configuração do emulador) — o script detecta, mede e instrui, mas não força nada que exija reboot ou que a UI reverta.

---

## 7. O que o HL Optimizer NÃO faz (placebo)

- Desativar 100+ serviços de lista de YouTube → ganho ~0%, risco alto.
- Desativar o Windows Defender inteiro → usamos **exclusões** (mesmo ganho, menos risco).
- Desligar paginação / "limpar RAM" → piora o 1% low.
- HPET / `useplatformclock` / MSI mode / prioridade de IRQ → sem evidência sólida.
- Desinstalar apps do fabricante → só paramos/desabilitamos (reversível).
- Prometer "+200% FPS" → todo ganho é uma **faixa** e depende do hardware.
- Desativar o Modo de Jogo do Windows → na prática ele ajuda.

---

## 8. Problemas comuns

| Sintoma | Causa provável / solução |
|---|---|
| "precisa ser executado como administrador" | Use `HL-Optimizer.cmd` (eleva sozinho) ou clique direito → Executar como administrador. |
| Script não abre ("execução de scripts desabilitada") | Política de execução. O `.cmd` já usa `-ExecutionPolicy Bypass`. Se o TI bloqueia, não há como contornar — peça liberação. |
| Um serviço aparece `[FALHOU]` | Serviço protegido por política ou dependente de outro. Normal em ambiente corporativo. |
| SysMain não foi desativado | Disco do sistema detectado como HDD — é intencional (Superfetch ajuda em HDD). |
| Não consigo desativar o hypervisor | WSL2/Docker/WSA em uso. O script **recusa** por segurança; desinstale/feche e rode de novo. |
| VBS continua ativo | Chaves são protegidas; desligue manualmente em Segurança do Windows → Isolamento do núcleo → Integridade de memória. |
| Depois de reverter, ainda falta reiniciar | `hypervisorlaunchtype` só muda após reboot. |
| Acentos estranhos no console | O `.ps1` está em UTF-8 **com BOM**; não edite em editor que remove o BOM e salva em ANSI. |

---

## 9. Aviso

Uso por sua conta e risco. Sempre teste primeiro com `-DryRun` e, se possível, em VM. Mudanças de virtualização (Hyper-V, VBS) reduzem proteção/recursos do sistema — são **decisões conscientes**, não "otimização gratuita". Meça com **frame time / 1% low** (CapFrameX, PresentMon), 3 rodadas de 60 s na mesma cena, antes e depois.

---

*HL Optimizer v1.0.0 — marca e proprietária: **HL**. Documento de referência: [`../PROMPT_OTIMIZACAO_PC.md`](../PROMPT_OTIMIZACAO_PC.md).*
