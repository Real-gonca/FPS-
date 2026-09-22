using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HL.Optimizer.Pro.Core.Interfaces;
using HL.Optimizer.Pro.Core.Models;
using System.Collections.ObjectModel;

namespace HL.Optimizer.Pro.ViewModels;

public partial class DiagnosticsViewModel : BaseViewModel
{
    private readonly IDiagnosisService _diagnosis;

    [ObservableProperty] private ObservableCollection<DiagnosisResult> _results = new();
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _currentCheck = "";
    [ObservableProperty] private int _issuesCount;
    [ObservableProperty] private int _warningsCount;

    public DiagnosticsViewModel(IDiagnosisService diagnosis)
    {
        _diagnosis = diagnosis;
    }

    public override Task InitializeAsync() => Task.CompletedTask;

    [RelayCommand]
    private async Task RunDiagnostics()
    {
        IsRunning = true;
        IsLoading = true;
        Results.Clear();
        try
        {
            var progress = new Progress<string>(s => CurrentCheck = s);
            var results = await _diagnosis.RunDiagnosisAsync(progress);
            Results = new ObservableCollection<DiagnosisResult>(results);
            IssuesCount = results.Count(r => r.Severity == DiagnosisSeverity.Error || r.Severity == DiagnosisSeverity.Critical);
            WarningsCount = results.Count(r => r.Severity == DiagnosisSeverity.Warning);
            StatusMessage = $"Diagnóstico concluído: {IssuesCount} problemas, {WarningsCount} avisos";
        }
        finally
        {
            IsRunning = false;
            IsLoading = false;
            CurrentCheck = "";
        }
    }

    [RelayCommand]
    private async Task FixIssue(DiagnosisResult? result)
    {
        if (result == null || result.FixAction == null) return;
        IsLoading = true;
        try
        {
            var res = await result.FixAction();
            if (res.Success)
            {
                result.IsFixed = true;
                StatusMessage = $"{result.Title} corrigido: {res.Message}";
            }
            else
            {
                StatusMessage = $"Falha ao corrigir {result.Title}: {res.Error}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro: {ex.Message}";
        }
        finally { IsLoading = false; }
    }
}
