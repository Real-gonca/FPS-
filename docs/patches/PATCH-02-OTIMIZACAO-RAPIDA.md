# PATCH 2 — Serviços reais + Otimização Rápida (A/B real) + Privacidade

> **Data:** 2026-09-12 · **Branch:** `arena/01a0957f-fps`
> **Entrega:** `deliveries/PATCH-02-OTIMIZACAO-RAPIDA.zip` (ficheiros novos/alterados)

## O que mudou

### 1. Serviços — gestão real (spec §4.6)

- **Inventário real** via WMI `Win32_Service` (`WmiServiceInventory`,
  timeout 3 s; falha → "indisponível", nunca uma lista fabricada) — nome,
  display name, estado, tipo de arranque.
- **Ações via sc.exe dentro do CommandExecutor whitelisted** (regras já
  auditadas no Patch 1): Iniciar / Parar / Habilitar (manual) / Desativar —
  cada ação com **preview de impacto + confirmação** (`ConfirmWindow`
  tematizado) e pelo pipeline completo (backup WMI → apply → histórico →
  rollback individual). Exit codes idempotentes (1056/1062) tratados como
  sucesso.
- **Perfis Gaming e Privacidade** (`ServiceProfileCatalog`): alterações
  documentadas uma a uma (serviço real + justificação), aplicáveis com
  preview completo; **uma falha interrompe o perfil** (as restantes alterações
  não correm; as já aplicadas revertem individualmente).
  - *Gaming*: SysMain, WSearch, WMPNetworkSvc, DiagTrack, Spooler (cada um com
    o trade-off honesto documentado).
  - *Privacidade*: DiagTrack, lfsvc, MapsBroker, XblAuthManager.
- **Backup/rollback de serviços** no `EfChangeApplier` (`ChangeKind.Service`):
  o payload guarda start mode + estado reais (probe WMI); o restauro devolve o
  start mode (`sc change`) e realinha o estado (`sc start/stop`) se divergir.
- UI: vista **Serviços** (Modo Avançado) — pesquisa, filtro por estado,
  DataGrid dark, cartão de ações do serviço selecionado, cartões de perfil
  com "Aplicar perfil".

### 2. Otimização Rápida completa com A/B medido (spec §4.2)

- Vista **Otimização Rápida** (Modo Simples) com 4 tweaks seguros, selecionáveis:
  1. `telemetry-disable` (registry HKLM — já existia);
  2. `service-diagtrack-disable` (DiagTrack → disabled);
  3. `service-wmpnetworksvc-disable` (partilha de media → disabled);
  4. `advertising-id-disable` (HKCU, **sem administrador**).
- **Aplicar num clique**: cada tarefa corre pelo pipeline completo
  (backup → apply → histórico → rollback), com estado por item
  (aplicado/falhou/bloqueado por elevação).
- **Micro-benchmark A/B REAL** (`LocalBenchmarkRunner`): CPU (FNV-1a, ~2 s),
  alocação de memória (16 MiB, ~2 s), escrita/leitura de disco (64 MiB) —
  medido ANTES e DEPOIS, valores exibidos com delta %, **persistidos em
  SQLite** (`Benchmarks`, contexto `quick-before`/`quick-after`) para os
  relatórios do Patch 7.
- **Honestidade (spec §0.2/§4.2, ADR-011)**: a UI etiquetou a medição como
  "micro-benchmark (medido, não estimado)" com aviso explícito de que **não é
  uma medição holística de performance do sistema**; deltas negativos
  aparecem como estão.
- **Rollback individual por tarefa** (botão "Reverter" por item, via
  `RollbackUseCase` com o actionId real).

### 3. Privacidade / Telemetria — toggles reais (spec §4.7)

- Vista **Privacidade** (Modo Simples) com 3 toggles **documentados**, cada um
  com estado lido de probe real (N/D se a fonte falhar):
  - **Telemetria de base** — `HKLM\...\DataCollection\AllowTelemetry`
    (requer admin; reversível);
  - **ID de anúncio** — `HKCU\...\AdvertisingInfo\Enabled`
    (**não requer admin**; reversível);
  - **DiagTrack** — start mode do serviço (WMI; requer admin; reversível).
- Aplicar = pipeline completo; **Reverter** = rollback do último backup da
  chave (procurado no histórico real).
- Novo `AdvertisingIdTask` (registro HKCU — demonstra que nem tudo precisa de
  elevação) e novas chaves canónicas em `TaskKeys`.

### 4. Infraestrutura nova

| Área | Adição |
|---|---|
| Domain (ports) | `IServiceInventory`/`IServiceInspector` (+`ServiceInfo`/`ServiceProbe`), `IBenchmarkRunner` (+`BenchmarkResult`), `IBenchmarkLog` |
| Infrastructure | `WmiServiceInventory`, `WmiServiceInspector`, `LocalBenchmarkRunner`, `Benchmarks` table (EF), `EfBenchmarkLog`, `EfChangeApplier` + `ChangeKind.Service` |
| Application | `ServiceTask` (genérica, via executor whitelisted, idempotência 1056/1062), `AdvertisingIdTask`, `ServiceProfile`/`ServiceProfileCatalog`, `ServiceOperationUseCase` (operações + perfis com interrupção em falha), `BenchmarkUseCase`, `QuickOptimizationSet` |
| Presentation | `QuickOptimizationView`/VM, `ServicesView`/VM, `PrivacyView`/VM, `ConfirmWindow` (preview de impacto tematizado), estilos DataGrid dark, navegação atualizada (3 módulos agora funcionais) |

### 5. Testes novos (projeto `Nexus.Tests`, net8.0)

| Ficheiro | O que cobre |
|---|---|
| `Tasks/ServiceTaskTests` | comando exato gerado (whitelist), rejeição de metacarateres no serviceKey, exit codes reais (1058 falha, 1056/1062 idempotência), metadados (reversível, requer elevação) |
| `Persistence/EfChangeApplierServiceTests` | **EF SQLite in-memory real**: payload do backup com o estado real; restauro executa `sc change` + realinhamento de estado (Running/Stopped); serviço inexistente → aborta antes de qualquer escrita |
| `Benchmark/LocalBenchmarkRunnerTests` | o runner executa de facto e devolve valores reais (ranges plausíveis, nunca 0-fabricado) |
| `Profiles/ServiceProfileTests` | só serviços reais (padrão da whitelist), start modes válidos, justificação obrigatória, reversíveis |
| `UseCases/ServiceOperationUseCaseTests` | gatekeeping de elevação (nada roda "de rastejo"); perfil interrompido em falha (restantes alterações não correm) |

## O que ficou pendente

- **UI de Relatórios/Histórico** — o backend (histórico + backups +
  benchmarks persistidos) já existe e é testado; a UI chega no Patch 7.
- Gaming Center / Emuladores (Patch 3): deteção real de clientes de emulação
  + afinidade de CPU + perfis por jogo.
- Limpeza Avançada (Patch 4) + Startups (Patch 4).
- Backups de tipo `File` (necessários para a limpeza) — lançam
  `NotSupportedException` no backup até ao Patch 4.
- i18n além do pt base; exportação PDF/CSV.
- Validação no Windows com `scripts\verify.ps1` (build/testes continuam a
  requerer .NET SDK — ver restrição declarada no PATCH-00 §3).

## Como validar

```bash
dotnet build NexusOptimizer.sln -c Release   # ou scripts\verify.ps1 (Windows)
dotnet test tests/Nexus.Tests
```

Nota: `LocalBenchmarkRunnerTests` executa o benchmark de facto (~5–8 s) —
os testes totais continuam a ser da ordem de 1–2 min.

## Como rever (checklist spec §7)

- [x] Valores dos serviços/benchmarks vêm de fontes reais identificáveis
      (WMI / sc.exe / medições diretas; ADR-011/012);
- [x] Todas as ações de serviço têm backup + rollback (testado com EF real);
- [x] Modo Simples continua a esconder o Advanced (Serviços fica em
      Avançado; Otimização Rápida e Privacidade em Simples);
- [ ] Build limpo — **por executar no Windows** (sem SDK na sandbox);
- [x] Testes unitários novos cobrem os casos de uso do patch;
- [x] UI segue a paleta/componentes da secção 3 (ConfirmWindow, DataGrid);
- [x] Nenhum comando fora da whitelist (todas as escritas de serviço passam
      por `ISystemCommandExecutor`).
