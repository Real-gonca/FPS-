using System.Management;
using System.Runtime.Versioning;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Estado de segurança do computador: antivírus, firewall e atualizações do Windows.
///
/// Fontes:
///   • WMI SecurityCenter2 (root\SecurityCenter2) — antivírus registados e firewall
///   • WMI Win32_QuickFixEngineering / Microsoft Update (via COM) — atualizações instaladas
///   • Registo — políticas de telemetria e estado do Windows Defender
///
/// Nota: o namespace SecurityCenter2 não existe em Windows Server nem em alguns Windows
/// "LTSC"; nesses casos devolvemos "não detetado" com uma nota, em vez de um falso "sem proteção".
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SecurityService
{
    private readonly ILogger<SecurityService> _logger;
    private readonly IProcessRunner _runner;

    public SecurityService(ILogger<SecurityService> logger, IProcessRunner runner)
    {
        _logger = logger;
        _runner = runner;
    }

    /// <summary>Fotografia do estado de segurança (nunca lança exceção).</summary>
    public SecurityStatus GetStatus()
    {
        var antivirus = DetectAntivirus(out var avName);
        var firewallEnabled = DetectFirewall();
        var (lastUpdate, pending) = DetectUpdates();
        var defender = IsDefenderPresent();

        var notes = new List<string>();
        if (!antivirus) notes.Add("Antivírus não detetado pelo Centro de Segurança do Windows.");
        if (!firewallEnabled) notes.Add("Firewall do Windows desativada em pelo menos um perfil.");
        if (pending > 0) notes.Add($"{pending} atualizações de segurança pendentes.");
        if (notes.Count == 0) notes.Add("Proteção ativa e atualizada.");

        return new SecurityStatus
        {
            AntivirusEnabled = antivirus,
            RealTimeProtection = defender ? IsRealTimeProtectionOn() : antivirus,
            AntivirusName = avName,
            FirewallEnabled = firewallEnabled,
            WindowsUpdateRecent = lastUpdate is null || lastUpdate > DateTime.Now.AddDays(-35),
            PendingUpdates = pending,
            LastUpdateDate = lastUpdate,
            DefenderPresent = defender,
            Notes = string.Join(" ", notes),
        };
    }

    /// <summary>Ativação da proteção em tempo real (só faz sentido com o Defender presente).</summary>
    public bool IsRealTimeProtectionOn()
    {
        try
        {
            // Preferência do Defender: 1 = ativado, 2 = desativado.
            var value = RegistryProviderSafeGet(
                @"SOFTWARE\Microsoft\Windows Defender\Real-Time Protection",
                "DisableRealtimeMonitoring");

            return value is int disabled && disabled == 0;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Ativa a proteção em tempo real do Defender (ação reversível e de baixo risco).</summary>
    public Result EnableRealTimeProtection()
    {
        try
        {
            var result = _runner.Run("powershell.exe",
                "-NoProfile -NonInteractive -Command \"Set-MpPreference -DisableRealtimeMonitoring $false\"");

            return result.Success
                ? Result.Ok("Proteção em tempo real do Windows Defender ativada.")
                : Result.Fail("Não foi possível ativar a proteção em tempo real (pode estar gerida por política de domínio).", result.Output);
        }
        catch (Exception ex)
        {
            return Result.Fail("Falha ao contactar o serviço do Defender.", ex.Message);
        }
    }

    /// <summary>Ativa a firewall em todos os perfis (domínio, privado, público).</summary>
    public IReadOnlyList<Result> EnableFirewall()
    {
        var results = new List<Result>();

        foreach (var profile in new[] { "domainprofile", "privateprofile", "publicprofile" })
        {
            var result = _runner.Run("netsh", $"advfirewall set {profile} state on");
            results.Add(result.Success
                ? Result.Ok($"Firewall ativada no perfil {TranslateProfile(profile)}.")
                : Result.Fail($"Não foi possível ativar a firewall no perfil {TranslateProfile(profile)}.", result.Output));
        }

        return results;
    }

    /// <summary>Desativa a firewall — NUNCA recomendado; existe apenas para permitir reversão de tweaks antigos.</summary>
    public IReadOnlyList<Result> DisableFirewall()
    {
        var results = new List<Result>();
        foreach (var profile in new[] { "domainprofile", "privateprofile", "publicprofile" })
        {
            var result = _runner.Run("netsh", $"advfirewall set {profile} state off");
            results.Add(result.Success ? Result.Ok($"Firewall desativada no perfil {TranslateProfile(profile)}.") : Result.Fail("Falha ao desativar a firewall."));
        }

        return results;
    }

    /// <summary>Verifica se o Defender está instalado e operacional.</summary>
    public bool IsDefenderPresent()
    {
        try
        {
            var result = _runner.Run("powershell.exe", "-NoProfile -NonInteractive -Command \"(Get-MpComputerStatus).AMServiceEnabled\"");
            return result.Output.Contains("True", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Alternativa: existência do serviço.
            return RegistryProviderSafeGet(@"SOFTWARE\Microsoft\Windows Defender", "IsServiceRunning") is int running && running == 1;
        }
    }

    /// <summary>Aplica a telemetria do Windows conforme a política escolhida (usada no módulo de privacidade).</summary>
    public Result SetTelemetryLevel(int level)
    {
        var value = Math.Clamp(level, 0, 3);
        var result = _runner.Run("reg.exe",
            $"add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection\" /v AllowTelemetry /t REG_DWORD /d {value} /f");

        return result.Success
            ? Result.Ok($"Nível de telemetria do Windows definido como {TranslateTelemetry(value)}.")
            : Result.Fail("Não foi possível alterar o nível de telemetria (pode estar sob política de domínio).", result.Output);
    }

    // ------------------------------------------------------------------ internos

    private bool DetectAntivirus(out string name)
    {
        name = "Não detetado";

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\SecurityCenter2",
                "SELECT displayName, productState FROM AntiVirusProduct");

            var count = 0;
            foreach (ManagementObject obj in searcher.Get())
            {
                count++;
                var display = obj["displayName"]?.ToString() ?? "Antivírus";
                var state = Convert.ToInt32(obj["productState"] ?? 0);

                // productState: byte do meio indica se está ativo (0x10 = ativo, 0x00 = desligado).
                var active = (state >> 12 & 0x0F) == 0x1;

                if (active)
                {
                    name = display;
                    return true;
                }

                if (name == "Não detetado") name = $"{display} (inativo)";
            }

            if (count == 0)
            {
                _logger.LogInformation("SecurityCenter2 não reportou antivírus registados.");
                return IsDefenderPresent();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SecurityCenter2 indisponível — a recorrer ao estado do Defender.");
            if (IsDefenderPresent())
            {
                name = "Microsoft Defender";
                return true;
            }
        }

        return false;
    }

    private bool DetectFirewall()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\SecurityCenter2",
                "SELECT name, productState FROM FirewallProduct");

            foreach (ManagementObject obj in searcher.Get())
            {
                var state = Convert.ToInt32(obj["productState"] ?? 0);
                if ((state & 0x0100) == 0x0100 || (state >> 8 & 0x0F) == 0x1) return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "SecurityCenter2 não expôs o estado da firewall.");
        }

        // Fallback fiável: netsh.
        try
        {
            var result = _runner.Run("netsh", "advfirewall show allprofiles state");
            if (result.Success)
            {
                var enabled = result.Output.Split('\n').Count(line => line.Contains("ON", StringComparison.OrdinalIgnoreCase));
                return enabled > 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "netsh advfirewall indisponível.");
        }

        return false;
    }

    private (DateTime? lastUpdate, int pending) DetectUpdates()
    {
        DateTime? last = null;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT InstalledOn FROM Win32_QuickFixEngineering");

            foreach (ManagementObject obj in searcher.Get())
            {
                var raw = obj["InstalledOn"]?.ToString();
                if (string.IsNullOrWhiteSpace(raw)) continue;

                if (DateTime.TryParse(raw, out var date) && (last is null || date > last)) last = date;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Win32_QuickFixEngineering indisponível.");
        }

        var pending = CountPendingUpdates();
        return (last, pending);
    }

    /// <summary>
    /// Conta atualizações pendentes consultando o serviço de atualizações.
    /// Usa PowerShell (COM Microsoft.Update.Session) porque a API nativa exige o SDK do Windows Update.
    /// </summary>
    private int CountPendingUpdates()
    {
        try
        {
            const string script =
                "$s = New-Object -ComObject Microsoft.Update.Session; " +
                "$sr = $s.CreateUpdateSearcher(); " +
                "$r = $sr.Search('IsInstalled=0 and Type=''Software'''); " +
                "Write-Output $r.Updates.Count";

            var result = _runner.Run("powershell.exe", $"-NoProfile -NonInteractive -Command \"{script}\"");

            if (result.Success && int.TryParse(result.Output.Trim().Split('\n').Last().Trim(), out var count))
                return count;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Consulta de atualizações pendentes indisponível (sem Internet ou WU desativado).");
        }

        return 0;
    }

    private object? RegistryProviderSafeGet(string subKey, string name)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(subKey);
            return key?.GetValue(name);
        }
        catch
        {
            return null;
        }
    }

    private static string TranslateProfile(string profile) => profile switch
    {
        "domainprofile" => "Domínio",
        "privateprofile" => "Privada",
        "publicprofile" => "Pública",
        _ => profile,
    };

    private static string TranslateTelemetry(int level) => level switch
    {
        0 => "Segurança (dados mínimos)",
        1 => "Básica",
        2 => "Melhorada",
        _ => "Completa (padrão da Microsoft)",
    };
}
