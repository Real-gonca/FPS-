using System.Windows;
using HL.Optimizer.ViewModels;
namespace HL.Optimizer;
public partial class App : Application
{
    private FileStream? instanceLock;
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(MainViewModel.DataDirectory);
            instanceLock = new FileStream(Path.Combine(MainViewModel.DataDirectory, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            MessageBox.Show("HL is already running or its local data folder is unavailable.", "HL OPTIMIZER PRO");
            Shutdown(1); return;
        }
        catch (UnauthorizedAccessException error)
        {
            MessageBox.Show(error.Message, "HL · Data access failed"); Shutdown(1); return;
        }
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e) { instanceLock?.Dispose(); base.OnExit(e); }
}
