using Nexus.Domain.Optimization;
using Nexus.Domain.Ports;

namespace Nexus.Application.Tasks;

/// <summary>Tipo de operação sobre um serviço (via sc.exe whitelisted).</summary>
public enum ServiceOperation
{
    /// <summary>Alterar o tipo de arranque (auto | demand | disabled).</summary>
    ChangeStartMode,

    /// <summary>Iniciar o serviço.</summary>
    Start,

    /// <summary>Parar o serviço.</summary>
    Stop,
}

/// <summary>
/// Tarefa genérica de serviço (spec §4.6): executa EXATAMENTE UM comando
/// sc.exe que já está na whitelist do CommandExecutor — nada de outros
/// binários, nada de metacaracteres. O nome do serviço (chave curta) é
/// validado pelo padrão da whitelist.
///
/// Reversibilidade: o backup (estado anterior via WMI) é criado pelo
/// ChangeApplier ANTES do apply; o rollback devolve start mode + estado.
/// </summary>
public sealed class ServiceTask : IOptimizationTask
{
    /// <summary>
    /// Exit codes do sc.exe que indicam "já estava no estado pretendido"
    /// (idempotência — não são erros): 1056 = já a correr, 1062 = já parado.
    /// </summary>
    private const int ServiceAlreadyRunning = 1056;
    private const int ServiceNotActive = 1062;

    private readonly ISystemCommandExecutor _executor;
    private readonly ServiceOperation _operation;
    private readonly string? _desiredStartMode;
    private readonly string _justification;
    private readonly string? _documentationUrl;

    public ServiceTask(
        string key,
        string serviceKey,
        string displayName,
        ServiceOperation operation,
        string? desiredStartMode,
        string justification,
        ISystemCommandExecutor executor,
        string? documentationUrl = null,
        RiskLevel risk = RiskLevel.Low,
        FeatureVisibility visibility = FeatureVisibility.Advanced)
    {
        Key = key;
        ServiceKey = serviceKey;
        DisplayName = displayName;
        _operation = operation;
        _desiredStartMode = desiredStartMode;
        _justification = justification;
        _executor = executor;
        _documentationUrl = documentationUrl;
        Risk = risk;
        Visibility = visibility;
    }

    public string Key { get; }

    /// <summary>Nome curto do serviço (WMI Name) — o que o sc.exe usa.</summary>
    public string ServiceKey { get; }

    public string DisplayName { get; }

    public string Name => _operation switch
    {
        ServiceOperation.ChangeStartMode => $"{_desiredStartMode == "disabled" ? "Desativar" : "Habilitar"} serviço: {DisplayName}",
        ServiceOperation.Start => "Iniciar serviço: " + DisplayName,
        ServiceOperation.Stop => "Parar serviço: " + DisplayName,
        _ => DisplayName,
    };

    public string Description =>
        $"Serviço: {DisplayName} ({ServiceKey}). Ação: {OperationText()}. " +
        _justification +
        " Reversível por rollback (o estado anterior fica guardado no backup).";

    private string OperationText() => _operation switch
    {
        ServiceOperation.ChangeStartMode => "alterar o tipo de arranque para " + _desiredStartMode,
        ServiceOperation.Start => "iniciar o serviço",
        ServiceOperation.Stop => "parar o serviço",
        _ => string.Empty,
    };

    public string? DocumentationUrl => _documentationUrl;

    public RiskLevel Risk { get; }

    public bool Reversible => true;

    public bool RequiresElevation => true;

    public FeatureVisibility Visibility { get; }

    public TimeSpan EstimatedDuration => TimeSpan.FromSeconds(5);

    public IReadOnlyList<ChangeDescriptor> DescribeChanges() => new[]
    {
        new ChangeDescriptor(
            ChangeKind.Service,
            ServiceKey,
            _operation == ServiceOperation.ChangeStartMode
                ? "start=" + _desiredStartMode
                : _operation.ToString(),
            _justification),
    };

    public async Task ApplyAsync(CancellationToken ct = default)
    {
        var spec = _operation switch
        {
            ServiceOperation.ChangeStartMode => new CommandSpec("sc.exe", $"change {ServiceKey} start={_desiredStartMode}"),
            ServiceOperation.Start => new CommandSpec("sc.exe", $"start {ServiceKey}"),
            ServiceOperation.Stop => new CommandSpec("sc.exe", $"stop {ServiceKey}"),
            _ => throw new InvalidOperationException("Operação de serviço desconhecida."),
        };

        var result = await _executor.RunAsync(spec, ct);

        if (!result.WasWhitelisted)
            throw new InvalidOperationException(
                $"Comando rejeitado pela whitelist do CommandExecutor: {result.RejectionReason}");

        if (result.Succeeded)
            return;

        bool idempotent = (_operation == ServiceOperation.Start && result.ExitCode == ServiceAlreadyRunning)
                          || (_operation == ServiceOperation.Stop && result.ExitCode == ServiceNotActive);
        if (idempotent)
            return;

        throw new InvalidOperationException(
            $"sc.exe terminou com código {result.ExitCode}: {result.StandardError.Trim()}"
            + (string.IsNullOrWhiteSpace(result.StandardError) ? " (sem detalhes no stderr)" : string.Empty));
    }
}
