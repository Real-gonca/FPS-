# FPS-

As otimizações num PC melhoram o seu desempenho e eficiência. Basicamente, elas ajudam o computador a funcionar mais rápido, de forma mais suave e a usar os seus recursos (como memória e processador) de maneira mais inteligente.

## Conteúdo

### 1. HL Optimizer — kit pronto para usar

Pasta **[HL-Optimizer/](HL-Optimizer/)** — marca e proprietária: **HL** · versão 1.0.0 · `FPS / Games / Emulators`.

| Arquivo | Função |
|---|---|
| [HL-Optimizer.ps1](HL-Optimizer/HL-Optimizer.ps1) | Script principal com **painel de início** da marca e 11 módulos (62 funções). |
| [HL-Optimizer.cmd](HL-Optimizer/HL-Optimizer.cmd) | Duplo clique → pede admin e abre o painel. |
| [HL-Simular.cmd](HL-Optimizer/HL-Simular.cmd) | Duplo clique → **DryRun**: mostra tudo sem alterar nada. |
| [HL-Reverter.cmd](HL-Optimizer/HL-Reverter.cmd) | Duplo clique → restaura o backup mais recente. |
| [README-HL.md](HL-Optimizer/README-HL.md) | Manual: menu, perfis, segurança, troubleshooting. |

Recursos: perfis **Safe → Balanced → Extreme**, `-DryRun`, backup JSON/CSV de cada mudança, verificação `antes → depois`, reversão total, lista de proteção que aborta o script, diagnóstico do sistema, benchmark rápido, sessão de jogo com boost temporário, limpeza segura de temporários/cache de shader, foco em emuladores (BlueStacks, LDPlayer, MuMu, MEmu, PCSX2, Dolphin, RPCS3, Ryujinx).

### 2. PROMPT_OTIMIZACAO_PC.md — o prompt que gera o kit

**[PROMPT_OTIMIZACAO_PC.md](PROMPT_OTIMIZACAO_PC.md)** — spec completa para gerar (ou regenerar/auditar) o kit com qualquer IA de código:

- **Marca e painel de início obrigatórios** (HL Optimizer): 8 badges de saúde, layout adaptativo, os 6 lugares onde a marca aparece, encoding UTF-8 com BOM.
- **Lista de 45+ funções obrigatórias** agrupadas por responsabilidade (UI, backup, serviços, registro, detecção, ações, módulos).
- **5 camadas de ações**: diagnóstico → seguro → equilibrado → agressivo/experimental → boost temporário.
- **Base técnica** (Anexo A): ordem real de impacto, chaves de registro, tabela de serviços, lista de "nunca desativar" e lista de placebos proibidos.
- **Prompt de auditoria** (Anexo D): 12 checagens PASS/FAIL para revisar o código gerado.

> Aviso: altera serviços, registro e plano de energia. Rode sempre `-DryRun` primeiro, com ponto de restauração, e meça com frame time / 1% low (CapFrameX, PresentMon).
