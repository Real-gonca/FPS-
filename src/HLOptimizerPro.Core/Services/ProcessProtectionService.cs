using HLOptimizerPro.Core.Models;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Lista de proteção de processos.
///
/// REQUISITO DE SEGURANÇA (não negociável): nunca encerrar, alterar prioridade nem afinidade
/// de processos críticos do sistema. Esta classe é a única fonte de verdade e é usada por
/// TODOS os módulos que mexem em processos (memória, App Tuner, Game Mode).
///
/// A lista cobre:
///   • processos do núcleo (System, Idle, Memory Compression, csrss, wininit, smss...)
///   • subsistema gráfico e de áudio (dwm, audiodg) — encerrá-los provoca ecrã preto
///   • serviços de segurança (MsMpEng, SecurityHealthService, lsass, services)
///   • serviços de infraestrutura (svchost, RpcSs, winlogon, spoolsv, WmiPrvSE)
///   • o próprio HL Optimizer Pro (evita auto-encerramento acidental)
/// </summary>
public static class ProcessProtectionService
{
    /// <summary>Processos que NUNCA podem ser encerrados nem "otimizados".</summary>
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        // Núcleo do Windows
        "system", "idle", "registry", "memory compression", "secure system",
        "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
        "svchost", "fontdrvhost", "dwm", "dwm.exe", "sihost", "ctfmon", "rdpclip",
        "taskhostw", "runtimebroker", "shellexperiencehost", "startmenuexperiencehost",
        "searchapp", "searchindexer", "searchhost", "textinputhost",
        "audiodg", "spoolsv", "conhost", "wmiprvse", "wudfhost", "dllhost",
        "winstation", "systemsettings", "securityhealthsystray", "securityhealthservice",
        "msmpeng", "nissrv", "mpcmdrun", "windefend", "smartscreen",
        "logonui", "explorer",              // explorer: encerrar provoca perda de trabalho do utilizador
        "powershell", "pwsh", "cmd", "conhost.exe",
        "tiworker", "trustedinstaller", "usoclient", "moUsoCoreWorker", "wuauclt",
        "nvidia display container ls", "nvcontainer", "nvspcaps64", "amdow", "radeonsoftware",
        "hloptimizerpro", "hloptimizerpro.app",
    };

    /// <summary>Processos do sistema cuja terminação deixaria o Windows instável (subconjunto crítico).</summary>
    private static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "memory compression", "secure system",
        "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
        "svchost", "dwm", "audiodg", "explorer", "csrss.exe",
    };

    /// <summary>Prefixo/padrões que identificam serviços de sistema por padrão de nome.</summary>
    private static readonly string[] SuspiciousPrefixes = { "svchost", "dllhost", "wudfhost", "taskhost" };

    /// <summary>True quando o processo não pode ser tocado por motivos de segurança.</summary>
    public static bool IsProtected(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return true;   // sem nome → não mexer

        var name = Normalize(processName);
        if (Protected.Contains(name)) return true;

        // Padrões genéricos (ex.: "svchost#3" em nomes de pooling).
        foreach (var prefix in SuspiciousPrefixes)
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;

        // Serviços do Windows usam nomes com "Win" + sufixos numéricos (WinRshost, WpnService...).
        if (name.Length > 3 && name.StartsWith("win", StringComparison.OrdinalIgnoreCase) && char.IsUpper(processName.Trim()[3]))
            return true;

        return false;
    }

    /// <summary>True quando o processo é essencial ao funcionamento do Windows.</summary>
    public static bool IsSystemCritical(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return true;
        return Critical.Contains(Normalize(processName));
    }

    /// <summary>Processos de sessão do utilizador que podem ser encerrados com aviso (não críticos).</summary>
    public static bool CanSafelyTerminate(ProcessUsageInfo process)
        => !process.IsProtected && !process.IsSystemCritical && process.Pid > 4;

    /// <summary>
    /// Verifica se um processo pertence à pasta do Windows (defesa extra: nunca mexer em system32).
    /// </summary>
    public static bool IsInsideWindowsDirectory(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return false;

        try
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrWhiteSpace(windows)) return false;

            return Path.GetFullPath(executablePath)
                .StartsWith(Path.GetFullPath(windows), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;   // em caso de dúvida, considerar protegido
        }
    }

    private static string Normalize(string name)
    {
        var trimmed = name.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4]
            : trimmed;
    }
}
