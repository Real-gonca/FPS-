using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HL.Optimizer.Pro.Core.Interfaces;
using HL.Optimizer.Pro.Core.Models;

namespace HL.Optimizer.Pro.ViewModels;

public partial class SystemInfoViewModel : BaseViewModel
{
    private readonly ISystemInfoService _systemInfo;

    [ObservableProperty] private SystemInfo? _info;
    [ObservableProperty] private string _windowsVersion = "";

    public SystemInfoViewModel(ISystemInfoService systemInfo)
    {
        _systemInfo = systemInfo;
    }

    public override async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            Info = await _systemInfo.GetSystemInfoAsync();
            WindowsVersion = await _systemInfo.GetWindowsVersionAsync();
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await InitializeAsync();
    }

    [RelayCommand]
    private void CopyToClipboard()
    {
        if (Info == null) return;
        var text = $@"HL OPTIMIZER PRO - Informações do Sistema
Computador: {Info.ComputerName}
Usuário: {Info.UserName}
OS: {Info.OsName} {Info.OsVersion} Build {Info.OsBuild} {Info.Architecture}
CPU: {Info.CpuName} ({Info.CpuCores} cores, {Info.CpuLogicalProcessors} threads)
GPU: {Info.GpuName} Driver {Info.GpuDriverVersion}
RAM: {Info.TotalRamBytes / (1024*1024*1024)} GB
Placa-mãe: {Info.Motherboard}
BIOS: {Info.BiosManufacturer} {Info.BiosVersion}
Uptime: {Info.Uptime}
Discos: {string.Join(", ", Info.Disks.Select(d => $"{d.Name} {d.FreeBytes/(1024*1024*1024):0}GB livre de {d.TotalBytes/(1024*1024*1024):0}GB"))}
";
        try
        {
            System.Windows.Clipboard.SetText(text);
            StatusMessage = "Informações copiadas para área de transferência";
        }
        catch { }
    }
}
