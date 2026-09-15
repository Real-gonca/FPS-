using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Presentation.ViewModels.Tools;

/// <summary>Seções "Sistema" e "Backup" da tela de Ferramentas.</summary>
/// <remarks>
/// <para>
/// <b>Ferramentas longas com streaming.</b> <c>sfc /scannow</c> e
/// <c>DISM /RestoreHealth</c> podem levar minutos: a saída é transmitida por
/// <see cref="IProgress{T}"/> e aparece no diálogo de progresso e no log da tela
/// (a infraestrutura cuida do UTF-16 que o <c>sfc</c> escreve quando redirecionado).
/// </para>
/// <para>
/// <b>Restauração de sistema é destrutiva.</b> Exige confirmação com botão vermelho e
/// administrator; avisamos que o Windows reiniciará.
/// </para>
/// </remarks>
public sealed partial class ToolsViewModel
{
    /// <summary>Pontos de restauração disponíveis.</summary>
    public ObservableCollection<RestorePointInfo> RestorePoints { get; } = [];

    /// <summary>Backups locais criados pelo aplicativo.</summary>
    public ObservableCollection<string> LocalBackups { get; } = [];

    /// <summary>Ponto de restauração selecionado.</summary>
    [ObservableProperty]
    private RestorePointInfo? _selectedRestorePoint;

    /// <summary>Backup selecionado.</summary>
    [ObservableProperty]
    private string? _selectedBackup;

    /// <summary>Chave do registro para backup (entrada do usuário).</summary>
    [ObservableProperty]
    private string _registryKeyPath = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Unidade para o chkdsk.</summary>
    [ObservableProperty]
    private string _chkdskDrive = "C";

    /// <summary>Descrição do ponto de restauração a criar.</summary>
    [ObservableProperty]
    private string _restorePointDescription = "HL PRO OPTIMIZER";

    /// <summary>Indica se há pontos de restauração.</summary>
    public bool HasRestorePoints => RestorePoints.Count > 0;

    /// <summary>Indica se há backups locais.</summary>
    public bool HasLocalBackups => LocalBackups.Count > 0;

    // ---------------------------------------------------------------------
    // Reparo do sistema
    // ---------------------------------------------------------------------

    /// <summary>Executa o System File Checker.</summary>
    [RelayCommand]
    private Task RunSfcAsync() =>
        ExecuteToolAsync(
            (progress, ct) => Repair.RunSfcScanAsync(progress, ct),
            L("Tools_SfcScan"),
            ActionKind.Repair,
            requiresAdmin: true);

    /// <summary>Executa o DISM /ScanHealth.</summary>
    [RelayCommand]
    private Task RunDismScanAsync() =>
        ExecuteToolAsync(
            (progress, ct) => Repair.RunDismScanHealthAsync(progress, ct),
            L("Tools_DismScan"),
            ActionKind.Repair,
            requiresAdmin: true);

    /// <summary>Executa o DISM /RestoreHealth.</summary>
    [RelayCommand]
    private async Task RunDismRestoreAsync()
    {
        var confirmed = await ConfirmAsync(
            L("Tools_DismRepair"),
            L("Tools_DismRestoreConfirm"),
            L("Common_Start")).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        await ExecuteToolAsync(
            (progress, ct) => Repair.RunDismRestoreHealthAsync(progress, ct),
            L("Tools_DismRepair"),
            ActionKind.Repair,
            requiresAdmin: true).ConfigureAwait(true);
    }

    /// <summary>Agenda o chkdsk para o próximo boot.</summary>
    [RelayCommand]
    private async Task ScheduleChkdskAsync()
    {
        var drive = string.IsNullOrWhiteSpace(ChkdskDrive) ? "C" : ChkdskDrive.Trim().TrimEnd(':');

        var confirmed = await ConfirmAsync(
            L("Tools_Chkdsk"),
            LF("Tools_ChkdskConfirm", drive),
            L("Common_Start")).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        var scheduled = await ExecuteToolAsync(
            (_, ct) => Repair.ScheduleChkdskAsync(drive, fixErrors: true, scanBadSectors: false, ct),
            L("Tools_Chkdsk"),
            ActionKind.Repair,
            requiresAdmin: true).ConfigureAwait(true);

        if (scheduled)
        {
            await _dialogs.ShowInfoAsync(L("Tools_Chkdsk"), L("Msg_RestartRequired")).ConfigureAwait(true);
        }
    }

    /// <summary>Inicia uma verificação rápida do Windows Defender.</summary>
    [RelayCommand]
    private Task RunDefenderQuickScanAsync() =>
        ExecuteToolAsync((_, ct) => Repair.StartDefenderScanAsync(quickScan: true, ct), L("Tools_DefenderScan"), ActionKind.Repair);

    /// <summary>Inicia uma verificação completa do Windows Defender.</summary>
    [RelayCommand]
    private async Task RunDefenderFullScanAsync()
    {
        var confirmed = await ConfirmAsync(
            L("Tools_DefenderScan"),
            L("Tools_DefenderFullConfirm"),
            L("Common_Start")).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        await ExecuteToolAsync(
            (_, ct) => Repair.StartDefenderScanAsync(quickScan: false, ct),
            L("Tools_DefenderScan"),
            ActionKind.Repair).ConfigureAwait(true);
    }

    /// <summary>Abre o solucionador de problemas de desempenho.</summary>
    [RelayCommand]
    private Task RunTroubleshooterAsync() =>
        ExecuteToolAsync(
            (_, ct) => Repair.RunPerformanceTroubleshooterAsync(ct),
            L("Tools_Troubleshooter"),
            ActionKind.Repair);

    /// <summary>Limpa componentes antigos do Windows Update.</summary>
    [RelayCommand]
    private Task CleanupWindowsUpdateAsync() =>
        ExecuteToolAsync(
            (progress, ct) => Repair.CleanupWindowsUpdateAsync(progress, ct),
            L("Tools_CleanupUpdates"),
            ActionKind.Repair,
            requiresAdmin: true);

    /// <summary>Abre a Segurança do Windows.</summary>
    [RelayCommand]
    private Task OpenWindowsSecurityAsync() => OpenExternalAsync("windowsdefender://");

    // ---------------------------------------------------------------------
    // Pontos de restauração
    // ---------------------------------------------------------------------

    /// <summary>Lista os pontos de restauração.</summary>
    [RelayCommand]
    private async Task LoadRestorePointsAsync()
    {
        try
        {
            var points = await Backup.GetRestorePointsAsync().ConfigureAwait(true);

            OnUiThread(() =>
            {
                RestorePoints.Clear();

                foreach (var point in points.OrderByDescending(p => p.CreationTime))
                {
                    RestorePoints.Add(point);
                }

                OnPropertyChanged(nameof(HasRestorePoints));
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Falha ao listar os pontos de restauração.");
        }
    }

    /// <summary>Cria um ponto de restauração.</summary>
    [RelayCommand]
    private async Task CreateRestorePointAsync()
    {
        await RunBusyAsync(async () =>
        {
            var description = string.IsNullOrWhiteSpace(RestorePointDescription)
                ? "HL PRO OPTIMIZER"
                : RestorePointDescription.Trim();

            var point = await Backup.CreateRestorePointAsync(description).ConfigureAwait(true);

            if (point is null)
            {
                StatusMessage = L("Tools_RestorePointFailed");
                return;
            }

            await _history.RecordAsync(ActionKind.Backup, L("Tools_CreateRestorePoint"), point.Description, success: true)
                .ConfigureAwait(true);

            StatusMessage = LF("Tools_RestorePointCreated", point.Description);

            await LoadRestorePointsAsync().ConfigureAwait(true);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Restaura o sistema ao ponto selecionado.</summary>
    [RelayCommand]
    private async Task RestoreToPointAsync()
    {
        var point = SelectedRestorePoint;

        if (point is null)
        {
            return;
        }

        var confirmed = await ConfirmAsync(
            L("Tools_RestorePoint"),
            LF("Tools_RestoreConfirm", point.Description, point.CreationTime.ToString("dd/MM/yyyy HH:mm")),
            L("Common_Apply")).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        await ExecuteSimpleAsync(
            ct => Backup.RestoreToPointAsync(point.SequenceNumber, ct),
            L("Tools_RestorePoint"),
            ActionKind.Backup,
            requiresAdmin: true,
            successMessage: "Msg_RestartRequired").ConfigureAwait(true);
    }

    // ---------------------------------------------------------------------
    // Backup / restauração
    // ---------------------------------------------------------------------

    /// <summary>Faz backup de uma chave do registro em .reg.</summary>
    [RelayCommand]
    private async Task BackupRegistryAsync()
    {
        if (string.IsNullOrWhiteSpace(RegistryKeyPath))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var file = await Backup.BackupRegistryAsync(RegistryKeyPath.Trim()).ConfigureAwait(true);

            await _history.RecordAsync(ActionKind.Backup, L("Tools_BackupRegistry"), file, success: true).ConfigureAwait(true);

            StatusMessage = LF("Tools_BackupCreated", file);

            await LoadLocalBackupsAsync().ConfigureAwait(true);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Restaura um arquivo .reg escolhido pelo usuário.</summary>
    [RelayCommand]
    private async Task RestoreRegistryAsync()
    {
        var path = await _dialogs.ShowOpenFileDialogAsync(new FileDialogOptions(
            L("Tools_RestoreRegistry"),
            "Registro (*.reg)|*.reg|Todos os arquivos (*.*)|*.*")).ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var confirmed = await ConfirmAsync(
            L("Tools_RestoreRegistry"),
            LF("Tools_RestoreRegistryConfirm", path)).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        await ExecuteSimpleAsync(
            ct => Backup.RestoreRegistryAsync(path, ct),
            L("Tools_RestoreRegistry"),
            ActionKind.Backup,
            requiresAdmin: true,
            successMessage: "Msg_RestartRequired").ConfigureAwait(true);
    }

    /// <summary>Cria o pacote de backup completo do aplicativo.</summary>
    [RelayCommand]
    private async Task CreateFullBackupAsync()
    {
        await RunBusyAsync(async () =>
        {
            var folder = await Backup.CreateFullBackupAsync().ConfigureAwait(true);

            await _history.RecordAsync(ActionKind.Backup, L("Tools_Backup"), folder, success: true).ConfigureAwait(true);

            StatusMessage = LF("Tools_BackupCreated", folder);

            await LoadLocalBackupsAsync().ConfigureAwait(true);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Lista os backups locais.</summary>
    [RelayCommand]
    private async Task LoadLocalBackupsAsync()
    {
        try
        {
            var backups = await Backup.GetLocalBackupsAsync().ConfigureAwait(true);

            OnUiThread(() =>
            {
                LocalBackups.Clear();

                foreach (var backup in backups.OrderByDescending(b => b, StringComparer.OrdinalIgnoreCase))
                {
                    LocalBackups.Add(backup);
                }

                OnPropertyChanged(nameof(HasLocalBackups));
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Falha ao listar os backups locais.");
        }
    }

    /// <summary>Abre a pasta do backup selecionado.</summary>
    [RelayCommand]
    private Task OpenBackupFolderAsync()
    {
        var target = SelectedBackup ?? LocalBackups.FirstOrDefault();

        return string.IsNullOrWhiteSpace(target)
            ? Task.CompletedTask
            : OpenExternalAsync(target);
    }

    /// <summary>Exporta as configurações do aplicativo em JSON.</summary>
    [RelayCommand]
    private async Task ExportSettingsAsync()
    {
        var path = await _dialogs.ShowSaveFileDialogAsync(new FileDialogOptions(
            L("Tools_ExportSettings"),
            "JSON (*.json)|*.json",
            InitialFileName: $"hl-config-{DateTime.Now:yyyy-MM-dd}.json",
            DefaultExtension: ".json")).ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var saved = await Backup.ExportSettingsAsync(path).ConfigureAwait(true);

            StatusMessage = LF("Common_Exported", saved);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Importa configurações de um JSON exportado.</summary>
    [RelayCommand]
    private async Task ImportSettingsAsync()
    {
        var path = await _dialogs.ShowOpenFileDialogAsync(new FileDialogOptions(
            L("Common_Import"),
            "JSON (*.json)|*.json|Todos os arquivos (*.*)|*.*")).ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var imported = await Backup.ImportSettingsAsync(path).ConfigureAwait(true);

            if (imported is null)
            {
                StatusMessage = L("Msg_GenericError");
                return;
            }

            await _settings.SaveAsync(imported).ConfigureAwait(true);

            StatusMessage = L("Settings_Saved");

            await _dialogs.ShowInfoAsync(L("Common_Import"), L("Msg_RestartRequired")).ConfigureAwait(true);
        }, L("Common_Loading")).ConfigureAwait(true);
    }
}
