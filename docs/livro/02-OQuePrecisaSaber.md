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
