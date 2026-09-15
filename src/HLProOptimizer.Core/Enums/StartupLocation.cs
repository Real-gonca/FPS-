namespace HLProOptimizer.Core.Enums;

/// <summary>Onde um item de inicialização está registrado.</summary>
public enum StartupLocation
{
    /// <summary>HKCU\...\CurrentVersion\Run.</summary>
    RegistryCurrentUser = 0,

    /// <summary>HKLM\...\CurrentVersion\Run.</summary>
    RegistryLocalMachine = 1,

    /// <summary>HKCU\...\CurrentVersion\RunOnce.</summary>
    RegistryCurrentUserOnce = 2,

    /// <summary>HKLM\...\CurrentVersion\RunOnce.</summary>
    RegistryLocalMachineOnce = 3,

    /// <summary>Pasta Inicializar do usuário.</summary>
    StartupFolderUser = 4,

    /// <summary>Pasta Inicializar compartilhada (All Users).</summary>
    StartupFolderCommon = 5,

    /// <summary>Tarefa agendada com gatilho de logon.</summary>
    TaskScheduler = 6,

    /// <summary>Serviço Windows com inicialização automática.</summary>
    Service = 7
}
