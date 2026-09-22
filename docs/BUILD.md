# Build Instructions - HL OPTIMIZER PRO

## Requisitos
- Windows 10 1903+ ou Windows 11 (x64)
- .NET 8 SDK (8.0.0+)
- Visual Studio 2022 17.8+ com workload .NET Desktop

## Clonar
```bash
git clone https://github.com/Real-gonca/FPS-.git
cd FPS-
```

## Restaurar Pacotes
```bash
dotnet restore HL.Optimizer.Pro.sln
```

## Build Debug
```bash
dotnet build HL.Optimizer.Pro.sln -c Debug -p:Platform=x64
```

## Build Release
```bash
dotnet build HL.Optimizer.Pro.sln -c Release -p:Platform=x64
```

## Executar
```bash
dotnet run --project src/HL.Optimizer.Pro/HL.Optimizer.Pro.csproj -c Release
```

Ou abrir `HL.Optimizer.Pro.sln` no Visual Studio e pressionar F5 (x64).

## Publicar Single File (opcional)
```bash
dotnet publish src/HL.Optimizer.Pro/HL.Optimizer.Pro.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

## Permissões
O app.manifest solicita `requireAdministrator`. Ao executar, o Windows pedirá UAC. Sem admin, algumas otimizações ficarão indisponíveis e será mostrado aviso "Execute como administrador".

## Estrutura de Saída
- `bin/x64/Release/net8.0-windows/HL.Optimizer.Pro.exe`
- `logs.db` será criado em `%LocalAppData%\HL Optimizer Pro\`

## Troubleshooting
- **WMI falha**: Execute como administrador
- **PerformanceCounter falha**: Instale ` lodctr /R` no CMD admin
- **SQLite**: Se falhar, logs caem em arquivo texto
- **BlueStacks detecção**: Requer BlueStacks instalado nos paths padrão
