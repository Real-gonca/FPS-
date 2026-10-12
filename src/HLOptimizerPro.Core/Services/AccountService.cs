using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Conta local da aplicação (ver secção 5 do briefing).
///
/// Decisões de segurança:
///   • A senha nunca é guardada — apenas o hash PBKDF2-SHA256 com 100 000 iterações e um salt
///     aleatório de 16 bytes por conta.
///   • A comparação do hash é feita em tempo constante (evita ataques por temporização).
///   • "Agora não" cria uma sessão local sem conta: a aplicação funciona por completo sem login.
///   • O login com a Microsoft (MSAL) é opcional e vive na camada de apresentação — aqui só
///     guardamos o identificador da conta, nunca tokens.
/// </summary>
public sealed class AccountService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogger<AccountService> _logger;
    private readonly object _gate = new();

    public AccountService(ILogger<AccountService> logger)
    {
        _logger = logger;
        Current = Load();
    }

    /// <summary>Conta ativa (pode ser uma sessão local sem credenciais).</summary>
    public UserAccount Current { get; private set; }

    public bool IsSignedIn => !string.IsNullOrEmpty(Current.Email) || Current.IsMicrosoftAccount;

    public event EventHandler<UserAccount>? AccountChanged;

    /// <summary>Cria uma conta local com nome, e-mail e senha.</summary>
    public Result CreateAccount(string displayName, string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return Result.Fail("Introduza um e-mail válido.");

        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            return Result.Fail("A senha tem de ter pelo menos 8 caracteres.");

        var existing = Load();
        if (!string.IsNullOrEmpty(existing.Email) && string.Equals(existing.Email, email.Trim(), StringComparison.OrdinalIgnoreCase))
            return Result.Fail("Já existe uma conta local com este e-mail. Use «Entrar».");

        var salt = RandomNumberGenerator.GetBytes(16);

        var account = new UserAccount
        {
            UserId = Guid.NewGuid().ToString("N"),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? ExtractName(email) : displayName.Trim(),
            Email = email.Trim(),
            PasswordSalt = Convert.ToBase64String(salt),
            PasswordHash = Convert.ToBase64String(HashPassword(password, salt, 100_000)),
            PasswordIterations = 100_000,
            CreatedAt = DateTime.Now,
            LastLoginAt = DateTime.Now,
            IsLocalOnly = false,
        };

        lock (_gate)
        {
            Current = account;
            Save();
        }

        AccountChanged?.Invoke(this, account);
        _logger.LogInformation("Conta local criada para {Email}.", account.Email);
        return Result.Ok($"Conta criada. Bem-vindo, {account.DisplayName}!");
    }

    /// <summary>Inicia sessão numa conta local.</summary>
    public Result SignIn(string email, string password, bool rememberMe)
    {
        var account = Load();

        if (string.IsNullOrWhiteSpace(account.Email))
            return Result.Fail("Não existe nenhuma conta neste computador. Crie uma ou escolha «Agora não».");

        if (!string.Equals(account.Email, email?.Trim(), StringComparison.OrdinalIgnoreCase))
            return Result.Fail("E-mail ou senha incorretos.");

        if (string.IsNullOrWhiteSpace(account.PasswordHash) || string.IsNullOrWhiteSpace(account.PasswordSalt))
            return Result.Fail("Esta conta não tem senha definida. Use «Agora não» ou crie uma conta nova.");

        var salt = Convert.FromBase64String(account.PasswordSalt);
        var expected = Convert.FromBase64String(account.PasswordHash);
        var actual = HashPassword(password ?? string.Empty, salt, account.PasswordIterations);

        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            _logger.LogWarning("Tentativa de login falhada para {Email}.", email);
            return Result.Fail("E-mail ou senha incorretos.");
        }

        account.LastLoginAt = DateTime.Now;
        account.RememberMe = rememberMe;

        lock (_gate)
        {
            Current = account;
            Save();
        }

        AccountChanged?.Invoke(this, account);
        return Result.Ok($"Bem-vindo de volta, {account.DisplayName}!");
    }

    /// <summary>Sessão local ("Agora não"): funciona sem conta e sem Internet.</summary>
    public Result<UserAccount> ContinueAsLocal()
    {
        var account = new UserAccount
        {
            UserId = Guid.NewGuid().ToString("N"),
            DisplayName = "Utilizador",
            Email = string.Empty,
            IsLocalOnly = true,
            CreatedAt = DateTime.Now,
            LastLoginAt = DateTime.Now,
        };

        lock (_gate)
        {
            Current = account;
            Save();
        }

        AccountChanged?.Invoke(this, account);
        return Result<UserAccount>.Ok(account, "Sessão local iniciada. Pode criar uma conta mais tarde em Configuração.");
    }

    /// <summary>Regista o resultado de um login com a Microsoft (o token é obtido na camada de UI).</summary>
    public Result<UserAccount> SignInWithMicrosoft(string accountId, string displayName, string email)
    {
        var account = new UserAccount
        {
            UserId = accountId,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? ExtractName(email) : displayName,
            Email = email,
            IsMicrosoftAccount = true,
            MicrosoftAccountId = accountId,
            CreatedAt = DateTime.Now,
            LastLoginAt = DateTime.Now,
        };

        lock (_gate)
        {
            Current = account;
            Save();
        }

        AccountChanged?.Invoke(this, account);
        return Result<UserAccount>.Ok(account, $"Sessão iniciada com a conta Microsoft {account.Email}.");
    }

    /// <summary>Atualiza o nome de utilizador (ecrã Configuração).</summary>
    public Result UpdateProfile(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return Result.Fail("O nome não pode estar vazio.");

        lock (_gate)
        {
            Current.DisplayName = displayName.Trim();
            Save();
        }

        AccountChanged?.Invoke(this, Current);
        return Result.Ok("Perfil atualizado.");
    }

    /// <summary>Termina a sessão (não apaga a conta — as otimizações aplicadas ficam registadas).</summary>
    public Result SignOut(bool deleteLocalData)
    {
        try
        {
            lock (_gate)
            {
                if (deleteLocalData && File.Exists(AppPaths.AccountFile)) File.Delete(AppPaths.AccountFile);
                Current = deleteLocalData ? new UserAccount() : Load();
            }

            AccountChanged?.Invoke(this, Current);
            return Result.Ok(deleteLocalData ? "Conta e sessão removidas deste computador." : "Sessão terminada.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao terminar a sessão.");
            return Result.Fail("Não foi possível terminar a sessão.", ex.Message);
        }
    }

    // ------------------------------------------------------------------ internos

    private static byte[] HashPassword(string password, byte[] salt, int iterations)
        => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);

    private static string ExtractName(string email)
    {
        var at = email.IndexOf('@');
        var name = at > 0 ? email[..at] : email;
        return string.IsNullOrWhiteSpace(name) ? "Utilizador" : name;
    }

    private void Save()
    {
        try
        {
            AppPaths.EnsureCreated();
            File.WriteAllText(AppPaths.AccountFile, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao guardar a conta.");
        }
    }

    private UserAccount Load()
    {
        try
        {
            AppPaths.EnsureCreated();
            if (!File.Exists(AppPaths.AccountFile)) return new UserAccount { IsLocalOnly = true };

            var json = File.ReadAllText(AppPaths.AccountFile);
            return JsonSerializer.Deserialize<UserAccount>(json, JsonOptions) ?? new UserAccount();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível ler a conta guardada.");
            return new UserAccount { IsLocalOnly = true };
        }
    }
}
