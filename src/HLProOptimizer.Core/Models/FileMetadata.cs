namespace HLProOptimizer.Core.Models;

/// <summary>Metadados de um executável (informações de versão + assinatura).</summary>
/// <param name="FilePath">Caminho do arquivo.</param>
/// <param name="Publisher">Empresa (CompanyName ou signatário Authenticode).</param>
/// <param name="ProductName">Nome do produto.</param>
/// <param name="FileVersion">Versão do arquivo.</param>
/// <param name="Description">Descrição.</param>
/// <param name="IsSigned">Se possui assinatura digital válida.</param>
/// <param name="SizeBytes">Tamanho do arquivo.</param>
public sealed record FileMetadata(
    string FilePath,
    string? Publisher,
    string? ProductName,
    string? FileVersion,
    string? Description,
    bool IsSigned,
    long SizeBytes);
