using System.Runtime.Versioning;

namespace HLOptimizerPro.Core.Helpers;

/// <summary>
/// Caminhos de dados locais da aplicação.
/// Tudo vive em <c>%AppData%\HLOptimizerPro</c> (Roaming) para respeitar o perfil do
/// utilizador e funcionar em ambientes com políticas restritas. Nenhum dado sai do
/// computador sem consentimento explícito (ver <c>SettingsService</c>).
/// </summary>
[SupportedOSPlatform("windows")]
public static class AppPaths
{
    /// <summary>Raiz dos dados: %AppData%\HLOptimizerPro</summary>
    public static string Root { get; } = ResolveRoot();

    public static string Logs => Combine(AppConstants.LogFolderName);
    public static string Backups => Combine(AppConstants.BackupFolderName);
    public static string Reports => Combine(AppConstants.ReportFolderName);
    public static string History => Combine("history");

    public static string SettingsFile => Combine(AppConstants.SettingsFileName);
    public static string AccountFile => Combine(AppConstants.AccountFileName);
    public static string LicenseFile => Combine(AppConstants.LicenseFileName);
    public static string JournalFile => Combine(AppConstants.JournalFileName);
    public static string ActivityFile => Combine(AppConstants.ActivityFileName);
    public static string MetricsHistoryFile => Combine(AppConstants.MetricsHistoryFileName);

    /// <summary>Caminho do ficheiro de log do dia (Serilog).</summary>
    public static string TodayLogFile => Path.Combine(Logs, $"hl-{DateTime.Now:yyyyMMdd}.log");

    /// <summary>Junta um nome à raiz de dados sem permitir escapar da pasta (defesa contra path traversal).</summary>
    public static string Combine(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
            throw new ArgumentException("Caminho relativo inválido.", nameof(relative));

        var combined = Path.GetFullPath(Path.Combine(Root, relative));
        if (!combined.StartsWith(Root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Caminho fora da pasta de dados: {relative}");

        return combined;
    }

    /// <summary>Cria todas as pastas necessárias. Idempotente.</summary>
    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Backups);
        Directory.CreateDirectory(Reports);
        Directory.CreateDirectory(History);
    }

    private static string ResolveRoot()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            // Fallback (serviços/contas sem perfil): pasta temporária do utilizador.
            appData = Path.Combine(Path.GetTempPath(), "HLOptimizerPro");
        }

        return Path.Combine(appData, AppConstants.AppFolderName);
    }
}
