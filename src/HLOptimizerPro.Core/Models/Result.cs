namespace HLOptimizerPro.Core.Models;

/// <summary>
/// Resultado de uma operação que pode falhar de forma esperada (falta de permissões,
/// comando externo indisponível, serviço já otimizado...).
/// Evita usar exceções para fluxo normal e permite mostrar mensagens claras na UI.
/// </summary>
public class Result
{
    protected Result(bool success, string message)
    {
        Success = success;
        Message = message;
    }

    public bool Success { get; }

    /// <summary>Mensagem pronta a apresentar ao utilizador (pt-PT/pt-AO).</summary>
    public string Message { get; }

    /// <summary>Detalhe técnico opcional (vai para o log, não para a UI).</summary>
    public string? TechnicalDetails { get; init; }

    public static Result Ok(string message = "Operação concluída com sucesso.", string? technicalDetails = null)
        => new(true, message) { TechnicalDetails = technicalDetails };

    public static Result Fail(string message, string? technicalDetails = null)
        => new(false, message) { TechnicalDetails = technicalDetails };

    public static Result From(bool success, string successMessage, string failureMessage, string? technicalDetails = null)
        => success ? Ok(successMessage) : Fail(failureMessage, technicalDetails);
}

/// <summary>Resultado de uma operação que devolve um valor em caso de sucesso.</summary>
public sealed class Result<T> : Result
{
    private Result(bool success, string message, T? value) : base(success, message) => Value = value;

    public T? Value { get; }

    public static Result<T> Ok(T value, string message = "Operação concluída com sucesso.") => new(true, message, value);

    public static new Result<T> Fail(string message, string? technicalDetails = null)
        => new(false, message, default) { TechnicalDetails = technicalDetails };

    /// <summary>
    /// Falha parcial: houve avisos, mas a operação produziu um resultado útil que a interface
    /// deve mostrar (ex.: limpeza que removeu ficheiros mas falhou num dos alvos).
    /// </summary>
    public static Result<T> Partial(T value, string message, string? technicalDetails = null)
        => new(false, message, value) { TechnicalDetails = technicalDetails };

    public static Result<T> From(bool success, T? value, string successMessage, string failureMessage, string? technicalDetails = null)
        => success ? Ok(value!, successMessage) : Fail(failureMessage, technicalDetails);
}
