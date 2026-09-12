# Nexus Optimizer

Suite de otimização e manutenção para **Windows 10/11** — *otimização mensurável, reversível e transparente*.

> **Estado atual: Patch 0 (Auditoria) + Patch 1 (Fundação) concluídos.**
> Detalhes em [`docs/patches/`](docs/patches/). Roadmap de patches abaixo.

## Princípios (inegociáveis)

- **Zero dados simulados** — todo o valor apresentado vem de WMI / PerformanceCounter / Registry / DriveInfo. Sem fonte real → `N/D`, nunca um número "bonito".
- **Reversibilidade total** — cada alteração tem backup + rollback funcional (pipeline `Backup → Apply → History → Rollback`).
- **Transparência** — cada tweak documenta o que faz, porquê é seguro e como reverte.
- **CommandExecutor com whitelist** — nenhum comando de sistema roda sem autorização explícita em código (audítada com testes).
- **Modo Simples vs. Avançado** — o Simples esconde *tudo* o que é marcado como Avançado (navegação, recomendações, pesquisa).

## Arquitetura (Clean Architecture, .NET 8 / C# 12)

| Projeto | Camada | Conteúdo |
|---|---|---|
| `src/Nexus.Domain` | Domain | Entidades, ports (interfaces), regras puras — zero dependências externas |
| `src/Nexus.Application` | Application | Use cases, `ScoreEngine`, `RecommendationsEngine`, orquestrador `Backup→Apply→History→Rollback`, tarefas |
| `src/Nexus.Infrastructure` | Infrastructure | WMI, PerformanceCounter, Registry, CommandExecutor whitelisted, EF Core/SQLite, Serilog |
| `src/Nexus.Presentation` | Presentation | WPF (MVVM + CommunityToolkit.Mvvm), tema dark-neon, title bar customizado, LiveCharts2 |
| `tests/Nexus.Tests` | Tests | Unitários (xUnit) — Domain/Application/Infrastructure mockável |

## Compilar, testar, correr

```bash
dotnet build NexusOptimizer.sln        # .NET 8 SDK (Windows, Linux ou macOS)
dotnet test tests/Nexus.Tests          # testes correm em qualquer OS
# Windows:
dotnet run --project src/Nexus.Presentation
```

> A app (WPF) **corre apenas em Windows**. Em Linux/macOS o build é cross-compile
> (`EnableWindowsTargeting`) — as fontes de sistema degradam honestamente para `N/D`.
> Ver `docs/DEVELOPMENT.md`.

## Roadmap de patches

| Patch | Conteúdo | Estado |
|---|---|---|
| **0** | Auditoria + decisões de arquitetura | ✅ |
| **1** | Fundação: design system, shell, notificações tipadas, Dashboard (score + métricas reais + tendência + recomendações), pipeline completo, 1ª tarefa real (telemetria), testes | ✅ |
| **2** | Serviços reais (WMI + sc.exe) + Otimização Rápida completa com benchmark A/B | ⏳ |
| **3** | Gaming Center / Emuladores (deteção real + afinidade de CPU) | ⏳ |
| **4** | Startup Manager + Limpeza Avançada (preview obrigatório) | ⏳ |
| **5** | Monitor honesto (métricas ao vivo completas, incl. rede) | ⏳ |
| **6** | Rede, Personalização, Ferramentas Rápidas, Configurações completas | ⏳ |
| **7** | Relatórios/Histórico (UI) + exportação PDF/CSV | ⏳ |

## Documentação

- [`docs/DECISIONS.md`](docs/DECISIONS.md) — decisões de arquitetura (ADRs)
- [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md) — toolchain, build, testes
- [`docs/patches/PATCH-00-AUDITORIA.md`](docs/patches/PATCH-00-AUDITORIA.md)
- [`docs/patches/PATCH-01-FUNDACAO.md`](docs/patches/PATCH-01-FUNDACAO.md)

## Disclaimer

Ferramenta de sistema: qualquer alteração ao Windows pode ter efeitos. Todos os tweaks
deste projeto têm backup/rollback e documentação, mas use por sua conta e risco.
Projeto independente — sem afiliação com a Microsoft.
