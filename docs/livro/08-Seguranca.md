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
