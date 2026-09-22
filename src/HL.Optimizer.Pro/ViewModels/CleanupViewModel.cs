using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HL.Optimizer.Pro.Core.Interfaces;
using HL.Optimizer.Pro.Core.Models;
using System.Collections.ObjectModel;

namespace HL.Optimizer.Pro.ViewModels;

public partial class CleanupViewModel : BaseViewModel
{
    private readonly ICleanupService _cleanup;

    [ObservableProperty] private ObservableCollection<CleanupItem> _items = new();
    [ObservableProperty] private long _totalSize;
    [ObservableProperty] private string _totalSizeFormatted = "0 B";
    [ObservableProperty] private bool _isAnalyzing;
    [ObservableProperty] private bool _isCleaning;
    [ObservableProperty] private string _currentAction = "";
    [ObservableProperty] private int _selectedCount;

    public CleanupViewModel(ICleanupService cleanup)
    {
        _cleanup = cleanup;
    }

    public override Task InitializeAsync() => Task.CompletedTask;

    [RelayCommand]
    private async Task Analyze()
    {
        IsAnalyzing = true;
        IsLoading = true;
        Items.Clear();
        TotalSize = 0;
        try
        {
            var progress = new Progress<string>(s => CurrentAction = s);
            var results = await _cleanup.AnalyzeAsync(progress);
            Items = new ObservableCollection<CleanupItem>(results);
            UpdateTotals();
            StatusMessage = $"{Items.Count} categorias encontradas";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro: {ex.Message}";
        }
        finally
        {
            IsAnalyzing = false;
            IsLoading = false;
            CurrentAction = "";
        }
    }

    [RelayCommand]
    private async Task CleanSelected()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (!selected.Any())
        {
            System.Windows.MessageBox.Show("Nenhum item selecionado", "Limpeza", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        var confirm = System.Windows.MessageBox.Show($"Deseja limpar {selected.Count} itens?\n\nTotal: {CleanupItem.FormatBytes(selected.Sum(s => s.SizeBytes))}\n\nEsta ação não pode ser desfeita.",
            "Confirmar Limpeza", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);

        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        IsCleaning = true;
        IsLoading = true;
        try
        {
            var progress = new Progress<string>(s => CurrentAction = s);
            var result = await _cleanup.CleanupAsync(selected, progress);
            StatusMessage = $"{CleanupItem.FormatBytes(result.TotalFreedBytes)} liberados, {result.TotalFilesDeleted} arquivos removidos";
            System.Windows.MessageBox.Show($"{CleanupItem.FormatBytes(result.TotalFreedBytes)} liberados!\n{result.TotalFilesDeleted} arquivos removidos.",
                "Limpeza Concluída", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            await Analyze();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro: {ex.Message}";
        }
        finally
        {
            IsCleaning = false;
            IsLoading = false;
            CurrentAction = "";
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in Items) item.IsSelected = true;
        UpdateTotals();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var item in Items) item.IsSelected = false;
        UpdateTotals();
    }

    partial void OnItemsChanged(ObservableCollection<CleanupItem> value) => UpdateTotals();

    private void UpdateTotals()
    {
        var selected = Items.Where(i => i.IsSelected);
        TotalSize = selected.Sum(i => i.SizeBytes);
        TotalSizeFormatted = CleanupItem.FormatBytes(TotalSize);
        SelectedCount = selected.Count();
    }
}
