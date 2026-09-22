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
