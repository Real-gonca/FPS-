using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HL.Optimizer.Pro.Core.Interfaces;
using HL.Optimizer.Pro.Core.Models;
using System.Collections.ObjectModel;

namespace HL.Optimizer.Pro.ViewModels;

public partial class RestoreViewModel : BaseViewModel
{
    private readonly IRestoreService _restore;
    private readonly ILogService _log;

    [ObservableProperty] private ObservableCollection<RestorePointInfo> _restorePoints = new();
    [ObservableProperty] private string _newRestorePointDescription = $"HL Optimizer - {DateTime.Now:dd/MM/yyyy HH:mm}";
    [ObservableProperty] private bool _isCreating;

    public RestoreViewModel(IRestoreService restore, ILogService log)
    {
        _restore = restore;
        _log = log;
    }

    public override async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            var points = await _restore.GetRestorePointsAsync();
            RestorePoints = new ObservableCollection<RestorePointInfo>(points);
            StatusMessage = $"{points.Count} pontos de restauração encontrados";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task CreateRestorePoint()
    {
        if (string.IsNullOrWhiteSpace(NewRestorePointDescription))
        {
            System.Windows.MessageBox.Show("Descrição não pode estar vazia", "Restauração", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        IsCreating = true;
        IsLoading = true;
        try
        {
            StatusMessage = "Criando ponto de restauração... Isso pode levar alguns minutos";
            var success = await _restore.CreateRestorePointAsync(NewRestorePointDescription);
            if (success)
            {
                StatusMessage = "Ponto de restauração criado com sucesso";
                System.Windows.MessageBox.Show("Ponto de restauração criado com sucesso!", "Restauração", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                await _log.LogAsync("Criar ponto de restauração", "Restauração", "SUCESSO", NewRestorePointDescription);
                await InitializeAsync();
            }
            else
            {
                StatusMessage = "Falha ao criar ponto de restauração - verifique se a proteção do sistema está ativada";
                System.Windows.MessageBox.Show("Falha ao criar ponto de restauração.\n\nVerifique se:\n- Proteção do sistema está ativada\n- Você está executando como administrador\n- Há espaço em disco suficiente", "Erro", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
        finally
        {
            IsCreating = false;
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenSystemRestore()
    {
        _restore.OpenSystemRestore();
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await InitializeAsync();
    }
}
