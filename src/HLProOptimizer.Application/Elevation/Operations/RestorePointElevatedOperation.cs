using HLProOptimizer.Core.Abstractions;
using Newtonsoft.Json;

namespace HLProOptimizer.Application.Elevation.Operations;

/// <summary>Cria um ponto de restauração do Windows em processo elevado.</summary>
public sealed class RestorePointElevatedOperation : IElevatedOperation
{
    private readonly IBackupService _backupService;

    /// <summary>Cria a operação.</summary>
    /// <param name="backupService">Serviço de backup.</param>
    public RestorePointElevatedOperation(IBackupService backupService)
    {
        _backupService = backupService;
    }

    /// <inheritdoc />
    public string Id => ElevatedOperationIds.RestorePoint;

    /// <inheritdoc />
    public string Description => "Criar ponto de restauração como administrador";

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(string payloadJson, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var request = JsonConvert.DeserializeObject<RestorePointRequest>(payloadJson ?? string.Empty) ?? new RestorePointRequest();
        var description = string.IsNullOrWhiteSpace(request.Description)
            ? $"HL PRO OPTIMIZER - {DateTime.Now:dd/MM/yyyy HH:mm}"
            : request.Description;

        progress?.Report($"Criando ponto de restauração '{description}'...");

        var point = await _backupService.CreateRestorePointAsync(description, cancellationToken).ConfigureAwait(false);

        return point is null
            ? JsonConvert.SerializeObject(new { Success = false, Description = description })
            : JsonConvert.SerializeObject(point);
    }

    /// <summary>Requisição da operação.</summary>
    public sealed class RestorePointRequest
    {
        /// <summary>Descrição do ponto.</summary>
        public string? Description { get; set; }
    }
}
