# Decisões de Arquitetura (ADRs)

Registo das decisões relevantes do projeto e do **porquê** (spec §0.4).

## ADR-001 — WPF (não Avalonia)

**Decisão:** UI em WPF sobre `net8.0-windows`.

**Porquê:**
- O produto é Windows 10/11 only (spec §1) — cross-platform não está nos objetivos;
- WPF dá o melhor suporte a `WindowChrome` (title bar customizada, spec §3.2),
  theming via ResourceDictionaries, e é a plataforma com melhor compatibilidade do
  LiveCharts2 WPF;
- Avalonia 11 ficaria documentado como alternativa futura se surgisse requisito
  cross-platform (a separação Clean Architecture já isola a Presentation).

**Custo aceite:** nenhum build/run fora do Windows (mitigado com
`EnableWindowsTargeting` para cross-*compile* e testes nas camadas inferiores).

## ADR-002 — Elevação: `asInvoker` + runtime (não `requireAdministrator`)

**Decisão:** manifest com `requestedExecutionLevel=asInvoker` + deteção em runtime
(`WindowsIdentity`/`WindowsPrincipal`) + relançamento `runas` a partir da UI.

**Porquê:** a spec exige `requireAdministrator` **com fallback elegante se negado**.
Com `requireAdministrator`, ao recusar o UAC o processo nem chega a arrancar —
não existe espaço para "fallback elegante" (a app simplesmente não abre).
Com `asInvoker`:
- a app abre sempre → degrada para **modo limitado** (telemetria/leitura + N/D,
  ações de sistema bloqueadas com explicação);
- o header mostra "Executar como Admin" → relançamento elevado via `runas`;
- se o utilizador recusar o UAC, a app mantém-se funcional em modo limitado.

Isso satisfaz a *intenção* da spec (elevação real quando necessário + fallback
elegante) melhor do que a leitura literal.

## ADR-003 — TFM: Domain/Application/Infrastructure em `net8.0` puro

**Decisão:** as camadas 1–3 compilam para `net8.0` (portable); apenas a
Presentation é `net8.0-windows`.

**Porquê:**
- `System.Management` (WMI), `System.Diagnostics.PerformanceCounter` e
  `Microsoft.Win32.Registry` compilam em qualquer OS (empacotados como packages);
- em runtime fora do Windows, as fontes degradam honestamente para `N/D`
  (guardas `OperatingSystem.IsWindows()` + try/catch) — a "regra de ouro" mantém-se;
- resultado prático: **os testes unitários correm em CI Linux** sem Windows.

## ADR-004 — Whitelist em código, não em configuração

**Decisão:** as regras do `CommandExecutor` vivem em `CommandWhitelist.Default`
(C#) e cada regra nova exige justificação + teste unitário.

**Porquê:** uma whitelist em ficheiro de configuração poderia ser adulterada em
runtime (o utilizador — ou malware — adiciona `powershell.exe`). Em código, a
whitelist muda só com uma nova build auditável. Defesa em profundidade:
metacaracteres de shell são sempre rejeitados, e a execução é **sem shell**
(`UseShellExecute=false`).

## ADR-005 — `N/D` como tipo de dado, não como string solta

**Decisão:** `MetricValue` (struct com `double?` + `IsAvailable`) percorre
Domain→Application→UI; a UI formata `N/D` num único lugar.

**Porquê:** impede "números bonitos" acidentais (ex.: `0.0` quando a fonte falhou)
e torna a honestidade **estrutural** — um valor só existe se a fonte o produziu.

## ADR-006 — Score com reponderação transparente

**Decisão:** peso de cada fator (disco 30 / RAM 30 / CPU 20 / temperatura 20);
fator N/D → peso redistribuído proporcionalmente; todos N/D → score `N/D`.
O `ScoreResult.Summary` explica quantos fatores falharam (visível no tooltip da UI).

**Porquê:** "medir melhor do que se consegue medir" seria fabricar confiança.
A reponderação é a única alternativa honesta a excluir o score inteiro.

## ADR-007 — Sem "RAM Booster" fictício

**Decisão:** não existe "RAM Booster" neste produto. Quando a RAM está sob
pressão, a recomendação real é "Reduzir consumo de memória de fundo" (Modo
Avançado): lista processos por consumo e o utilizador decide.

**Porquê:** `EmptyStandbyList`/trim de standby pages não melhora performance de
forma mensurável (o Windows gere a standby list de propósito) — apresentá-lo
como "RAM otimizada" violaria o princípio de zero dados simulados (spec §0.2).

## ADR-008 — Persistência: SQLite + EF Core, `EnsureCreated`

**Decisão:** base local em `%LOCALAPPDATA%\NexusOptimizer\nexus.db`
(histórico, backups, amostras, definições). Schema estável e simples →
`EnsureCreated` no arranque. Quando o schema passar a evoluir com dados de
utilizadores em produção, migramos para EF Migrations.

## ADR-009 — Telemetria: amostragem 5 s + retenção 7 dias

**Decisão:** sampler `BackgroundService` a cada 5 s; temperatura WMI throttled a
15 s (custo do WMI); retenção de amostras 7 dias (purge por `ExecuteDelete` em
cada gravação).

**Porquê:** 5 s é granularidade suficiente para o gráfico de tendência e para o
score, sem competir com o sistema que a app otimiza (spec §5: consumo mínimo em
idle). A WMI é a fonte honesta para temperatura/VRAM e é lenta — o throttle é
explicitamente exigido pela spec.

## ADR-010 — Limitações conhecidas das fontes (documentadas, assumidas)

- **VRAM total via `Win32_VideoController.AdapterRAM`** é 32 bits → acima de 4 GB
  vem truncado. Uso de VRAM em tempo real exigiria API de vendor (NVIDIA/AMD) —
  planeado para o patch de Monitor; até lá a UI mostra VRAM *total* e N/D para uso.
- **Temperatura** vem da zona térmica ACPI (grão por zona, não por core).
- **Uptime** via `Environment.TickCount64` (ms desde boot) — fonte real, sem WMI.
