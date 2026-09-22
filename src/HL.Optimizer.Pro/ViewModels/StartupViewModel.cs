using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HL.Optimizer.Pro.Core.Interfaces;
using HL.Optimizer.Pro.Core.Models;
using System.Collections.ObjectModel;

namespace HL.Optimizer.Pro.ViewModels;

public partial class StartupViewModel : BaseViewModel
{
    private readonly IStartupService _startup;

    [ObservableProperty] private ObservableCollection<StartupItem> _items = new();
    [ObservableProperty] private ObservableCollection<StartupItem> _filteredItems = new();
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private ObservableCollection<ServiceInfo> _services = new();

    public StartupViewModel(IStartupService startup)
    {
        _startup = startup;
    }

    public override async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            var startupItems = await _startup.GetStartupItemsAsync();
            Items = new ObservableCollection<StartupItem>(startupItems);
            FilteredItems = new ObservableCollection<StartupItem>(startupItems);

            var svcs = await _startup.GetServicesAsync();
            Services = new ObservableCollection<ServiceInfo>(svcs.Take(100));
        }
        finally { IsLoading = false; }
    }

    partial void OnSearchTextChanged(string value) => Filter();

    private void Filter()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
            FilteredItems = new ObservableCollection<StartupItem>(Items);
        else
            FilteredItems = new ObservableCollection<StartupItem>(Items.Where(i => i.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || i.Publisher.Contains(SearchText, StringComparison.OrdinalIgnoreCase)));
    }

    [RelayCommand]
    private async Task ToggleItem(StartupItem? item)
    {
        if (item == null) return;
        IsLoading = true;
        try
        {
            var success = await _startup.SetStartupItemEnabledAsync(item, !item.IsEnabled);
            if (success)
            {
                item.IsEnabled = !item.IsEnabled;
                StatusMessage = $"{item.Name} {(item.IsEnabled ? "ativado" : "desativado")}";
            }
            else
            {
                StatusMessage = $"Falha ao alterar {item.Name} - requer administrador";
            }
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void OpenLocation(StartupItem? item)
    {
        if (item == null || string.IsNullOrEmpty(item.Path)) return;
        try
        {
            var path = item.Path.Trim('"').Split('"')[0];
            if (File.Exists(path))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Directory.Exists(Path.GetDirectoryName(path)))
                System.Diagnostics.Process.Start("explorer.exe", Path.GetDirectoryName(path)!);
        }
        catch { }
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await InitializeAsync();
    }
}
