using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HL.Optimizer.Pro.Core.Interfaces;
using HL.Optimizer.Pro.Core.Models;
using System.Collections.ObjectModel;

namespace HL.Optimizer.Pro.ViewModels;

public partial class GamesViewModel : BaseViewModel
{
    private readonly IGameDetectionService _gameService;
    private readonly IEmulatorDetectionService _emulatorService;
    private readonly IPerformanceMonitorService _perf;

    [ObservableProperty] private ObservableCollection<GameProfile> _games = new();
    [ObservableProperty] private ObservableCollection<EmulatorInfo> _emulators = new();
    [ObservableProperty] private GameProfile? _selectedGame;
    [ObservableProperty] private EmulatorInfo? _selectedEmulator;
    [ObservableProperty] private bool _isGameModeActive;
    [ObservableProperty] private PerformanceMetrics? _metrics;

    public GamesViewModel(IGameDetectionService gameService, IEmulatorDetectionService emulatorService, IPerformanceMonitorService perf)
    {
        _gameService = gameService;
        _emulatorService = emulatorService;
        _perf = perf;
        _perf.MetricsUpdated += (s, m) => Metrics = m;
    }

    public override async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            var gamesTask = _gameService.DetectGamesAsync();
            var emusTask = _emulatorService.DetectEmulatorsAsync();
            await Task.WhenAll(gamesTask, emusTask);
            Games = new ObservableCollection<GameProfile>(await gamesTask);
            Emulators = new ObservableCollection<EmulatorInfo>(await emusTask);
            Metrics = _perf.CurrentMetrics;
            SelectedGame = Games.FirstOrDefault();
            SelectedEmulator = Emulators.FirstOrDefault();
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task EnableGameMode()
    {
        if (SelectedGame == null) return;
        IsLoading = true;
        try
        {
            var success = await _gameService.ApplyGameBoosterAsync(SelectedGame);
            IsGameModeActive = success;
            StatusMessage = success ? $"Modo Jogo ativado para {SelectedGame.Name}" : "Falha ao ativar Modo Jogo";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task DisableGameMode()
    {
        if (SelectedGame == null) return;
        IsLoading = true;
        try
        {
            var success = await _gameService.RevertGameBoosterAsync(SelectedGame);
            IsGameModeActive = !success;
            StatusMessage = success ? "Modo Jogo desativado" : "Falha ao desativar";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task ApplyEmulatorProfile()
    {
        if (SelectedEmulator == null) return;
        IsLoading = true;
        try
        {
            var success = await _emulatorService.ApplyPerformanceProfileAsync(SelectedEmulator, SelectedEmulator.PerformanceProfile);
            StatusMessage = success ? $"Perfil aplicado para {SelectedEmulator.Name}" : "Falha ao aplicar perfil";
            if (success)
                System.Windows.MessageBox.Show($"Perfil de desempenho aplicado:\n\nResolução: {SelectedEmulator.PerformanceProfile.Resolution}\nDPI: {SelectedEmulator.PerformanceProfile.Dpi}\nRAM: {SelectedEmulator.PerformanceProfile.RamMB} MB\nCPU: {SelectedEmulator.PerformanceProfile.CpuCores} cores",
                    "Emulator Boost", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await InitializeAsync();
    }

    [RelayCommand]
    private void LaunchGame(GameProfile? game)
    {
        if (game == null || string.IsNullOrEmpty(game.ExecutablePath)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = game.ExecutablePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao iniciar jogo: {ex.Message}";
        }
    }
}
