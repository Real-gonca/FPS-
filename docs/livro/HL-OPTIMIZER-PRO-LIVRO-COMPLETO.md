# HL OPTIMIZER PRO
## O Livro Completo de Desenvolvimento

### Mais Desempenho. Mais Estabilidade. Mais Você.

**Subtítulo:** Do Zero ao Software Desktop Profissional para Windows

**Autor:** HL Software Engineering Team  
**Versão:** 1.0.0  
**Ano:** 2026  
**Tecnologia:** C# .NET 8 + WPF + MVVM + Windows APIs

---

> Este livro ensina como criar um software desktop profissional real para Windows, não um protótipo visual. Você vai aprender desde os fundamentos até a implementação completa de um otimizador empresarial com 12 módulos, dados reais do sistema e operações seguras.

**Público-alvo:**
- Desenvolvedores C# intermediários que querem ir para desktop profissional
- Engenheiros que querem aprender WPF moderno
- Quem quer criar ferramentas de sistema Windows reais

**O que você vai construir:**
Um software com Dashboard empresarial, Booster com 3 modos, Tweaks com 8 categorias, Limpeza profissional, Game Booster, Emulator Boost, Monitor em tempo real, Diagnóstico, Gerenciador de inicialização, Ferramentas avançadas, Restauração e Log Center.

---
# Capítulo 1: Introdução - Por que HL Optimizer Pro?

## 1.1 O Problema dos Otimizadores Falsos

90% dos "otimizadores" no mercado são:
- Interfaces bonitas sem funcionalidade real
- Números inventados: "Seu PC está 82% melhor que outros"
- Promessas absurdas: "+500% FPS"
- Dashboards que parecem painel de videogame
- Código desorganizado, sem MVVM, lógica no XAML

**HL Optimizer Pro nasce para ser diferente:** um produto comercial real, transparente, seguro e reversível.

## 1.2 O que é um Software Desktop Profissional?

Características:
1. **Janela desktop real** - não website, não Electron disfarçado
2. **Dados reais** - WMI, PerformanceCounter, Registry, APIs nativas
3. **Operações reais** - powercfg, netsh, reg.exe, serviços Windows
4. **Arquitetura profissional** - MVVM, DI, modular, testável
5. **Segurança** - confirmação, backup, reversibilidade, logs
6. **Transparência** - índice calculado com critérios claros, sem enganar usuário

## 1.3 Visão Geral do Projeto

```
HL.Optimizer.Pro
├── Core (coração do sistema)
│   ├── Models - dados
│   ├── Services - lógica de sistema
│   ├── Interfaces - contratos
│   └── Utilities - helpers
├── Optimization (otimizações específicas)
├── Monitoring (gráficos tempo real)
├── Diagnostics (verificações)
├── Gaming (booster)
├── Views (12 páginas WPF)
├── ViewModels (MVVM)
├── Themes (Dark premium)
└── Localization (4 idiomas)
```

## 1.4 O que Você Vai Aprender Neste Livro

- C# .NET 8 moderno (ImplicitUsings, Nullable, etc.)
- WPF com WindowChrome customizado
- MVVM correto com CommunityToolkit.Mvvm
- Dependency Injection com Microsoft.Extensions.DependencyInjection
- WMI (System.Management) para informações reais
- PerformanceCounter para métricas tempo real
- Registry para tweaks
- PowerShell/CMD apenas quando necessário
- SQLite para logs locais
- Powercfg, Netsh, Ipconfig para operações de sistema
- Design premium dark mode empresarial
- Segurança e reversibilidade

## 1.5 Metodologia

Não vamos criar apenas interface bonita. Cada capítulo:
1. Explica o conceito
2. Mostra o que precisa saber
3. Implementa com código real
4. Testa e valida

Vamos priorizar:
- clareza sobre efeitos
- confiança
- transparência
- velocidade
- usabilidade
- segurança
# Capítulo 2: O Que Você Precisa Saber Antes de Começar

## 2.1 Pré-requisitos Técnicos

### Linguagens
- **C# intermediário**: classes, interfaces, async/await, LINQ, generics, eventos
- **XAML básico**: bindings, resources, styles, templates
- **SQL básico**: para SQLite

### Conceitos Windows
- **Registry**: HKEY_LOCAL_MACHINE, HKEY_CURRENT_USER, tipos de valores (DWORD, String, Binary)
- **Serviços Windows**: StartType (Auto, Manual, Disabled)
- **WMI**: Windows Management Instrumentation - query language WQL
- **Power Plans**: GUIDs conhecidos (High Performance 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c)
- **Rede**: IP, Gateway, DNS, Ping, Winsock

### Ferramentas
- Windows 10 1903+ ou 11
- Visual Studio 2022 17.8+ com workload .NET Desktop
- .NET 8 SDK
- Git

## 2.2 Fundamentos C# .NET 8 que Você Precisa Dominar

### ImplicitUsings e Nullable
```csharp
// Antigo: using System; using System.Collections.Generic; etc.
// .NET 8 com ImplicitUsings: já vem implícito
// Nullable enable: obriga tratar null
public string? Nome { get; set; } // pode ser null
public string Titulo { get; set; } = ""; // nunca null
```

### Async/Await para Operações de Sistema
Toda operação WMI, Registry, File System deve ser async para não travar UI:
```csharp
public async Task<SystemInfo> GetSystemInfoAsync()
{
    return await Task.Run(() =>
    {
        // Operação pesada WMI aqui
        var query = WmiHelper.Query("SELECT * FROM Win32_Processor");
        // ...
    });
}
```

### Dependency Injection
```csharp
var services = new ServiceCollection();
services.AddSingleton<ISystemInfoService, SystemInfoService>();
services.AddSingleton<MainViewModel>();
var provider = services.BuildServiceProvider();
var vm = provider.GetRequiredService<MainViewModel>();
```

### LINQ para Filtros
```csharp
var lowDisks = disks.Where(d => d.FreePercent < 15).ToList();
var grouped = optimizations.GroupBy(o => o.Category);
```

## 2.3 WPF Moderno - O Que Realmente Importa

### WindowChrome Customizado
Para janela sem bordas padrão Windows, com header próprio:
```xml
<Window WindowStyle="None" AllowsTransparency="False">
    <WindowChrome.WindowChrome>
        <WindowChrome CaptionHeight="0" ResizeBorderThickness="6"/>
    </WindowChrome.WindowChrome>
</Window>
```
E no code-behind, DragMove() no MouseDown do header.

### ResourceDictionary e Temas
Crie `Themes/DarkTheme.xaml` com cores:
```xml
<Color x:Key="PrimaryDarkColor">#0A1628</Color>
<SolidColorBrush x:Key="PrimaryDarkBrush" Color="{StaticResource PrimaryDarkColor}"/>
<LinearGradientBrush x:Key="PrimaryGradientBrush" StartPoint="0,0" EndPoint="1,1">
    <GradientStop Color="#2D7FF9" Offset="0"/>
    <GradientStop Color="#1A5FCC" Offset="1"/>
</LinearGradientBrush>
```

### Styles para Botões Premium
```xml
<Style x:Key="PrimaryButton" TargetType="Button">
    <Setter Property="Background" Value="{StaticResource PrimaryGradientBrush}"/>
    <Setter Property="Foreground" Value="White"/>
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="Button">
                <Border Background="{TemplateBinding Background}" CornerRadius="10" Effect="{StaticResource ButtonShadow}">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Border>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

## 2.4 MVVM - O Padrão que Separa Amadores de Profissionais

### Errado (lógica no XAML code-behind):
```csharp
// MainWindow.xaml.cs
private void Button_Click(object sender, RoutedEventArgs e)
{
    // Acesso direto a Registry aqui - ERRADO!
    Registry.SetValue(...);
}
```

### Certo (MVVM):
```csharp
// ViewModel
[RelayCommand]
private async Task Optimize()
{
    await _optimizationService.ExecuteAsync();
}

// View
<Button Command="{Binding OptimizeCommand}"/>
```

### CommunityToolkit.Mvvm
```csharp
public partial class DashboardViewModel : ObservableObject
{
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isLoading;

    [RelayCommand]
    private async Task ExecuteOptimization() { ... }
}
```

## 2.5 WMI - Como Obter Dados Reais do Sistema

WMI é a API mais poderosa do Windows para informações de hardware/software.

Exemplo: CPU
```csharp
using System.Management;

var query = new ManagementObjectSearcher("SELECT Name, NumberOfCores FROM Win32_Processor");
foreach (ManagementObject obj in query.Get())
{
    string name = obj["Name"]?.ToString();
    uint cores = (uint)obj["NumberOfCores"];
}
```

Principais classes WMI que usamos:
- `Win32_OperatingSystem` - SO, versão, build, RAM
- `Win32_Processor` - CPU
- `Win32_VideoController` - GPU
- `Win32_BaseBoard` - Placa-mãe
- `Win32_BIOS` - BIOS
- `Win32_DiskDrive` - Discos
- `Win32_NetworkAdapter` - Rede
- `Win32_Service` - Serviços
- `Win32_PnPEntity` - Drivers com erro
- `Win32_DesktopMonitor` - Monitores

Crie um helper `WmiHelper` para encapsular:
```csharp
public static List<ManagementBaseObject> Query(string wql, string scope = @"root\cimv2")
{
    var result = new List<ManagementBaseObject>();
    using var searcher = new ManagementObjectSearcher(scope, wql);
    foreach (ManagementBaseObject obj in searcher.Get()) result.Add(obj);
    return result;
}
```

## 2.6 Registry - Tweaks com Segurança

Registry é onde ficam 90% dos tweaks Windows.

Leitura:
```csharp
using Microsoft.Win32;
using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects");
var value = key?.GetValue("VisualFXSetting");
```

Escrita segura com backup:
```csharp
// Backup antes
var backupDir = Path.Combine(LocalAppData, "HL Optimizer Pro", "Backups");
File.Copy(registryFile, backupDir + ".backup");

// Escrita
using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
using var k = baseKey.CreateSubKey(path, true);
k.SetValue(name, value, RegistryValueKind.DWord);
```

Sempre crie `RegistryHelper` com métodos `GetValue`, `SetValue`, `DeleteValue`, `KeyExists`.

## 2.7 PerformanceCounter - Métricas Tempo Real

```csharp
var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
cpuCounter.NextValue(); // primeira chamada sempre 0
Thread.Sleep(500);
float usage = cpuCounter.NextValue(); // valor real

// RAM
var memStatus = new MEMORYSTATUSEX();
GlobalMemoryStatusEx(ref memStatus);
long total = (long)memStatus.ullTotalPhys;
long avail = (long)memStatus.ullAvailPhys;
double ramUsage = (double)(total-avail)/total*100;
```

## 2.8 PowerShell e CMD Quando Necessário

Use apenas quando não há API nativa:

```csharp
var psi = new ProcessStartInfo
{
    FileName = "powercfg",
    Arguments = "/setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",
    UseShellExecute = false,
    CreateNoWindow = true
};
Process.Start(psi)?.WaitForExit();

// PowerShell
var ps = new ProcessStartInfo
{
    FileName = "powershell.exe",
    Arguments = "-NoProfile -Command \"Clear-RecycleBin -Force\"",
    UseShellExecute = false,
    CreateNoWindow = true
};
```

## 2.9 SQLite para Logs Locais

```csharp
using Microsoft.Data.Sqlite;

var dbPath = Path.Combine(LocalAppData, "HL Optimizer Pro", "logs.db");
using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();
var cmd = conn.CreateCommand();
cmd.CommandText = "CREATE TABLE IF NOT EXISTS Logs (Id INTEGER PRIMARY KEY, Action TEXT, Result TEXT)";
cmd.ExecuteNonQuery();
```

## 2.10 Segurança e Admin

Detectar admin:
```csharp
using var identity = WindowsIdentity.GetCurrent();
var principal = new WindowsPrincipal(identity);
bool isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
```

Pedir admin (app.manifest):
```xml
<requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
```

Ou reiniciar como admin:
```csharp
var psi = new ProcessStartInfo { FileName = exePath, Verb = "runas", UseShellExecute = true };
Process.Start(psi);
```

## 2.11 Resumo do que Precisa Saber

- C# async/await, DI, LINQ, Nullable
- WPF WindowChrome, ResourceDictionary, Styles, DataTemplates
- MVVM com ObservableObject e RelayCommand
- WMI queries para dados reais
- Registry leitura/escrita com backup
- PerformanceCounter para CPU/RAM/Disco
- PowerShell/CMD para operações sem API
- SQLite para persistência local
- Admin detection e UAC
- Design dark mode premium

Se domina isso, está pronto para Capítulo 3: Arquitetura.
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
# Capítulo 4: Configurando Ambiente e Criando Projeto do Zero

## 4.1 Instalação

1. **Windows 10 1903+ ou 11 x64**
2. **.NET 8 SDK**: https://dotnet.microsoft.com/download/dotnet/8.0
3. **Visual Studio 2022 17.8+** com workloads:
   - .NET Desktop Development
   - Git

Verifique:
```bash
dotnet --info
# Deve mostrar 8.0.x
```

## 4.2 Criando Solution

Via CLI:
```bash
mkdir HL.Optimizer.Pro
cd HL.Optimizer.Pro
dotnet new sln -n HL.Optimizer.Pro
dotnet new wpf -n HL.Optimizer.Pro -f net8.0-windows -o src/HL.Optimizer.Pro
dotnet sln add src/HL.Optimizer.Pro/HL.Optimizer.Pro.csproj
```

Via Visual Studio: New Project > WPF App (.NET) > Target .NET 8, nome HL.Optimizer.Pro

## 4.3 Configurando csproj

Edite `HL.Optimizer.Pro.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <Platforms>x64</Platforms>
    <ApplicationManifest>Properties\app.manifest</ApplicationManifest>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.2.2" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
    <PackageReference Include="System.Management" Version="8.0.0" />
    <PackageReference Include="System.Diagnostics.PerformanceCounter" Version="8.0.0" />
    <PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.0" />
  </ItemGroup>
</Project>
```

## 4.4 app.manifest para Admin

Crie `Properties/app.manifest`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges>
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
</assembly>
```

Isso faz Windows pedir UAC ao executar. Sem admin, otimizações de sistema falham.

## 4.5 Estrutura de Pastas

Crie via CLI ou Explorer:

```
src/HL.Optimizer.Pro/
├── Core/
│   ├── Models/
│   ├── Services/
│   ├── Interfaces/
│   └── Utilities/
├── Optimization/Services, Startup, Registry, Network, Power, Cleanup
├── Monitoring/
├── Diagnostics/
├── Gaming/
├── Views/
│   └── Controls/
├── ViewModels/
├── Themes/
├── Resources/Icons/
├── Localization/
└── Properties/
```

## 4.6 App.xaml e DI

`App.xaml`:
```xml
<Application StartupUri="MainWindow.xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Themes/DarkTheme.xaml"/>
                <ResourceDictionary Source="Themes/Converters.xaml"/>
                <ResourceDictionary Source="Themes/Controls.xaml"/>
                <ResourceDictionary Source="Localization/pt-BR.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

`App.xaml.cs`:
```csharp
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    protected override void OnStartup(StartupEventArgs e)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISystemInfoService, SystemInfoService>();
        // ... todos os serviços e ViewModels
        Services = services.BuildServiceProvider();
        base.OnStartup(e);
    }
}
```

## 4.7 Primeiro Build

```bash
dotnet restore
dotnet build -c Release -p:Platform=x64
```

Se compilar, ambiente está OK. Próximo capítulo: Core Models.
# Capítulo 5: Implementando o Core - Coração do Sistema

## 5.1 Models - Estruturas de Dados

### SystemInfo.cs
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

Crie também: PerformanceMetrics, OptimizationItem (com Id, Name, Category, Risk, ExecuteAction Func<Task<>>), TweakItem, CleanupItem, GameProfile, EmulatorInfo, StartupItem, LogEntry, DiagnosisResult.

### OptimizationItem - O Mais Importante
```csharp
public class OptimizationItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public OptimizationCategory Category { get; set; }
    public OptimizationRisk Risk { get; set; } = OptimizationRisk.Seguro;
    public bool Reversible { get; set; } = true;
    public bool RequiresAdmin { get; set; }
    public Func<Task<OptimizationResult>>? ExecuteAction { get; set; }
    public Func<Task<OptimizationResult>>? RevertAction { get; set; }
}
```

## 5.2 Interfaces - Contratos

`IServices.cs` com todas as interfaces. Exemplo:

```csharp
public interface ISystemInfoService
{
    Task<SystemInfo> GetSystemInfoAsync();
}
public interface IOptimizationService
{
    Task<List<OptimizationItem>> GetAvailableOptimizationsAsync();
    Task<OptimizationResult> ExecuteOptimizationAsync(OptimizationItem item);
}
```

## 5.3 Utilities - Helpers

### WmiHelper
```csharp
public static List<ManagementBaseObject> Query(string wql)
{
    var result = new List<ManagementBaseObject>();
    using var searcher = new ManagementObjectSearcher(@"root\cimv2", wql);
    foreach (ManagementBaseObject obj in searcher.Get()) result.Add(obj);
    return result;
}
```

### RegistryHelper
```csharp
public static bool SetValue(string keyPath, string valueName, object value, RegistryValueKind kind, RegistryHive hive)
{
    using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
    using var key = baseKey.CreateSubKey(keyPath, true);
    key.SetValue(valueName, value, kind);
    return true;
}
```

### AdminHelper, PowerShellHelper, NativeMethods, Converters

## 5.4 Services - Lógica Real

### SystemInfoService
```csharp
public async Task<SystemInfo> GetSystemInfoAsync()
{
    return await Task.Run(() =>
    {
        var info = new SystemInfo();
        var osQuery = WmiHelper.Query("SELECT Caption, Version FROM Win32_OperatingSystem");
        if (osQuery.Count > 0) info.OsName = WmiHelper.GetPropertyString(osQuery[0], "Caption");
        var cpuQuery = WmiHelper.Query("SELECT Name, NumberOfCores FROM Win32_Processor");
        // ... etc
        foreach (var o in osQuery) o.Dispose();
        return info;
    });
}
```

### PerformanceMonitorService
```csharp
public class PerformanceMonitorService : IPerformanceMonitorService
{
    private PerformanceCounter? _cpuCounter;
    private Timer? _timer;
    public event EventHandler<PerformanceMetrics>? MetricsUpdated;

    public void StartMonitoring(int intervalMs = 1000)
    {
        _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        _cpuCounter.NextValue(); Thread.Sleep(500);
        _timer = new Timer(OnTimer, null, 0, intervalMs);
    }
    private void OnTimer(object? state)
    {
        var metrics = new PerformanceMetrics { CpuUsage = _cpuCounter.NextValue() };
        // RAM via GlobalMemoryStatusEx, etc.
        MetricsUpdated?.Invoke(this, metrics);
    }
}
```

### OptimizationService
Lista de otimizações com ações reais:
```csharp
items.Add(new OptimizationItem
{
    Id = "power_highperf",
    Name = "Ativar plano Alto Desempenho",
    Category = OptimizationCategory.Energia,
    ExecuteAction = async () => { await _power.SetHighPerformanceAsync(); return new OptimizationResult { Success = true }; }
});
```

### CleanupService
```csharp
public async Task<List<CleanupItem>> AnalyzeAsync()
{
    var items = new List<CleanupItem>();
    items.AddRange(AnalyzePath(Path.GetTempPath(), "Temp Usuário", ...));
    // Browser caches, Recycle Bin, etc.
    return items;
}
private (long, int) CleanDirectory(string path)
{
    long freed = 0; int count = 0;
    foreach (var file in new DirectoryInfo(path).GetFiles("*", SearchOption.AllDirectories))
    {
        if (file.LastWriteTime > DateTime.Now.AddDays(-1)) continue; // segurança
        freed += file.Length; file.Delete(); count++;
    }
    return (freed, count);
}
```

### Outros Services
- StartupService: Registry Run + Startup Folder + Task Scheduler
- NetworkService: NetworkInterface.GetAllNetworkInterfaces, Ping, netsh
- PowerService: powercfg /list, /getactivescheme, /setactive
- GameDetectionService: Registry Uninstall + file scan Steam
- EmulatorDetectionService: paths conhecidos BlueStacks etc.
- RestoreService: WMI SystemRestore CreateRestorePoint + rstrui.exe
- LogService: SQLite + fallback arquivo
- DiagnosisService: checks disco, RAM, drivers, rede

Cada serviço deve ter try/catch e fallback.

Próximo: ViewModels e Views.
# Capítulo 6: UI Premium - Dark Mode Empresarial

## 6.1 Paleta de Cores

Defina em `Themes/DarkTheme.xaml`:

```xml
<Color x:Key="PrimaryDarkColor">#0A1628</Color> <!-- fundo principal -->
<Color x:Key="SecondaryDarkColor">#111F3A</Color> <!-- sidebar, header -->
<Color x:Key="CardDarkColor">#162447</Color> <!-- cards -->
<Color x:Key="SurfaceDarkColor">#1B2E5C</Color> <!-- inputs, surface -->
<Color x:Key="BorderDarkColor">#243A6B</Color> <!-- bordas discretas -->
<Color x:Key="ElectricBlueColor">#2D7FF9</Color> <!-- primária -->
<Color x:Key="AccentGreenColor">#00D26A</Color> <!-- sucesso -->

<SolidColorBrush x:Key="PrimaryDarkBrush" Color="{StaticResource PrimaryDarkColor}"/>
<!-- ... -->

<LinearGradientBrush x:Key="PrimaryGradientBrush" StartPoint="0,0" EndPoint="1,1">
    <GradientStop Color="#2D7FF9" Offset="0"/>
    <GradientStop Color="#1A5FCC" Offset="1"/>
</LinearGradientBrush>

<DropShadowEffect x:Key="CardShadow" BlurRadius="20" ShadowDepth="0" Opacity="0.3" Color="#000000"/>
<CornerRadius x:Key="LargeCorner">14</CornerRadius>
```

## 6.2 Controles Premium

### PrimaryButton
```xml
<Style x:Key="PrimaryButton" TargetType="Button">
    <Setter Property="Background" Value="{StaticResource PrimaryGradientBrush}"/>
    <Setter Property="Foreground" Value="White"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="Button">
                <Border Background="{TemplateBinding Background}" CornerRadius="10" Effect="{StaticResource ButtonShadow}" Padding="{TemplateBinding Padding}">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter Property="Background" Value="{StaticResource ElectricBlueLightBrush}"/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

Crie também: SuccessButton (green gradient), SecondaryButton (surface + border), IconButton (36x36 transparent), CardBorder, SidebarButton (RadioButton com Tag icon), SearchTextBox, ModernProgressBar (6px height), ModernToggle (44x24), ScrollBar (6px).

### SidebarButton
```xml
<Style x:Key="SidebarButton" TargetType="RadioButton">
    <Setter Property="Height" Value="42"/>
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="RadioButton">
                <Border x:Name="Bd" Background="Transparent" CornerRadius="10" Padding="16,0">
                    <Grid>
                        <Grid.ColumnDefinitions><ColumnDefinition Width="22"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                        <TextBlock x:Name="Icon" Text="{TemplateBinding Tag}" FontSize="14" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                        <ContentPresenter Grid.Column="1" VerticalAlignment="Center" Margin="12,0,0,0"/>
                    </Grid>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsChecked" Value="True">
                        <Setter TargetName="Bd" Property="Background" Value="{StaticResource ElectricBlueBrush}"/>
                        <Setter Property="Foreground" Value="White"/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

## 6.3 MainWindow - Header + Sidebar + Content

Estrutura:
```xml
<Window WindowStyle="None">
    <Grid>
        <Grid.RowDefinitions><RowDefinition Height="64"/><RowDefinition Height="*"/></Grid.RowDefinitions>
        <!-- HEADER -->
        <Border Grid.Row="0" Background="{StaticResource SecondaryDarkBrush}">
            <Grid>
                <Grid.ColumnDefinitions><ColumnDefinition Width="280"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                <!-- Logo HL + Title -->
                <!-- Search Box -->
                <!-- Language Combo + Notifications + Settings + Min/Max/Close -->
            </Grid>
        </Border>
        <!-- MAIN -->
        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions><ColumnDefinition Width="280"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
            <!-- SIDEBAR -->
            <Border Grid.Column="0" Background="{StaticResource SecondaryDarkBrush}">
                <ScrollViewer>
                    <StackPanel>
                        <TextBlock Text="PRINCIPAL" Style="SectionHeader"/>
                        <RadioButton Style="{StaticResource SidebarButton}" Content="Painel Principal" Tag="🏠" Command="{Binding NavigateCommand}" CommandParameter="Dashboard"/>
                        <!-- ... 12 itens -->
                    </StackPanel>
                </ScrollViewer>
            </Border>
            <!-- CONTENT -->
            <ContentControl Grid.Column="1" Content="{Binding CurrentViewModel}">
                <ContentControl.Resources>
                    <DataTemplate DataType="{x:Type vm:DashboardViewModel}"><views:DashboardView/></DataTemplate>
                </ContentControl.Resources>
            </ContentControl>
        </Grid>
    </Grid>
</Window>
```

Code-behind para drag:
```csharp
MouseDown += (s,e) => { if (e.GetPosition(this).Y < 64) DragMove(); };
```

## 6.4 Dashboard - O Cartão de Visita

Layout:
- Top row: Health Card (circular progress) + Metrics (CPU/RAM/GPU/Disco) + System Info mini
- Quick Actions: 6 atalhos em UniformGrid
- Bottom: Categories + Last Actions

Health Card com Ellipse e StrokeDashArray para progresso circular.

Metrics com 4 cards SurfaceDark, cada com ícone colorido, valor grande e ProgressBar.

## 6.5 Outros Views

Cada view segue mesmo padrão:
- Título grande branco Bold 22px
- Subtítulo secondary 12px
- CardBorder com seções
- Uso de SurfaceDark para itens internos

Exemplo BoosterView: 3 cards para modos Econômico/Equilibrado/Desempenho, cada com borda destacada quando SelectedMode ==.

TweaksView: WrapPanel de cards 320px width, cada com estado atual/recomendado, risco, botões Aplicar/Reverter.

CleanupView: Lista com CheckBox, nome, path, tamanho, categoria, total no header, botão Limpar.

GamesView: Lista jogos + emuladores esquerda, detalhes direita com booster settings CheckBoxes.

## 6.6 Converters

Crie `Converters.xaml` com:
- CountToVisibility
- InverseBoolToVisibility
- BoolToVisibility
- PercentageToBrush (verde >=80, amarelo >=50, vermelho <50)
- StringToVisibility (Personalizado)
- InverseBool
- PercentageToWidth
- BoolToColor

## 6.7 Tipografia e Espaçamento

- Fonte: Segoe UI (padrão Windows)
- Títulos: 22px Bold
- Section headers: 10px Bold LetterSpacing 1, Tertiary color
- Body: 11-12px
- Padding cards: 20px
- Margin entre cards: 12-16px
- CornerRadius: 10-14px

Evite:
- Excesso gradientes (use só em primary buttons)
- Excesso efeitos
- Textos exagerados
- Emojis demais (use com moderação para ícones)

Próximo: implementando módulos específicos.
# Capítulo 7: Implementando os 12 Módulos

## 7.1 DashboardViewModel - O Cérebro

```csharp
public partial class DashboardViewModel : BaseViewModel
{
    private readonly ISystemInfoService _systemInfo;
    private readonly IOptimizationService _optimization;

    [ObservableProperty] private SystemInfo? _sysInfo;
    [ObservableProperty] private OptimizationIndex? _optIndex;
    [ObservableProperty] private bool _isOptimizing;
    [ObservableProperty] private double _optimizationProgress;

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
        // 1. Perguntar ponto de restauração
        var result = MessageBox.Show("Criar ponto de restauração?", "HL Optimizer", MessageBoxButton.YesNoCancel);
        if (result == MessageBoxResult.Cancel) return;
        if (result == MessageBoxResult.Yes)
        {
            await _restore.CreateRestorePointAsync($"HL Optimizer {DateTime.Now}");
        }

        // 2. Executar
        IsOptimizing = true;
        var selected = Optimizations.Where(o => o.IsSelected).ToList();
        var progress = new Progress<OptimizationProgress>(p => OptimizationProgress = p.Percentage);
        var results = await _optimization.ExecuteOptimizationsAsync(selected, progress);
        IsOptimizing = false;
    }
}
```

Índice transparente:
```csharp
public async Task<OptimizationIndex> CalculateOptimizationIndexAsync()
{
    var items = await GetAvailableOptimizationsAsync();
    int completed = 0;
    foreach (var item in items) if (await IsAppliedAsync(item)) completed++;
    return new OptimizationIndex { TotalOptimizations = items.Count, CompletedOptimizations = completed };
}
```

## 7.2 Booster - 3 Modos

ViewModel:
```csharp
[RelayCommand]
private async Task ApplyMode(string mode)
{
    SelectedMode = mode;
    switch (mode)
    {
        case "Econômico": await _power.SetPowerSavingAsync(); break;
        case "Equilibrado": await _power.SetBalancedAsync(); break;
        case "Desempenho": await _power.SetHighPerformanceAsync(); break;
    }
    ActivePowerPlan = await _power.GetActivePowerPlanAsync();
}
```

PowerService:
```csharp
public async Task SetPowerPlanAsync(string guid)
{
    var psi = new ProcessStartInfo { FileName = "powercfg", Arguments = $"/setactive {guid}", UseShellExecute = false, CreateNoWindow = true };
    Process.Start(psi)?.WaitForExit(5000);
}
public string HighPerformanceGuid => "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
```

## 7.3 Tweaks - 8 Categorias

LoadTweaks:
```csharp
private async Task<List<TweakItem>> LoadTweaksAsync()
{
    return new List<TweakItem>
    {
        new() { Id="tweak_anim", Name="Desativar animações", Category=Interface, RecommendedState="Desativado", CurrentState="Ativo", Risk=Seguro },
        new() { Id="tweak_menudelay", Name="Acelerar menus", Category=Interface, RecommendedState="0ms", CurrentState="400ms" },
        // ... 14 tweaks
    };
}
```

Filtro:
```csharp
partial void OnSelectedCategoryChanged(string value) => Filter();
private void Filter()
{
    var query = AllTweaks.AsEnumerable();
    if (SelectedCategory != "Todos") query = query.Where(t => t.Category.ToString() == SelectedCategory);
    if (!string.IsNullOrWhiteSpace(SearchQuery)) query = query.Where(t => t.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
    FilteredTweaks = new ObservableCollection<TweakItem>(query);
}
```

## 7.4 Limpeza - Análise Real

```csharp
public async Task<List<CleanupItem>> AnalyzeAsync()
{
    var items = new List<CleanupItem>();
    items.AddRange(AnalyzePath(Path.GetTempPath(), "Temp Usuário", ...));
    items.AddRange(AnalyzePath(Path.Combine(Windows, "Temp"), "Temp Windows", ...));
    items.AddRange(AnalyzePath(Path.Combine(LocalAppData, "Microsoft", "Windows", "Explorer"), "Thumbnails", ..., "thumbcache_*.db"));
    // Browser caches
    items.AddRange(AnalyzePath(Path.Combine(LocalAppData, "Google", "Chrome", "User Data", "Default", "Cache"), "Chrome Cache", ...));
    // Recycle Bin
    var recycleSize = GetRecycleBinSizeSync();
    if (recycleSize > 0) items.Add(new CleanupItem { Name="Lixeira", SizeBytes=recycleSize, Category=RecycleBin });
    return items.Where(i => i.SizeBytes > 0).ToList();
}

private List<CleanupItem> AnalyzePath(string path, string name, string desc, CleanupCategory cat, string pattern="*")
{
    if (!Directory.Exists(path)) return new List<CleanupItem>();
    var di = new DirectoryInfo(path);
    long size = 0; int count = 0;
    try
    {
        var files = di.GetFiles(pattern, SearchOption.AllDirectories);
        foreach (var file in files)
        {
            if (file.LastWriteTime > DateTime.Now.AddDays(-1)) continue; // segurança
            size += file.Length; count++;
        }
    }
    catch {}
    if (size > 0) return new List<CleanupItem> { new() { Name=name, Description=desc, Path=path, SizeBytes=size, FileCount=count, Category=cat } };
    return new List<CleanupItem>();
}
```

Limpeza:
```csharp
public async Task<CleanupResult> CleanupAsync(IEnumerable<CleanupItem> items)
{
    long totalFreed = 0; int totalFiles = 0;
    foreach (var item in items.Where(i => i.IsSelected))
    {
        if (item.Category == RecycleBin) await ClearRecycleBinAsync();
        else { var (freed, files) = CleanDirectory(item.Path); totalFreed += freed; totalFiles += files; }
    }
    return new CleanupResult { TotalFreedBytes=totalFreed, TotalFilesDeleted=totalFiles };
}
```

## 7.5 Jogos - Detecção Real

```csharp
public async Task<List<GameProfile>> DetectGamesAsync()
{
    var games = new List<GameProfile>();
    // Registry Uninstall
    var uninstallKeys = new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" };
    foreach (var keyPath in uninstallKeys)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
        using var key = baseKey.OpenSubKey(keyPath);
        foreach (var subKeyName in key.GetSubKeyNames())
        {
            using var subKey = key.OpenSubKey(subKeyName);
            var displayName = subKey.GetValue("DisplayName")?.ToString() ?? "";
            if (displayName.ToLower().Contains("valorant")) games.Add(new GameProfile { Name=displayName, Type=Valorant });
        }
    }
    // File system Steam
    var steamPath = Path.Combine(ProgramFilesX86, "Steam", "steamapps", "common");
    if (Directory.Exists(steamPath))
    {
        foreach (var dir in Directory.GetDirectories(steamPath))
        {
            var exe = Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (exe != null) games.Add(new GameProfile { Name=Path.GetFileName(dir), ExecutablePath=exe });
        }
    }
    return games;
}
```

Game Booster:
```csharp
public async Task<bool> ApplyGameBoosterAsync(GameProfile game)
{
    if (game.BoosterSettings.HighPerformancePowerPlan) await _power.SetHighPerformanceAsync();
    var procs = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(game.ExecutablePath));
    foreach (var proc in procs) { try { proc.PriorityClass = ProcessPriorityClass.High; } catch {} }
    // Suspender não essenciais
    foreach (var name in new[] { "onedrive", "searchindexer" })
    {
        foreach (var p in Process.GetProcessesByName(name)) { try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch {} }
    }
    return true;
}
```

## 7.6 Emuladores

Paths conhecidos:
```csharp
var emulatorPaths = new Dictionary<EmulatorType, string[]>
{
    { BlueStacks, new[] { @"C:\Program Files\BlueStacks_nxt", @"C:\Program Files\BlueStacks" } },
    { LDPlayer, new[] { @"C:\LDPlayer", @"C:\Program Files\LDPlayer" } },
    // ...
};
```

Detecta via Directory.Exists + GetFiles exe + Process.GetProcesses.

Apply profile com backup:
```csharp
var configPath = Path.Combine(CommonApplicationData, "BlueStacks_nxt", "bluestacks.conf");
if (File.Exists(configPath)) File.Copy(configPath, configPath + ".backup", true);
// Não modifica sem confirmação do usuário - segurança
```

## 7.7 Inicialização

```csharp
public async Task<List<StartupItem>> GetStartupItemsAsync()
{
    var items = new List<StartupItem>();
    items.AddRange(GetRegistryStartupItems(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", RegistryHive.CurrentUser, RegistryCurrentUser));
    items.AddRange(GetRegistryStartupItems(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", RegistryHive.LocalMachine, RegistryLocalMachine));
    items.AddRange(GetStartupFolderItems()); // Environment.SpecialFolder.Startup
    return items;
}

private bool IsStartupEnabled(string name, RegistryHive hive)
{
    using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
    using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
    var data = key?.GetValue(name) as byte[];
    if (data != null && data.Length > 0) return data[0] == 2; // 2=enabled, 3=disabled
    return true;
}

public async Task<bool> SetStartupItemEnabledAsync(StartupItem item, bool enabled)
{
    using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
    using var key = baseKey.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", true);
    byte[] data = enabled ? new byte[] { 2,0,0,0,0,0,0,0,0,0,0,0 } : new byte[] { 3,0,0,0,0,0,0,0,0,0,0,0 };
    key.SetValue(item.Name, data, RegistryValueKind.Binary);
    return true;
}
```

## 7.8 Monitor

```csharp
public class PerformanceMonitorService
{
    private Timer? _timer;
    public void StartMonitoring(int intervalMs = 1000)
    {
        InitializeCounters();
        _timer = new Timer(OnTimer, null, 0, intervalMs);
    }
    private void OnTimer(object? state)
    {
        var metrics = new PerformanceMetrics
        {
            CpuUsage = _cpuCounter.NextValue(),
            RamUsage = CalculateRamUsage(),
            DiskReadMBps = _diskReadCounter.NextValue() / (1024*1024),
            // ...
        };
        MetricsUpdated?.Invoke(this, metrics);
        History.Add(metrics);
    }
}
```

View mostra ItemsControl de barras com Height binding para CpuUsage.

## 7.9 Sistema

Simples: chama SystemInfoService e mostra em cards.

## 7.10 Diagnóstico

```csharp
public async Task<List<DiagnosisResult>> RunDiagnosisAsync()
{
    var results = new List<DiagnosisResult>();
    results.Add(await CheckDiskHealthAsync());
    results.Add(await CheckMemoryAsync());
    results.Add(await CheckNetworkAsync());
    // ...
    return results;
}

public async Task<DiagnosisResult> CheckDiskHealthAsync()
{
    var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed);
    foreach (var drive in drives)
    {
        if (drive.TotalFreeSpace < 5L*1024*1024*1024)
            return new DiagnosisResult { Title="Pouco espaço", Severity=Warning, Problem=$"Unidade {drive.Name} com apenas {drive.TotalFreeSpace/(1024*1024*1024)}GB" };
    }
    // SMART via WMI Win32_DiskDrive Status != OK
    return new DiagnosisResult { Title="Disco saudável", Severity=Info };
}
```

## 7.11 Rede

```csharp
public async Task<NetworkInfo> GetNetworkInfoAsync()
{
    var interfaces = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == Up && n.NetworkInterfaceType != Loopback);
    var active = interfaces.FirstOrDefault();
    var props = active.GetIPProperties();
    var ipv4 = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == InterNetwork);
    return new NetworkInfo { IpAddress=ipv4.Address.ToString(), Gateway=props.GatewayAddresses.FirstOrDefault()?.Address.ToString(), DnsPrimary=props.DnsAddresses.FirstOrDefault()?.ToString() };
}

public async Task FlushDnsAsync()
{
    var psi = new ProcessStartInfo { FileName="ipconfig", Arguments="/flushdns", UseShellExecute=false, CreateNoWindow=true };
    Process.Start(psi)?.WaitForExit(5000);
}

public async Task SetDnsAsync(string primary, string secondary)
{
    var ni = NetworkInterface.GetAllNetworkInterfaces().First(n => n.OperationalStatus == Up);
    var psi = new ProcessStartInfo { FileName="netsh", Arguments=$"interface ip set dns \"{ni.Name}\" static {primary}", UseShellExecute=false, CreateNoWindow=true };
    Process.Start(psi)?.WaitForExit();
}
```

## 7.12 Ferramentas

```csharp
[RelayCommand] private void OpenTaskManager() => Launch("taskmgr.exe");
private void Launch(string file)
{
    Process.Start(new ProcessStartInfo { FileName=file, UseShellExecute=true });
}
```

Lista de 13 ferramentas: taskmgr, devmgmt.msc, diskmgmt.msc, eventvwr.msc, services.msc, msconfig, regedit, control, powershell, cmd, msinfo32, resmon, taskschd.msc

## 7.13 Restauração

```csharp
public async Task<bool> CreateRestorePointAsync(string description)
{
    try
    {
        using var mClass = new ManagementClass(@"\\.\\root\\default:SystemRestore");
        using var mMethod = mClass.GetMethodParameters("CreateRestorePoint");
        mMethod["Description"] = description;
        mMethod["RestorePointType"] = 12; // MODIFY_SETTINGS
        mMethod["EventType"] = 100;
        using var result = mClass.InvokeMethod("CreateRestorePoint", mMethod, null);
        return (uint)result["ReturnValue"] == 0;
    }
    catch
    {
        // Fallback PowerShell
        var psi = new ProcessStartInfo { FileName="powershell.exe", Arguments=$"-NoProfile -Command \"Checkpoint-Computer -Description '{description}' -RestorePointType MODIFY_SETTINGS\"", UseShellExecute=false, CreateNoWindow=true };
        using var proc = Process.Start(psi); proc?.WaitForExit(30000); return proc?.ExitCode == 0;
    }
}

public void OpenSystemRestore() => Process.Start(new ProcessStartInfo { FileName="rstrui.exe", UseShellExecute=true });
```

## 7.14 Configurações e Logs

LogService com SQLite:
```csharp
public void Initialize()
{
    var appData = Path.Combine(LocalApplicationData, "HL Optimizer Pro");
    Directory.CreateDirectory(appData);
    _dbPath = Path.Combine(appData, "logs.db");
    using var conn = new SqliteConnection($"Data Source={_dbPath}");
    conn.Open();
    conn.CreateCommand().CommandText = "CREATE TABLE IF NOT EXISTS Logs (Id INTEGER PRIMARY KEY, Timestamp TEXT, Action TEXT, Result TEXT)".ExecuteNonQuery();
}
```

Próximo capítulo: segurança.
# Capítulo 8: Segurança, Logs e Boas Práticas

## 8.1 Princípios de Segurança

1. **Nunca destrutivo sem confirmação**
```csharp
var confirm = MessageBox.Show($"Deseja limpar {selected.Count} itens?\nTotal: {total}", "Confirmar Limpeza", MessageBoxButton.YesNo, MessageBoxImage.Warning);
if (confirm != MessageBoxResult.Yes) return;
```

2. **Toda otimização rastreável**
```csharp
public class OptimizationItem
{
    public string Id { get; set; }
    public string Name { get; set; }
    public OptimizationRisk Risk { get; set; } // Seguro, Moderado, Avancado
    public bool Reversible { get; set; }
    public bool RequiresAdmin { get; set; }
    public string Command { get; set; }
    public string RevertCommand { get; set; }
}
```

3. **Reversível quando possível**
```csharp
ExecuteAction = () => SetServiceStartAsync("SysMain", 4), // Disabled
RevertAction = () => SetServiceStartAsync("SysMain", 2) // Auto
```

4. **Backup antes de alterar**
```csharp
public async Task BackupRegistryValueAsync(string keyPath, string valueName)
{
    var backupDir = Path.Combine(LocalAppData, "HL Optimizer Pro", "Backups");
    Directory.CreateDirectory(backupDir);
    var fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{keyPath.Replace("\\", "_")}_{valueName}.reg";
    var psi = new ProcessStartInfo { FileName="reg.exe", Arguments=$"export \"{keyPath}\" \"{Path.Combine(backupDir, fileName)}\" /y", UseShellExecute=false, CreateNoWindow=true };
    Process.Start(psi)?.WaitForExit(5000);
}
```

5. **Admin check**
```csharp
if (item.RequiresAdmin && !AdminHelper.IsAdministrator())
{
    return new OptimizationResult { Success=false, Message="Requer privilégios administrativos" };
}
```

6. **Nunca inventar resultados**
Se não pode implementar com segurança, informe motivo e desative:
```csharp
if (!File.Exists(configPath))
{
    StatusMessage = "Arquivo de configuração não encontrado - funcionalidade indisponível";
    return false;
}
```

## 8.2 Sistema de Logs

```csharp
public async Task LogAsync(string action, string category, string result, string details="", string command="", string? error=null, long? freedBytes=null)
{
    using var conn = new SqliteConnection($"Data Source={_dbPath}");
    conn.Open();
    var cmd = conn.CreateCommand();
    cmd.CommandText = "INSERT INTO Logs (Timestamp, Action, Category, Result, Details, Command, User, Error, FreedBytes) VALUES ($ts, $action, $cat, $result, $details, $cmd, $user, $err, $freed)";
    cmd.Parameters.AddWithValue("$ts", DateTime.Now.ToString("o"));
    cmd.Parameters.AddWithValue("$action", action);
    // ...
    cmd.ExecuteNonQuery();
}
```

Fallback para arquivo se SQLite falha:
```csharp
catch
{
    var logFile = Path.Combine(Path.GetDirectoryName(_dbPath), "hl_optimizer.log");
    File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {category} | {action} | {result} | {details}\n");
}
```

## 8.3 Restauração

Antes de otimizações importantes:
```csharp
var result = MessageBox.Show("Deseja criar um ponto de restauração antes de otimizar?\nRecomendado para segurança.", "HL Optimizer Pro", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
if (result == MessageBoxResult.Yes)
{
    var restoreCreated = await _restore.CreateRestorePointAsync($"HL Optimizer - Otimização {DateTime.Now:dd/MM/yyyy HH:mm}");
    if (!restoreCreated) MessageBox.Show("Não foi possível criar ponto de restauração. Continuando mesmo assim.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
}
```

## 8.4 O Que Nunca Fazer

- ❌ `+500% FPS` - nunca prometa FPS específico
- ❌ `82% melhor que outros computadores` - percentual deve ser índice transparente calculado pelo próprio software
- ❌ Apagar arquivos pessoais automaticamente
- ❌ Desativar componentes críticos automaticamente
- ❌ Alterar configurações perigosas sem confirmação
- ❌ Enviar dados pessoais para servidores sem consentimento
- ❌ Simular resultados

## 8.5 O Que Sempre Fazer

- ✅ Clareza, confiança, transparência, velocidade, usabilidade, segurança
- ✅ Explicar o que será alterado em cada modo (Econômico/Equilibrado/Desempenho)
- ✅ Mostrar espaço recuperável real
- ✅ Métricas reais CPU/RAM/GPU/Disco/Rede
- ✅ Logs com data, hora, ação, resultado, erro, comando, usuário
- ✅ Reversível quando possível

Próximo: Build e Deploy.
# Capítulo 9: Build, Deploy e Distribuição

## 9.1 Build

```bash
dotnet restore HL.Optimizer.Pro.sln
dotnet build HL.Optimizer.Pro.sln -c Release -p:Platform=x64
```

Saída: `src/HL.Optimizer.Pro/bin/x64/Release/net8.0-windows/HL.Optimizer.Pro.exe`

## 9.2 Publicar Single File

```bash
dotnet publish src/HL.Optimizer.Pro/HL.Optimizer.Pro.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Gera um único exe com tudo incluso.

## 9.3 Instalador (Opcional)

Use Inno Setup ou WiX Toolset para criar instalador MSI.

Exemplo Inno Setup script:
```ini
[Setup]
AppName=HL Optimizer Pro
AppVersion=1.0.0
DefaultDirName={pf}\HL Optimizer Pro
OutputBaseFilename=HL-Optimizer-Pro-Setup

[Files]
Source: "publish\HL.Optimizer.Pro.exe"; DestDir: "{app}"
Source: "README.md"; DestDir: "{app}"

[Icons]
Name: "{group}\HL Optimizer Pro"; Filename: "{app}\HL.Optimizer.Pro.exe"
```

## 9.4 Assinatura de Código

Para evitar SmartScreen, assine com certificado EV:
```bash
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f cert.pfx /p password HL.Optimizer.Pro.exe
```

## 9.5 Atualizações

Implemente sistema de atualização:
- Verificar versão em servidor
- Baixar novo exe
- Substituir com reinício

Ou use Squirrel.Windows, ClickOnce.

## 9.6 Distribuição

- GitHub Releases com zip
- Site próprio com download
- Microsoft Store (requer MSIX)

Crie `dist/HL-OPTIMIZER-PRO-v1.0.0-SOURCE.zip` com:
- sln, src, docs, README

## 9.7 Testes

Teste em:
- Windows 10 1903, 21H2, 22H2
- Windows 11 21H2, 22H2, 23H2
- Com e sem admin
- Com BlueStacks/LDPlayer instalado e sem
- Com jogos Steam e sem

Verifique:
- Dados reais aparecem (não fictícios)
- Botões executam operações reais
- Logs registram
- Pontos de restauração criam
- Limpeza calcula tamanho real
- Sem crash

Próximo: Conclusão.
# Capítulo 10: Conclusão - De Projeto a Produto Comercial

## 10.1 O Que Construímos

Um software desktop profissional real:
- 92 arquivos, 8748 linhas
- 12 módulos
- Arquitetura MVVM modular
- Dados reais, operações reais
- UI premium dark mode
- Segurança e transparência

Diferencial vs protótipos:
- ✅ Janela desktop real WPF, não website
- ✅ Dados reais WMI/PerformanceCounter/Registry
- ✅ Operações reais powercfg/netsh/reg
- ✅ MVVM correto, DI, modular
- ✅ Logs SQLite, pontos restauração reais
- ✅ Detecção real jogos/emuladores
- ✅ Limpeza real com cálculo tamanho
- ✅ Segurança: confirmação, reversibilidade, admin check

## 10.2 Próximos Passos

- LiveCharts para gráficos bonitos
- SQLite para perfis jogos/emuladores
- Auto-updater
- Tray icon minimize to tray
- Mais tweaks registry com backup
- Disk cleanup mais profundo
- Análise de drivers desatualizados
- Integração com Windows Update API
- Temas claro/escuro
- Mais idiomas

## 10.3 Como Monetizar

- Versão gratuita com funcionalidades básicas
- Pro com booster avançado, limpeza profunda, suporte
- Licença por máquina
- Assinatura anual

## 10.4 Lições Aprendidas

1. **Transparência vende**: índice claro é melhor que "82% melhor que outros"
2. **Segurança é diferencial**: backup e reversibilidade geram confiança
3. **Dados reais importam**: usuários percebem quando números são inventados
4. **Arquitetura importa**: MVVM permite crescer sem virar spaghetti
5. **Design empresarial**: dark mode premium passa confiança, não painel de jogo

## 10.5 Recursos para Continuar

- Livros: WPF 4.5 Unleashed, MVVM Survival Guide
- Docs: Microsoft Learn WPF, WMI, Registry
- Comunidade: r/csharp, r/dotnet, StackOverflow

## 10.6 Mensagem Final

Você agora sabe criar um software desktop profissional real para Windows. Não apenas interface bonita, mas produto comercial com:

- clareza
- confiança
- transparência
- velocidade
- usabilidade
- segurança

O código está em `src/HL.Optimizer.Pro/` pronto para compilar e distribuir.

**HL OPTIMIZER PRO - Mais Desempenho. Mais Estabilidade. Mais Você.**

---

**Fim do Livro.**

*© 2026 HL Software - Todos os direitos reservados*
