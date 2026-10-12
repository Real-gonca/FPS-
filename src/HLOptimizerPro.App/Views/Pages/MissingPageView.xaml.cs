using System.Windows.Controls;

namespace HLOptimizerPro.App.Views.Pages;

/// <summary>Vista de recurso (fallback) usada pelo ViewLocator quando não existe vista para a página.</summary>
public partial class MissingPageView : UserControl
{
    public MissingPageView()
    {
        InitializeComponent();
    }

    /// <summary>Mensagem explicativa definida pelo ViewLocator.</summary>
    public string Message
    {
        get => MessageBlock.Text;
        set => MessageBlock.Text = value;
    }
}
