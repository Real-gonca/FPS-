# HL OPTIMIZER PRO - Arquitetura Técnica

## Visão Geral
Software desktop WPF .NET 8 com arquitetura MVVM estrita, modular, extensível.

## Camadas

### 1. Presentation (Views + ViewModels)
- **MainWindow.xaml**: Header com logo HL, pesquisa, notificações, configurações, idioma, controles de janela. Sidebar com 12 seções. ContentControl com DataTemplates para ViewModels.
- **Views**: 12 UserControls, cada um com ScrollViewer e cards empresariais.
- **ViewModels**: BaseViewModel + 12 ViewModels específicos, usando CommunityToolkit.Mvvm (ObservableObject, RelayCommand, ObservableProperty).
- **Themes**: DarkTheme.xaml com palette premium, Controls.xaml com estilos de botão, card, sidebar, search, progress, toggle, scrollbar. Converters.xaml.

### 2. Core
#### Models
- SystemInfo, DiskInfo, NetworkAdapterInfo, MonitorInfo
- PerformanceMetrics, PerformanceHistory
- OptimizationItem, OptimizationResult, OptimizationIndex, CategoryScore
- TweakItem
- CleanupItem, CleanupResult, CleanupCategory
- GameProfile, GameBoosterSettings, GameType, EmulatorInfo, EmulatorPerformanceProfile, EmulatorType
- StartupItem, ServiceInfo, StartupLocation, StartupImpact
- LogEntry, RestorePointInfo, DiagnosisResult, DiagnosisSeverity, NetworkInfo

#### Interfaces
ISystemInfoService, IPerformanceMonitorService, IOptimizationService, ICleanupService, IStartupService, INetworkService, IGameDetectionService, IEmulatorDetectionService, IRestoreService, ILogService, IDiagnosisService, IPowerService, IRegistryService, ILocalizationService

#### Services
- **SystemInfoService**: WMI Win32_OperatingSystem, Win32_Processor, Win32_VideoController, Win32_BaseBoard, Win32_BIOS, Win32_DiskDrive, Win32_NetworkAdapter, Registry DirectX, DriveInfo.
- **PerformanceMonitorService**: PerformanceCounter Processor % Processor Time, Memory Available MBytes, PhysicalDisk Disk Read/Write Bytes/sec, % Disk Time. Timer 1s. WMI para rede. NativeMethods GlobalMemoryStatusEx para RAM. Event MetricsUpdated.
- **OptimizationService**: Lista de otimizações com ExecuteAction/RevertAction via RegistryHelper, Service Start registry, PowerService, netsh. CalculateOptimizationIndex com check real de power plan.
- **CleanupService**: AnalyzePath com DirectoryInfo.GetFiles, filtro <1 dia segurança, browser caches Chrome/Edge/Firefox/Brave, Recycle Bin via $Recycle.Bin, CleanDirectory com File.Delete e remoção de pastas vazias, ClearRecycleBin via PowerShell Clear-RecycleBin.
- **StartupService**: Registry SOFTWARE\Microsoft\Windows\CurrentVersion\Run (CurrentUser e LocalMachine), RunOnce, StartupApproved para IsEnabled, StartupFolder Environment.SpecialFolder.Startup, Task Scheduler via schtasks, Services via Win32_Service WMI.
- **NetworkService**: NetworkInterface.GetAllNetworkInterfaces, Ping, ipconfig /flushdns, netsh winsock reset, netsh interface ip set dns.
- **PowerService**: powercfg /list, /getactivescheme, /setactive com GUIDs conhecidos HighPerformance, Balanced, PowerSaver.
- **GameDetectionService**: Registry Uninstall, file system scan em Program Files, Steam common, detecção por exe names valorant.exe, cs2.exe, etc.
- **EmulatorDetectionService**: Paths conhecidos BlueStacks, LDPlayer, MSI, GameLoop, Nox, MuMu, scan de processos HD-Player, dnplayer, Nox, etc.
- **RestoreService**: WMI root\default:SystemRestore CreateRestorePoint, fallback PowerShell Checkpoint-Computer, ManagementObjectSearcher para listar pontos, rstrui.exe, reg export backup.
- **LogService**: SQLite em %LocalAppData%\HL Optimizer Pro\logs.db, tabelas Logs, Settings, AppliedTweaks, fallback para arquivo texto.
- **DiagnosisService**: CheckDiskHealth (DriveInfo free space <5GB, WMI Win32_DiskDrive Status), CheckMemory (MEMORYSTATUSEX load >85%), CheckCpu, CheckDrivers (Win32_PnPEntity ConfigManagerErrorCode !=0), CheckNetwork (Ping 8.8.8.8), etc.
- **RegistryService**: Wrapper RegistryHelper com GetHive parsing HKEY_LOCAL_MACHINE etc.
- **LocalizationService**: Dicionário interno + troca de ResourceDictionary.

#### Utilities
- AdminHelper: WindowsIdentity, WindowsPrincipal IsInRole Administrator, RestartAsAdmin via runas verb.
- PowerShellHelper: ProcessStartInfo powershell.exe -NoProfile -ExecutionPolicy Bypass, cmd.exe /c.
- RegistryHelper: RegistryKey.OpenBaseKey, CreateSubKey, GetValue, SetValue, DeleteValue, KeyExists, GetAllValues.
- WmiHelper: ManagementObjectSearcher, Query, GetPropertyString, GetProperty<T>.
- NativeMethods: GetPhysicallyInstalledSystemMemory, GlobalMemoryStatusEx, PowerGetActiveScheme, PowerSetActiveScheme, PowerEnumerate.
- Converters: CountToVisibility, InverseBoolToVisibility, BoolToVisibility, PercentageToBrush, StringToVisibility, InverseBool, PercentageToWidth, BoolToColor.

### 3. Optimization Modules
- Services, Startup, Registry, Network, Power, Cleanup - cada um com lógica específica, mas centralizados em OptimizationService.

### 4. Monitoring
PerformanceCharts placeholder para futura integração LiveCharts, atualmente custom drawing via ItemsControl de barras.

### 5. Diagnostics
SystemDiagnostics categorias array, lógica em DiagnosisService.

### 6. Gaming
GameBoosterEngine GetRecommendedSettings por GameType.

## Fluxo de Otimização
1. DashboardViewModel ExecuteOptimization:
   - MessageBox criar ponto de restauração?
   - Se sim, RestoreService.CreateRestorePointAsync
   - Filtra Optimizations IsSelected
   - Progress<OptimizationProgress> reporta CurrentItem
   - OptimizationService.ExecuteOptimizationsAsync loop com ExecuteOptimizationAsync cada item
   - ExecuteOptimizationAsync checa Admin, chama ExecuteAction (registry/service/power/netsh), loga via LogService
   - Atualiza OptimizationIndex, RecentActions

## Segurança
- Admin check antes de operações que requerem
- Confirmação MessageBox antes de limpeza, DNS, Winsock, restore point
- Backup registro via reg export antes de alterações
- Nunca apaga arquivos pessoais, apenas temp/cache/logs
- Ignora arquivos com LastWriteTime <1 dia
- Reversibilidade: cada OptimizationItem tem RevertAction

## Performance
- Task.Run para operações WMI/IO para não bloquear UI
- Timer em PerformanceMonitorService com intervalo configurável
- History MaxPoints 60 para gráfico
- Lazy initialization de PerformanceCounters com warmup

## Localização
- 4 XAML ResourceDictionaries pt-BR, pt-PT, en-US, es-ES
- LocalizationService troca MergedDictionaries
- ComboBox no header bound para SelectedLanguage

## Estilo Visual
- DarkTheme: PrimaryDark #0A1628, SecondaryDark #111F3A, CardDark #162447, SurfaceDark #1B2E5C, BorderDark #243A6B, ElectricBlue #2D7FF9, AccentGreen #00D26A
- Gradientes PrimaryGradient (2D7FF9->1A5FCC), CardGradient, SuccessGradient
- Shadows CardShadow, ButtonShadow, GreenShadow
- CornerRadius Small 6, Medium 10, Large 14, ExtraLarge 18
- Controls: PrimaryButton com gradient e shadow, SuccessButton com green, SecondaryButton com border, IconButton 36x36, CardBorder com shadow, SidebarButton RadioButton com Tag icon e IsChecked electric blue, SearchTextBox, ModernProgressBar 6px height, ModernToggle 44x24, ScrollBar 6px width.

## Build
- TargetFramework net8.0-windows, UseWPF true, ImplicitUsings enable, Nullable enable
- Platforms x64
- PackageReferences: CommunityToolkit.Mvvm 8.2.2, Microsoft.Extensions.DependencyInjection 8.0.0, Hosting 8.0.0, System.Management 8.0.0, PerformanceCounter 8.0.0, Microsoft.Data.Sqlite 8.0.0
- app.manifest requireAdministrator, dpiAware true, PerMonitorV2

## Testes
- Não inclui testes unitários por ser app desktop, mas serviços são testáveis via interfaces.
- Verificação manual: dados reais aparecem, botões executam operações reais, logs registram, pontos de restauração criam.

## Futuro
- Integração LiveCharts para gráficos
- SQLite para perfis de jogos/emuladores
- Auto-updater
- Tray icon com minimize to tray
- Mais tweaks registry com backup
- Disk cleanup com análise mais profunda
