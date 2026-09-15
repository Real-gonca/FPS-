namespace HLProOptimizer.Core.Models;

/// <summary>Informações exibidas na aba "Sobre".</summary>
/// <param name="ProductName">Nome do produto.</param>
/// <param name="Version">Versão semântica.</param>
/// <param name="BuildDate">Data de compilação do assembly.</param>
/// <param name="License">Tipo de licença.</param>
/// <param name="RuntimeVersion">Versão do .NET em execução.</param>
/// <param name="LogFilePath">Caminho do log ativo.</param>
/// <param name="DatabasePath">Caminho do banco SQLite.</param>
public sealed record AboutInfo(
    string ProductName,
    string Version,
    DateTime BuildDate,
    string License,
    string RuntimeVersion,
    string LogFilePath,
    string DatabasePath);
