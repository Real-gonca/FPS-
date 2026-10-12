using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Cálculo da pontuação do sistema (Avaliação).
///
/// Como funciona: cada critério medido retira pontos a uma base de 100, com limites calibrados
/// para o Windows 10/11 (ver constantes internas). O resultado final é a média ponderada de
/// quatro dimensões:
///
///     Desempenho   — 30 %   (CPU, RAM, disco, tipo de armazenamento, arranque, energia)
///     Estabilidade — 25 %   (temperatura, uptime, dispositivos com erro, saúde/fragmentação)
///     Segurança    — 25 %   (antivírus, firewall, atualizações, UAC, Secure Boot)
///     Otimização   — 20 %   (otimizações já aplicadas, telemetria, sensor de armazenamento)
///
/// Este serviço é PURO (sem I/O): recebe <see cref="AssessmentInput"/> e devolve
/// <see cref="AssessmentReport"/>. Isso torna-o determinístico e testável — e é por isso que
/// os testes unitários apenas precisam de montar o objeto de entrada.
/// </summary>
public sealed class ScoringService
{
    // ---- Pesos das dimensões (somam 1.0) -------------------------------------
    public const double PerformanceWeight = 0.30;
    public const double StabilityWeight = 0.25;
    public const double SecurityWeight = 0.25;
    public const double OptimizationWeight = 0.20;

    /// <summary>Calcula o relatório completo a partir dos dados medidos.</summary>
    public AssessmentReport Calculate(AssessmentInput input)
    {
        var performance = ScorePerformance(input);
        var stability = ScoreStability(input);
        var security = ScoreSecurity(input);
        var optimization = ScoreOptimization(input);

        var breakdowns = new List<ScoreBreakdown> { performance, stability, security, optimization };

        var overall = Math.Round(
            breakdowns.Sum(b => b.Score * b.Weight) / breakdowns.Sum(b => b.Weight), 1);

        var recommendations = BuildRecommendations(input, breakdowns);

        return new AssessmentReport
        {
            OverallScore = Math.Clamp(overall, 0, 100),
            Grade = Formatters.ScoreGradeLabel(overall),
            Breakdowns = breakdowns,
            Diagnosis = BuildDiagnosis(overall, breakdowns),
            Recommendations = recommendations,
            Hardware = input.Hardware,
        };
    }

    // =====================================================================
    //  DESEMPENHO — 30 %
    // =====================================================================
    private static ScoreBreakdown ScorePerformance(AssessmentInput input)
    {
        var criteria = new List<ScoreCriterion>();

        // CPU — o uso médio elevado indica processos a competir por tempo de processador.
        var cpuPenalty = input.CpuUsagePercent switch
        {
            < 50 => 0,
            < 70 => 5,
            < 85 => 12,
            _ => 20,
        };
        criteria.Add(new ScoreCriterion
        {
            Name = "Uso de CPU (média recente)",
            MeasuredValue = Formatters.Percent(input.CpuUsagePercent, 1, alreadyInPercent: true),
            Penalty = cpuPenalty,
            Advice = cpuPenalty >= 5 ? "Reduza o uso de CPU com o Modo Jogo ou desativando apps em segundo plano." : null,
        });

        // RAM — acima de 85 % o sistema começa a usar o ficheiro de paginação de forma agressiva.
        var ramPenalty = input.RamUsagePercent switch
        {
            < 70 => 0,
            < 80 => 5,
            < 90 => 12,
            _ => 20,
        };
        criteria.Add(new ScoreCriterion
        {
            Name = "Uso de memória RAM",
            MeasuredValue = Formatters.Percent(input.RamUsagePercent, 1, alreadyInPercent: true),
            Penalty = ramPenalty,
            Advice = ramPenalty >= 5 ? "Use «Memória RAM → Reduzir Uso de Memória» e feche separadores/programas não usados." : null,
        });

        // Espaço livre — abaixo de 10 % o Windows degrada-se visivelmente.
        var diskPenalty = input.DiskFreePercent switch
        {
            >= 20 => 0,
            >= 10 => 6,
            >= 5 => 12,
            _ => 20,
        };
        criteria.Add(new ScoreCriterion
        {
            Name = "Espaço livre no disco do sistema",
            MeasuredValue = Formatters.Percent(input.DiskFreePercent, 1, alreadyInPercent: true),
            Penalty = diskPenalty,
            Advice = diskPenalty >= 6 ? "Execute a «Limpeza Rápida» e remova ficheiros grandes desnecessários." : null,
        });

        // Tipo de armazenamento — um HDD limita o arranque e os tempos de carregamento.
        var storagePenalty = input.StorageType.ToLowerInvariant() switch
        {
            var t when t.Contains("nvme") => 0,
            var t when t.Contains("ssd") => 2,
            var t when t.Contains("hdd") => 10,
            _ => 0,   // desconhecido → não penalizamos por falta de informação
        };
        criteria.Add(new ScoreCriterion
        {
            Name = "Tipo de armazenamento",
            MeasuredValue = input.StorageType,
            Penalty = storagePenalty,
            Advice = storagePenalty >= 10 ? "Um SSD reduz drasticamente os tempos de arranque e de carregamento de jogos." : null,
        });

        // Programas de arranque — o fator mais visível no tempo de arranque.
        var startupPenalty = input.StartupEnabledCount switch
        {
            <= 8 => 0,
            <= 14 => 5,
            <= 20 => 10,
            _ => 15,
        };
        startupPenalty += Math.Min(10, input.HighImpactStartupItems.Count * 3);

        criteria.Add(new ScoreCriterion
        {
            Name = "Programas de arranque ativos",
            MeasuredValue = $"{input.StartupEnabledCount} de {input.StartupTotalCount}",
            Penalty = startupPenalty,
            Advice = startupPenalty >= 5
                ? $"Desative os que não precisa (ex.: {string.Join(", ", input.HighImpactStartupItems.Take(3))})."
                : null,
        });

        // Plano de energia
        var planPenalty = input.PowerPlan switch
        {
            var p when p.Contains("Alto desempenho", StringComparison.OrdinalIgnoreCase) => 0,
            var p when p.Contains("Desempenho", StringComparison.OrdinalIgnoreCase) => 0,
            var p when p.Contains("Equilibrado", StringComparison.OrdinalIgnoreCase) => 3,
            var p when p.Contains("Economia", StringComparison.OrdinalIgnoreCase) => 8,
            _ => 0,
        };
        criteria.Add(new ScoreCriterion
        {
            Name = "Plano de energia",
            MeasuredValue = input.PowerPlan,
            Penalty = planPenalty,
            Advice = planPenalty >= 3 ? "Ative o plano «Alto Desempenho» ou use um perfil de Tweaks." : null,
        });

        return Build("Desempenho", "Capacidade de resposta do processador, memória e armazenamento.", PerformanceWeight, criteria);
    }

    // =====================================================================
    //  ESTABILIDADE — 25 %
    // =====================================================================
    private static ScoreBreakdown ScoreStability(AssessmentInput input)
    {
        var criteria = new List<ScoreCriterion>();

        // Uptime — sessões muito longas acumulam fugas de memória e atualizações pendentes.
        var uptimeDays = input.Uptime.TotalDays;
        var uptimePenalty = uptimeDays switch
        {
            < 7 => 0,
            < 14 => 4,
            _ => 8,
        };
        criteria.Add(new ScoreCriterion
        {
            Name = "Tempo desde o último arranque",
            MeasuredValue = Formatters.Uptime(input.Uptime),
            Penalty = uptimePenalty,
            Advice = uptimePenalty >= 4 ? "Reiniciar liberta memória em cache e conclui atualizações pendentes." : null,
        });

        // Temperatura — só avaliada quando existe sensor (não penalizamos VMs).
        if (input.CpuTemperatureCelsius is { } temperature)
        {
            var tempPenalty = temperature switch
            {
                < 70 => 0,
                < 80 => 5,
                < 90 => 12,
                _ => 20,
            };
            criteria.Add(new ScoreCriterion
            {
                Name = "Temperatura do processador",
                MeasuredValue = Formatters.Temperature(temperature),
                Penalty = tempPenalty,
                Advice = tempPenalty >= 5 ? "Limpe o pó, melhore a ventilação e verifique a pasta térmica." : null,
            });
        }
        else
        {
            criteria.Add(new ScoreCriterion
            {
                Name = "Temperatura do processador",
                MeasuredValue = "não medida (sem sensor acessível)",
                Penalty = 0,
                Advice = "Sem acesso aos sensores: execute como administrador para medir temperaturas.",
            });
        }

        // Dispositivos com erro no Gestor de Dispositivos (ConfigManagerErrorCode ≠ 0).
        var devicePenalty = input.ProblemDevices switch
        {
            0 => 0,
            <= 2 => 8,
            _ => 15,
        };
        criteria.Add(new ScoreCriterion
        {
            Name = "Dispositivos com problemas",
            MeasuredValue = $"{input.ProblemDevices} dispositivo(s)",
            Penalty = devicePenalty,
            Advice = devicePenalty >= 8 ? "Abra o Gestor de Dispositivos e atualize/reinstale os controladores em falta." : null,
        });

        // Saúde física dos discos (SMART).
        criteria.Add(new ScoreCriterion
        {
            Name = "Saúde dos discos (SMART)",
            MeasuredValue = input.DiskHealthUnknown > 0
                ? "não determinada pelo controlador"
                : input.DiskHealthy ? "Boa" : "Falhas detetadas",
            Penalty = input.DiskHealthy ? 0 : 18,
            Advice = input.DiskHealthy ? null : "Faça já uma cópia de segurança: o disco reporta problemas de saúde.",
        });

        // Fragmentação (só relevante para HDD; em SSD o Windows nunca desfragmenta).
        if (input.FragmentationPercent is { } fragmentation && !input.StorageType.Contains("SSD", StringComparison.OrdinalIgnoreCase))
        {
            var fragPenalty = fragmentation switch
            {
                <= 10 => 0,
                <= 20 => 5,
                _ => 10,
            };
            criteria.Add(new ScoreCriterion
            {
                Name = "Fragmentação do disco",
                MeasuredValue = Formatters.Percent(fragmentation, 0, alreadyInPercent: true),
                Penalty = fragPenalty,
                Advice = fragPenalty > 0 ? "Otimize a unidade em «Ferramentas → Unidade de Disco»." : null,
            });
        }

        // Atualizações do Windows em atraso — afeta estabilidade e segurança.
        var updatePenalty = input.WindowsUpdateRecent ? 0 : 6;
        criteria.Add(new ScoreCriterion
        {
            Name = "Atualizações do sistema em atraso",
            MeasuredValue = input.WindowsUpdateRecent ? "em dia (menos de 35 dias)" : "atrasadas",
            Penalty = updatePenalty,
            Advice = updatePenalty > 0 ? "Verifique as atualizações do Windows." : null,
        });

        return Build("Estabilidade", "Fiabilidade do sistema: temperaturas, controladores, discos e atualizações.",
            StabilityWeight, criteria);
    }

    // =====================================================================
    //  SEGURANÇA — 25 %
    // =====================================================================
    private static ScoreBreakdown ScoreSecurity(AssessmentInput input)
    {
        var criteria = new List<ScoreCriterion>
        {
            new()
            {
                Name = "Antivírus ativo",
                MeasuredValue = input.AntivirusEnabled ? "Ativo" : "Não detetado",
                Penalty = input.AntivirusEnabled ? 0 : 22,
                Advice = input.AntivirusEnabled ? null : "Ative o Microsoft Defender ou instale uma solução antivírus.",
            },
            new()
            {
                Name = "Proteção em tempo real",
                MeasuredValue = input.RealTimeProtection ? "Ativa" : "Desativada",
                Penalty = input.RealTimeProtection ? 0 : 12,
                Advice = input.RealTimeProtection ? null : "Reative a proteção em tempo real nas definições de segurança do Windows.",
            },
            new()
            {
                Name = "Firewall",
                MeasuredValue = input.FirewallEnabled ? "Ativa" : "Desativada",
                Penalty = input.FirewallEnabled ? 0 : 20,
                Advice = input.FirewallEnabled ? null : "Ative a firewall do Windows em todos os perfis de rede.",
            },
            new()
            {
                Name = "Arranque seguro (Secure Boot)",
                MeasuredValue = input.SecureBootEnabled ? "Ativo" : "Desativado ou indisponível (BIOS legado)",
                Penalty = input.SecureBootEnabled ? 0 : 6,
                Advice = input.SecureBootEnabled ? null : "Ativar no firmware UEFI protege contra rootkits de arranque.",
            },
            new()
            {
                Name = "Controlo de Conta de Utilizador (UAC)",
                MeasuredValue = input.UacEnabled ? "Ativo" : "Desativado",
                Penalty = input.UacEnabled ? 0 : 10,
                Advice = input.UacEnabled ? null : "Desativar o UAC retira uma barreira importante contra malware.",
            },
            new()
            {
                Name = "Atualizações de segurança pendentes",
                MeasuredValue = input.PendingUpdates == 0 ? "Nenhuma" : $"{input.PendingUpdates} pendente(s)",
                Penalty = input.PendingUpdates switch { 0 => 0, <= 5 => 6, <= 15 => 11, _ => 16 },
                Advice = input.PendingUpdates > 0 ? "Instale as atualizações pendentes no Windows Update." : null,
            },
            new()
            {
                Name = "SmartScreen",
                MeasuredValue = input.SmartScreenEnabled ? "Ativo" : "Desativado",
                Penalty = input.SmartScreenEnabled ? 0 : 5,
                Advice = input.SmartScreenEnabled ? null : "O SmartScreen bloqueia downloads e sites maliciosos conhecidos.",
            },
        };

        return Build("Segurança", "Proteção contra malware e exposição a vulnerabilidades conhecidas.",
            SecurityWeight, criteria);
    }

    // =====================================================================
    //  OTIMIZAÇÃO — 20 %
    // =====================================================================
    private static ScoreBreakdown ScoreOptimization(AssessmentInput input)
    {
        var criteria = new List<ScoreCriterion>();

        // Quantas das otimizações disponíveis já estão aplicadas neste computador.
        var ratio = input.TotalAvailableOptimizations <= 0
            ? 1.0
            : Math.Clamp((double)input.AppliedOptimizations / input.TotalAvailableOptimizations, 0, 1);
        var optimizationPenalty = (1 - ratio) * 25;

        criteria.Add(new ScoreCriterion
        {
            Name = "Otimizações do Windows aplicadas",
            MeasuredValue = $"{input.AppliedOptimizations} de {input.TotalAvailableOptimizations}",
            Penalty = optimizationPenalty,
            Advice = optimizationPenalty >= 5 ? "Abra «Otimizações» e aplique o conjunto recomendado para o seu uso." : null,
        });

        criteria.Add(new ScoreCriterion
        {
            Name = "Telemetria do Windows",
            MeasuredValue = input.TelemetryReduced ? "Reduzida" : "Padrão (completa)",
            Penalty = input.TelemetryReduced ? 0 : 4,
            Advice = input.TelemetryReduced ? null : "Reduza a telemetria em «Otimizações → Privacidade».",
        });

        criteria.Add(new ScoreCriterion
        {
            Name = "Sensor de Armazenamento (limpeza automática)",
            MeasuredValue = input.StorageSenseEnabled ? "Ativo" : "Desativado",
            Penalty = input.StorageSenseEnabled ? 0 : 4,
            Advice = input.StorageSenseEnabled ? null : "Ligue o Sensor de Armazenamento para libertar espaço sozinho.",
        });

        criteria.Add(new ScoreCriterion
        {
            Name = "Modo Jogo configurado",
            MeasuredValue = input.GameModeConfigured ? "Configurado" : "Não configurado",
            Penalty = input.GameModeConfigured ? 0 : 3,
            Advice = input.GameModeConfigured ? null : "Configure o Modo Jogo para priorizar automaticamente os seus jogos.",
        });

        criteria.Add(new ScoreCriterion
        {
            Name = "Programas de arranque geridos",
            MeasuredValue = input.DisabledStartupItems ? "Otimizados" : "Sem alterações",
            Penalty = input.DisabledStartupItems ? 0 : 3,
            Advice = input.DisabledStartupItems ? null : "Desative programas de arranque desnecessários.",
        });

        criteria.Add(new ScoreCriterion
        {
            Name = "Efeitos visuais do Windows",
            MeasuredValue = input.VisualEffectsOptimized ? "Ajustados para desempenho" : "Padrão",
            Penalty = input.VisualEffectsOptimized ? 0 : 3,
            Advice = input.VisualEffectsOptimized ? null : "Ajuste os efeitos visuais em «Otimizações → Windows».",
        });

        return Build("Otimização", "Quanto do potencial deste computador já está aproveitado.",
            OptimizationWeight, criteria);
    }

    // =====================================================================
    //  Auxiliares
    // =====================================================================
    private static ScoreBreakdown Build(string name, string description, double weight, List<ScoreCriterion> criteria)
    {
        var score = Math.Clamp(100 - criteria.Sum(c => c.Penalty), 0, 100);

        return new ScoreBreakdown
        {
            Name = name,
            Description = description,
            Weight = weight,
            Score = Math.Round(score, 1),
            Health = score switch
            {
                >= 85 => HealthLevel.Otimo,
                >= 65 => HealthLevel.Atencao,
                _ => HealthLevel.Critico,
            },
            Criteria = criteria,
        };
    }

    /// <summary>Diagnóstico em linguagem natural, gerado a partir das dimensões mais fracas.</summary>
    private static string BuildDiagnosis(double overall, List<ScoreBreakdown> breakdowns)
    {
        var grade = Formatters.ScoreGradeLabel(overall);
        var weakest = breakdowns.OrderBy(b => b.Score).First();
        var problems = breakdowns
            .SelectMany(b => b.Criteria)
            .Where(c => c.IsProblem)
            .OrderByDescending(c => c.Penalty)
            .Take(3)
            .Select(c => $"{c.Name.ToLowerInvariant()} ({c.MeasuredValue})")
            .ToList();

        if (problems.Count == 0)
        {
            return $"Classificação {grade}: este computador está bem configurado e não encontrámos problemas relevantes. " +
                   $"A dimensão com menor pontuação é «{weakest.Name}» ({weakest.ScoreText}), o que é normal em uso diário.";
        }

        return $"Classificação {grade}. Principais pontos a melhorar: {string.Join("; ", problems)}. " +
               $"A dimensão mais afetada é «{weakest.Name}» ({weakest.ScoreText}). " +
               "Aplique as recomendações sugeridas — todas as alterações são reversíveis.";
    }

    /// <summary>Converte os critérios penalizados em recomendações acionáveis (tela Recomendações).</summary>
    private static List<Recommendation> BuildRecommendations(AssessmentInput input, List<ScoreBreakdown> breakdowns)
    {
        var recommendations = new List<Recommendation>();
        var index = 0;

        var missing = breakdowns
            .SelectMany(b => b.Criteria.Select(c => (Breakdown: b, Criterion: c)))
            .Where(x => x.Criterion.IsProblem)
            .OrderByDescending(x => x.Criterion.Penalty)
            .Take(12)
            .ToList();

        foreach (var (breakdown, criterion) in missing)
        {
            var (category, actionKey, icon, benefit) = MapAdvice(criterion, breakdown.Name, input);

            recommendations.Add(new Recommendation
            {
                Id = $"rec-{++index:D2}",
                Title = BuildTitle(criterion, breakdown.Name),
                Description = criterion.Advice ?? $"Reveja «{criterion.Name}» (valor medido: {criterion.MeasuredValue}).",
                Priority = criterion.Penalty switch
                {
                    >= 15 => RecommendationPriority.Alta,
                    >= 7 => RecommendationPriority.Media,
                    _ => RecommendationPriority.Baixa,
                },
                Category = category,
                ExpectedBenefit = benefit,
                ActionKey = actionKey,
                IconKind = icon,
            });
        }

        // Recomendações de estado (não vêm de penalizações mas de boas práticas medíveis).
        if (input.PendingUpdates > 0)
        {
            recommendations.Insert(0, new Recommendation
            {
                Id = "rec-updates",
                Title = $"Instalar {input.PendingUpdates} atualização(ões) pendente(s)",
                Description = "As atualizações corrigem vulnerabilidades e problemas de estabilidade conhecidos.",
                Priority = RecommendationPriority.Alta,
                Category = RecommendationCategory.Seguranca,
                ExpectedBenefit = "Menor exposição a vulnerabilidades e menos falhas do sistema.",
                ActionKey = "windows.update",
                IconKind = "ShieldCheck",
            });
        }

        return recommendations;
    }

    private static string BuildTitle(ScoreCriterion criterion, string dimension)
    {
        var name = criterion.Name;

        return name switch
        {
            "Uso de memória RAM" => "Reduzir o uso de memória RAM",
            "Uso de CPU (média recente)" => "Diminuir a carga no processador",
            "Espaço livre no disco do sistema" => "Libertar espaço no disco do sistema",
            "Programas de arranque ativos" => "Otimizar os programas de arranque",
            "Plano de energia" => "Ativar um plano de energia de alto desempenho",
            "Tipo de armazenamento" => "Considerar migrar o sistema para SSD",
            "Temperatura do processador" => "Melhorar a refrigeração do processador",
            "Fragmentação do disco" => "Otimizar a unidade de disco",
            "Dispositivos com problemas" => "Resolver controladores com erro",
            "Saúde dos discos (SMART)" => "Proteger os seus dados (disco com sinais de falha)",
            "Atualizações do sistema em atraso" => "Instalar atualizações do Windows",
            "Antivírus ativo" => "Ativar a proteção antivírus",
            "Proteção em tempo real" => "Reativar a proteção em tempo real",
            "Firewall" => "Ativar a firewall do Windows",
            "Controlo de Conta de Utilizador (UAC)" => "Reativar o Controlo de Conta de Utilizador",
            "SmartScreen" => "Ativar o SmartScreen",
            "Telemetria do Windows" => "Reduzir a telemetria do Windows",
            "Sensor de Armazenamento (limpeza automática)" => "Ativar o Sensor de Armazenamento",
            "Modo Jogo configurado" => "Configurar o Modo Jogo",
            "Efeitos visuais do Windows" => "Ajustar os efeitos visuais",
            _ => $"{name} — melhoria possível em {dimension}",
        };
    }

    /// <summary>Liga cada critério penalizado à ação concreta que o resolve.</summary>
    private static (RecommendationCategory category, string? actionKey, string icon, string benefit) MapAdvice(
        ScoreCriterion criterion, string dimension, AssessmentInput input) => criterion.Name switch
    {
        "Uso de memória RAM" =>
            (RecommendationCategory.Memoria, "mem.reduce", "Memory", "Mais memória disponível para jogos e aplicações; menos stutter."),
        "Uso de CPU (média recente)" =>
            (RecommendationCategory.Desempenho, "tweaks.balanced", "Cpu64Bit", "Menos competição por tempo de processador e resposta mais rápida."),
        "Espaço livre no disco do sistema" =>
            (RecommendationCategory.Armazenamento, "cleanup.quick", "HarddiskRemove", "Espaço recuperado e sistema mais fluido em ficheiros temporários."),
        "Programas de arranque ativos" =>
            (RecommendationCategory.Inicializacao, "startup.optimize", "RocketLaunch", "Arranque mais rápido e menos consumo em segundo plano."),
        "Plano de energia" =>
            (RecommendationCategory.Desempenho, "power.high", "PowerPlug", "Processador em frequência máxima sob carga; melhor consistência de FPS."),
        "Tipo de armazenamento" =>
            (RecommendationCategory.Armazenamento, null, "Harddisk", "Tempos de arranque e de carregamento drasticamente menores."),
        "Temperatura do processador" =>
            (RecommendationCategory.Sistema, null, "Thermometer", "Menos throttling térmico e maior vida útil dos componentes."),
        "Fragmentação do disco" =>
            (RecommendationCategory.Armazenamento, "disk.optimize", "HarddiskPlus", "Leituras sequenciais mais rápidas em discos mecânicos."),
        "Dispositivos com problemas" =>
            (RecommendationCategory.Sistema, "device.open-manager", "Devices", "Hardware a funcionar corretamente e menos erros de sistema."),
        "Saúde dos discos (SMART)" =>
            (RecommendationCategory.Armazenamento, "backup.copy", "DatabaseAlert", "Proteção dos seus dados contra falha iminente do disco."),
        "Atualizações do sistema em atraso" or "Atualizações de segurança pendentes" =>
            (RecommendationCategory.Seguranca, "windows.update", "ShieldSync", "Vulnerabilidades corrigidas e maior estabilidade."),
        "Antivírus ativo" =>
            (RecommendationCategory.Seguranca, "security.enable-av", "ShieldCheck", "Proteção contra malware em tempo real."),
        "Proteção em tempo real" =>
            (RecommendationCategory.Seguranca, "security.enable-realtime", "ShieldCheck", "Bloqueio de ameaças à medida que aparecem."),
        "Firewall" =>
            (RecommendationCategory.Seguranca, "security.enable-firewall", "Wall", "Tráfego de rede não autorizado bloqueado."),
        "Controlo de Conta de Utilizador (UAC)" =>
            (RecommendationCategory.Seguranca, "security.enable-uac", "AccountAlert", "Barreira contra alterações não autorizadas no sistema."),
        "SmartScreen" =>
            (RecommendationCategory.Seguranca, "security.enable-smartscreen", "WebCheck", "Downloads e sites perigosos bloqueados automaticamente."),
        "Telemetria do Windows" =>
            (RecommendationCategory.Seguranca, "privacy.telemetry.min", "EyeOff", "Menos dados enviados para a Microsoft e menos tráfego em segundo plano."),
        "Sensor de Armazenamento (limpeza automática)" =>
            (RecommendationCategory.Armazenamento, "disk.storagesense", "Broom", "Espaço livre mantido automaticamente."),
        "Modo Jogo configurado" =>
            (RecommendationCategory.Graficos, "gamemode.configure", "ControllerClassic", "Prioridade automática do jogo; FPS mais estáveis."),
        "Efeitos visuais do Windows" =>
            (RecommendationCategory.Desempenho, "performance.visualeffects", "AnimationPlay", "Interface mais leve, com menos uso de GPU."),
        _ => (RecommendationCategory.Sistema, null, "TuneVariant", $"Melhora o indicador «{dimension}»."),
    };

    /// <summary>Classificação textual partilhada com a UI.</summary>
    public static string GradeFor(double score) => Formatters.ScoreGradeLabel(score);
}
