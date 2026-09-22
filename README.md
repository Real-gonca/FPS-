# HL OPTIMIZER PRO
### Mais Desempenho. Mais Estabilidade. Mais Você.

Software desktop profissional para Windows 10 e Windows 11, desenvolvido em **C# .NET 8 + WPF + MVVM**.

---

## 🏢 Identidade Visual
- **Tema:** Dark Mode premium empresarial
- **Cores:** Azul escuro `#0A1628`, Azul elétrico `#2D7FF9`, Branco, Verde `#00D26A` para estados positivos
- **Estilo:** Bordas discretas, sombras suaves, tipografia Segoe UI limpa, sem aparência de template genérico
- **Inspiração:** Software comercial real de otimização, não dashboard de jogo

---

## 🏗️ Arquitetura

```
HL.Optimizer.Pro
├── Core
│   ├── Models (SystemInfo, PerformanceMetrics, OptimizationItem, TweakItem, CleanupItem, GameProfile, etc.)
│   ├── Services (SystemInfoService, PerformanceMonitorService, OptimizationService, CleanupService, etc.)
│   ├── Interfaces (IServices.cs com todas as interfaces)
│   └── Utilities (AdminHelper, PowerShellHelper, RegistryHelper, WmiHelper, NativeMethods, Converters)
├── Optimization
│   ├── Services, Startup, Registry, Network, Power, Cleanup
├── Monitoring (PerformanceCharts)
├── Diagnostics (SystemDiagnostics)
├── Gaming (GameBoosterEngine)
├── Views (12 páginas + Controls)
├── ViewModels (MVVM com CommunityToolkit.Mvvm)
├── Resources/Icons
├── Themes (DarkTheme, Controls, Converters)
└── Localization (pt-BR, pt-PT, en-US, es-ES)
```

**Princípios:**
- MVVM correto, sem lógica de sistema no XAML
- Serviços injetados via DI (Microsoft.Extensions.DependencyInjection)
- Dados reais do sistema via WMI, PerformanceCounter, Registry, PowerShell
- Sem dados fictícios quando possível obter real
- Código organizado, extensível, modular

---

## 📊 Funcionalidades Implementadas

### 1. Painel Principal (Dashboard)
- Estado do Sistema: saudável / atenção / otimização recomendada
- Índice de otimização transparente: `12 de 15 otimizações concluídas = 80%`
- Métricas reais: CPU, RAM, GPU, Disco, Rede, Processos, Temperatura (quando disponível)
- Gráfico em tempo real (histórico de 60 pontos)
- Atalhos rápidos: Limpeza, Modo Jogo, Inicialização, Rede, Diagnóstico, Restauração
- Informações do sistema: Windows, CPU, GPU, RAM, Disco, Rede
- Categorias de otimização com score por categoria
- Últimas ações com log real

### 2. Botão [ EXECUTAR OTIMIZAÇÃO ]
- Cria ponto de restauração opcional
- Diagnóstico
- Lista de ações com seleção/desseleção
- Progresso
- Logs
- Resultado

### 3. Booster
- Três modos com explicação clara:
  - **Econômico:** economia máxima, CPU reduzida, plano economia
  - **Equilibrado:** uso diário, plano balanceado
  - **Desempenho:** máximo desempenho, CPU máxima, plano alto desempenho
- Otimização de serviços (SysMain, WSearch, DiagTrack)
- Limpeza de memória segura (trim working sets)
- Plano de energia real via powercfg

### 4. Tweaks
Categorias: Windows, Privacidade, Interface, Desempenho, Rede, Input, Explorer, Sistema
Cada tweak:
- Nome, descrição, categoria, estado atual, recomendado, risco, necessidade admin, impacto
- Aplicar / Reverter
- Exemplos: animações, MenuShowDelay, StartupDelay, telemetria, Nagle, Prefetch, etc.

### 5. Limpeza
Analisa:
- Windows Temp, User Temp, Local Temp, Logs, Windows Update cache, Thumbnails, Browser Cache (Chrome, Edge, Firefox, Brave), Lixeira
Mostra espaço recuperável por categoria
- Botão Analisar -> Limpar Selecionados
- Nunca apaga arquivos pessoais automaticamente
- Segurança: ignora arquivos <1 dia

### 6. Jogos - HL GAME BOOSTER
- Detecção via Registry Uninstall + file system scan (Steam, etc.)
- Perfis: Free Fire, BlueStacks, Valorant, CS2, Fortnite, Minecraft, Personalizado
- Funções: Modo Jogo, prioridade alta, suspender processos não essenciais, plano energia, fechar background apps
- Métricas reais CPU/RAM/GPU, sem promessa de FPS específico

### 7. Emuladores - EMULATOR BOOST
Detecta: BlueStacks, LDPlayer, MSI App Player, GameLoop, Nox, MuMu
Mostra: CPU, RAM, GPU, resolução, DPI, renderizador, processos
Perfis configuráveis com backup automático antes de alterar

### 8. Inicialização
- Registry Run (CurrentUser e LocalMachine), Startup Folder, Task Scheduler
- Nome, Editor, Caminho, Impacto (Baixo/Médio/Alto), Estado
- Desativar/Ativar via StartupApproved, abrir localização
- Não desativa críticos automaticamente

### 9. Monitor de Desempenho
- Gráficos tempo real CPU, RAM, GPU, Disco, Rede
- Atualização 1s/2s/5s/10s configurável
- Histórico 60 pontos
- Leitura/Escrita disco, Download/Upload rede

### 10. Sistema
- CPU, GPU, RAM, Motherboard, BIOS, Windows, arquitetura, discos, rede, DirectX, monitores, usuário, nome computador

### 11. Diagnóstico
Verifica: Disco (espaço, SMART), RAM (uso >85%), CPU, Drivers (PnP errors), Windows Update, Serviços, Inicialização, Rede (ping, latência), Espaço, Integridade (sfc)
Relatório: problema, causa provável, impacto, recomendação, ação disponível

### 12. Rede
- IP, Gateway, DNS, adaptador, velocidade, latência, perda pacotes
- Ferramentas: Ping, Flush DNS, Reset Winsock, Teste conectividade
- DNS personalizados: Google, Cloudflare, Quad9, Automático, com aviso antes de alterar

### 13. Ferramentas Avançadas
- Process Manager, Services Manager, Startup Manager, Hosts Editor, DNS Tools, Disk Tools
- Atalhos reais: Task Manager, Device Manager, Disk Management, Event Viewer, Services, MsConfig, Regedit, Control Panel, PowerShell, CMD, System Info, Resource Monitor, Task Scheduler

### 14. Restauração
- Criar ponto de restauração (WMI SystemRestore + fallback PowerShell)
- Listar pontos existentes
- Abrir System Restore (rstrui.exe)
- Backup de registro antes de alterações

### 15. Segurança
Cada otimização: ID, nome, descrição, categoria, risco (Seguro/Moderado/Avançado), reversibilidade, comando, método reversão, necessidade admin

### 16. Logs
- Data, hora, ação, resultado, erro, comando, usuário, bytes liberados
- SQLite local + fallback arquivo
- Log Center com exportação CSV

### 17. Configurações
Geral, Aparência, Idioma (PT-PT, PT-BR, EN, ES), Desempenho, Notificações, Logs, Privacidade, Atualizações, Segurança

### 18. Pesquisa Global
Pesquisa otimizações, ferramentas, configurações via header search

### 19. Notificações
✓ Otimização concluída, ⚠ Reinicialização necessária, ⚠ Admin necessário, ✓ GB liberados

### 20. Administrador
Detecta automaticamente, botão Executar como administrador quando necessário

---

## 🔧 Tecnologias
- **.NET 8** (net8.0-windows)
- **WPF** com WindowChrome customizado
- **CommunityToolkit.Mvvm** 8.2.2
- **System.Management** para WMI
- **System.Diagnostics.PerformanceCounter** para métricas
- **Microsoft.Data.Sqlite** para logs
- **PowerShell/CMD** apenas quando necessário
- **APIs nativas Windows**: powercfg, netsh, ipconfig, reg.exe, etc.

---

## 🚀 Como Executar

### Requisitos
- Windows 10 1903+ ou Windows 11
- .NET 8 SDK

### Build
```bash
dotnet restore
dotnet build -c Release -r win-x64
```

### Executar
```bash
dotnet run --project src/HL.Optimizer.Pro/HL.Optimizer.Pro.csproj
```
Ou abrir `HL.Optimizer.Pro.sln` no Visual Studio 2022 e compilar x64.

O app requer administrador para otimizações completas (definido em app.manifest).

---

## 🛡️ Princípios de Segurança
- Nenhuma alteração destrutiva sem confirmação
- Toda otimização rastreável, reversível, registrada, explicada
- Não simula resultados - se não pode implementar com segurança, informa motivo e desativa
- Não promete "+500% FPS" ou "82% melhor que outros computadores"
- Índice de otimização transparente baseado em critérios próprios
- Não envia dados pessoais sem consentimento
- SQLite local, sem telemetria externa

---

## 📁 Estrutura de Pastas
```
docs/ - documentação
src/HL.Optimizer.Pro/
  App.xaml
  MainWindow.xaml
  Core/
  Optimization/
  Monitoring/
  Diagnostics/
  Gaming/
  Views/
  ViewModels/
  Resources/
  Themes/
  Localization/
```

---

## 🎯 Diferenciais vs Protótipos
- ✅ Janela desktop real WPF, não website
- ✅ Dados reais do sistema (WMI, PerformanceCounter, Registry)
- ✅ Operações reais (powercfg, netsh, reg, serviços)
- ✅ MVVM correto, DI, arquitetura modular
- ✅ Logs SQLite, pontos de restauração reais
- ✅ Detecção real de jogos/emuladores
- ✅ Limpeza real com cálculo de tamanho
- ✅ Segurança: confirmação, reversibilidade, admin check

---

## 📄 Licença
Proprietário - HL Software © 2026

---

## 👨‍💻 Autor
Desenvolvido como software desktop profissional comercial, não projeto acadêmico.
