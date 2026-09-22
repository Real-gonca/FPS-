using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HL.Optimizer.Pro.Core.Interfaces;
using System.Collections.ObjectModel;
using HL.Optimizer.Pro.Core.Models;

namespace HL.Optimizer.Pro.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly ILocalizationService _localization;
    private readonly ILogService _log;

    [ObservableProperty] private string _selectedLanguage = "pt-BR";
    [ObservableProperty] private bool _autoStartWithWindows;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _enableNotifications = true;
    [ObservableProperty] private bool _createRestorePointBeforeOptimization = true;
    [ObservableProperty] private bool _enableRealTimeMonitoring = true;
    [ObservableProperty] private ObservableCollection<LogEntry> _logs = new();
    [ObservableProperty] private string _appVersion = "1.0.0";

    public List<string> Languages { get; } = new() { "pt-BR", "pt-PT", "en-US", "es-ES" };

    public SettingsViewModel(ILocalizationService localization, ILogService log)
    {
        _localization = localization;
        _log = log;
        SelectedLanguage = _localization.CurrentLanguage;
    }

    public override async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            var logs = await _log.GetLogsAsync(100);
            Logs = new ObservableCollection<LogEntry>(logs);
            AppVersion = "1.0.0 - HL Optimizer Pro";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void ChangeLanguage(string lang)
    {
        SelectedLanguage = lang;
        _localization.SetLanguage(lang);
        StatusMessage = $"Idioma alterado para {lang}";
    }

    [RelayCommand]
    private async Task ClearLogs()
    {
        var confirm = System.Windows.MessageBox.Show("Limpar todos os logs?", "Configurações", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;
        await _log.ClearLogsAsync();
        Logs.Clear();
        StatusMessage = "Logs limpos";
    }

    [RelayCommand]
    private async Task ExportLogs()
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt",
                FileName = $"hl_optimizer_logs_{DateTime.Now:yyyyMMdd}.csv"
            };
            if (dialog.ShowDialog() == true)
            {
                var lines = new List<string> { "Timestamp,Action,Category,Result,Details,Error" };
                lines.AddRange(Logs.Select(l => $"\"{l.Timestamp:yyyy-MM-dd HH:mm:ss}\",\"{l.Action}\",\"{l.Category}\",\"{l.Result}\",\"{l.Details}\",\"{l.Error}\""));
                await File.WriteAllLinesAsync(dialog.FileName, lines);
                StatusMessage = $"Logs exportados para {dialog.FileName}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao exportar: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HL Optimizer Pro");
            System.Diagnostics.Process.Start("explorer.exe", path);
        }
        catch { }
    }
}
