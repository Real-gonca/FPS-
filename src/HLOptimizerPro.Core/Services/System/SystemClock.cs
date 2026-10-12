using HLOptimizerPro.Core.Services.Abstractions;

namespace HLOptimizerPro.Core.Services.System;

/// <summary>Relógio do sistema (implementação real de <see cref="IClock"/>).</summary>
public sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;

    public DateTime UtcNow => DateTime.UtcNow;
}
