namespace HLProOptimizer.Core.Enums;

/// <summary>
/// Colmeias do registro suportadas. Enum próprio para que o Core não referencie
/// <c>Microsoft.Win32</c> (disponível apenas em net8.0-windows).
/// </summary>
public enum RegistryHiveKind
{
    /// <summary>HKEY_CLASSES_ROOT.</summary>
    ClassesRoot = 0,

    /// <summary>HKEY_CURRENT_USER.</summary>
    CurrentUser = 1,

    /// <summary>HKEY_LOCAL_MACHINE.</summary>
    LocalMachine = 2,

    /// <summary>HKEY_USERS.</summary>
    Users = 3,

    /// <summary>HKEY_CURRENT_CONFIG.</summary>
    CurrentConfig = 4
}
