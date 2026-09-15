using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace HLProOptimizer.Presentation.Views.Dialogs;

/// <summary>Tipo visual da mensagem (define ícone e cor de destaque).</summary>
public enum MessageDialogKind
{
    /// <summary>Informação neutra.</summary>
    Info,

    /// <summary>Operação concluída com sucesso.</summary>
    Success,

    /// <summary>Atenção/aviso.</summary>
    Warning,

    /// <summary>Erro.</summary>
    Error,

    /// <summary>Pergunta (confirmação).</summary>
    Question
}

/// <summary>
/// Caixa de diálogo do tema dark premium (substitui o <c>MessageBox</c> do Windows).
/// </summary>
/// <remarks>
/// Uso: <c>MessageDialog.Show(owner, titulo, mensagem, MessageDialogKind.Warning)</c>
/// retorna <c>true</c>/<c>false</c> em confirmações e <c>true</c> em mensagens simples.
/// </remarks>
public partial class MessageDialog : Window
{
    private MessageDialog(
        string title,
        string message,
        MessageDialogKind kind,
        bool isConfirmation,
        string confirmText,
        string? cancelText,
        bool isDestructive)
    {
        InitializeComponent();

        TitleText.Text = title;
        MessageText.Text = message;

        ConfirmButton.Content = confirmText;
        CancelButton.Visibility = isConfirmation ? Visibility.Visible : Visibility.Collapsed;

        if (isConfirmation)
        {
            CancelButton.Content = cancelText ?? "Cancelar";
            CancelButton.IsCancel = true;
        }

        if (isDestructive)
        {
            ConfirmButton.Style = (Style)FindResource("Button.Danger");
        }

        ApplyKind(kind);
    }

    /// <summary>Exibe o diálogo de forma modal.</summary>
    /// <param name="owner">Janela dona (centraliza e bloqueia a interação).</param>
    /// <param name="title">Título.</param>
    /// <param name="message">Mensagem.</param>
    /// <param name="kind">Tipo visual.</param>
    /// <param name="isConfirmation">Se mostra o botão cancelar.</param>
    /// <param name="confirmText">Texto do botão principal.</param>
    /// <param name="cancelText">Texto do botão secundário.</param>
    /// <param name="isDestructive">Se destaca o botão principal em vermelho.</param>
    /// <returns>Resposta do usuário.</returns>
    public static bool Show(
        Window? owner,
        string title,
        string message,
        MessageDialogKind kind = MessageDialogKind.Info,
        bool isConfirmation = false,
        string confirmText = "OK",
        string? cancelText = null,
        bool isDestructive = false)
    {
        var dialog = new MessageDialog(title, message, kind, isConfirmation, confirmText, cancelText, isDestructive);

        if (owner is not null && owner.IsLoaded)
        {
            dialog.Owner = owner;
        }
        else
        {
            // Sem dono (ex.: chamada durante o startup): centraliza na tela.
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return dialog.ShowDialog() == true;
    }

    /// <summary>Aplica ícone, cor e foco conforme o tipo da mensagem.</summary>
    private void ApplyKind(MessageDialogKind kind)
    {
        var (glyph, brushKey, backgroundKey) = kind switch
        {
            MessageDialogKind.Success => ("\u2713", "Brush.Success", "Brush.Success.Soft"),
            MessageDialogKind.Warning => ("\u26A0", "Brush.Warning", "Brush.Warning.Soft"),
            MessageDialogKind.Error => ("\u2716", "Brush.Danger", "Brush.Danger.Soft"),
            MessageDialogKind.Question => ("?", "Brush.Accent.Solid", "Brush.Primary.Soft"),
            _ => ("\u2756", "Brush.Info", "Brush.Info.Soft")
        };

        IconGlyph.Text = glyph;
        IconGlyph.Foreground = (Brush)FindResource(brushKey);
        IconHost.Background = (Brush)FindResource(backgroundKey);

        // Em confirmações, o foco vai para o botão principal (Enter confirma).
        Loaded += (_, _) =>
        {
            if (CancelButton.Visibility == Visibility.Visible)
            {
                ConfirmButton.Focus();
            }
        };
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnDragMove(object sender, MouseButtonEventArgs e)
    {
        // Janela sem chrome: arrasta pelo conteúdo.
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // DragMove só é válido com o botão pressionado; ignora chamadas tardias.
            }
        }
    }
}
