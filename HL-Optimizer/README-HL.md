# HL Optimizer v2.0.0

**Marca / proprietária deste arquivo: HL**
**Versão:** 2.0.0 · build `2026.10-r2` · `FPS / Games / Emulators`
**Motor:** Windows PowerShell 5.1 nativo — **offline**, sem downloads, sem módulos externos.
**Tamanho:** 3.340 linhas · **119 funções** · **51 tweaks** · **58 apps** · **5 presets**.

Kit de otimização para **Windows 10/11 x64** com foco em FPS médio, **1% low (stutter)** e tempo de carregamento em **jogos** e **emuladores** (Android: BlueStacks 5, LDPlayer 9, MuMu 12, MEmu · Console: PCSX2, Dolphin, RPCS3, Ryujinx).

> Filosofia: **inventaria → mostra → aplica → verifica → registra → reverte**.
> Nada de "debloat" cego, nada de desativar o Defender inteiro, nada de HPET/timers mágicos.

---

## 1. O que mudou na v2 (vs. v1)

| Área | v1 | **v2** |
|---|---|---|
| Tweaks | ~25 ações fixas dentro dos perfis | **Motor data-driven** com **51 tweaks** em 8 categorias, cada um com ganho, risco, guarda e **undo individual** |
| Apps | nenhum | **58 apps via winget**, com instalado/desinstalar e busca |
| Windows Update | só "pausar" | **busca e instala via COM** (`Microsoft.Update.Session`), pausar 4 semanas, retomar |
| Sistema | — | **startup apps**, **tarefas agendadas de terceiros**, **Hyper-V/VMP/WSL/WSA** (features), **DNS**, **OneDrive** |
| Reversão | revert do backup inteiro | revert + **histórico de backups** + **undo por tweak** + restauração de Appx/tarefas/startup/DNS/features |
| Relatórios | log texto | **relatório HTML** com KPIs, badges, hardware e tabela de alterações |
| Medição | benchmark simples | **benchmark comparativo** (linha de base → depois → delta de CPU/RAM/processos/serviços) |
| Automação | 4 flags | **`-ListTweaks`, `-ListApps`, `-ApplyTweaks`, `-ApplyPreset`, `-ImportConfig`, `-Report`, `-AllowExperimental`** |
| Config | — | **exportar/importar JSON** (leva seu setup para outro PC) |

---

## 2. Arquivos

| Arquivo | Para que serve |
|---|---|
| `HL-Optimizer.ps1` | Motor completo + **painel de início** da marca + 9 módulos. |
| `HL-Optimizer.cmd` | Duplo clique: pede admin (UAC) e abre o **painel/menu**. |
| `HL-Simular.cmd` | Duplo clique: **DryRun** do perfil Equilibrado (não altera nada). |
| `HL-Catalogo.cmd` | Duplo clique: lista todos os **51 tweaks** com estado atual. |
| `HL-Reverter.cmd` | Duplo clique: **restaura** o backup mais recente. |
| `_logs\` | Logs, diagnóstico, benchmark, checklist e **relatório HTML**. |
| `_HL-Backup\` | Backups `state.json` + `state.csv` de cada sessão. |
| `HL-config_*.json` | Config exportada (criada por você). |

Mantenha os `.cmd` e o `.ps1` **na mesma pasta**.

---

## 3. Painel de início (branding)

```
╔════════════════════════════════════════════════════════════════════════════╗
║                     H  L     O  P  T  I  M  I  Z  E  R                     ║
║             FPS / Games / Emulators   |   v2.0.0 build 2026.10-r2          ║
╟────────────────────────────────────────────────────────────────────────────╢
║  Host: MEU-PC  |  User: jogador                                            ║
║  OS  : Windows 11 Pro  |  Build: 22631                                     ║
╟────────────────────────────────────────────────────────────────────────────╢
║  ▪ Virtualizacao (VT-x/SVM) .. LIGADA                                      ║
║  ▪ Hyper-V / hypervisor ...... DESLIGADO                                   ║
║  ▪ VBS / HVCI (Isolamento) ... ATIVO (-5..15% CPU)                         ║
║  ▪ Game DVR / Capturas ....... ATIVO (-5..15%)                             ║
║  ▪ Plano de energia .......... Equilibrado                                 ║
║  ▪ Disco do sistema (C:) ..... SSD                                         ║
║  ▪ Emuladores detectados ..... BlueStacks 5                                ║
║  ▪ Exclusoes no Defender ..... nenhuma                                     ║
╟────────────────────────────────────────────────────────────────────────────╢
║  Backup .......... 2026-10-25_14-32-07                                     ║
║  Sessao .......... 2026-10-25_14-40-11   |   DryRun: nao                   ║
╚════════════════════════════════════════════════════════════════════════════╝
```

Os **8 badges** são coloridos (verde/amarelo/vermelho) e atualizados a cada ação. A marca **HL** aparece em 6 lugares: painel, cabeçalho dos arquivos, título da janela, primeira linha do log, descrição do ponto de restauração e README.

---

## 4. Menu principal

| Opção | Módulo |
|---|---|
| `[1]` **Perfis 1-clique** | HL Gamer · HL Emulador · HL Privacidade · HL Minimalista · HL Notebook + perfis manuais Seguro/Equilibrado/Agressivo |
| `[2]` **Tweaks** | 51 itens em 8 categorias: ver estado, aplicar por número, aplicar `all`, **desfazer (D)**, detalhes (I), busca (S), presets (P) |
| `[3]` **Instalar apps** | 58 apps por categoria, marcação de instalados, busca livre no winget, desinstalar |
| `[4]` **Windows Update** | listar pendentes · instalar todas · pausar 4 semanas · retomar · abrir Configurações |
| `[5]` **Sistema** | startup apps · tarefas de terceiros · **Hyper-V/VMP/WSL/WSA** · info de hardware · DNS · OneDrive · serviços de fabricante |
| `[6]` **Diagnóstico** | diagnóstico completo · benchmark · **benchmark comparativo** · self-check · relatório HTML |
| `[7]` **Emuladores** | exclusões no Defender, GPU por app, diagnóstico e checklist |
| `[8]` **Sessão de jogo** | boost temporário (pausa bloatware, plano alto, prioridade High) e **restaura ao fechar** |
| `[9]` **Reverter** | histórico de backups, restauração seletiva e undo por tweak |
| `[T]` Limpeza | temporários + cache de shader (confirmação por pasta, mostra MB) |
| `[R]` | relatório HTML com KPIs e alterações |
| `[C]` | config export/import (JSON) |
| `[L] [B] [P] [A]` | pastas de logs/backups · placebos · sobre |
| `[0]` | sair |

---

## 5. Catálogo de tweaks (51)

| Categoria | Qtd | Exemplos |
|---|---|---|
| **Gaming** | 8 | Game DVR off (5 regs) · MMCSS GPU Priority 8 · `Win32PrioritySeparation=38` · FSE off · HAGS · mouse sem aceleração |
| **Telemetria** | 10 | AllowTelemetry=0 · DiagTrack/dmwappush/WerSvc · ID de anúncios · histórico · sugestões silenciosas · busca web · 8 tarefas CEIP |
| **Debloat** | 10 | Xbox (5 pacotes) · Bing News/Weather/Sports · 3D Viewer · Solitaire · Teams/Skype/People · Groove/Filmes/Clipchamp · Copilot/IA · 3rd-party OEM |
| **Serviços** | 6 | SysMain (só SSD) · WSearch · Spooler (se não há impressora) · Bluetooth (se não há device) · Xbox services (se não há Game Pass) · 7 inúteis |
| **UI** | 11 | menu de contexto clássico · extensões visíveis · barra à esquerda · sem widgets · Copilot off · **Recall off** · TaskbarEndTask |
| **Boot** | 2 | sem atraso de startup · desligar Fast Startup (corrige bugs de GPU) |
| **Rede** | 2 | throttling multimídia (experimental) · Delivery Optimization sem P2P |
| **Update** | 2 | Store sem auto-update · sem drivers automáticos (experimental) |

Cada tweak carrega **ganho esperado, risco, guarda de contexto** e, quando precisa, **reinício**. Exemplo de guarda: `SysMain` só é aplicado se o disco do sistema for **SSD**; `Spooler` só se **não** houver impressora; serviços Xbox só se **não** houver Game Pass em uso.

### Perfis 1-clique

| Preset | O que faz |
|---|---|
| **HL Gamer** | Game DVR off, MMCSS, prioridade, telemetria, debloat básico, Store sem auto-update |
| **HL Emulador** | Gamer + exclusões no Defender + GPU por app + checklist de emulador |
| **HL Privacidade** | telemetria zero, sem ID de anúncios, sem histórico, sem sugestões, sem busca web |
| **HL Minimalista** | remove apps do Windows, desliga widgets/Copilot (sem mexer em serviços) |
| **HL Notebook** | menos calor/consumo: energia, telemetria, sugestões, boot |

---

## 6. Instalador de apps (58)

Categorias: Navegadores (5) · Gaming (10) · Benchmark (7) · Mídia (8) · Dev (9) · Utilitários (11) · Comunicação (3) · Escritório (3) · Segurança (2).

Usa `winget install --id <id> -e --silent`. Se o ID mudou na sua versão do winget, a falha é registrada no log e **não interrompe** os outros apps. Se o `winget` não existir, o módulo avisa e não faz nada.

---

## 7. Segurança e reversão

- **Backup antes de cada escrita:** `_HL-Backup\<data>\state.json` (+ `state.csv`) com tipo, alvo, valor **antes**, **depois**, ganho, risco e dica de reversão.
- **Verificação sempre:** cada mudança é relida; o log mostra `antes -> depois [OK]` ou `[FALHOU]`.
- **Tipos de reversão suportados:** Registro · Chave criada · Serviço · Plano de energia · `bcdedit` · Exclusão do Defender · **Appx removido** · **Tarefa agendada** · **Startup** · **DNS** · Features (aviso) · Info (aviso).
- **Undo por tweak:** no catálogo, `D 1,3` desfaz só aqueles alvos consultando o backup mais recente que os contenha.
- **Lista de proteção:** `RpcSs`, `DcomLaunch`, `PlugPlay`, `Power`, `AudioSrv`, `AudioEndpointBuilder`, `Dwm`, `EventLog`, `Winmgmt`, `Themes`, `CryptSvc`, `BFE`, `mpssvc`, `Dhcp`, `Dnscache`, `NlaSvc`, `WinDefend`… → se alguém tentar, o script **aborta com código 4**.
- **Ponto de restauração** antes de aplicar (se a Proteção do Sistema estiver ativa).
- **Nunca** reinicia sozinho, **nunca** baixa executáveis, **nunca** desinstala programas.
- **Idempotente:** rodar duas vezes mostra "já aplicado".

Códigos de saída: `0` ok · `1` erro · `2` cancelado · `3` sem admin · `4` lista de proteção · `5` falha de backup.

---

## 8. Linha de comando

```powershell
.\HL-Optimizer.ps1                                   # painel + menu
.\HL-Optimizer.ps1 -DryRun                           # simula tudo (não altera nada)
.\HL-Optimizer.ps1 -ListTweaks                       # catálogo com estado atual
.\HL-Optimizer.ps1 -ListApps                         # catálogo winget
.\HL-Optimizer.ps1 -ApplyTweaks "game-dvr,diagtrack"  # aplica por id
.\HL-Optimizer.ps1 -ApplyPreset gamer                # preset 1-clique
.\HL-Optimizer.ps1 -Profile Balanced -NonInteractive
.\HL-Optimizer.ps1 -EmulatorFocus
.\HL-Optimizer.ps1 -Report                           # gera relatório HTML
.\HL-Optimizer.ps1 -ImportConfig .\HL-config_2026-10-25_14-40.json
.\HL-Optimizer.ps1 -Revert                           # desfaz o último backup
.\HL-Optimizer.ps1 -Revert -BackupPath ".\_HL-Backup\2026-10-25_14-32-07"
.\HL-Optimizer.ps1 -ApplyTweaks "hags-on" -AllowExperimental
```

---

## 9. Ordem real de impacto (não se engane)

| # | Item | Ganho típico | Onde está no HL |
|---|---|---|---|
| 1 | VT-x/SVM na BIOS + Hyper-V desligado | **10–100%+** em emulador | badge + `[5]` Sistema → Recursos |
| 2 | Isolamento de núcleo (VBS/HVCI) off | 5–15% CPU | badge + instrução no perfil Agressivo |
| 3 | Game DVR / capturas | 5–15% | tweak `game-dvr` |
| 4 | Config interna do emulador | 20–200% | `[7]` checklist |
| 5 | Plano de energia + GPU | 3–10% | perfil Seguro + `[7]` GPU por app |
| 6 | Overlays (Discord/NVIDIA/Steam/RTSS) | 3–10% | manual (fora do script) |
| 7 | Exclusões no antivírus | 2–10% | `[7]` e `[5]` |
| 8 | Serviços de fabricante | 2–6% | `[5]` → serviços de fabricante |
| 9 | SysMain/WSearch/DeliveryOpt | 0–3% | tweaks |
| 10 | Telemetria/Spooler/Xbox/Bluetooth | ~0% | tweaks |

Itens **1, 2 e 4 dependem de você** (BIOS, UI de Segurança do Windows, config do emulador) — o HL detecta, mede e instrui, mas não força nada que exija reboot ou que a UI reverte.

---

## 10. O que o HL Optimizer NÃO faz (placebo)

- Desativar 100+ serviços de lista de YouTube → ganho ~0%, risco alto.
- Desativar o Windows Defender inteiro → usamos **exclusões** (mesmo ganho, menos risco).
- Desligar paginação / "limpar RAM" → piora o 1% low.
- HPET / `useplatformclock` / MSI mode / prioridade de IRQ → sem evidência sólida.
- Desinstalar apps do fabricante → só paramos/desabilitamos (reversível).
- Prometer "+200% FPS" → todo ganho é uma **faixa** e depende do hardware.
- Desativar o Modo de Jogo do Windows → na prática ele ajuda (temos tweak para **manter ligado**).

---

## 11. Problemas comuns

| Sintoma | Solução |
|---|---|
| "precisa ser executado como administrador" | Use `HL-Optimizer.cmd` (eleva sozinho). |
| "execução de scripts desabilitada" | O `.cmd` já usa `-ExecutionPolicy Bypass`. Se o TI bloqueia por GPO, não há como contornar. |
| Um serviço aparece `[FALHOU]` | Protegido por política ou com dependentes. Normal em ambiente corporativo. |
| `SysMain` não foi desativado | Disco do sistema é HDD — intencional (Superfetch ajuda em HDD). |
| Hyper-V não desativa | WSL2/Docker/WSA em uso. O script **recusa**; feche/desinstale e tente de novo. |
| VBS continua ativo | Chaves protegidas: Segurança do Windows → Isolamento do núcleo → Integridade de memória = Desligado. |
| `winget` não encontrado | Instale o "App Installer" na Microsoft Store. |
| `[3]` Instalar apps falha em algum ID | O ID mudou na sua versão do winget. O log mostra o erro; use a busca `[B]`. |
| Appx não volta no undo | Apps da Store voltam pela própria Store (o backup guarda o nome completo). |
| Acentos estranhos no console | O `.ps1` está em UTF-8 **com BOM**; não salve em ANSI. |
| Update travado em 0% | Use `[4]` → pausar/retomar, ou reinicie o serviço `wuauserv` manualmente. |

---

## 12. Aviso

Uso por sua conta e risco. Teste primeiro com `-DryRun` (ou `HL-Simular.cmd`) e, se possível, em VM. Mudanças de virtualização (Hyper-V, VBS) e de drivers reduzem proteção/recursos do sistema — são **decisões conscientes**, não "otimização gratuita". Meça com **frame time / 1% low** (CapFrameX, PresentMon), 3 rodadas de 60 s na mesma cena.

---

*HL Optimizer v2.0.0 — marca e proprietária: **HL**. Spec do projeto: [`../PROMPT_OTIMIZACAO_PC.md`](../PROMPT_OTIMIZACAO_PC.md).*
