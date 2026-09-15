using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>
/// Cálculo do Score do Sistema (0-100). Implementação pura e determinística na
/// camada Application - totalmente coberta por testes unitários.
/// </summary>
public interface ISystemScoreCalculator
{
    /// <summary>Calcula o score composto (desempenho, estabilidade, segurança, limpeza).</summary>
    /// <param name="input">Métricas de entrada.</param>
    SystemScoreResult Calculate(ScoreInput input);
}
