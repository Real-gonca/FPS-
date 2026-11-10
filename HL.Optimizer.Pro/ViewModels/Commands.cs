using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
namespace HL.Optimizer.ViewModels;
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null) { if (Equals(field, value)) return false; field = value; Changed(property); return true; }
}
public sealed class AsyncCommand(Func<Task> execute, Action<Exception> error) : ICommand
{
    private bool running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !running;
    public async void Execute(object? parameter)
    {
        if (running) return; running = true; CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await execute(); } catch (OperationCanceledException) { } catch (Exception e) { error(e); }
        finally { running = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}
