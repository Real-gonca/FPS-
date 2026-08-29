# PATCH-01 — Interface mockup + catálogo Sparkle/WinUtil-grade

## O que muda

Primeira entrega do **HL Optimizer Pro — PowerShell Edition** neste repositório (estava vazio).

1. **Dashboard alinhado ao mockup**
   - Chrome próprio (logo HL em gradiente, título, min/max/fechar)
   - Sidebar com os 11 itens na ordem do mockup + card *Sistema Protegido* ligado a restore points reais da app
   - Painel central: saudação, modos Económico/Balanceado/Desempenho Máximo (default do Tiering Engine, sobreposição do utilizador), botão de admin com estado real de elevação, gauge circular do ScoreEngine, *Otimizar Agora* / *Análise Completa*
   - 4 stat cards: RAM libertada, espaço libertado, processos, uptime — todos de medições
   - Recomendações só quando há sinal medido (bytes de temp, ratio de RAM/disco, contagens)
   - Painel direito: hardware (OS/CPU/RAM/GPU/disco) + grelha 2×3 de Ferramentas Rápidas

2. **Desempenho / Tweaks**
   - Categorias Padrão / Avançado / Privacidade / Debloat com badges de risco
   - Estado aplicado/não aplicado + reverter item a item
   - Dynamic Tick / HPET filtrados fora de PC Potente

3. **Privacidade dedicada**
   - Toggles granulares, lista do que vai ser desativado, painéis nativos documentados (`ms-settings:…`)
   - Sem “tudo ou nada” silencioso

4. **Ferramentas rápidas ligadas a serviços**
   - Limpeza → `CleanupService.Discover()` + preset seguro
   - RAM Booster → trim (não mata processos); impacto = delta medido (pode ser 0)
   - Game Booster → arma/desarma prioridade por tier
   - Internet Booster → DNS Cloudflare/Google/OpenDNS/Quad9/custom; lê `/etc/resolv.conf` de verdade; **nunca** alega +Mbps
   - Telemetria → módulo de privacidade
   - Reparar Sistema → SFC/DISM/CHKDSK como Hail Mary, sem output inventado

5. **Presets × Tiering**
   - Essencial = Padrão
   - Recomendado = Padrão + Privacidade
   - Avançado = tudo, com confirmação item a item para Avançado/Debloat
   - PC Fraco (este host: ~4 GB / 2 núcleos) default Essencial

6. **Regras do Prompt Master anterior**
   - Zero scores/GB/segundos hardcoded
   - Pipeline `IOptimizationTask` com restore point + histórico + rollback
   - Whitelist de tipos de comando
   - Telemetria da app opt-in, default off

## Testes

```
npm test
```

Cobre: tiering, score, presets×tier, pipeline/rollback/whitelist, cleanup com tamanhos reais.

## Como correr

```
npm install
npm test
npm run dev          # API :8787
npm run dev:ui       # UI  :5173 (proxy /api)
```
