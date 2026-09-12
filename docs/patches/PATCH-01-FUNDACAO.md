# PATCH 1 — Fundação (spec §6, passo 2 + scaffolding)

> **Data:** 2026-09-12 · **Branch:** `arena/01a0957f-fps`
> **Entrega:** `deliveries/PATCH-01-FUNDACAO.zip` (ficheiros novos/alterados)

## O que mudou

### 1. Fundação de solução (Clean Architecture, spec §2.1)

- `NexusOptimizer.sln` + 5 projetos (`Nexus.Domain`, `Nexus.Application`,
  `Nexus.Infrastructure`, `Nexus.Presentation`, `Nexus.Tests`);
- `Directory.Build.props` (C# 12, nullable, `EnableWindowsTargeting`);
- DI via **Generic Host** (`Microsoft.Extensions.Hosting`) — a Presentation
  resolve o `MainWindow` do container;
- Serilog estruturado (rotação diária, 7 ficheiros, níveis em `appsettings.json`);
- EF Core + SQLite em `%LOCALAPPDATA%\NexusOptimizer\nexus.db`
  (tabelas: `Actions`, `Backups`, `TelemetrySamples`, `Settings`).

### 2. Design system (spec §3)

- **Paleta canónica exata** da spec §3.1 em `Themes/DarkNeon.xaml`
  (`#0B0E1A→#12172B`, `#161B2E/#232A45`, `#3B82F6/#60A5FA`, `#22D3EE`,
  `#34D399/#FBBF24/#F87171`, `#E5E9F5/#8B93B0`);
- Componentes: `Card`, botões primário/ghost com **glow** em hover, badges de
  risco, seletor de modo segmentado, toggle "Desempenho Máximo", lista de
  navegação, cartões de notificação (glassmorphism subtil, fade-in);
- **Gauge circular** `PerformanceGauge` (arco 270°, cor dinâmica por faixa:
  ≥85 verde, ≥70 ciano, ≥45 âmbar, <45 vermelho; `N/D` sem score real);
- **Title bar customizado** (`WindowChrome`, sem moldura nativa) com marca,
  seletor **Simples/Avançado**, **Modo Desempenho Máximo**, **Executar como
  Admin**, **indicador de score** e botões de janela (spec §3.3);
- **Sidebar** com os 12 módulos da spec §3.3 — os 11 não implementados mostram
  placeholder **honesto** (o que o módulo vai fazer + patch planeado),
  zero dados fictícios.

### 3. Notificações tipadas (spec §3.2)

- `INotificationHub` (Application) → `NotificationCenter` (Presentation):
  empilhadas (máx. 5), 4 tipos (info/sucesso/aviso/erro) com cor/ícone próprios,
  **auto-dismiss configurável** (persistido em `AppSettings`), dispensa manual.

### 4. Dashboard real (spec §4.1)

- **Score de Desempenho** calculado por `ScoreEngine` (pesos: disco 30, RAM 30,
  CPU 20, temperatura 20; **reponderação transparente** com N/D; tooltip explica);
- **Cards** (todos com `N/D` honesto): CPU, RAM livre, disco livre,
  temperatura, processos, uptime — fontes: PerformanceCounter / GC / DriveInfo /
  WMI / `TickCount64` / `Process`;
- **Gráfico de tendência** (LiveCharts2) das últimas N horas (predefinido 12 h)
  a partir de amostras **persistidas** em SQLite (amostragem 5 s, retenção 7 d);
- **Otimizações Recomendadas** por `RecommendationsEngine`: só disparam com
  dados reais, ordenadas por impacto, badge de risco (Baixo/Médio/Alto),
  indicador de reversibilidade, botão Aplicar (ou "Em breve" quando a tarefa
  ainda não existe neste patch).

### 5. Pipeline de otimização completo (spec §2.2)

`Backup → Apply → History → Rollback` em `OptimizationOrchestrator`:
- histórico escrito **antes** de aplicar (auditabilidade em falha);
- backup de registry (valor + tipo, ou ausência) antes de qualquer escrita;
- falha no apply → **rollback automático**; rollback manual via `RollbackUseCase`;
- **gatekeeping de elevação**: tarefas que exigem admin ficam `Blocked`
  em modo limitado, com notificação explicativa.

### 6. Primeira tarefa real: `telemetry-disable`

Desativação da telemetria de base do Windows
(`AllowTelemetry=0`, `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection`) —
documentada (efeito, porquê é seguro, como reverte, link MS Learn),
**reversível por rollback**, baixa, visível no Modo Simples. É a única ação
aplicável nesta versão — o resto está marcado "Em breve" de forma honesta.

### 7. CommandExecutor com whitelist (spec §2.2/§7)

`WhitelistedCommandExecutor` + `IProcessRunner` (mockável): regras em código
(`sc.exe` query/start/stop/pause/continue/change, `systeminfo`, `taskkill /IM
<name> /F`), rejeição de metacarateres de shell, execução **sem shell**,
timeout por regra, rejeições logadas.

### 8. Modo Simples/Avançado (spec §1/§7)

- filtro `ModeFilter` central (navegação + recomendações);
- persistido em `Settings` (SQLite);
- items Advanced: Limpeza Avançada, Inicialização, Serviços, Rede,
  Personalização, Relatórios/Histórico;
- troca de modo em runtime re-filtra sidebar e recomendações.

### 9. Testes (novo projeto `Nexus.Tests`, net8.0 — correm em qualquer OS)

| Ficheiro | O que cobre |
|---|---|
| `MetricValueTests` | regra `N/D` + formatação pt-PT |
| `ModeFilterTests` | Simples esconde Advanced (ambos os modos) |
| `ScoreEngineTests` | valor esperado (cálculo à mão), reponderação, todos-N/D → null, monotonia, clamping |
| `RecommendationsEngineTests` | regras só com dados reais, modo, ordenação, "aprox." em timeout |
| `WhitelistedCommandExecutorTests` | 12 casos: binário desconhecido, metacarateres, pipes, verbos inválidos, timeout, exit codes (com `IProcessRunner` fake — **nenhum processo real**) |
| `OrchestratorTests` | happy path, rollback automático, backup falhado, rollback falhado, rollback manual, bloqueio por elevação |

## O que ficou pendente (honestamente)

- **Nenhum módulo 4.2–4.11 está funcional ainda** — a sidebar mostra-os com
  placeholder honesto (patch planeado em cada um);
- **benchmark A/B** (`Before`/`After` em `OptimizationResult`) — estrutura
  reservada, medição real no patch 2;
- backups de tipo `Service`/`File` — lançam `NotSupportedException` no backup
  (as tarefas são rejeitadas **antes** de escrever algo);
- exportação PDF/CSV (patch 7); i18n além do pt base (arquitetura pronta);
- UI de Relatórios/Histórico (o **backend** de histórico/backups já existe e é
  testado — a UI chega no patch 7);
- validação em máquina real: **build + testes a correr no Windows** com
  `scripts/verify.ps1` (a sandbox desta sessão não tinha SDK/NuGet — ver
  PATCH-00 §3).

## Como validar

```bash
dotnet build NexusOptimizer.sln -c Release   # ou scripts/verify.ps1 (Windows)
dotnet test tests/Nexus.Tests
```

## Como rever (checklist spec §7)

- [x] Todo valor apresentado tem fonte real identificável no código
      (mapa de fontes: ADR-010 + `SystemTelemetryProvider`);
- [x] Toda ação registry tem backup + rollback (testado: `OrchestratorTests`,
      `EfChangeApplier`);
- [x] Modo Simples esconde o Advanced (navegação + recomendações;
      `ModeFilterTests`);
- [ ] Build limpo — **por executar no Windows** (sem SDK na sandbox);
- [x] Testes unitários cobrem os casos de uso introduzidos;
- [x] UI segue a paleta/componentes da secção 3;
- [x] Nenhum comando fora da whitelist (`WhitelistedCommandExecutorTests`).
