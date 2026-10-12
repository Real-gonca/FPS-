namespace HLOptimizerPro.Core.Services.System;

/// <summary>
/// Chaves usadas no dicionário de backup de cada otimização.
///
/// Porque existem constantes para isto? Porque a reversão tem de funcionar mesmo depois de a
/// aplicação ser fechada e reaberta (o diário é gravado em disco). Como não podemos serializar
/// lambdas, gravamos dados suficientes para reconstruir a reversão: estas chaves descrevem
/// exatamente o que há a restaurar.
/// </summary>
public static class BackupKeys
{
    // ---- Registro -------------------------------------------------------------
    public const string RegHive = "reg.hive";
    public const string RegSubKey = "reg.subkey";
    public const string RegValueName = "reg.value";
    public const string RegKind = "reg.kind";
    public const string RegExisted = "reg.existed";
    public const string RegOriginal = "reg.original";
    public const string RegCreatedKey = "reg.createdKey";   // true quando a chave foi criada por nós

    // ---- Serviços do Windows --------------------------------------------------
    public const string ServiceName = "svc.name";
    public const string ServiceOriginalStart = "svc.start";
    public const string ServiceOriginalStartType = "svc.startType";

    // ---- Planos de energia (powercfg) ----------------------------------------
    public const string PowerRevert = "power.revert";       // linha de comando a executar para reverter
    public const string PowerOriginalScheme = "power.scheme";

    // ---- Comandos genéricos (netsh, bcdedit, schtasks...) --------------------
    public const string CommandRevert = "cmd.revert";
    public const string CommandOriginalOutput = "cmd.output";

    // ---- Ficheiros ------------------------------------------------------------
    public const string FilePath = "file.path";
    public const string FileBackupPath = "file.backup";
    public const string FileExisted = "file.existed";

    // ---- Preferências do Windows 10/11 expostas na UI ------------------------
    public const string UiOriginal = "ui.original";
}
