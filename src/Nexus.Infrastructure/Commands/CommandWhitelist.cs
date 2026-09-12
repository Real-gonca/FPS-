namespace Nexus.Infrastructure.Commands;

/// <summary>
/// Regra explícita: binário + padrão de argumentos permitido + timeout +
/// justificação auditável. A whitelist vive em CÓDIGO (não em configuração
/// em tempo de execução) — cada regra acrescentada exige um teste unitário
/// (spec §2.2 / §7).
/// </summary>
public sealed record CommandRule(
    string FileName,
    string ArgumentsPattern,
    int TimeoutSeconds,
    string Justification);

public static class CommandWhitelist
{
    /// <summary>
    /// Metacaracteres de shell — sempre rejeitados nos argumentos, mesmo que
    /// um padrão mais largo os aceitasse (defesa em profundidade: o executor
    /// não usa shell, mas não dá superfície de ataque de qualquer forma).
    /// </summary>
    private static readonly char[] ShellMetacharacters = ['&', '|', '<', '>', '^', ';', '(', ')', '`', '$'];

    /// <summary>
    /// Regras do Patch 1 — mínimo necessário e seguro. O Patch 2 (Serviços)
    /// valida as regras sc.exe já previstas aqui; novos binários entram apenas
    /// com justificação + testes.
    /// </summary>
    public static IReadOnlyList<CommandRule> Default => new List<CommandRule>
    {
        // Consulta de estado/descrição de serviços (somente leitura).
        new("sc.exe",
            @"^(query|queryex|qc|qdescription)\s+[A-Za-z0-9_.\\\-]{1,128}$",
            10,
            "Consulta de estado/descrição de um único serviço (somente leitura) — Serviços Manager (patch 2)."),

        // Iniciar/parar/pausar/retomar um único serviço nomeado.
        new("sc.exe",
            @"^(start|stop|pause|continue)\s+[A-Za-z0-9_.\\\-]{1,128}$",
            15,
            "Start/stop/pause/continue de um único serviço nomeado — Serviços Manager (patch 2)."),

        // Alterar tipo de arranque de um único serviço.
        new("sc.exe",
            @"^change\s+[A-Za-z0-9_.\\\-]{1,128}\s+start=(auto|demand|disabled)$",
            15,
            "Alterar o tipo de arranque de um único serviço — perfis de serviços (patch 2)."),

        // Metadados do sistema para relatórios (sem argumentos).
        new("systeminfo",
            @"^$",
            30,
            "Coleção de metadados do sistema para relatórios — Relatórios (patch 7)."),

        // Terminar processo nomeado (sem wildcards).
        new("taskkill",
            @"^/IM\s+[A-Za-z0-9_.\-]{1,64}\s+/F$",
            10,
            "Terminar um processo pelo nome exato (sem wildcards) — Gaming Center (patch 3)."),
    };

    public static bool ContainsShellMetacharacters(string arguments) =>
        arguments.Any(ShellMetacharacters.Contains);
}
