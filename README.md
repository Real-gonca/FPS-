# HL Optimizer Pro — PowerShell Edition

Otimizador de desempenho para Windows, com interface desktop (tema navy + neon) e um pipeline **sempre reversível**.

As otimizações num PC melhoram o desempenho e a eficiência — mas **nenhum número nesta app é decorativo**. Score, RAM libertada, espaço, processos e uptime vêm de medições reais antes/depois.

## O que está incluído

| Área | Função |
|---|---|
| Tiering Engine | PC Fraco / Médio / Potente a partir de RAM, núcleos e VRAM medidas |
| ScoreEngine | 0–100 ponderado (RAM, disco, CPU, processos, startup, privacidade) |
| Catálogo | Padrão, Avançado, Privacidade, Debloat — benchmark de categorias Sparkle/WinUtil, código original |
| Presets | Essencial / Recomendado / Avançado cruzados com o tier |
| Pipeline | Restore point da app + histórico + rollback visível |
| Ferramentas | Limpeza, RAM Booster, Game Booster, DNS, Telemetria, SFC/DISM/CHKDSK (Hail Mary) |

No Windows, o executor (`scripts/windows/Apply-HLTweaks.ps1`) só aceita tipos na whitelist e cria um restore point nativo antes de aplicar. Neste host Linux a UI corre contra métricas reais do kernel (`/proc`, `os`, `statfs`); tweaks de registry/sc/bcdedit ficam no snapshot reversível da app — **não são fingidos como aplicados no SO**.

## Arranque

```bash
npm install
npm test
npm run dev      # API em 0.0.0.0:8787
npm run dev:ui   # UI em 0.0.0.0:5173
```

## Regras de produto

- Sem dados simulados para impacto
- Tudo reversível, visível em Relatórios
- Elevação refletida com honestidade
- Telemetria da própria app desligada por defeito
