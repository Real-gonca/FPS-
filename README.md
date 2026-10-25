# FPS-

As otimizações num PC melhoram o seu desempenho e eficiência. Basicamente, elas ajudam o computador a funcionar mais rápido, de forma mais suave e a usar os seus recursos (como memória e processador) de maneira mais inteligente.

## Conteúdo

### 1. HL Optimizer v2 — kit pronto para usar

Pasta **[HL-Optimizer/](HL-Optimizer/)** — marca e proprietária: **HL** · v2.0.0 · `FPS / Games / Emulators`.
**3.340 linhas · 119 funções · 51 tweaks · 58 apps · 5 presets.**

| Arquivo | Função |
|---|---|
| [HL-Optimizer.ps1](HL-Optimizer/HL-Optimizer.ps1) | Motor completo + **painel de início** da marca + 9 módulos. |
| [HL-Optimizer.cmd](HL-Optimizer/HL-Optimizer.cmd) | Duplo clique → UAC → painel/menu. |
| [HL-Simular.cmd](HL-Optimizer/HL-Simular.cmd) | Duplo clique → **DryRun**: mostra tudo sem alterar nada. |
| [HL-Catalogo.cmd](HL-Optimizer/HL-Catalogo.cmd) | Lista os 51 tweaks com o estado atual. |
| [HL-Reverter.cmd](HL-Optimizer/HL-Reverter.cmd) | Restaura o backup mais recente. |
| [README-HL.md](HL-Optimizer/README-HL.md) | Manual completo: menu, catálogo, segurança, troubleshooting. |

**Módulos:** perfis 1-clique (Gamer/Emulador/Privacidade/Minimalista/Notebook) · motor de tweaks data-driven com undo individual · instalador winget (58 apps) · Windows Update via COM · startup/tarefas/Hyper-V/WSL/WSA · DNS · diagnóstico + benchmark comparativo · relatório HTML · config export/import · foco em emuladores · sessão de jogo com boost temporário · limpeza segura.

**Segurança:** backup JSON/CSV de cada mudança, verificação `antes → depois`, lista de proteção que aborta o script, ponto de restauração, `-DryRun`, reversão total e por tweak.

### 2. PROMPT_OTIMIZACAO_PC.md — a spec que gera/audita o kit

**[PROMPT_OTIMIZACAO_PC.md](PROMPT_OTIMIZACAO_PC.md)** — prompt completo para gerar (ou regenerar/auditar) o kit com qualquer IA de código:

- **Marca e painel de início obrigatórios** (HL Optimizer): 8 badges de saúde, layout adaptativo, os 6 lugares onde a marca aparece, encoding UTF-8 com BOM.
- **Lista de 45+ funções obrigatórias** agrupadas por responsabilidade.
- **Arquitetura v2** (Anexo E): catálogo de tweaks, engine, catálogo de apps, Windows Update, undo, config, relatórios.
- **Base técnica** (Anexo A): ordem real de impacto, chaves de registro, tabela de serviços, lista de "nunca desativar" e placebos proibidos.
- **Prompt de auditoria** (Anexo D): 12 checagens PASS/FAIL para revisar o código gerado.

> Aviso: altera serviços, registro, plano de energia e recursos do Windows. Rode sempre `-DryRun` primeiro, com ponto de restauração, e meça com frame time / 1% low (CapFrameX, PresentMon).
