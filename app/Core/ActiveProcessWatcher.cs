using System;
using System.Diagnostics;
using System.Windows.Threading;

namespace PrecisionCurve.Core
{
    /// <summary>
    /// Reports the process that owns the foreground window, which is what
    /// automatic profile switching keys on.
    ///
    /// Polling GetForegroundWindow is intentional: a WinEvent hook
    /// (EVENT_SYSTEM_FOREGROUND) is more elegant but fires while a window is
    /// being torn down, where the owning process is already gone, and it can
    /// miss the activation that follows a UAC prompt. A 300 ms poll costs
    /// nothing and never races with process teardown because every lookup is
    /// individually guarded.
    /// </summary>
    public sealed class ActiveProcessWatcher : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private string _current = string.Empty;
        private bool _disposed;

        public event EventHandler<string> ProcessChanged;

        public string CurrentProcess => _current;

        public ActiveProcessWatcher(TimeSpan? interval = null)
        {
            _timer = new DispatcherTimer
            {
                Interval = interval ?? TimeSpan.FromMilliseconds(300)
            };
            _timer.Tick += OnTick;
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();

        private void OnTick(object sender, EventArgs e)
        {
            string name = TryGetForegroundProcess();
            if (name == null || string.Equals(name, _current, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _current = name;
            ProcessChanged?.Invoke(this, name);
        }

        private static string TryGetForegroundProcess()
        {
            try
            {
                IntPtr window = NativeMethods.GetForegroundWindow();
                if (window == IntPtr.Zero) return null;

                NativeMethods.GetWindowThreadProcessId(window, out uint pid);
                if (pid == 0) return null;

                using (Process process = Process.GetProcessById((int)pid))
                {
                    return process.ProcessName;
                }
            }
            catch (Exception)
            {
                // The process exited between the two calls, or is protected.
                return null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _timer.Tick -= OnTick;
        }
    }
}
