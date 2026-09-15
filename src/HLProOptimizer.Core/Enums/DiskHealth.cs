namespace HLProOptimizer.Core.Enums;

/// <summary>Saúde estimada de um volume, baseada no percentual de espaço livre.</summary>
public enum DiskHealth
{
    /// <summary>Crítico: menos de 5% livre.</summary>
    Critical = 0,

    /// <summary>Atenção: menos de 10% livre.</summary>
    Warning = 1,

    /// <summary>Saudável.</summary>
    Healthy = 2
}
