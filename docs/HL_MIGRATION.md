# HL Optimizer Pro — inspeção e migração

## Resultado da inspeção inicial

O repositório recebido não contém um otimizador HL anterior. Contém 60 ficheiros de um
produto diferente, **PrecisionCurve**, orientado a processamento de rato:

| Componente existente | Função / reutilização |
|---|---|
| `app/Core/CurveMath`, `ConfigBuilder`, `Profile`, `ProfileStore` | Curvas e perfis; manter no produto legado. Persistência JSON serve de referência arquitetural, sem migrar perfis para tweaks Windows. |
| `DriverClient`, `NativeMethods` | ABI do driver; não apropriado para operações de otimização de sistema. Não referenciado no HL. |
| `RawInputMonitor`, `HotkeyManager`, `ActiveProcessWatcher` | Input e seleção por processo; candidatos futuros a um módulo opcional de rato, após análise anti-cheat e consentimento. |
| `app/ViewModels`, `MainWindow`, tema e `CurveGraph` | UI WPF de curvas; preservada. Nova UI HL independente evita regressões e acoplamento do driver. |
| `driver/`, `shared/`, `tools/pcctl` | Driver C, ABI e CLI próprios; preservados integralmente. Não se devem instalar para executar um cleaner. |
| `service/` | Supervisor PrecisionCurve; preservado, não instalado pelo HL. |
| `tests/`, scripts e documentação | Suites host C/JS e validadores; executados. Os testes do HL ficam separados. |

Não foi encontrada implementação prévia de Cleaner, Health, DNS, Debloat ou Restore Center.
As limitações de hardware/input e riscos anti-cheat do legado já estão documentados em
`REALITY_CHECK.md`; não são funcionalidades de otimização a «corrigir» silenciosamente.
Os testes host passaram. Isso não prova ausência de erros no driver real, no serviço ou no WPF.
Sem SDK/Windows, não se afirma que a compilação do legado esteja validada.

## Estratégia incremental

A nova raiz é `HL.Optimizer.Pro/`, com namespace próprio e processo não elevado. Não houve
remoção de funcionalidades anteriores, mudança de ABI ou conversão de drivers em «boosters».
Separação inicial: Core portátil → Infrastructure Windows → ViewModels → WPF.

### Incremento 1 — implementado, validação Windows pendente
- Shell com os 16 destinos, estilos, identidade HL e ícone original.
- Amostragem Windows API e inventários read-only.
- Diagnóstico amostral sem pontuação artificial.
- Cleaner restrito a temporários antigos do utilizador, manifesto, confirmação e relatório.
- Dois ajustes HKCU, journal write-ahead, compensação, verificação e Undo.
- Histórico, exportação JSON, preferências locais e catálogo de ferramentas nativas.
- Testes de núcleo e script build/publish. Estado detalhado em `HL.Optimizer.Pro/README.md`.

### Incremento 2 — hardware e experiência
- Validar o primeiro incremento em VM; corrigir build e runtime antes de mais operações.
- Introduzir CommunityToolkit.Mvvm e provider LibreHardwareMonitor/WMI (com licenças revistas),
  sensores anuláveis, histórico circular limitado, diagnóstico de permissões e cancelamento UI.
- Design system acessível, SVGs consistentes, gráficos, filtros/pesquisa e idiomas pt-PT/en-US.
- DriveMap volume→disco, grandes ficheiros read-only, WM_DEVICECHANGE com debounce e ToastWindow.

### Incremento 3 — manutenção segura
- Expandir categorias Cleaner com testes de containment por handles e políticas por aplicação.
- Startup via tarefas/serviços e alterações reversíveis sem eliminar entradas.
- PC Health Full/Gaming; atualização e integridade via métodos suportados, sem inferir drivers antigos.
- Journal versionado/backup de falhas, eventual SQLite, permissões administrativas por operação,
  criação/verificação de pontos de restauro antes de tarefas avançadas.

### Incremento 4 — rede, apps e gaming
- DNS por adaptador com snapshot DHCP/estático/IPv6, provas de latência DNS e rollback testado.
- Appx/Uninstall com dependências, nenhuma remoção crítica preselecionada.
- Deteção de bibliotecas de jogos, perfis por aplicação, sessão e restauro resiliente a crash.
- Suspensão de apps só com allowlist e consentimento; nunca de serviços/proteções essenciais.
- SFC/DISM/CHKDSK com runner de processos, logs de stdout/stderr, UAC e códigos finais.

### Incremento 5 — preparação comercial
- Testes unitários e integração Windows para sensores, DNS, Startup, processos e USB.
- VMs Windows 10/11, HDD/SSD, 4/8 GB e GPUs Intel/AMD/NVIDIA, sem inventar dados indisponíveis.
- Auditoria de segurança, assinatura, instalador, update verificado, acessibilidade e desempenho.
- Só publicar uma versão estável depois dos critérios e checklist; não chamar «completo» ao incremento 1.
