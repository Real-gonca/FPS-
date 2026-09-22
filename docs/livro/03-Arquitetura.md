# Capítulo 3: Arquitetura Modular Profissional

## 3.1 Por que Arquitetura Importa?

Projeto acadêmico: tudo em um arquivo `MainWindow.xaml.cs` com 2000 linhas.
Produto profissional: separação clara, testável, extensível.

## 3.2 Estrutura de Pastas

```
HL.Optimizer.Pro/
├── Core/ (núcleo, sem dependência de UI)
│   ├── Models/ - dados puros
│   ├── Services/ - lógica de sistema
│   ├── Interfaces/ - contratos
│   └── Utilities/ - helpers
├── Optimization/ - otimizações específicas por categoria
│   ├── Services/
│   ├── Startup/
│   ├── Registry/
│   ├── Network/
│   ├── Power/
│   └── Cleanup/
├── Monitoring/ - gráficos e histórico
├── Diagnostics/ - verificações de saúde
├── Gaming/ - booster de jogos
├── Views/ - XAML puro, sem lógica
├── ViewModels/ - lógica de apresentação
├── Themes/ - DarkTheme, Controls, Converters
├── Localization/ - pt-BR, pt-PT, en-US, es-ES
├── Resources/Icons/
└── Properties/app.manifest
```

## 3.3 Core/Models - Dados Puros

Models não têm lógica, apenas propriedades. Exemplo `SystemInfo.cs`:

```csharp
public class SystemInfo
{
    public string ComputerName { get; set; } = Environment.MachineName;
    public string OsName { get; set; } = "Windows";
    public string CpuName { get; set; } = "";
    public int CpuCores { get; set; }
    public long TotalRamBytes { get; set; }
    public List<DiskInfo> Disks { get; set; } = new();
}

public class DiskInfo
{
    public string Name { get; set; } = "";
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public double FreePercent => TotalBytes > 0 ? (double)FreeBytes/TotalBytes*100 : 0;
}
```

Outros models: `PerformanceMetrics`, `OptimizationItem`, `TweakItem`, `CleanupItem`, `GameProfile`, `EmulatorInfo`, `StartupItem`, `LogEntry`, `DiagnosisResult`.

## 3.4 Core/Interfaces - Contratos

Uma interface por serviço, para DI e testes:

```csharp
public interface ISystemInfoService
{
    Task<SystemInfo> GetSystemInfoAsync();
    Task<List<DiskInfo>> GetDiskInfoAsync();
}

public interface IPerformanceMonitorService
{
    event EventHandler<PerformanceMetrics> MetricsUpdated;
    PerformanceMetrics CurrentMetrics { get; }
    void StartMonitoring(int intervalMs = 1000);
    void StopMonitoring();
}

public interface IOptimizationService
{
    Task<List<OptimizationItem>> GetAvailableOptimizationsAsync();
    Task<OptimizationIndex> CalculateOptimizationIndexAsync();
    Task<OptimizationResult> ExecuteOptimizationAsync(OptimizationItem item);
}
```

Todas as interfaces em um único arquivo `IServices.cs` para facilitar.

## 3.5 Core/Services - Lógica Real

Cada serviço implementa uma interface e faz operações reais.

Exemplo `SystemInfoService`:

```csharp
public class SystemInfoService : ISystemInfoService
{
    public async Task<SystemInfo> GetSystemInfoAsync()
    {
        return await Task.Run(() =>
        {
            var info = new SystemInfo();
            // OS via WMI
            var osQuery = WmiHelper.Query("SELECT Caption, Version FROM Win32_OperatingSystem");
            if (osQuery.Count > 0)
            {
                info.OsName = WmiHelper.GetPropertyString(osQuery[0], "Caption");
            }
            // CPU
            var cpuQuery = WmiHelper.Query("SELECT Name, NumberOfCores FROM Win32_Processor");
            // ... GPU, RAM via GlobalMemoryStatusEx, Disks via DriveInfo, etc.
            return info;
        });
    }
}
```

Princípios:
- `Task.Run` para não bloquear UI
- Try/catch para cada WMI query (WMI pode falhar)
- Dispose de ManagementBaseObject
- Fallbacks (se WMI falha, tenta DriveInfo, etc.)

## 3.6 Core/Utilities - Helpers Reutilizáveis

### AdminHelper
```csharp
public static bool IsAdministrator()
{
    using var identity = WindowsIdentity.GetCurrent();
    var principal = new WindowsPrincipal(identity);
    return principal.IsInRole(WindowsBuiltInRole.Administrator);
}
```

### RegistryHelper
Encapsula Registry com hive parsing:
```csharp
public static object? GetValue(string keyPath, string valueName, RegistryHive hive)
{
    using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
    using var key = baseKey.OpenSubKey(keyPath);
    return key?.GetValue(valueName);
}
```

### WmiHelper
```csharp
public static List<ManagementBaseObject> Query(string wql, string scope = @"root\cimv2")
{
    var result = new List<ManagementBaseObject>();
    using var searcher = new ManagementObjectSearcher(scope, wql);
    foreach (ManagementBaseObject obj in searcher.Get()) result.Add(obj);
    return result;
}
```

### NativeMethods
P/Invoke para APIs nativas:
```csharp
[DllImport("kernel32.dll")]
public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
```

### Converters
Para XAML: `BoolToVisibility`, `InverseBoolToVisibility`, `PercentageToBrush`, etc.

## 3.7 ViewModels - MVVM Correto

Base:
```csharp
public abstract class BaseViewModel : ObservableObject
{
    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
    public virtual Task InitializeAsync() => Task.CompletedTask;
}
```

Exemplo DashboardViewModel:
```csharp
public partial class DashboardViewModel : BaseViewModel
{
    private readonly ISystemInfoService _systemInfo;
    private readonly IOptimizationService _optimization;

    [ObservableProperty] private SystemInfo? _sysInfo;
    [ObservableProperty] private OptimizationIndex? _optIndex;
    [ObservableProperty] private bool _isOptimizing;

    public DashboardViewModel(ISystemInfoService systemInfo, IOptimizationService optimization)
    {
        _systemInfo = systemInfo;
        _optimization = optimization;
    }

    public override async Task InitializeAsync()
    {
        IsLoading = true;
        SysInfo = await _systemInfo.GetSystemInfoAsync();
        OptIndex = await _optimization.CalculateOptimizationIndexAsync();
        IsLoading = false;
    }

    [RelayCommand]
    private async Task ExecuteOptimization()
    {
        IsOptimizing = true;
        // Lógica...
        IsOptimizing = false;
    }
}
```

Pontos-chave:
- `[ObservableProperty]` gera propriedade com INotifyPropertyChanged
- `[RelayCommand]` gera ICommand
- Injeção de dependências via construtor
- Nunca acessa Registry/WMI direto, sempre via serviço

## 3.8 Views - XAML Puro

Views não têm lógica, apenas bindings:

```xml
<UserControl>
    <StackPanel>
        <TextBlock Text="{Binding SysInfo.CpuName}"/>
        <Button Command="{Binding ExecuteOptimizationCommand}" Content="EXECUTAR OTIMIZAÇÃO"/>
    </StackPanel>
</UserControl>
```

Code-behind apenas `InitializeComponent()`.

## 3.9 MainWindow - Composição

MainWindow.xaml tem:
- Header com logo, search, idioma, notificações, controles janela
- Sidebar com RadioButtons para navegação
- ContentControl com DataTemplates que mapeiam ViewModel -> View

```xml
<ContentControl Content="{Binding CurrentViewModel}">
    <ContentControl.Resources>
        <DataTemplate DataType="{x:Type viewModels:DashboardViewModel}">
            <views:DashboardView/>
        </DataTemplate>
        <!-- ... outros -->
    </ContentControl.Resources>
</ContentControl>
```

MainViewModel gerencia navegação:
```csharp
[RelayCommand]
private async Task Navigate(string page)
{
    CurrentViewModel = page switch
    {
        "Dashboard" => DashboardVM,
        "Booster" => BoosterVM,
        _ => DashboardVM
    };
    await CurrentViewModel.InitializeAsync();
}
```

## 3.10 App.xaml.cs - DI Setup

```csharp
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISystemInfoService, SystemInfoService>();
        services.AddSingleton<IPerformanceMonitorService, PerformanceMonitorService>();
        // ... todos os serviços
        services.AddSingleton<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        // ...

        Services = services.BuildServiceProvider();
        base.OnStartup(e);
    }
}
```

## 3.11 Fluxo de Dados

```
WMI/Registry/FileSystem -> Service -> ViewModel (ObservableProperty) -> View (Binding)
View (Button Command) -> ViewModel (RelayCommand) -> Service (Execute) -> LogService (SQLite)
```

Nunca: View -> Service direto. Sempre via ViewModel.

## 3.12 Vantagens desta Arquitetura

- **Testável**: pode mockar ISystemInfoService
- **Extensível**: adicionar novo serviço é criar interface + implementação + registrar no DI
- **Manutenível**: cada camada tem responsabilidade única
- **Profissional**: igual a produtos comerciais reais

Próximo capítulo: configurando ambiente e criando projeto do zero.
