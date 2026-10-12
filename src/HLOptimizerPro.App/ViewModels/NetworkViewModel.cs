using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLOptimizerPro.App.Services;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services;

namespace HLOptimizerPro.App.ViewModels;

/// <summary>
/// ViewModel da ferramenta Rede (3.4-b).
///
/// O que faz (tudo medido, nada estimado):
///   • Mostra o adaptador ativo, IPv4, máscara, gateway, DNS e velocidade de ligação;
///   • Mede a latência ao gateway e à Internet (4 amostras, média) e o IP público/operadora;
///   • Faz benchmark real a 5 servidores DNS (Google, Cloudflare, OpenDNS, Norton, Quad9) com
///     envio de consultas UDP à porta 53 e apresenta média, jitter, perda de pacotes e pontuação;
///   • Recomenda o melhor servidor e aplica-o (com opção de limpar a cache DNS e renovar o IP);
///   • Permite limpar a cache DNS, renovar IP e repor a pilha de rede.
/// </summary>
public sealed partial class NetworkViewModel : ViewModelBase
{
    private readonly NetworkService _network;
    private readonly DnsBenchmarkService _dns;
    private readonly IDialogService _dialogs;

    public NetworkViewModel(
        NotificationService notifications,
        NetworkService network,
        DnsBenchmarkService dns,
        IDialogService dialogs)
        : base(notifications)
    {
        _network = network;
        _dns = dns;
        _dialogs = dialogs;

        Title = "Rede";
        Subtitle = "Estado da ligação, latências e o melhor servidor DNS para a sua Internet";
        IconKind = "LanConnect";

        DnsResults = new ObservableCollection<DnsRow>();
    }

    // ---- Estado da ligação ----------------------------------------------------

    [ObservableProperty] private string _connectionStateText = "A analisar a ligação…";
    [ObservableProperty] private HealthLevel _connectionHealth = HealthLevel.Desconhecido;
    [ObservableProperty] private string _adapterNameText = "—";
    [ObservableProperty] private string _adapterTypeText = "—";
    [ObservableProperty] private string _ipv4Text = "—";
    [ObservableProperty] private string _subnetText = "—";
    [ObservableProperty] private string _gatewayText = "—";
    [ObservableProperty] private string _dnsCurrentText = "—";
    [ObservableProperty] private string _speedText = "—";
    [ObservableProperty] private string _macText = "—";
    [ObservableProperty] private string _publicIpText = "—";
    [ObservableProperty] private string _ispText = "—";
    [ObservableProperty] private string _gatewayLatencyText = "—";
    [ObservableProperty] private string _internetLatencyText = "—";
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private string _recommendationText = string.Empty;
    [ObservableProperty] private HealthLevel _recommendationLevel = HealthLevel.Desconhecido;
    [ObservableProperty] private bool _hasRecommendation;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private string _currentDnsServersText = "—";

    /// <summary>Resultados do benchmark DNS (um por fornecedor testado).</summary>
    public ObservableCollection<DnsRow> DnsResults { get; }

    private DnsProvider? _recommendedProvider;
    private NetworkAdapterInfo? _activeAdapter;

    /// <summary>Linha do benchmark: já com os textos formatados para a interface.</summary>
    public sealed record DnsRow(
        DnsProvider Provider,
        string Name,
        string ServersText,
        string AverageText,
        string JitterText,
        string LossText,
        string ScoreText,
        string StatusText,
        HealthLevel Health,
        bool IsBest,
        bool IsCurrent)
    {
        public string BestText => IsBest ? "Melhor opção" : IsCurrent ? "Em uso" : string.Empty;
    }

    // ---- Análise --------------------------------------------------------------

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        await RunBusyAsync(async () =>
        {
            ProgressText = "A analisar a ligação…";

            var progress = new Progress<string>(message => ProgressText = message);
            var report = await _network.AnalyzeAsync(runDnsBenchmark: true, progress: progress);

            _activeAdapter = report.ActiveAdapter;

            ConnectionStateText = report.InternetAvailable
                ? "Ligação à Internet ativa"
                : "Sem ligação à Internet detetada";
            ConnectionHealth = report.InternetAvailable ? HealthLevel.Otimo : HealthLevel.Critico;

            AdapterNameText = report.ActiveAdapter?.Name ?? "Nenhum adaptador ativo";
            AdapterTypeText = report.ActiveAdapter?.Description ?? "—";
            Ipv4Text = report.ActiveAdapter?.IPv4 ?? "—";
            SubnetText = report.ActiveAdapter?.SubnetMask ?? "—";
            GatewayText = report.ActiveAdapter?.Gateway ?? "—";
            DnsCurrentText = report.ActiveAdapter?.DnsText ?? "—";
            CurrentDnsServersText = report.ActiveAdapter is { } adapter
                ? string.Join(", ", _network.GetConfiguredDns(adapter.Name))
                : "—";
            SpeedText = report.ActiveAdapter?.SpeedText ?? "—";
            MacText = report.ActiveAdapter?.MacAddress ?? "—";

            PublicIpText = report.PublicIpAddress;
            IspText = report.Isp;

            GatewayLatencyText = report.GatewayLatencyMs > 0
                ? Formatters.Milliseconds(report.GatewayLatencyMs)
                : "não medido";
            InternetLatencyText = report.InternetLatencyMs > 0
                ? Formatters.Milliseconds(report.InternetLatencyMs)
                : "não medido";

            BuildDnsRows(report);

            RecommendationText = report.DnsChangeRecommended && report.BestDns is not null
                ? $"Recomendação: mudar o DNS para {report.BestDns.Provider.Name} " +
                  $"({Formatters.Milliseconds(report.BestDns.AverageMs)} de média) — " +
                  $"{report.BestDns.StatusText.ToLowerInvariant()}."
                : report.BestDns is not null
                    ? $"O DNS atual já é a melhor opção medida ({report.BestDns.Provider.Name})."
                    : "Não foi possível medir os servidores DNS (verifique a ligação à Internet).";

            HasRecommendation = report.BestDns is not null;
            RecommendationLevel = report.BestDns?.Health ?? HealthLevel.Desconhecido;
            _recommendedProvider = report.BestDns?.Provider;

            SetStatus($"Análise concluída — latência à Internet {InternetLatencyText}.");
        }, "A analisar a rede…");
    }

    private void BuildDnsRows(NetworkReport report)
    {
        DnsResults.Clear();
        _recommendedProvider = null;

        if (report.DnsResults.Count == 0)
        {
            HasResults = false;
            return;
        }

        var reachable = report.DnsResults.Where(r => r.IsReachable).ToList();
        var best = reachable.Count > 0 ? reachable.OrderBy(r => r.AverageMs).First() : null;

        foreach (var result in report.DnsResults)
        {
            DnsResults.Add(new DnsRow(
                result.Provider,
                result.Provider.Name,
                result.Provider.ServersText,
                result.IsReachable ? Formatters.Milliseconds(result.AverageMs) : "sem resposta",
                result.IsReachable ? Formatters.Milliseconds(result.JitterMs) : "—",
                $"{result.PacketLossPercent:0} % de perda",
                result.ScoreText,
                result.StatusText,
                result.Health,
                IsBest: ReferenceEquals(result, best),
                IsCurrent: result.Provider.IsCurrent));
        }

        HasResults = true;
        _recommendedProvider = best?.Provider;
        RecommendationLevel = best?.Health ?? HealthLevel.Desconhecido;
    }

    /// <summary>Repete o benchmark DNS sem fazer a análise completa (mais rápido).</summary>
    [RelayCommand]
    private async Task BenchmarkAsync()
    {
        await RunBusyAsync(async () =>
        {
            ProgressText = "A testar servidores DNS…";

            var providers = DnsBenchmarkService.Providers.ToList();
            var results = new List<DnsBenchmarkResult>();

            foreach (var provider in providers)
            {
                ProgressText = $"A testar {provider.Name}…";
                results.Add(await _dns.TestAsync(provider));
            }

            var reachable = results.Where(r => r.IsReachable).ToList();
            var best = reachable.Count > 0 ? reachable.OrderBy(r => r.AverageMs).First() : null;

            DnsResults.Clear();
            foreach (var result in results)
            {
                DnsResults.Add(new DnsRow(
                    result.Provider, result.Provider.Name, result.Provider.ServersText,
                    result.IsReachable ? Formatters.Milliseconds(result.AverageMs) : "sem resposta",
                    result.IsReachable ? Formatters.Milliseconds(result.JitterMs) : "—",
                    $"{result.PacketLossPercent:0} % de perda",
                    result.ScoreText, result.StatusText, result.Health,
                    IsBest: ReferenceEquals(result, best),
                    IsCurrent: result.Provider.IsCurrent));
            }

            HasResults = true;
            _recommendedProvider = best?.Provider;

            RecommendationText = best is not null
                ? $"{best.Provider.Name} respondeu em {Formatters.Milliseconds(best.AverageMs)} de média " +
                  $"(jitter {Formatters.Milliseconds(best.JitterMs)})."
                : "Nenhum servidor DNS respondeu — verifique a ligação.";

            HasRecommendation = best is not null;
            RecommendationLevel = best?.Health ?? HealthLevel.Desconhecido;
            SetStatus("Benchmark DNS concluído.");
        }, "A testar servidores DNS…");
    }

    // ---- Aplicar DNS e limpezas ----------------------------------------------

    /// <summary>Aplica o DNS recomendado (ou o escolhido na lista), com limpeza de cache opcional.</summary>
    [RelayCommand]
    private async Task ApplyDnsAsync(DnsRow? row)
    {
        var provider = row?.Provider ?? _recommendedProvider;

        if (provider is null)
        {
            await _dialogs.ShowInfoAsync("Sem recomendação", "Execute primeiro o benchmark DNS.");
            return;
        }

        if (_activeAdapter is null)
        {
            await _dialogs.ShowInfoAsync("Sem adaptador", "Não foi detetado nenhum adaptador ativo.");
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Alterar servidor DNS",
            $"Aplicar {provider.Name} ({provider.ServersText}) no adaptador «{_activeAdapter.Name}»?\n\n" +
            "• Deixamos DHCP automático desligado neste adaptador (endereço IP continua a ser obtido normalmente);\n" +
            "• O valor anterior fica registado e pode voltar a «automático» a qualquer momento com «Repor pilha de rede»." +
            "\n\n" + provider.Notes,
            "Aplicar DNS");

        if (!confirmed) return;

        await RunBusyAsync(() =>
        {
            var result = _network.ApplyDns(_activeAdapter, provider, flushDns: true, renewIp: false, forceDhcp: false);
            Report(result, "Rede");

            if (result.Success)
            {
                DnsCurrentText = provider.ServersText;
                CurrentDnsServersText = provider.ServersText;
            }

            return Task.CompletedTask;
        }, "A aplicar o DNS…");
    }

    /// <summary>Restaura o DNS automático (DHCP) no adaptador ativo.</summary>
    [RelayCommand]
    private async Task RestoreDhcpDnsAsync()
    {
        if (_activeAdapter is null)
        {
            await _dialogs.ShowInfoAsync("Sem adaptador", "Não foi detetado nenhum adaptador ativo.");
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Voltar ao DNS automático",
            $"O adaptador «{_activeAdapter.Name}» volta a obter os servidores DNS automaticamente (DHCP).",
            "Repor automático");

        if (!confirmed) return;

        await RunBusyAsync(() =>
        {
            var result = _network.ApplyDns(_activeAdapter, DnsBenchmarkService.Providers[0],
                flushDns: true, renewIp: true, forceDhcp: true);

            Report(result, "Rede");
            SetStatus("DNS reposto em modo automático (DHCP).");
            return Task.CompletedTask;
        }, "A repor o DNS automático…");
    }

    [RelayCommand]
    private async Task FlushDnsAsync()
    {
        await RunBusyAsync(() =>
        {
            Report(_network.FlushDns(), "Rede");
            return Task.CompletedTask;
        }, "A limpar a cache DNS…");
    }

    [RelayCommand]
    private async Task RenewIpAsync()
    {
        await RunBusyAsync(() =>
        {
            Report(_network.RenewIp(_activeAdapter?.Name), "Rede");
            return Task.CompletedTask;
        }, "A renovar o endereço IP…");
    }

    [RelayCommand]
    private async Task ResetNetworkStackAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            "Repor a pilha de rede",
            "Esta operação repõe o Winsock, o TCP/IP e o DNS e exige REINICIAR o Windows para concluir.\n\n" +
            "Use-a apenas se estiver com problemas persistentes de ligação.",
            "Repor pilha de rede", danger: true);

        if (!confirmed) return;

        await RunBusyAsync(() =>
        {
            Report(_network.ResetNetworkStack(), "Rede");
            return Task.CompletedTask;
        }, "A repor a pilha de rede…");
    }

    /// <summary>Otimiza o TCP para latência baixa (Nagle desligado, throttling de rede removido).</summary>
    [RelayCommand]
    private async Task OptimizeTcpAsync()
    {
        await RunBusyAsync(() =>
        {
            Report(_network.OptimizeTcpForLatency(), "Rede");
            return Task.CompletedTask;
        }, "A otimizar o TCP para latência…");
    }

    public override async Task OnNavigatedToAsync(object? parameter = null)
    {
        await base.OnNavigatedToAsync(parameter);

        // Primeira análise automática (não bloqueia a interface: corre como operação ocupada).
        if (!HasResults)
            await AnalyzeAsync();
    }
}
