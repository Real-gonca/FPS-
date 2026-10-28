using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using PrecisionCurve.Core;
using PrecisionCurve.ViewModels;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace PrecisionCurve
{
    /// <summary>
    /// Application lifetime: one tray icon, one main window (hidden at start),
    /// one view model, one driver handle.
    ///
    /// Command line:
    ///     PrecisionCurve.exe                start minimised to the tray
    ///     PrecisionCurve.exe --show         start with the window visible
    ///     PrecisionCurve.exe --selftest [vectors.json] [--quiet]
    ///                                       verify CurveMath against the C
    ///                                       reference and exit (CI friendly)
    /// </summary>
    public partial class App : Application
    {
        private const string InstanceMutexName = @"Local\PrecisionCurve.SingleInstance";

        private Mutex _instanceMutex;
        private ProfileStore _store;
        private DriverClient _driver;
        private MainViewModel _viewModel;
        private MainWindow _window;

        private WinForms.NotifyIcon _tray;
        private WinForms.ToolStripMenuItem _enableItem;
        private WinForms.ToolStripMenuItem _profileItem;
        private RawInputMonitor _rawInput;
        private ActiveProcessWatcher _processWatcher;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (TryHandleSelfTest(e.Args))
            {
                return;
            }

            bool abiOk = Abi.SelfCheck(out string abiError);
            if (!abiOk)
            {
                MessageBox.Show(
                    "This build is internally inconsistent and will not talk to the driver:\n\n" +
                    abiError,
                    "PrecisionCurve", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(2);
                return;
            }

            _instanceMutex = new Mutex(true, InstanceMutexName, out bool isFirstInstance);
            if (!isFirstInstance)
            {
                MessageBox.Show("PrecisionCurve is already running (look for the tray icon).",
                                "PrecisionCurve", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(0);
                return;
            }

            StartServices();

            bool showWindow = Array.Exists(e.Args, a =>
                string.Equals(a, "--show", StringComparison.OrdinalIgnoreCase));

            if (showWindow)
            {
                ShowWindow();
            }
            else
            {
                _tray.ShowBalloonTip(4000, "PrecisionCurve is running",
                    _viewModel.DriverConnected
                        ? "Click the tray icon to shape your curve."
                        : "Driver not connected. See docs/BUILD_AND_SIGN.md to install it.",
                    WinForms.ToolTipIcon.Info);
            }
        }

        private void StartServices()
        {
            _store = new ProfileStore();
            _store.Load();

            _driver = new DriverClient();
            _viewModel = new MainViewModel(_driver, _store);
            _viewModel.Initialize();

            _viewModel.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.AccelerationEnabled) ||
                    args.PropertyName == nameof(MainViewModel.DriverConnected))
                {
                    UpdateTrayState();
                }
            };

            _window = new MainWindow(_viewModel);

            CreateTrayIcon();

            _rawInput = new RawInputMonitor();
            _rawInput.RateUpdated += (s, rate) =>
                _viewModel.SetPollRate(rate, RawInputMonitor.GetMice().Count);
            _rawInput.Start();

            _processWatcher = new ActiveProcessWatcher();
            _processWatcher.ProcessChanged += (s, name) => _viewModel.SetActiveProcess(name);
            _processWatcher.Start();
        }

        // ------------------------------------------------------------------
        // Tray
        // ------------------------------------------------------------------

        private void CreateTrayIcon()
        {
            _tray = new WinForms.NotifyIcon
            {
                Icon = BuildTrayIcon(),
                Text = "PrecisionCurve",
                Visible = true
            };

            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("Open PrecisionCurve", null, (s, e) => ShowWindow());

            menu.Items.Add(new WinForms.ToolStripSeparator());

            _enableItem = new WinForms.ToolStripMenuItem("Acceleration enabled")
            {
                CheckOnClick = false
            };
            _enableItem.Click += (s, e) => _viewModel.ToggleEnabled();
            menu.Items.Add(_enableItem);

            var stages = new WinForms.ToolStripMenuItem("DPI stage");
            for (int i = 0; i < NativeMethods.StageMax; i++)
            {
                int index = i;
                var item = new WinForms.ToolStripMenuItem($"Stage {i + 1}", null,
                    (s, e) => _viewModel.SelectStage(index));
                stages.DropDownItems.Add(item);
            }
            menu.Items.Add(stages);

            _profileItem = new WinForms.ToolStripMenuItem("Next profile", null,
                (s, e) => _viewModel.SelectNextProfile());
            menu.Items.Add(_profileItem);

            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("Configuration folder", null, (s, e) => _viewModel.Commands.OpenConfigFolder.Execute(null));
            menu.Items.Add("Exit", null, (s, e) => ExitApplication());

            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (s, e) => ShowWindow();

            UpdateTrayState();
        }

        private void UpdateTrayState()
        {
            if (_enableItem == null || _viewModel == null) return;

            _enableItem.Checked = _viewModel.AccelerationEnabled;
            _enableItem.Text = _viewModel.AccelerationEnabled
                ? "Acceleration: on"
                : "Acceleration: off";

            _tray.Text = _viewModel.DriverConnected
                ? $"PrecisionCurve — {(_viewModel.AccelerationEnabled ? "on" : "off")}"
                : "PrecisionCurve — driver not connected";
        }

        private void ShowWindow()
        {
            _window?.ShowFromTray();
        }

        private void ExitApplication()
        {
            _viewModel?.Dispose();
            _window?.CloseForExit();
            Shutdown(0);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _rawInput?.Dispose();
                _processWatcher?.Dispose();
                _viewModel?.Dispose();

                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                }
            }
            catch (Exception)
            {
                // Never let shutdown throw: Windows is already tearing us down.
            }

            _instanceMutex?.Dispose();
            base.OnExit(e);
        }

        /// <summary>
        /// Draws the tray icon at runtime so the repository needs no binary
        /// assets: a dark rounded tile with the acceleration curve on it.
        /// </summary>
        private static Drawing.Icon BuildTrayIcon()
        {
            const int size = 32;

            using (var bitmap = new Drawing.Bitmap(size, size))
            using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.Clear(Drawing.Color.Transparent);

                using (var background = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 21, 26, 33)))
                {
                    graphics.FillEllipse(background, 0, 0, size - 1, size - 1);
                }

                using (var pen = new Drawing.Pen(Drawing.Color.FromArgb(255, 79, 156, 249), 3f))
                {
                    pen.StartCap = Drawing.Drawing2D.LineCap.Round;
                    pen.EndCap = Drawing.Drawing2D.LineCap.Round;

                    graphics.DrawBezier(pen,
                        new Drawing.PointF(7, 23),
                        new Drawing.PointF(13, 23),
                        new Drawing.PointF(14, 9),
                        new Drawing.PointF(25, 8));
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Drawing.Icon temporary = Drawing.Icon.FromHandle(handle))
                    {
                        return (Drawing.Icon)temporary.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(handle);   // FromHandle does not own the handle
                }
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        // ------------------------------------------------------------------
        // --selftest
        // ------------------------------------------------------------------

        private bool TryHandleSelfTest(string[] args)
        {
            int index = Array.FindIndex(args, a =>
                string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase));

            if (index < 0) return false;

            bool quiet = Array.Exists(args, a =>
                string.Equals(a, "--quiet", StringComparison.OrdinalIgnoreCase));

            string vectors = (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                ? args[index + 1]
                : Path.Combine(AppContext.BaseDirectory, "vectors.json");

            int failures = CurveMath.SelfTest(vectors, out string message);

            string logPath = Path.Combine(AppContext.BaseDirectory, "selftest.log");
            File.WriteAllText(logPath, message + Environment.NewLine);

            if (!quiet)
            {
                MessageBox.Show(message, "PrecisionCurve self-test",
                                MessageBoxButton.OK,
                                failures == 0 ? MessageBoxImage.Information : MessageBoxImage.Error);
            }

            Shutdown(failures == 0 ? 0 : 1);
            return true;
        }
    }
}
