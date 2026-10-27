using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using PrecisionCurve.ViewModels;

namespace PrecisionCurve
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;
        private bool _allowClose;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();

            _vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _vm;

            _vm.CurveChanged += OnCurveChanged;
            _vm.PropertyChanged += OnViewModelPropertyChanged;

            Loaded += OnLoaded;
            Closing += OnClosing;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RefreshCurve();
            TryEnableDarkTitleBar();
            _vm.RegisterHotkeys(HotkeysToggle.IsChecked == true);
        }

        private void OnCurveChanged(object sender, IReadOnlyList<(double Speed, double Gain)> points)
        {
            Graph.SetCurve(points, _vm.MaxSpeed);
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.LiveSpeed):
                case nameof(MainViewModel.LiveGain):
                case nameof(MainViewModel.DriverConnected):
                case nameof(MainViewModel.AccelerationEnabled):
                    Graph.SetLive(_vm.LiveSpeed, _vm.LiveGain,
                                  _vm.DriverConnected && _vm.AccelerationEnabled);
                    break;

                case nameof(MainViewModel.MaxSpeed):
                case nameof(MainViewModel.SelectedProfile):
                    RefreshCurve();
                    break;
            }
        }

        private void RefreshCurve()
        {
            Graph.SetCurve(_vm.SampleCurve(), _vm.MaxSpeed);
            Graph.SetLive(_vm.LiveSpeed, _vm.LiveGain, _vm.DriverConnected && _vm.AccelerationEnabled);
        }

        private void OnHotkeysToggled(object sender, RoutedEventArgs e)
        {
            // Fires while the visual tree is still being built.
            if (!IsLoaded || DataContext is not MainViewModel vm) return;

            vm.RegisterHotkeys(HotkeysToggle.IsChecked == true);
        }

        /// <summary>Hide to tray instead of exiting (the tray menu quits).</summary>
        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (_allowClose) return;

            e.Cancel = true;
            Hide();
        }

        public void ShowFromTray()
        {
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
            Topmost = true;
            Topmost = false;
        }

        public void CloseForExit()
        {
            _allowClose = true;
            Close();
        }

        // ------------------------------------------------------------------

        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaUseImmersiveDarkModeLegacy = 19;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute,
                                                        ref int value, int size);

        private void TryEnableDarkTitleBar()
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                int enabled = 1;

                if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
                }
            }
            catch (Exception)
            {
                // Cosmetic only: an old build without DWM still runs fine.
            }
        }
    }
}
