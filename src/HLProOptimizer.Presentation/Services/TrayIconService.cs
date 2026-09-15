using System.Runtime.InteropServices;
using System.Windows;
using HLProOptimizer.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using WinForms = System.Windows.Forms;

namespace HLProOptimizer.Presentation.Services;

/// <summary>
/// Ícone da bandeja do sistema (system tray) com menu de ações rápidas.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que WinForms?</b> O WPF não tem NotifyIcon nativo. Usar
/// <c>UseWindowsForms=true</c> evita uma dependência NuGet extra (Hardcodet etc.)
/// e mantém o comportamento padrão do Windows (balões, menu de contexto, duplo clique).
/// </para>
/// <para>
/// <b>Ícone gerado em runtime</b>: desenhamos um bitmap 32×32 com o gradiente da
/// marca e as iniciais "HL" — sem necessidade de um <c>.ico</c> binário no repositório.
/// O handle é liberado com <c>DestroyIcon</c> no <see cref="Dispose"/>.
/// </para>
/// <para>
/// O serviço respeita <see cref="Core.Models.AppSettings.ShowTrayIcon"/> e reage a
/// mudanças de configuração em runtime (evento <c>SettingsChanged</c>).
/// </para>
/// </remarks>
public sealed class TrayIconService : IDisposable
{
    private readonly ISettingsService _settings;
    private readonly ILocalizationService _localization;
    private readonly ILogger<TrayIconService> _logger;

    private WinForms.NotifyIcon? _notifyIcon;
    private Drawing.Icon? _icon;
    private IntPtr _iconHandle;
    private Window? _window;
    private bool _disposed;

    /// <summary>Cria o serviço da bandeja.</summary>
    /// <param name="settings">Configurações (mostrar ícone, minimizar para a bandeja).</param>
    /// <param name="localization">Localização dos textos do menu.</param>
    /// <param name="logger">Logger.</param>
    public TrayIconService(ISettingsService settings, ILocalizationService localization, ILogger<TrayIconService> logger)
    {
        _settings = settings;
        _localization = localization;
        _logger = logger;

        _settings.SettingsChanged += OnSettingsChanged;
    }

    /// <summary>
    /// Solicitado quando o usuário abre a janela pela bandeja.
    /// O parâmetro é a chave de navegação desejada (ou <c>null</c> para manter a tela atual).
    /// </summary>
    public event EventHandler<string?>? OpenRequested;

    /// <summary>Solicitado quando o usuário pede para sair do aplicativo.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>Solicitado quando o usuário alterna o Modo Gamer pelo menu da bandeja.</summary>
    public event EventHandler? GameModeToggleRequested;

    /// <summary>Indica se o ícone está visível.</summary>
    public bool IsVisible => _notifyIcon?.Visible ?? false;

    /// <summary>Inicializa o ícone associado a uma janela.</summary>
    /// <param name="window">Janela principal.</param>
    public void Initialize(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        _window = window;

        try
        {
            _icon = CreateBrandIcon();

            _notifyIcon = new WinForms.NotifyIcon
            {
                Icon = _icon,
                Text = "HL PRO OPTIMIZER",
                Visible = _settings.Current.ShowTrayIcon
            };

            _notifyIcon.DoubleClick += (_, _) => OnOpen();
            _notifyIcon.ContextMenuStrip = BuildMenu();

            _logger.LogInformation("Ícone da bandeja inicializado (visível={Visible}).", _notifyIcon.Visible);
        }
        catch (Exception ex)
        {
            // A bandeja é conveniência: se falhar, o aplicativo segue sem ela.
            _logger.LogWarning(ex, "Não foi possível inicializar o ícone da bandeja.");
        }
    }

    /// <summary>Exibe uma notificação (balão) na bandeja.</summary>
    /// <param name="title">Título.</param>
    /// <param name="message">Mensagem.</param>
    /// <param name="isWarning">Se usa o ícone de aviso.</param>
    public void ShowBalloon(string title, string message, bool isWarning = false)
    {
        if (_notifyIcon is null || !_notifyIcon.Visible || !_settings.Current.NotificationsEnabled)
        {
            return;
        }

        try
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = message;
            _notifyIcon.BalloonTipIcon = isWarning ? WinForms.ToolTipIcon.Warning : WinForms.ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(4000);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao exibir a notificação da bandeja.");
        }
    }

    /// <summary>Reconstrói o menu (usado após trocar o idioma).</summary>
    public void Refresh()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Visible = _settings.Current.ShowTrayIcon;
        _notifyIcon.ContextMenuStrip = BuildMenu();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _settings.SettingsChanged -= OnSettingsChanged;

        try
        {
            if (_notifyIcon is not null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            _icon?.Dispose();
            _icon = null;

            if (_iconHandle != IntPtr.Zero)
            {
                _ = DestroyIcon(_iconHandle);
                _iconHandle = IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao liberar os recursos da bandeja.");
        }
    }

    // ---------------------------------------------------------------------
    // Internos
    // ---------------------------------------------------------------------

    private void OnSettingsChanged(object? sender, Core.Models.AppSettings settings) => Refresh();

    private WinForms.ContextMenuStrip BuildMenu()
    {
        var menu = new WinForms.ContextMenuStrip
        {
            Font = new Drawing.Font("Segoe UI", 9.5f),
            ShowImageMargin = false
        };

        menu.Items.Add(new WinForms.ToolStripMenuItem(_localization["App_Title"])
        {
            Font = new Drawing.Font("Segoe UI Semibold", 9.5f),
            Enabled = false
        });

        menu.Items.Add(new WinForms.ToolStripSeparator());

        menu.Items.Add(new WinForms.ToolStripMenuItem(_localization["Nav_Dashboard"]), (_, _) => OnOpen(NavigationKeys.Dashboard));
        menu.Items.Add(new WinForms.ToolStripMenuItem(_localization["Nav_Optimization"]), (_, _) => OnOpen(NavigationKeys.Optimization));
        menu.Items.Add(new WinForms.ToolStripMenuItem(_localization["Nav_GameMode"]), (_, _) => GameModeToggleRequested?.Invoke(this, EventArgs.Empty));

        menu.Items.Add(new WinForms.ToolStripSeparator());

        menu.Items.Add(new WinForms.ToolStripMenuItem(_localization["App_Close"]), (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        return menu;
    }

    /// <summary>Restaura a janela e pede a navegação para a tela escolhida.</summary>
    /// <param name="navigationKey">Chave da tela (ou <c>null</c> para manter a atual).</param>
    private void OnOpen(string? navigationKey = null)
    {
        try
        {
            if (_window is not null)
            {
                _window.Show();

                if (_window.WindowState == WindowState.Minimized)
                {
                    _window.WindowState = WindowState.Normal;
                }

                _window.Activate();
            }

            OpenRequested?.Invoke(this, navigationKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao restaurar a janela a partir da bandeja.");
        }
    }

    /// <summary>Desenha o ícone da marca (gradiente azul → ciano com "HL").</summary>
    private Drawing.Icon CreateBrandIcon()
    {
        using var bitmap = new Drawing.Bitmap(32, 32);

        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Drawing.Color.Transparent);

            using var background = new Drawing2D.LinearGradientBrush(
                new Drawing.Rectangle(0, 0, 32, 32),
                Drawing.Color.FromArgb(255, 37, 99, 235),
                Drawing.Color.FromArgb(255, 6, 182, 212),
                Drawing2D.LinearGradientMode.ForwardDiagonal);

            using var path = RoundedRect(new Drawing.Rectangle(1, 1, 30, 30), 8);
            graphics.FillPath(background, path);

            using var font = new Drawing.Font("Segoe UI", 11f, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
            using var textBrush = new Drawing.SolidBrush(Drawing.Color.White);

            var format = new Drawing.StringFormat
            {
                Alignment = Drawing.StringAlignment.Center,
                LineAlignment = Drawing.StringAlignment.Center
            };

            graphics.DrawString("HL", font, textBrush, new Drawing.RectangleF(0, 0, 32, 32), format);
        }

        _iconHandle = bitmap.GetHicon();

        return Drawing.Icon.FromHandle(_iconHandle);
    }

    private static Drawing2D.GraphicsPath RoundedRect(Drawing.Rectangle bounds, int radius)
    {
        var path = new Drawing2D.GraphicsPath();
        var diameter = radius * 2;

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
