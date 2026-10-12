using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Programas de arranque do Windows (critério central da pontuação de "Inicialização").
///
/// Lê as três fontes reais:
///   1. HKLM\...\Run (máquina)          → exige administrador para desativar
///   2. HKCU\...\Run (utilizador)
///   3. Pasta Startup do utilizador e do sistema (atalhos .lnk)
///
/// A desativação é REVERSÍVEL: em vez de apagar o valor, movemos a entrada para
/// <c>HKLM\...\Run_Disabled</c> (uma chave própria, criada por nós) — repõe-se com um clique
/// e o utilizador pode sempre recuperar. Nada é apagado definitivamente.
/// </summary>
public sealed class StartupService
{
    private const string RunMachine = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunUser = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string DisabledRunMachine = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run_Disabled_HLOptimizer";
    private const string DisabledRunUser = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run_Disabled_HLOptimizer";

    private readonly IRegistryProvider _registry;
    private readonly ILogger<StartupService> _logger;

    /// <summary>Publicadores/processos conhecidos cujo impacto no arranque é alto.</summary>
    private static readonly string[] HighImpactNames =
    {
        "steam", "epicgames", "battle.net", "riot", "uplay", "ubisoft", "origin", "ea desktop",
        "discord", "spotify", "skype", "teams", "zoom", "slack", "telegram", "whatsapp",
        "dropbox", "onedrive", "google drive", "megasync", "adobe", "creative cloud",
        "java", "blue stacks", "bluestacks", "nox", "lightroom", "itunes", "quicktime",
        "ccleaner", "utorrent", "utorrent", "xbox game bar", "gamebar",
    };

    /// <summary>Aplicações que o próprio Windows precisa — desativar causa problemas.</summary>
    private static readonly string[] SafeToKeep = { "securityhealth", "windowsdefender", "explorer", "onedrive (sincronização)" };

    public StartupService(IRegistryProvider registry, ILogger<StartupService> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <summary>Lista os programas de arranque e marca os que vale a pena desativar.</summary>
    public IReadOnlyList<StartupItem> GetStartupItems()
    {
        var items = new List<StartupItem>();

        ReadRunKey("HKLM", RunMachine, "Máquina (todos os utilizadores)", items);
        ReadRunKey("HKCU", RunUser, "Utilizador atual", items);
        ReadStartupFolder(Environment.SpecialFolder.Startup, "Pasta de Arranque (utilizador)", items);
        ReadStartupFolder(Environment.SpecialFolder.CommonStartup, "Pasta de Arranque (todos)", items);
        ReadDisabledEntries(items);

        return items.OrderByDescending(i => i.Enabled).ThenBy(i => i.Name).ToList();
    }

    /// <summary>Quantos programas de arranque estão ativos (usado na avaliação).</summary>
    public int EnabledCount => GetStartupItems().Count(i => i.Enabled);

    /// <summary>
    /// Desativa um programa de arranque movendo a entrada para a nossa chave de "desativados".
    /// Devolve o nome da chave onde ficou para permitir a reversão.
    /// </summary>
    public Result Disable(StartupItem item)
    {
        try
        {
            var (hive, subKey) = ParseLocation(item.Location);
            var suppressed = hive == "HKLM" ? DisabledRunMachine : DisabledRunUser;

            var value = _registry.GetValue(hive, subKey, item.Name);
            if (value is null) return Result.Fail($"A entrada «{item.Name}» já não existe no arranque.");

            // Guarda uma cópia íntegra antes de remover.
            _registry.SetValue(hive, suppressed, item.Name, value, RegistryValueKind.String);
            _registry.DeleteValue(hive, subKey, item.Name);

            _logger.LogInformation("Arranque desativado: {Name} ({Location})", item.Name, item.Location);
            return Result.Ok($"«{item.Name}» retirado do arranque. Pode reativar na mesma lista.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao desativar {Name}", item.Name);
            return Result.Fail($"Não foi possível desativar «{item.Name}».", ex.Message);
        }
    }

    /// <summary>Reativa um programa previamente desativado pela aplicação.</summary>
    public Result Enable(StartupItem item)
    {
        try
        {
            var (hive, suppressed) = ParseLocation(item.Location);
            var target = hive == "HKLM" ? RunMachine : RunUser;

            var value = _registry.GetValue(hive, suppressed, item.Name);
            if (value is null) return Result.Fail($"«{item.Name}» não está na lista de desativados.");

            _registry.SetValue(hive, target, item.Name, value, RegistryValueKind.String);
            _registry.DeleteValue(hive, suppressed, item.Name);

            return Result.Ok($"«{item.Name}» volta a arrancar com o Windows.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao reativar {Name}", item.Name);
            return Result.Fail($"Não foi possível reativar «{item.Name}».", ex.Message);
        }
    }

    // ------------------------------------------------------------------ internos

    private void ReadRunKey(string hive, string subKey, string location, List<StartupItem> items)
    {
        foreach (var name in _registry.GetValueNames(hive, subKey))
        {
            var command = _registry.GetValue(hive, subKey, name)?.ToString() ?? string.Empty;
            items.Add(BuildItem(name, command, location, enabled: true));
        }
    }

    private void ReadDisabledEntries(List<StartupItem> items)
    {
        // Entradas desativadas por nós continuam visíveis (com o toggle desligado) para o
        // utilizador poder reverter sem sair da aplicação.
        foreach (var name in _registry.GetValueNames("HKLM", DisabledRunMachine))
        {
            var command = _registry.GetValue("HKLM", DisabledRunMachine, name)?.ToString() ?? string.Empty;
            items.Add(BuildItem(name, command, "Máquina (todos os utilizadores)", enabled: false));
        }

        foreach (var name in _registry.GetValueNames("HKCU", DisabledRunUser))
        {
            var command = _registry.GetValue("HKCU", DisabledRunUser, name)?.ToString() ?? string.Empty;
            items.Add(BuildItem(name, command, "Utilizador atual", enabled: false));
        }
    }

    private void ReadStartupFolder(Environment.SpecialFolder folder, string location, List<StartupItem> items)
    {
        try
        {
            var path = Environment.GetFolderPath(folder);
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;

            foreach (var file in Directory.EnumerateFiles(path, "*.lnk"))
            {
                items.Add(new StartupItem
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    Publisher = "Atalho",
                    Command = file,
                    Location = location,
                    Enabled = true,
                    RecommendedToDisable = IsHighImpact(Path.GetFileNameWithoutExtension(file)),
                    Reason = "Atalho na pasta de arranque — desativar exige mover o ficheiro.",
                    IsMicrosoft = false,
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Não foi possível ler a pasta de arranque {Folder}", folder);
        }
    }

    private StartupItem BuildItem(string name, string command, string location, bool enabled)
    {
        var lower = $"{name} {command}".ToLowerInvariant();
        var isMicrosoft = lower.Contains(@"c:\windows")
            || lower.Contains("microsoft")
            || lower.Contains("windowsdefender")
            || lower.Contains("onedrive");

        var recommended = enabled && IsHighImpact(lower) && !SafeToKeep.Any(s => lower.Contains(s));
        var publisher = ExtractPublisher(command);

        return new StartupItem
        {
            Name = name,
            Publisher = publisher,
            Command = command,
            Location = location,
            Enabled = enabled,
            RecommendedToDisable = recommended,
            IsMicrosoft = isMicrosoft,
            Reason = recommended
                ? "Consome memória e CPU no arranque; pode ser aberto manualmente quando precisar."
                : isMicrosoft
                    ? "Componente do Windows — recomendamos manter."
                    : "Impacto baixo detetado no arranque.",
        };
    }

    private static bool IsHighImpact(string text)
    {
        var lower = text.ToLowerInvariant();
        return HighImpactNames.Any(name => lower.Contains(name));
    }

    private static string ExtractPublisher(string command)
    {
        try
        {
            var path = command.Trim('"').Split(' ')[0];
            if (File.Exists(path))
            {
                var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
                return versionInfo.CompanyName ?? "Desconhecido";
            }
        }
        catch
        {
            // Caminhos inválidos são comuns em entradas órfãs.
        }

        return "Desconhecido";
    }

    /// <summary>Converte o texto de localização de volta em (colmeia, chave) para operações de escrita.</summary>
    private static (string hive, string subKey) ParseLocation(string location) => location switch
    {
        "Máquina (todos os utilizadores)" => ("HKLM", RunMachine),
        "Utilizador atual" => ("HKCU", RunUser),
        _ => ("HKCU", RunUser),
    };
}
