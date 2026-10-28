using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using PrecisionCurve.Core;

namespace PrecisionCurve.Service
{
    /// <summary>
    /// A Windows service that keeps the active profile applied and switches
    /// profiles by foreground process, independently of the tray application.
    ///
    /// Why not Microsoft.Extensions.Hosting.WindowsServices? This project is
    /// meant to build offline with nothing but the .NET SDK, so the ~90 lines
    /// of SCM interop are written out instead of pulled in.
    ///
    /// Both the GUI and this service talk to the driver through the same
    /// shared Core classes; only one of them needs to be running.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            bool console = Array.Exists(args, a =>
                string.Equals(a, "--console", StringComparison.OrdinalIgnoreCase));

            if (console)
            {
                return RunConsole();
            }

            return ServiceHost.Run();
        }

        /// <summary>`--console` mode: same supervisor, no SCM, for debugging.</summary>
        private static int RunConsole()
        {
            using (var supervisor = new Supervisor())
            {
                supervisor.Start();
                Console.WriteLine("PrecisionCurve supervisor running (console mode).");
                Console.WriteLine("Ctrl+C to stop.");

                using (var stop = new ManualResetEventSlim(false))
                {
                    Console.CancelKeyPress += (s, e) =>
                    {
                        e.Cancel = true;
                        stop.Set();
                    };
                    stop.Wait();
                }
            }
            return 0;
        }
    }

    /// <summary>
    /// The actual work: apply the active profile, notice when a new process
    /// takes the foreground, switch, and re-apply after a driver restart.
    /// </summary>
    internal sealed class Supervisor : IDisposable
    {
        private readonly ProfileStore _store = new ProfileStore();
        private readonly DriverClient _driver = new DriverClient();
        private Timer _timer;
        private string _appliedProfile;
        private bool _disposed;

        public void Start()
        {
            _store.Load();

            // A service may start before the driver's devices are ready.
            _driver.TryConnect();

            ApplyActive();
            _timer = new Timer(Tick, null, 500, 500);
        }

        private void Tick(object state)
        {
            try
            {
                if (!_driver.IsConnected)
                {
                    _driver.TryConnect();
                    _appliedProfile = null;   // force a re-apply after reconnect
                }

                string process = ForegroundProcess();

                if (_store.AutoSwitch && !string.IsNullOrEmpty(process))
                {
                    Profile match = _store.MatchProcess(process);
                    if (match != null &&
                        !string.Equals(match.Name, _store.ActiveProfileName, StringComparison.OrdinalIgnoreCase))
                    {
                        _store.ActiveProfileName = match.Name;
                        _appliedProfile = null;
                    }
                }

                Profile active = _store.ActiveProfile;
                if (active == null) return;

                if (!string.Equals(active.Name, _appliedProfile, StringComparison.OrdinalIgnoreCase))
                {
                    ApplyActive();
                }
            }
            catch (Exception)
            {
                // A service must survive a transient failure; the next tick retries.
            }
        }

        private void ApplyActive()
        {
            Profile active = _store.ActiveProfile;
            if (active == null) return;

            NativeMethods.PC_SET_CONFIG config = ConfigBuilder.Build(active);

            if (_driver.TrySetConfig(config, out _))
            {
                _driver.TrySetEnabled(active.Enabled);
                _appliedProfile = active.Name;
            }
        }

        private static string ForegroundProcess()
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
                return null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _timer?.Dispose();
            _driver.Dispose();
        }
    }

    /// <summary>Minimal SCM plumbing (advapi32), no external packages.</summary>
    internal static class ServiceHost
    {
        private const string ServiceName = "PrecisionCurve";

        // SCM constants
        private const uint ServiceWin32OwnProcess = 0x00000010;
        private const uint ServiceAcceptStop = 0x00000001;
        private const uint ServiceAcceptShutdown = 0x00000004;
        private const uint ServiceRunning = 0x00000004;
        private const uint ServiceStartPending = 0x00000002;
        private const uint ServiceStopPending = 0x00000003;
        private const uint ServiceStopped = 0x00000001;
        private const uint ControlStop = 0x00000001;
        private const uint ControlShutdown = 0x00000005;
        private const uint ErrorServiceSpecificError = 1066;
        private const uint NoError = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceStatus
        {
            public uint ServiceType;
            public uint CurrentState;
            public uint ControlsAccepted;
            public uint Win32ExitCode;
            public uint ServiceSpecificExitCode;
            public uint CheckPoint;
            public uint WaitHint;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceTableEntry
        {
            public IntPtr ServiceName;
            public IntPtr ServiceProc;
        }

        private delegate void ServiceMainDelegate(uint argc, IntPtr argv);
        private delegate uint HandlerExDelegate(uint control, uint eventType, IntPtr eventData, IntPtr context);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool StartServiceCtrlDispatcherW(IntPtr table);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr RegisterServiceCtrlHandlerExW(
            string serviceName, HandlerExDelegate handler, IntPtr context);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetServiceStatus(IntPtr handle, ref ServiceStatus status);

        private static IntPtr _statusHandle;
        private static Supervisor _supervisor;
        private static readonly ManualResetEventSlim StopEvent = new ManualResetEventSlim(false);

        // Keep delegates alive for the lifetime of the process: the SCM holds
        // raw function pointers to them.
        private static readonly ServiceMainDelegate ServiceMainCallback = ServiceMain;
        private static readonly HandlerExDelegate HandlerCallback = Handler;

        public static int Run()
        {
            IntPtr name = Marshal.StringToHGlobalUni(ServiceName);
            IntPtr table = Marshal.AllocHGlobal(Marshal.SizeOf<ServiceTableEntry>() * 2);

            try
            {
                var entry = new ServiceTableEntry
                {
                    ServiceName = name,
                    ServiceProc = Marshal.GetFunctionPointerForDelegate(ServiceMainCallback)
                };
                Marshal.StructureToPtr(entry, table, false);
                Marshal.StructureToPtr(new ServiceTableEntry(), table + Marshal.SizeOf<ServiceTableEntry>(), false);

                if (!StartServiceCtrlDispatcherW(table))
                {
                    return Marshal.GetLastWin32Error();
                }
                return 0;
            }
            finally
            {
                Marshal.FreeHGlobal(table);
                Marshal.FreeHGlobal(name);
            }
        }

        private static void ServiceMain(uint argc, IntPtr argv)
        {
            _statusHandle = RegisterServiceCtrlHandlerExW(ServiceName, HandlerCallback, IntPtr.Zero);

            if (_statusHandle == IntPtr.Zero)
            {
                return;
            }

            ReportStatus(ServiceStartPending, 0, 3000);

            try
            {
                _supervisor = new Supervisor();
                _supervisor.Start();

                ReportStatus(ServiceRunning, ServiceAcceptStop | ServiceAcceptShutdown, 0);

                StopEvent.Wait();

                ReportStatus(ServiceStopPending, 0, 5000);
            }
            catch (Exception)
            {
                ReportStatus(ServiceStopped, 0, 0, ErrorServiceSpecificError);
                return;
            }
            finally
            {
                _supervisor?.Dispose();
                _supervisor = null;
            }

            ReportStatus(ServiceStopped, 0, 0);
        }

        private static uint Handler(uint control, uint eventType, IntPtr eventData, IntPtr context)
        {
            switch (control)
            {
                case ControlStop:
                case ControlShutdown:
                    StopEvent.Set();
                    return NoError;
                default:
                    return NoError;
            }
        }

        private static void ReportStatus(uint state, uint accepted, uint waitHint, uint win32Exit = 0)
        {
            if (_statusHandle == IntPtr.Zero) return;

            var status = new ServiceStatus
            {
                ServiceType = ServiceWin32OwnProcess,
                CurrentState = state,
                ControlsAccepted = accepted,
                Win32ExitCode = win32Exit,
                ServiceSpecificExitCode = 0,
                CheckPoint = 0,
                WaitHint = waitHint
            };

            SetServiceStatus(_statusHandle, ref status);
        }
    }
}
