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
