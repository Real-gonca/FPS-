namespace Nexus.Domain.Ports;

/// <summary>Tipos de valor suportados (espelho do modelo do Windows Registry).</summary>
public enum RegistryValueKind
{
    Dword,
    Qword,
    String,
    MultiString,
    Binary,
}

/// <summary>Estado atual de um valor de registry (probe para backup).</summary>
public sealed record RegistryProbe(bool Exists, RegistryValueKind? Kind, object? Value);

/// <summary>
/// Acesso real ao registry do Windows.
/// <paramref name="path"/> usa o formato "HKLM\SOFTWARE\..." / "HKCU\...".
/// Falhas (chave inexistente, permissões, não-Windows) degradam para
/// "inexistente" ou lançam exceção controlada — nunca crash da app (spec §5).
/// </summary>
public interface IRegistryAccess
{
    Task<RegistryProbe> ProbeAsync(string path, string valueName, CancellationToken ct = default);

    Task SetAsync(string path, string valueName, RegistryValueKind kind, object value, CancellationToken ct = default);

    Task DeleteAsync(string path, string valueName, CancellationToken ct = default);
}
