# PATCH 0 — Auditoria (spec §6, passo 1)

> **Data:** 2026-09-12 · **Escopo:** mapear o que existe vs. a especificação,
> listar gaps por módulo, registar decisões.

## 1. Estado do repositório auditado

O repositório `FPS-` no commit `a672e325` contém **apenas** `README.md`
(2 linhas, descrição genérica de "otimizações num PC") e `.git`.

**Conclusão: projeto *greenfield* — não existe código-base para auditar.**
O mapa de gaps é, portanto, 100% da especificação:

| Módulo (spec §4) | Estado encontrado | Gap |
|---|---|---|
| 4.1 Dashboard | inexistente | todo |
| 4.2 Otimização Rápida | inexistente | todo |
| 4.3 Limpeza Avançada | inexistente | todo |
| 4.4 Desempenho / Monitor | inexistente | todo |
| 4.5 Inicialização | inexistente | todo |
| 4.6 Serviços | inexistente | todo |
| 4.7 Privacidade / Telemetria | inexistente | todo |
| 4.8 Gaming Center / Emuladores | inexistente | todo |
| 4.9 Ferramentas Rápidas | inexistente | todo |
| 4.10 Relatórios / Histórico | inexistente | todo (backend do histórico chega no Patch 1) |
| 4.11 Configurações | inexistente | persistência no Patch 1; UI no patch 6 |
| Arquitetura (§2) | inexistente | todo — resolvido no Patch 1 (scaffolding completo) |
| Design system (§3) | inexistente | paleta/componentes resolvidos no Patch 1 |
| Requisitos não-funcionais (§5) | inexistente | aplicados desde o início (whitelist, N/D, logging, testes) |

## 2. Decisões de arquitetura tomadas

Ver [`../DECISIONS.md`](../DECISIONS.md) — destaques:

1. **WPF** (não Avalonia) — produto Windows-only (ADR-001).
2. **Elevação asInvoker + runtime** — a única forma de cumprir "requireAdmin
   *com fallback elegante*" (ADR-002).
3. **TFM**: Domain/Application/Infrastructure em `net8.0` portable →
   testes unitários em CI Linux; Presentation em `net8.0-windows` (ADR-003).
4. **Whitelist em código** (não em config) + execução sem shell (ADR-004).
5. **`N/D` como tipo de dado** (`MetricValue`) — honestidade estrutural (ADR-005).
6. **Sem RAM Booster fictício** — ação real = lista de processos por consumo (ADR-007).

## 3. Restrição de toolchain (declarada explicitamente — spec §0.5)

O ambiente de desenvolvimento com IA (sandbox Linux desta sessão) **não tem
acesso a `nuget.org` nem aos servidores de download da Microsoft** (verificado:
`api.nuget.org`, `dot.net`, `builds.dotnet.microsoft.com` bloqueados; apenas
GitHub/PyPI/NPM alcançáveis) e não existe .NET SDK pré-instalado.

**Consequência declarada:** o build e os testes **não foram executados na
sandbox**. A entrega do Patch 1 inclui:
- código completo (5 projetos + testes), revisto com rigor na ausência de compilador;
- `scripts/verify.ps1` / `scripts/verify.sh` — validação de uma mão na máquina do
  utilizador (build + testes + resumo);
- versões de pacotes pinadas para releases estáveis conhecidas;
- nota: se o build acusar qualquer discrepância de API (ex.: LiveCharts2 rc),
  o erro do compilador indica o membro correto — correção pontual, sem redesign.

## 4. Riscos e mitigações

| Risco | Mitigação |
|---|---|
| WPF cross-compile em Linux pode surfacear diferenças de API | testes unitários nas camadas testáveis; WPF validado no Windows (verify.ps1) |
| Versões rc do LiveCharts2 (rc5.1) | pinada; fallback documentado (`dotnet add package LiveChartsCore.SkiaSharpView.WPF`) |
| WMI lento pode travar a UI | tudo em `Task.Run` + timeouts (2 s) + throttle de temperatura (15 s) |
| Ações de registry sem admin | `RunOptimizationUseCase` bloqueia com notificação (nada executa "de rastejo") |
| SQLite em escrita concorrente (UI + sampler) | stores singleton com scope por operação (uma escrita por operação) |

## 5. Plano de entrega (alinhado com spec §6)

| Patch | Conteúdo | Estado |
|---|---|---|
| 0 | Esta auditoria + decisões | ✅ |
| 1 | Fundação: scaffolding + design system + shell + notificações + Dashboard + pipeline + 1ª tarefa real | ✅ (este patchset) |
| 2 | Serviços reais (WMI + sc.exe) + Otimização Rápida com benchmark A/B | ⏳ |
| 3 | Gaming Center / Emuladores | ⏳ |
| 4 | Startup + Limpeza Avançada (preview) | ⏳ |
| 5 | Monitor honesto completo | ⏳ |
| 6 | Rede, Personalização, Ferramentas, Configurações | ⏳ |
| 7 | Relatórios/Histórico (UI) + exportação PDF/CSV | ⏳ |
