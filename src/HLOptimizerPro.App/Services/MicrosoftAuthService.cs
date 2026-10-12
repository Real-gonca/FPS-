using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;

namespace HLOptimizerPro.App.Services;

/// <summary>
/// Login opcional com a conta Microsoft (MSAL).
///
/// Como está implementado de forma responsável:
///   • O identificador de cliente (ClientId) tem de ser configurado pela HL Software — sem ele, o
///     botão explica que o login Microsoft não está configurado e sugere a conta local. Não
///     fingimos um login que não funciona.
///   • Usa <c>AcquireTokenInteractive</c> com <c>WithUseEmbeddedWebView(false)</c> (navegador do
///     sistema): o utilizador vê o endereço real da Microsoft e as suas credenciais nunca passam
///     pela nossa aplicação.
///   • Nenhum token é guardado em disco pela aplicação: usamos o cache em memória do MSAL e
///     registamos apenas o identificador da conta (nome/e-mail) no perfil local.
/// </summary>
public sealed class MicrosoftAuthService
{
    /// <summary>
    /// Identificador de cliente da aplicação registada no Azure AD (multi-tenant + contas pessoais).
    /// Em produção é substituído pelo valor real; o valor vazio mantém a funcionalidade desativada.
    /// </summary>
    private const string ClientId = "00000000-0000-0000-0000-000000000000";

    private static readonly string[] Scopes = { "User.Read" };

    private readonly ILogger<MicrosoftAuthService> _logger;
    private readonly AccountService _accounts;
    private IPublicClientApplication? _client;

    public MicrosoftAuthService(ILogger<MicrosoftAuthService> logger, AccountService accounts)
    {
        _logger = logger;
        _accounts = accounts;
    }

    /// <summary>True quando existe um ClientId configurado.</summary>
    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && ClientId != "00000000-0000-0000-0000-000000000000";

    /// <summary>Inicia o login interativo com a Microsoft.</summary>
    public async Task<Result<UserAccount>> SignInAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return Result<UserAccount>.Fail(
                "O login com a conta Microsoft ainda não está configurado nesta versão. " +
                "Pode usar uma conta local ou continuar com «Agora não» — todas as funcionalidades continuam disponíveis.");
        }

        try
        {
            _client ??= PublicClientApplicationBuilder
                .Create(ClientId)
                .WithAuthority(AzureCloudInstance.AzurePublic, AadAuthorityAudience.AzureAdAndPersonalMicrosoftAccount)
                .WithRedirectUri("http://localhost")
                .Build();

            var result = await _client.AcquireTokenInteractive(Scopes)
                .WithUseEmbeddedWebView(false)   // navegador do sistema: o utilizador vê o endereço real
                .WithPrompt(Prompt.SelectAccount)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(true);

            _logger.LogInformation("Login Microsoft concluído para {Account}.", result.Account.Username);

            return _accounts.SignInWithMicrosoft(
                result.Account.HomeAccountId.Identifier,
                result.Account.Username.Split('@')[0],
                result.Account.Username);
        }
        catch (MsalClientException ex) when (ex.ErrorCode == "authentication_canceled")
        {
            return Result<UserAccount>.Fail("O login com a Microsoft foi cancelado.");
        }
        catch (MsalException ex)
        {
            _logger.LogError(ex, "Falha no login Microsoft.");
            return Result<UserAccount>.Fail($"Não foi possível concluir o login com a Microsoft ({ex.ErrorCode}).", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado no login Microsoft.");
            return Result<UserAccount>.Fail("Erro inesperado no login com a Microsoft.", ex.Message);
        }
    }

    /// <summary>Termina a sessão da Microsoft (limpa o cache em memória do MSAL).</summary>
    public async Task<Result> SignOutAsync()
    {
        try
        {
            if (_client is not null)
            {
                foreach (var account in await _client.GetAccountsAsync().ConfigureAwait(true))
                    await _client.RemoveAsync(account).ConfigureAwait(true);
            }

            return _accounts.SignOut(deleteLocalData: false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao terminar a sessão da Microsoft.");
            return Result.Fail("Não foi possível terminar a sessão da Microsoft.", ex.Message);
        }
    }
}
