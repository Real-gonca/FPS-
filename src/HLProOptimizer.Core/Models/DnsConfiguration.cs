namespace HLProOptimizer.Core.Models;

/// <summary>Configuração de DNS de um adaptador.</summary>
/// <param name="AdapterName">Nome da conexão.</param>
/// <param name="PrimaryDns">DNS primário (vazio = automático/DHCP).</param>
/// <param name="SecondaryDns">DNS secundário.</param>
/// <param name="IsAutomatic">Se obtém DNS via DHCP.</param>
/// <param name="DohEnabled">Se DNS-over-HTTPS está ativo.</param>
public sealed record DnsConfiguration(
    string AdapterName,
    string PrimaryDns,
    string SecondaryDns,
    bool IsAutomatic,
    bool DohEnabled)
{
    /// <summary>Presets de DNS públicos recomendados.</summary>
    public static class Presets
    {
        /// <summary>Cloudflare (baixa latência).</summary>
        public static readonly DnsPreset Cloudflare = new("Cloudflare", "1.1.1.1", "1.0.0.1");

        /// <summary>Google Public DNS.</summary>
        public static readonly DnsPreset Google = new("Google", "8.8.8.8", "8.8.4.4");

        /// <summary>Quad9 (bloqueio de malware).</summary>
        public static readonly DnsPreset Quad9 = new("Quad9", "9.9.9.9", "149.112.112.112");

        /// <summary>OpenDNS.</summary>
        public static readonly DnsPreset OpenDns = new("OpenDNS", "208.67.222.222", "208.67.220.220");

        /// <summary>AdGuard DNS (bloqueio de anúncios).</summary>
        public static readonly DnsPreset AdGuard = new("AdGuard", "94.140.14.14", "94.140.15.15");

        /// <summary>Todos os presets disponíveis.</summary>
        public static IReadOnlyList<DnsPreset> All { get; } = [Cloudflare, Google, Quad9, OpenDns, AdGuard];
    }
}

/// <summary>Preset nomeado de servidores DNS.</summary>
/// <param name="Name">Nome amigável.</param>
/// <param name="Primary">Servidor primário.</param>
/// <param name="Secondary">Servidor secundário.</param>
public sealed record DnsPreset(string Name, string Primary, string Secondary);
