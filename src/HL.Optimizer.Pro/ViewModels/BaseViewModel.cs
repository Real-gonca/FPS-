using CommunityToolkit.Mvvm.ComponentModel;

namespace HL.Optimizer.Pro.ViewModels;

public abstract class BaseViewModel : ObservableObject
{
    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private string _statusMessage = "";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public virtual Task InitializeAsync() => Task.CompletedTask;
}
