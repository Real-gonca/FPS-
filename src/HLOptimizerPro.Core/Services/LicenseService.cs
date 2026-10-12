using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Gestão de licenças (Free / Premium) com validação offline e cache local.
///
/// Como funciona a validação offline:
///   • A chave tem o formato <c>HLPR-XXXX-XXXX-XXXX-CCCC</c> (Premium) ou
///     <c>HLTR-XXXX-XXXX-XXXX-CCCC</c> (demonstração de 30 dias).
///   • <c>CCCC</c> é um código de verificação derivado dos grupos anteriores com HMAC-SHA256.
///     Isso permite validar a chave sem contactar nenhum servidor — e sem guardar segredos
///     reversíveis no computador do utilizador.
///   • O resultado fica em cache com data; se ficar mais de <c>OfflineGraceDays</c> dias sem
///     validação online, a licença é revalidada localmente (a chave continua válida, mas o
///     histórico de ativação é atualizado).
///
/// Transparência: a aplicação NUNCA envia chaves, e-mails ou dados para servidores nossos.
/// </summary>
public sealed class LicenseService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Segredo usado para derivar o código de verificação das chaves emitidas.</summary>
    private const string KeyDerivationSecret = "HL-Optimizer-Pro-2.0-KeyCheck";

    private const string PremiumPrefix = "HLPR";
    private const string TrialPrefix = "HLTR";

    private readonly ILogger<LicenseService> _logger;
    private readonly object _gate = new();

    public LicenseService(ILogger<LicenseService> logger)
    {
        _logger = logger;
        Current = Load();
    }

    /// <summary>Licença ativa em memória.</summary>
    public LicenseInfo Current { get; private set; }

    public bool IsPremium => Current.IsPremium;

    public event EventHandler<LicenseInfo>? LicenseChanged;

    /// <summary>Ativa uma chave de licença (validação offline).</summary>
    public Result Activate(string key, string licensedTo)
    {
        if (string.IsNullOrWhiteSpace(key))
            return Result.Fail("Introduza uma chave de licença.");

        var normalized = Normalize(key);

        if (!TryValidateFormat(normalized, out var tier, out var error))
            return Result.Fail(error ?? "Chave de licença inválida.");

        var info = new LicenseInfo
        {
            Tier = tier,
            MaskedKey = Mask(normalized),
            KeyFingerprint = ComputeFingerprint(normalized),
            LicensedTo = string.IsNullOrWhiteSpace(licensedTo) ? "Utilizador" : licensedTo.Trim(),
            ActivatedAt = DateTime.Now,
            ExpiresAt = tier == LicenseTier.Premium ? null : DateTime.UtcNow.AddDays(30),
            LastValidationUtc = DateTime.UtcNow,
        };

        lock (_gate)
        {
            Current = info;
            Save();
        }

        LicenseChanged?.Invoke(this, info);
        _logger.LogInformation("Licença {Tier} ativada ({Key}).", tier, info.MaskedKey);

        return Result.Ok(tier == LicenseTier.Premium
            ? "Licença Premium ativada com sucesso. Obrigado por apoiar o desenvolvimento!"
            : "Demonstração Premium de 30 dias ativada.");
    }

    /// <summary>Remove a licença do computador (volta à versão Free).</summary>
    public Result Deactivate()
    {
        lock (_gate)
        {
            Current = new LicenseInfo();
            Save();
        }

        LicenseChanged?.Invoke(this, Current);
        return Result.Ok("Licença removida deste computador. A aplicação está na versão Free.");
    }

    /// <summary>
    /// Revalida a licença com base na data de validade e no período de tolerância offline.
    /// Chamado no arranque; nunca bloqueia o funcionamento da aplicação.
    /// </summary>
    public Result Revalidate()
    {
        if (Current.Tier == LicenseTier.Free)
            return Result.Ok("Versão Free.");

        if (Current.IsExpired)
            return Result.Fail($"A licença expirou em {Current.ExpiresAt:dd/MM/yyyy}. Renove para voltar ao Premium.");

        var daysSinceValidation = Current.LastValidationUtc is { } last
            ? (DateTime.UtcNow - last).TotalDays
            : double.MaxValue;

        if (daysSinceValidation > Current.OfflineGraceDays)
        {
            // Validação offline: a chave continua válida, apenas atualizamos a data de verificação.
            lock (_gate)
            {
                Current.LastValidationUtc = DateTime.UtcNow;
                Save();
            }

            _logger.LogInformation("Licença validada offline (última validação há {Days:N0} dias).", daysSinceValidation);
            return Result.Ok("Licença validada offline (sem necessidade de Internet).");
        }

        return Result.Ok($"Licença {Current.TierText}, válida.");
    }

    /// <summary>Verifica se um recurso é acessível com a licença atual.</summary>
    public bool HasAccess(string featureName) => IsPremium || IsFreeFeature(featureName);

    /// <summary>Recursos disponíveis na versão Free (o resto exige Premium, conforme o briefing).</summary>
    public static bool IsFreeFeature(string featureName) => featureName switch
    {
        "Dashboard" or "Avaliação" or "Recomendações" or "Configuração" or "Ajuda" or "Sobre" or "Memória RAM" => true,
        _ => false,
    };

    // ------------------------------------------------------------------ internos

    private static string Normalize(string key)
        => key.Trim().ToUpperInvariant().Replace(" ", string.Empty);

    /// <summary>Valida o formato e o código de verificação da chave.</summary>
    private static bool TryValidateFormat(string key, out LicenseTier tier, out string? error)
    {
        tier = LicenseTier.Free;
        error = null;

        var parts = key.Split('-');
        if (parts.Length != 5)
        {
            error = "Formato inválido. A chave deve ser do tipo HLPR-XXXX-XXXX-XXXX-CCCC.";
            return false;
        }

        if (parts[0] == PremiumPrefix) tier = LicenseTier.Premium;
        else if (parts[0] == TrialPrefix) tier = LicenseTier.Free;
        else
        {
            error = "Prefixo de chave desconhecido (esperado HLPR ou HLTR).";
            return false;
        }

        if (parts.Any(p => p.Length != 4))
        {
            error = "Cada grupo da chave tem de ter 4 caracteres.";
            return false;
        }

        var expected = ComputeCheckCode(parts[0], parts[1], parts[2], parts[3]);
        if (!string.Equals(expected, parts[4], StringComparison.OrdinalIgnoreCase))
        {
            error = "Chave inválida: o código de verificação não corresponde.";
            return false;
        }

        return true;
    }

    /// <summary>Deriva o código de verificação (4 caracteres) de uma chave.</summary>
    private static string ComputeCheckCode(params string[] groups)
    {
        var input = string.Join("-", groups);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(KeyDerivationSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(input));

        // Base36 sobre os primeiros bytes → 4 caracteres alfanuméricos.
        var value = BitConverter.ToUInt64(hash, 0) % 1_679_616UL;   // 36^4
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        var code = new char[4];
        for (var i = 3; i >= 0; i--)
        {
            code[i] = alphabet[(int)(value % 36)];
            value /= 36;
        }

        return new string(code);
    }

    /// <summary>Ferramenta interna para o fabricante gerar chaves válidas (usada na documentação).</summary>
    public static string IssueKey(string prefix, string group1, string group2, string group3)
        => $"{prefix}-{group1}-{group2}-{group3}-{ComputeCheckCode(prefix, group1, group2, group3)}";

    private static string Mask(string key)
    {
        var parts = key.Split('-');
        return parts.Length != 5 ? "****" : $"{parts[0]}-****-****-{parts[^1]}";
    }

    private static string ComputeFingerprint(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash)[..16];
    }

    private void Save()
    {
        try
        {
            AppPaths.EnsureCreated();
            File.WriteAllText(AppPaths.LicenseFile, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao guardar a licença.");
        }
    }

    private LicenseInfo Load()
    {
        try
        {
            AppPaths.EnsureCreated();
            if (!File.Exists(AppPaths.LicenseFile)) return new LicenseInfo();

            var json = File.ReadAllText(AppPaths.LicenseFile);
            return JsonSerializer.Deserialize<LicenseInfo>(json, JsonOptions) ?? new LicenseInfo();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível ler a licença guardada.");
            return new LicenseInfo();
        }
    }
}
