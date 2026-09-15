using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Presentation.Views.Dialogs;

/// <summary>
/// Janela de progresso com log em tempo real e cancelamento.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thread de UI.</b> O <see cref="Progress{T}"/> é criado na thread de UI (no
/// <c>Loaded</c>), então ele captura o <c>SynchronizationContext</c> do WPF e os
/// callbacks chegam já na thread certa — mutar a <see cref="ObservableCollection{T}"/>
/// do log é seguro sem <c>Dispatcher.Invoke</c>.
/// </para>
/// <para>
/// <b>Log limitado.</b> Operações como SFC/DISM emitem milhares de linhas; mantemos
/// as últimas <see cref="MaxLogLines"/> para não estourar memória nem travar a renderização.
/// </para>
/// <para>
/// <c>ShowDialog()</c> bomba um loop de mensagens aninhado, então o trabalho
/// assíncrono continua progredindo enquanto a janela está modal.
/// </para>
/// </remarks>
public partial class ProgressDialog : Window
{
    /// <summary>Linha do log (horário + mensagem).</summary>
    /// <param name="Timestamp">Hora do registro.</param>
    /// <param name="Message">Mensagem.</param>
    public sealed record LogLine(string Timestamp, string Message);

    private const int MaxLogLines = 400;

    private readonly ObservableCollection<LogLine> _log = [];
    private readonly CancellationTokenSource _cts = new();
    private readonly Func<IProgress<ScanProgress>, CancellationToken, Task> _work;
    private readonly DispatcherTimer _clock;
    private readonly Stopwatch _stopwatch = new();
    private bool _finished;

    /// <summary>Cria a janela de progresso.</summary>
    /// <param name="title">Título exibido.</param>
    /// <param name="work">Trabalho assíncrono (recebe progresso e token de cancelamento).</param>
    public ProgressDialog(string title, Func<IProgress<ScanProgress>, CancellationToken, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        InitializeComponent();

        _work = work;

        TitleText.Text = title;
        LogList.ItemsSource = _log;

        _clock = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        _clock.Tick += (_, _) => ElapsedText.Text = _stopwatch.Elapsed.ToString(@"mm\:ss");

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    /// <summary>True quando o trabalho terminou sem cancelamento.</summary>
    public bool WasCompleted { get; private set; }

    /// <summary>True quando o usuário pediu cancelamento.</summary>
    public bool WasCancelled => _cts.IsCancellationRequested;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _stopwatch.Start();
        _clock.Start();

        // Progress<T> captura o SynchronizationContext atual (UI) => callbacks na thread certa.
        var progress = new Progress<ScanProgress>(OnProgressReported);

        AppendLog("Iniciando...");

        try
        {
            await _work(progress, _cts.Token).ConfigureAwait(true);

            WasCompleted = true;
            AppendLog("Concluído.");
        }
        catch (OperationCanceledException)
        {
            AppendLog("Operação cancelada pelo usuário.");
        }
        catch (Exception ex)
        {
            AppendLog($"Erro: {ex.Message}");
        }
        finally
        {
            Finish();
        }
    }

    /// <summary>Atualiza barra, percentual e log com o progresso reportado.</summary>
    private void OnProgressReported(ScanProgress progress)
    {
        StepText.Text = string.IsNullOrWhiteSpace(progress.Message)
            ? progress.CurrentStepName
            : progress.Message;

        if (progress.TotalSteps > 0)
        {
            Progress.IsIndeterminate = false;
            Progress.Value = progress.Percent;
            PercentText.Text = $"{progress.Percent:0}%";
        }
        else
        {
            // Sem total conhecido: barra animada.
            Progress.IsIndeterminate = true;
            PercentText.Text = "…";
        }

        if (!string.IsNullOrWhiteSpace(progress.Message))
        {
            AppendLog(progress.Message);
        }
    }

    /// <summary>Encerra a operação: troca os botões e devolve o resultado.</summary>
    private void Finish()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;

        _clock.Stop();
        _stopwatch.Stop();
        ElapsedText.Text = _stopwatch.Elapsed.ToString(@"mm\:ss");

        Progress.IsIndeterminate = false;
        Progress.Value = WasCompleted ? 100 : Progress.Value;
        PercentText.Text = WasCompleted ? "100%" : PercentText.Text;
        StepText.Text = WasCompleted ? "Concluído." : (WasCancelled ? "Cancelado." : "Interrompido.");

        CancelButton.Visibility = Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Visible;
        CloseButton.Focus();
    }

    private void AppendLog(string message)
    {
        if (_log.Count >= MaxLogLines)
        {
            _log.RemoveAt(0);
        }

        _log.Add(new LogLine(DateTime.Now.ToString("HH:mm:ss"), message));

        // Mantém a última linha visível.
        LogList.ScrollIntoView(_log[^1]);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        StepText.Text = "Cancelando...";
        AppendLog("Cancelamento solicitado...");

        _cts.Cancel();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Fecha enquanto o trabalho roda: pede cancelamento e aguarda o finally.
        if (!_finished)
        {
            _cts.Cancel();
            e.Cancel = true;

            // Dá um tempo para o trabalho responder ao cancelamento e então fecha.
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (!_finished)
                    {
                        Finish();
                    }

                    Close();
                }),
                DispatcherPriority.Background);

            return;
        }

        // DialogResult NÃO é usado de propósito: atribuí-lo aqui fecharia a janela
        // em cascata (reentrância no Closing). O chamador lê WasCompleted após ShowDialog.
        _cts.Dispose();
    }

    private void OnDragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // Ignora chamadas fora do estado pressionado.
            }
        }
    }
}
