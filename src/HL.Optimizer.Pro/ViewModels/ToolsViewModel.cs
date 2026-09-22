using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HL.Optimizer.Pro.Core.Interfaces;
using HL.Optimizer.Pro.Core.Models;
using System.Diagnostics;

namespace HL.Optimizer.Pro.ViewModels;

public partial class ToolsViewModel : BaseViewModel
{
    private readonly INetworkService _network;
    private readonly ILogService _log;

    [ObservableProperty] private NetworkInfo? _networkInfo;
    [ObservableProperty] private double _pingLatency = -1;
    [ObservableProperty] private string _selectedDns = "Automático";
    [ObservableProperty] private string _customDnsPrimary = "";
    [ObservableProperty] private string _customDnsSecondary = "";

    public List<string> DnsOptions { get; } = new() { "Automático", "Google (8.8.8.8)", "Cloudflare (1.1.1.1)", "Quad9 (9.9.9.9)", "Personalizado" };

    public ToolsViewModel(INetworkService network, ILogService log)
    {
        _network = network;
        _log = log;
    }

    public override async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            NetworkInfo = await _network.GetNetworkInfoAsync();
            PingLatency = await _network.PingAsync();
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task RefreshNetwork()
    {
        IsLoading = true;
        try
        {
            NetworkInfo = await _network.GetNetworkInfoAsync();
            PingLatency = await _network.PingAsync();
            StatusMessage = "Informações de rede atualizadas";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task FlushDns()
    {
        IsLoading = true;
        try
        {
            await _network.FlushDnsAsync();
            StatusMessage = "Cache DNS limpo com sucesso";
            await _log.LogAsync("Flush DNS", "Rede", "SUCESSO", "Cache DNS limpo");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task ResetWinsock()
    {
        var confirm = System.Windows.MessageBox.Show("Resetar Winsock requer reinicialização. Continuar?", "Rede", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        IsLoading = true;
        try
        {
            await _network.ResetWinsockAsync();
            StatusMessage = "Winsock resetado - reinicie o computador";
            System.Windows.MessageBox.Show("Winsock resetado. Reinicie o computador para aplicar.", "Rede", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task ApplyDns()
    {
        string primary = "", secondary = "";
        switch (SelectedDns)
        {
            case "Google (8.8.8.8)": primary = "8.8.8.8"; secondary = "8.8.4.4"; break;
            case "Cloudflare (1.1.1.1)": primary = "1.1.1.1"; secondary = "1.0.0.1"; break;
            case "Quad9 (9.9.9.9)": primary = "9.9.9.9"; secondary = "149.112.112.112"; break;
            case "Personalizado": primary = CustomDnsPrimary; secondary = CustomDnsSecondary; break;
            case "Automático": primary = ""; secondary = ""; break;
        }

        if (SelectedDns != "Automático" && string.IsNullOrEmpty(primary))
        {
            System.Windows.MessageBox.Show("DNS primário inválido", "Erro", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var confirm = System.Windows.MessageBox.Show($"Alterar DNS para {primary} / {secondary}?\n\nEsta operação requer administrador.", "DNS", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        IsLoading = true;
        try
        {
            if (SelectedDns == "Automático")
            {
                // Reset to automatic via netsh
                var psi = new ProcessStartInfo { FileName = "netsh", Arguments = "interface ip set dns \"Ethernet\" dhcp", UseShellExecute = false, CreateNoWindow = true };
                using var proc = Process.Start(psi);
                proc?.WaitForExit();
                StatusMessage = "DNS configurado para automático";
            }
            else
            {
                await _network.SetDnsAsync(primary, secondary);
                StatusMessage = $"DNS alterado para {primary}";
            }
            await _log.LogAsync($"Alterar DNS para {primary}", "Rede", "SUCESSO", $"DNS: {primary}/{secondary}");
        }
        finally { IsLoading = false; }
    }

    // Windows Tools launchers
    [RelayCommand] private void OpenTaskManager() => Launch("taskmgr.exe");
    [RelayCommand] private void OpenDeviceManager() => Launch("devmgmt.msc");
    [RelayCommand] private void OpenDiskManagement() => Launch("diskmgmt.msc");
    [RelayCommand] private void OpenEventViewer() => Launch("eventvwr.msc");
    [RelayCommand] private void OpenServices() => Launch("services.msc");
    [RelayCommand] private void OpenMsConfig() => Launch("msconfig.exe");
    [RelayCommand] private void OpenRegedit() => Launch("regedit.exe");
    [RelayCommand] private void OpenControlPanel() => Launch("control.exe");
    [RelayCommand] private void OpenPowerShell() => Launch("powershell.exe");
    [RelayCommand] private void OpenCmd() => Launch("cmd.exe");
    [RelayCommand] private void OpenSystemInfo() => Launch("msinfo32.exe");
    [RelayCommand] private void OpenResourceMonitor() => Launch("resmon.exe");
    [RelayCommand] private void OpenTaskScheduler() => Launch("taskschd.msc");
    [RelayCommand] private void OpenHostsEditor()
    {
        try
        {
            var hostsPath = @"C:\Windows\System32\drivers\etc\hosts";
            Process.Start(new ProcessStartInfo { FileName = "notepad.exe", Arguments = hostsPath, UseShellExecute = true, Verb = "runas" });
        }
        catch (Exception ex) { StatusMessage = $"Erro ao abrir hosts: {ex.Message}"; }
    }

    private void Launch(string file)
    {
        try { Process.Start(new ProcessStartInfo { FileName = file, UseShellExecute = true }); }
        catch (Exception ex) { StatusMessage = $"Erro ao abrir {file}: {ex.Message}"; }
    }
}
