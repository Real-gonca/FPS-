using HLOptimizerPro.Core.Helpers;

namespace HLOptimizerPro.Core.Models;

/// <summary>Informação estática (detetada uma vez no arranque) da máquina.</summary>
public sealed class HardwareInfo
{
    public string CpuName { get; init; } = "Desconhecido";
    public int CpuCores { get; init; }
    public int CpuThreads { get; init; }
    public double CpuMaxClockMhz { get; init; }

    public string GpuName { get; init; } = "Sem GPU dedicada detetada";
    public long GpuVramBytes { get; init; }

    public long TotalRamBytes { get; init; }

    public string StorageName { get; init; } = "Desconhecido";
    public string StorageType { get; init; } = "Desconhecido";   // SSD NVMe / SSD SATA / HDD
    public long StorageTotalBytes { get; init; }

    public string OperatingSystem { get; init; } = "Windows";
    public string OsBuild { get; init; } = string.Empty;
    public string Architecture { get; init; } = "x64";
    public string MachineName { get; init; } = Environment.MachineName;
    public string BiosVersion { get; init; } = string.Empty;
    public string MotherboardName { get; init; } = string.Empty;

    /// <summary>Resumo de hardware para a tela PC Boost / "deteção de sistema".</summary>
    public string Summary =>
        $"{CpuName} • {TotalRamBytes / 1024d / 1024d / 1024d:N1} GB RAM • {GpuName} • {StorageType} " +
        $"{StorageTotalBytes / 1024d / 1024d / 1024d:N0} GB • {OperatingSystem}";
}

/// <summary>Informação do disco usado pela aplicação e pelas recomendações.</summary>
public sealed class DiskInfo
{
    public string Drive { get; init; } = "C:";
    public string VolumeLabel { get; init; } = string.Empty;
    public string FileSystem { get; init; } = "NTFS";
    public string MediaType { get; init; } = "Desconhecido";
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
    public bool IsSystemDrive { get; init; }

    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);
    public double FreePercent => TotalBytes <= 0 ? 0 : FreeBytes * 100.0 / TotalBytes;
    public double UsedPercent => TotalBytes <= 0 ? 0 : UsedBytes * 100.0 / TotalBytes;
}

/// <summary>Ocupação de memória detalhada (módulo RAM).</summary>
public sealed class MemorySnapshot
{
    public long TotalBytes { get; init; }
    public long AvailableBytes { get; init; }
    public long UsedBytes { get; init; }

    /// <summary>Memória em cache do sistema de ficheiros (Windows: "Cached bytes").</summary>
    public long CachedBytes { get; init; }

    /// <summary>Lista de reserva ("standby"), libertada pela limpeza de memória em espera.</summary>
    public long StandbyBytes { get; init; }

    /// <summary>Páginas modificadas ainda por escrever em disco.</summary>
    public long ModifiedBytes { get; init; }

    /// <summary>Memória de ficheiros mapeados (cache de mapa de ficheiros).</summary>
    public long MappedBytes { get; init; }

    public long PageFileTotalBytes { get; init; }
    public long PageFileUsedBytes { get; init; }

    /// <summary>Taxa de uso (0–1).</summary>
    public double Usage => TotalBytes <= 0 ? 0 : (double)UsedBytes / TotalBytes;

    /// <summary>Percentagem livre (0–100) — base da dica "manter ≥ 20 % livre".</summary>
    public double FreePercent => TotalBytes <= 0 ? 0 : AvailableBytes * 100.0 / TotalBytes;

    /// <summary>Uso do ficheiro de paginação (0–1).</summary>
    public double PageFileUsage => PageFileTotalBytes <= 0 ? 0 : (double)PageFileUsedBytes / PageFileTotalBytes;

    /// <summary>
    /// Pressão de memória: combina ocupação, falta de espaço livre e uso do pagefile.
    /// 0 = folgado, 1 = crítico. Fórmula documentada e testada em <c>MemoryPressureTests</c>.
    /// </summary>
    public double Pressure
    {
        get
        {
            if (TotalBytes <= 0) return 0;

            // 65 % de ocupação já é considerado pressão moderada em Windows moderno.
            var usageComponent = Math.Clamp((Usage - 0.65) / 0.30, 0, 1);

            // Perder a folga de 20 % penaliza fortemente.
            var freeComponent = Math.Clamp((AppConstants.MinFreeRamPercent - FreePercent) / AppConstants.MinFreeRamPercent, 0, 1);

            var pageComponent = Math.Clamp((PageFileUsage - 0.5) / 0.5, 0, 1);

            return Math.Clamp(usageComponent * 0.55 + freeComponent * 0.30 + pageComponent * 0.15, 0, 1);
        }
    }

    /// <summary>Classificação de saúde da memória para pintura de gauges/barras.</summary>
    public HealthLevel Health => Usage switch
    {
        < 0.70 => HealthLevel.Otimo,
        < 0.85 => HealthLevel.Atencao,
        _ => HealthLevel.Critico,
    };
}

/// <summary>Leitura instantânea de CPU/GPU/RAM/disco usada pelos gauges em tempo real.</summary>
public sealed class SystemMetrics
{
    public DateTime Timestamp { get; init; } = DateTime.Now;

    /// <summary>Uso total de CPU (0–1).</summary>
    public double CpuUsage { get; init; }

    /// <summary>Frequência atual em MHz (pode ser 0 se o contador não estiver disponível).</summary>
    public double CpuClockMhz { get; init; }

    /// <summary>Uso de CPU por núcleo (0–1), opcional.</summary>
    public double[] CpuPerCore { get; init; } = Array.Empty<double>();

    public double GpuUsage { get; init; }          // 0–1 (0 quando não detetável)
    public bool GpuUsageAvailable { get; init; }
    public long GpuUsedVramBytes { get; init; }
    public long GpuTotalVramBytes { get; init; }

    public MemorySnapshot Memory { get; init; } = new();

    public double DiskActivePercent { get; init; } // 0–100
    public long DiskReadBytesPerSecond { get; init; }
    public long DiskWriteBytesPerSecond { get; init; }

    public double? CpuTemperatureCelsius { get; init; }
    public double? GpuTemperatureCelsius { get; init; }

    public TimeSpan Uptime { get; init; } = TimeSpan.Zero;

    /// <summary>Somatório ponderado usado pelo cabeçalho ("Sistema otimizado").</summary>
    public HealthLevel OverallHealth
    {
        get
        {
            var worst = HealthLevel.Otimo;
            void Consider(HealthLevel level)
            {
                if (level > worst && level != HealthLevel.Desconhecido) worst = level;
            }

            Consider(CpuUsage switch { < 0.55 => HealthLevel.Otimo, < 0.80 => HealthLevel.Atencao, _ => HealthLevel.Critico });
            Consider(Memory.Health);
            if (GpuUsageAvailable)
                Consider(GpuUsage switch { < 0.75 => HealthLevel.Otimo, < 0.92 => HealthLevel.Atencao, _ => HealthLevel.Critico });
            if (DiskActivePercent > 0)
                Consider(DiskActivePercent switch { < 60 => HealthLevel.Otimo, < 85 => HealthLevel.Atencao, _ => HealthLevel.Critico });
            // Temperaturas: 75 °C aviso, 88 °C crítico.
            if (CpuTemperatureCelsius is { } t)
                Consider(t switch { < 75 => HealthLevel.Otimo, < 88 => HealthLevel.Atencao, _ => HealthLevel.Critico });

            return worst;
        }
    }
}

/// <summary>Ponto persistido para o gráfico "Desempenho do Sistema" (24 h).</summary>
public sealed class MetricSample
{
    public DateTime Timestamp { get; set; }
    public double CpuPercent { get; set; }
    public double RamPercent { get; set; }
    public double GpuPercent { get; set; }
    public double? CpuTemperature { get; set; }

    /// <summary>Linha compacta para o ficheiro JSONL: timestamp|cpu|ram|gpu|temp.</summary>
    public string ToLine() =>
        FormattableString.Invariant($"{Timestamp:O}|{CpuPercent:F2}|{RamPercent:F2}|{GpuPercent:F2}|{(CpuTemperature?.ToString("F1") ?? string.Empty)}");

    public static MetricSample? FromLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var parts = line.Split('|');
        if (parts.Length < 4 || !DateTime.TryParse(parts[0], out var ts)) return null;

        static double Num(string s) => double.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

        return new MetricSample
        {
            Timestamp = ts,
            CpuPercent = Num(parts[1]),
            RamPercent = Num(parts[2]),
            GpuPercent = Num(parts[3]),
            CpuTemperature = parts.Length > 4 && double.TryParse(parts[4], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var temp) ? temp : null,
        };
    }
}

/// <summary>Processo que consome recursos (lista do módulo de memória e do monitor).</summary>
public sealed class ProcessUsageInfo
{
    public int Pid { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public long WorkingSetBytes { get; init; }
    public long PrivateBytes { get; init; }
    public double CpuPercent { get; init; }
    public DateTime StartTime { get; init; }
    public bool IsProtected { get; init; }        // nunca encerrar (lista de proteção)
    public bool IsSystemCritical { get; init; }
    public string? ExecutablePath { get; init; }
}
