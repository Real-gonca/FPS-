# Desenvolvimento

## Requisitos

| Componente | Versão | Notas |
|---|---|---|
| .NET SDK | 8.0.x | [instalação oficial](https://dotnet.microsoft.com/download/dotnet/8.0) |
| Sistema (run) | Windows 10/11 | a app é WPF — **não corre em Linux/macOS** |
| Sistema (build/test) | qualquer | `EnableWindowsTargeting` permite cross-compile do WPF |

## Comandos

```bash
# Build completo (todos os projetos, incluindo o WPF em cross-compile)
dotnet build NexusOptimizer.sln -c Release

# Testes unitários (correm em qualquer OS — não exigem Windows)
dotnet test tests/Nexus.Tests -v minimal

# Correr a app (apenas Windows)
dotnet run --project src/Nexus.Presentation

# Validação de uma mão (build + testes + resumo)
# Windows:   scripts\verify.ps1
# Linux/mac: scripts/verify.sh
```

## Dados e logs da aplicação (Windows)

| Item | Local |
|---|---|
| Base SQLite (histórico, backups, amostras, definições) | `%LOCALAPPDATA%\NexusOptimizer\nexus.db` |
| Logs Serilog (rotação diária, 7 ficheiros) | `%LOCALAPPDATA%\NexusOptimizer\logs\nexus-YYYY-MM-DD.log` |
| Backup de ações | tabelas `Backups` da mesma base (payload JSON do estado anterior) |

## Notas do ambiente de desenvolvimento com IA (sessões Arena)

- As sessões de agente rodam em sandbox Linux. **O sandbox de 2026-09-12 não tem
  acesso a `nuget.org`/`builds.dotnet.microsoft.com`** → o SDK não pôde ser
  instalado aí e o build/teste **não foi executado na sandbox**; a validação é
  feita na máquina do utilizador (`scripts/verify.ps1` / `dotnet build` +
  `dotnet test`).
- Se a sandbox tiver acesso a rede generalizada: `dotnet-install.sh --channel 8.0`
  e depois os comandos acima.

## Convenções

- PT (pt-PT) como idioma base da UI e dos logs; arquitetura pronta para i18n
  (texto da UI centralized em XAML/ViewModels).
- Nenhum número de UI sem fonte real; `MetricValue` + `N/D`.
- Tarefas novas: implementar `IOptimizationTask`, registar na
  `AddNexusApplication()` e adicionar regras de whitelist (se usarem comandos)
  **com testes**.
