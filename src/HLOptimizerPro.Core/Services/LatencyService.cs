using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services.Abstractions;
using HLOptimizerPro.Core.Services.System;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Módulo de Latência (3.4f): mede a latência real do sistema e aplica ajustes por componente.
///
/// O que é medido (e não inventado):
///   • Latência de rede — ping real ao gateway e à Internet (ver <see cref="NetworkService"/>).
///   • Resolução do temporizador do sistema — <c>NtQueryTimerResolution</c> (tipicamente 15,6 ms;
///     valores abaixo de 1 ms melhoram a consistência de input e de animação).
///   • Contagem de DPC/ISR — contadores do kernel; picos indicam drivers problemáticos.
///
/// Os "sliders" por componente não são decorativos: cada valor corresponde a um conjunto concreto
/// de tweaks reversíveis (registo, powercfg, rede). Nada é aplicado sem o utilizador clicar em
/// "Aplicar Ajustes" e tudo fica no diário para reversão.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LatencyService
{
    private readonly ILogger<LatencyService> _logger;
    private readonly TweakFactory _tweaks;
    private readonly NetworkService _network;
    private readonly PowerPlanService _powerPlans;
    private readonly IProcessRunner _runner;

    private readonly List<LatencyComponent> _components = new();

    public LatencyService(
        ILogger<LatencyService> logger,
        TweakFactory tweaks,
        NetworkService network,
        PowerPlanService powerPlans,
        IProcessRunner runner)
    {
        _logger = logger;
        _tweaks = tweaks;
        _network = network;
        _powerPlans = powerPlans;
        _runner = runner;

        BuildComponents();
    }

    /// <summary>Componentes ajustáveis (rato/entrada, CPU, kernel, GPU, rede, tela).</summary>
    public IReadOnlyList<LatencyComponent> Components => _components;

    /// <summary>Mede o estado atual de latência (usado no cabeçalho do módulo).</summary>
    public async Task<LatencyStatus> MeasureAsync(CancellationToken cancellationToken = default)
    {
        var gateway = _network.GetPrimaryAdapter()?.Gateway;
        var gatewayLatency = gateway is not null and not "—"
            ? await _network.MeasureLatencyAsync(gateway, cancellationToken).ConfigureAwait(false)
            : 0;

        var internetLatency = await _network.MeasureLatencyAsync("1.1.1.1", cancellationToken, samples: 6).ConfigureAwait(false);
        var jitter = await MeasureJitterAsync(cancellationToken).ConfigureAwait(false);

        return new LatencyStatus
        {
            CurrentLatencyMs = internetLatency > 0 ? Math.Round(internetLatency, 1) : Math.Round(gatewayLatency, 1),
            NetworkLatencyMs = Math.Round(internetLatency, 1),
            JitterMs = Math.Round(jitter, 1),
            DpcLatencyUs = GetDpcTime(),
            TimerResolutionMs = GetTimerResolution(),
        };
    }

    /// <summary>Cria as ações correspondentes ao valor atual dos sliders (chamado por "Aplicar Ajustes").</summary>
    public IReadOnlyList<OptimizationAction> BuildActions(bool simulationMode)
    {
        var actions = new List<OptimizationAction>();

        foreach (var component in _components)
        {
            actions.AddRange(BuildComponentActions(component, simulationMode));
        }

        return actions.Where(a => a.IsAvailable).ToList();
    }

    /// <summary>Resumo do que será alterado (mostrado no painel antes de aplicar).</summary>
    public IReadOnlyList<string> Preview() => _components
        .Select(c => $"{c.Name}: {c.ValueText} — {c.Description}")
        .ToList();

    // =====================================================================
    //  Definição dos componentes (cada um com tweaks reais)
    // =====================================================================
    private void BuildComponents()
    {
        _components.Add(new LatencyComponent
        {
            Key = "input",
            Name = "Rato / Entrada",
            Description = "Reduz o buffer de eventos do rato e desativa a aceleração do ponteiro.",
            IconKind = "Mouse",
            Value = 50,
            LowLabel = "Padrão do Windows",
            HighLabel = "Resposta direta",
        });

        _components.Add(new LatencyComponent
        {
            Key = "cpu",
            Name = "CPU",
            Description = "Impede que os núcleos baixem de frequência (core parking) e fixa o desempenho mínimo em 100 %.",
            IconKind = "Cpu64Bit",
            Value = 50,
            LowLabel = "Economia",
            HighLabel = "Frequência fixa",
        });

        _components.Add(new LatencyComponent
        {
            Key = "kernel",
            Name = "Kernel",
            Description = "Reduz a reserva de CPU para multimédia, eleva a prioridade de jogos (MMCSS) e remove o limitador de rede.",
            IconKind = "Memory",
            Value = 50,
            LowLabel = "Padrão",
            HighLabel = "Agressivo",
        });

        _components.Add(new LatencyComponent
        {
            Key = "gpu",
            Name = "GPU",
            Description = "Ativa o agendamento de GPU por hardware e desativa a captura automática (Game DVR).",
            IconKind = "ExpansionCard",
            Value = 50,
            LowLabel = "Padrão",
            HighLabel = "Prioridade de jogo",
        });

        _components.Add(new LatencyComponent
        {
            Key = "network",
            Name = "Rede",
            Description = "Desativa o algoritmo de Nagle (envio imediato de pacotes pequenos) e otimiza o TCP.",
            IconKind = "LanConnect",
            Value = 50,
            LowLabel = "Padrão",
            HighLabel = "Pacotes imediatos",
        });

        _components.Add(new LatencyComponent
        {
            Key = "display",
            Name = "Tela",
            Description = "Desativa a otimização de ecrã inteiro que introduz latência adicional e o atraso de composição.",
            IconKind = "Monitor",
            Value = 50,
            LowLabel = "Padrão",
            HighLabel = "Exclusivo direto",
        });
    }

    /// <summary>Constrói os tweaks concretos de um componente consoante o valor do slider.</summary>
    private IEnumerable<OptimizationAction> BuildComponentActions(LatencyComponent component, bool simulationMode)
    {
        // Valores baixos (slider à esquerda) = repor o comportamento predefinido do Windows.
        var aggressive = component.Value >= 45;

        switch (component.Key)
        {
            case "input":
                yield return _tweaks.Create(new RegistryTweakSpec
                {
                    Key = "latency.input.mousedataqueue",
                    Title = "Buffer de dados do rato",
                    Description = "Reduz a fila de eventos do rato de 100 para 10 amostras: o movimento chega mais depressa ao jogo.",
                    Hive = "HKLM",
                    SubKey = @"SYSTEM\CurrentControlSet\Services\mouclass\Parameters",
                    ValueName = "MouseDataQueueSize",
                    OptimizedValue = aggressive ? 10 : 100,
                    Category = OptimizationCategory.Sistema,
                    Area = OptimizationArea.DesempenhoSistema,
                    Risk = "Baixo — apenas o tamanho do buffer de entrada.",
                    RequiresRestart = true,
                    SimulationNote = aggressive
                        ? "MouseDataQueueSize passaria de 100 (predefinido) para 10 amostras."
                        : "MouseDataQueueSize voltaria ao valor predefinido do Windows (100).",
                }, simulationMode);

                yield return _tweaks.Create(new RegistryTweakSpec
                {
                    Key = "latency.input.acceleration",
                    Title = "Aceleração do ponteiro",
                    Description = "Desativa a aceleração do rato: o movimento é 1:1 com o físico, o que torna a mira previsível.",
                    Hive = "HKCU",
                    SubKey = @"Control Panel\Mouse",
                    ValueName = "MouseSpeed",
                    Kind = RegistryValueKind.String,
                    OptimizedValue = aggressive ? "0" : "1",
                    Category = OptimizationCategory.Sistema,
                    Area = OptimizationArea.DesempenhoSistema,
                    Risk = "Baixo — preferência do utilizador; reversível.",
                    RequiresRestart = false,
                    RequiresElevation = false,
                }, simulationMode);

                yield return _tweaks.Create(new RegistryTweakSpec
                {
                    Key = "latency.input.hover",
                    Title = "Atraso de abertura por passagem do rato",
                    Description = "Reduz o tempo que o Windows espera antes de abrir menus/submenus ao passar o rato.",
                    Hive = "HKCU",
                    SubKey = @"Control Panel\Desktop",
                    ValueName = "MenuShowDelay",
                    Kind = RegistryValueKind.String,
                    OptimizedValue = aggressive ? "0" : "400",
                    Category = OptimizationCategory.Sistema,
                    Area = OptimizationArea.DesempenhoSistema,
                    Risk = "Baixo — preferência de interface.",
                    RequiresRestart = false,
                    RequiresElevation = false,
                }, simulationMode);
                break;

            case "cpu":
                yield return _tweaks.Create(new CommandTweakSpec
                {
                    Key = "latency.cpu.throttlemin",
                    Title = "Desempenho mínimo do processador",
                    Description = "Fixa o desempenho mínimo do CPU em 100 %, evitando que os núcleos baixem de frequência durante o jogo.",
                    Target = "powercfg PROCTHROTTLEMIN",
                    FileName = "powercfg",
                    ApplyArguments = $"setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN {(aggressive ? 100 : 5)}",
                    RevertArguments = "setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN 5",
                    CurrentValueText = aggressive ? "valor atual do plano" : "100 %",
                    NewValueText = aggressive ? "100 % (frequência fixa)" : "5 % (predefinido)",
                    Category = OptimizationCategory.Sistema,
                    Area = OptimizationArea.DesempenhoSistema,
                    Risk = "Médio — aumenta o consumo de energia e a temperatura em portáteis.",
                    RiskLevel = HealthLevel.Atencao,
                    SimulateExplanation = aggressive
                        ? "O processador passaria a manter 100 % de desempenho mínimo (mais consumo e calor)."
                        : "O desempenho mínimo voltaria ao comportamento dinâmico predefinido.",
                }, simulationMode);
                break;

            case "kernel":
                yield return _tweaks.Create(new RegistryTweakSpec
                {
                    Key = "latency.kernel.systemresponsiveness",
                    Title = "Reserva de CPU multitarefa (MMCSS)",
                    Description = "Reduz para 10 % a CPU reservada ao sistema, deixando mais tempo de processador para o jogo.",
                    Hive = "HKLM",
                    SubKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
                    ValueName = "SystemResponsiveness",
                    OptimizedValue = aggressive ? 10 : 20,
                    Category = OptimizationCategory.Sistema,
                    Area = OptimizationArea.DesempenhoSistema,
                    Risk = "Baixo — afeta apenas a repartição de CPU em multimédia.",
                    RequiresRestart = true,
                }, simulationMode);

                yield return _tweaks.Create(new RegistryTweakSpec
                {
                    Key = "latency.kernel.gamespriority",
                    Title = "Prioridade da tarefa «Jogos» (MMCSS)",
                    Description = "Eleva a prioridade e a reserva de tempo de CPU para a categoria de jogos do Windows.",
                    Hive = "HKLM",
                    SubKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
                    ValueName = "Priority",
                    OptimizedValue = aggressive ? 6 : 2,
                    Category = OptimizationCategory.Sistema,
                    Area = OptimizationArea.DesempenhoSistema,
                    Risk = "Baixo.",
                    RequiresRestart = true,
                }, simulationMode);

                yield return _tweaks.Create(new RegistryTweakSpec
                {
                    Key = "latency.kernel.networkthrottling",
                    Title = "Limitador de rede do kernel",
                    Description = "Remove o limite de 10 pacotes/ms que o Windows aplica à atividade de rede em segundo plano.",
                    Hive = "HKLM",
                    SubKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
                    ValueName = "NetworkThrottlingIndex",
                    OptimizedValue = aggressive ? unchecked((int)0xFFFFFFFF) : 10,
                    Category = OptimizationCategory.Sistema,
                    Area = OptimizationArea.DesempenhoSistema,
                    Risk = "Baixo.",
                    RequiresRestart = true,
                }, simulationMode);
                break;

            case "gpu":
                yield return _tweaks.Create(new RegistryTweakSpec
                {
                    Key = "latency.gpu.hwschmode",
                    Title = "Agendamento de GPU por hardware",
                    Description = "Deixa a própria placa gráfica gerir a memória e as filas de trabalho, reduzindo a latência de submissão.",
                    Hive = "HKLM",
                    SubKey = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
                    ValueName = "HwSchMode",
                    OptimizedValue = aggressive ? 2 : 1,
                    Category = OptimizationCategory.Sistema,
                    Area = OptimizationArea.DesempenhoSistema,
                    Risk = "Médio — em GPUs antigas pode reduzir o desempenho; reverta se notar piora.",
                    RiskLevel = HealthLevel.Atencao,
                    RequiresRestart = true,
                }, simulationMode);

                yield return _tweaks.Create(new RegistryTweakSpec
                {
                    Key = "latency.gpu.gamedvr",
                    Title = "Captura e gravação em segundo plano (Game DVR)",
                    Description = "Desativa a gravação automática do Game Bar, que consome GPU e memória durante o jogo.",
                    Hive = "HKCU",
                    SubKey = @"System\GameConfigStore",
                    ValueName = "GameDVR_Enabled",
                    OptimizedValue = aggressive ? 0 : 1,
                    Category = OptimizationCategory.Sistema,
                    Area = OptimizationArea.DesempenhoSistema,
                    Risk = "Baixo — só desativa a captura automática (Alt+Tab continua a funcionar).",
                    RequiresRestart = false,
                    RequiresElevation = false,
                }, simulationMode);
                break;

            case "network":
                // O tweak do Nagle é por interface de rede: a chave do registo usa o GUID do adaptador.
                foreach (var adapter in _network.GetAdapters().Where(a => Guid.TryParse(a.Id, out _)).Take(2))
                {
                    var interfaceKey = adapter.Id;
                    var adapterName = adapter.Name;

                    yield return _tweaks.Create(new RegistryTweakSpec
                    {
                        Key = $"latency.network.nagle.{interfaceKey}",
                        Title = $"Nagle desativado — {adapterName}",
                        Description = "Envia os pacotes pequenos imediatamente em vez de os agrupar, reduzindo a latência em jogos online.",
                        Hive = "HKLM",
                        SubKey = $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{interfaceKey}",
                        ValueName = "TcpAckFrequency",
                        OptimizedValue = aggressive ? 1 : 2,
                        Category = OptimizationCategory.Sistema,
                        Area = OptimizationArea.DesempenhoSistema,
                        Risk = "Médio — aumenta ligeiramente o tráfego de rede; recomendado apenas a jogar online.",
                        RiskLevel = HealthLevel.Atencao,
                        RequiresRestart = true,
                    }, simulationMode);

                    yield return _tweaks.Create(new RegistryTweakSpec
                    {
                        Key = $"latency.network.nodelay.{interfaceKey}",
                        Title = $"Envio imediato (TCPNoDelay) — {adapterName}",
                        Description = "Desativa a espera por confirmações antes de enviar o próximo pacote pequeno.",
                        Hive = "HKLM",
                        SubKey = $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{interfaceKey}",
                        ValueName = "TCPNoDelay",
                        OptimizedValue = aggressive ? 1 : 0,
                        Category = OptimizationCategory.Sistema,
                        Area = OptimizationArea.DesempenhoSistema,
                        Risk = "Médio — reversível a qualquer momento.",
                        RiskLevel = HealthLevel.Atencao,
                        RequiresRestart = true,
                    }, simulationMode);
                }
                break;

            case "display":
                yield return _tweaks.Create(new RegistryTweakSpec
                {
                    Key = "latency.display.fsebehavior",
                    Title = "Otimização de ecrã inteiro",
                    Description = "Desativa a camada de compatibilidade que o Windows aplica sobre jogos em ecrã inteiro.",
                    Hive = "HKCU",
                    SubKey = @"System\GameConfigStore",
                    ValueName = "GameDVR_FSEBehaviorMode",
                    OptimizedValue = aggressive ? 2 : 0,
                    Category = OptimizationCategory.Sistema,
                    Area = OptimizationArea.Graficos,
                    Risk = "Baixo — reversível; alguns jogos antigos podem precisar do modo predefinido.",
                    RequiresRestart = false,
                    RequiresElevation = false,
                }, simulationMode);
                break;
        }
    }

    // =====================================================================
    //  Medições reais
    // =====================================================================

    /// <summary>Variação da latência (jitter) entre amostras consecutivas.</summary>
    private async Task<double> MeasureJitterAsync(CancellationToken cancellationToken)
    {
        var samples = new List<double>();

        for (var i = 0; i < 5; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var latency = await _network.MeasureLatencyAsync("1.1.1.1", cancellationToken, samples: 1).ConfigureAwait(false);
            if (latency > 0) samples.Add(latency);
            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
        }

        if (samples.Count < 2) return 0;

        var average = samples.Average();
        return samples.Select(s => Math.Abs(s - average)).Average();
    }

    /// <summary>Resolução atual do temporizador do sistema (ms) via NtQueryTimerResolution.</summary>
    public double GetTimerResolution()
    {
        try
        {
            var status = NtQueryTimerResolution(out var maximum, out var minimum, out var current);

            // Os valores são em unidades de 100 ns.
            return status == 0 ? Math.Round(current / 10000.0, 3) : 15.625;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "NtQueryTimerResolution indisponível.");
            return 15.625;
        }
    }

    /// <summary>Percentagem de tempo do kernel em DPC/ISR (indica carga de drivers).</summary>
    public double GetDpcTime()
    {
        try
        {
            var result = _runner.Run("typeperf.exe", "\"\\Processor(_Total)\\% DPC Time\" -sc 1");

            if (result.Success)
            {
                var line = result.Output.Split('\n').LastOrDefault(l => l.Contains(','));
                if (line is not null)
                {
                    var parts = line.Split(',');
                    if (parts.Length >= 2 && double.TryParse(parts[^1].Trim('"', ' '), System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var percent))
                    {
                        // Convertemos a percentagem em microssegundos aproximados por segundo.
                        return Math.Round(percent * 10_000, 0);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Não foi possível medir o tempo de DPC.");
        }

        return 0;
    }

    /// <summary>Ativa temporariamente a resolução alta do temporizador (enquanto a aplicação correr).</summary>
    public Result RequestHighResolutionTimer(bool enable)
    {
        try
        {
            var period = enable ? 1u : 0u;   // 1 ms
            var result = timeBeginPeriod(period);

            if (result != 0)
                return Result.Fail("Não foi possível solicitar a resolução alta do temporizador.");

            var current = GetTimerResolution();
            return Result.Ok($"Resolução do temporizador: {current:N3} ms " +
                             (enable ? "(ativa enquanto o HL Optimizer Pro estiver aberto)" : "(padrão do Windows)"));
        }
        catch (Exception ex)
        {
            return Result.Fail("Temporizador de alta resolução indisponível.", ex.Message);
        }
    }

    private static string SanitizeKey(string name) => new(name.Where(char.IsLetterOrDigit).ToArray());

    [DllImport("ntdll.dll")]
    private static extern int NtQueryTimerResolution(out uint maximumTime, out uint minimumTime, out uint currentTime);

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint timeBeginPeriod(uint period);
}
