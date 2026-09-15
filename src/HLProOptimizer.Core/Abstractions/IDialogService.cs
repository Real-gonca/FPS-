using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>
/// Abstração de diálogos para que os ViewModels não referenciem tipos WPF
/// (MessageBox/Window), preservando a testabilidade.
/// </summary>
public interface IDialogService
{
    /// <summary>Exibe uma mensagem informativa.</summary>
    Task ShowInfoAsync(string title, string message);

    /// <summary>Exibe um aviso.</summary>
    Task ShowWarningAsync(string title, string message);

    /// <summary>Exibe um erro.</summary>
    Task ShowErrorAsync(string title, string message);

    /// <summary>Confirmação sim/não.</summary>
    /// <param name="title">Título.</param>
    /// <param name="message">Mensagem.</param>
    /// <param name="confirmText">Texto do botão de confirmação.</param>
    /// <param name="cancelText">Texto do botão de cancelamento.</param>
    /// <param name="isDestructive">Se a ação é destrutiva (destaca o botão em vermelho).</param>
    Task<bool> ConfirmAsync(string title, string message, string confirmText = "Confirmar", string cancelText = "Cancelar", bool isDestructive = false);

    /// <summary>Diálogo de progresso com log em tempo real e cancelamento.</summary>
    /// <param name="title">Título da janela.</param>
    /// <param name="work">Trabalho assíncrono a executar.</param>
    /// <returns>True quando concluído sem cancelamento.</returns>
    Task<bool> ShowProgressAsync(string title, Func<IProgress<ScanProgress>, CancellationToken, Task> work);

    /// <summary>Abre um diálogo de seleção de arquivo.</summary>
    /// <param name="options">Opções do diálogo.</param>
    /// <returns>Caminho selecionado ou null.</returns>
    Task<string?> ShowOpenFileDialogAsync(FileDialogOptions options);

    /// <summary>Abre um diálogo de gravação de arquivo.</summary>
    /// <param name="options">Opções do diálogo.</param>
    /// <returns>Caminho de destino ou null.</returns>
    Task<string?> ShowSaveFileDialogAsync(FileDialogOptions options);

    /// <summary>Exibe o resultado de uma otimização em uma janela de resumo.</summary>
    /// <param name="result">Resultado da otimização.</param>
    Task ShowOptimizationResultAsync(OptimizationResult result);
}
