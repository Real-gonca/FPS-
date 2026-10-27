using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using PrecisionCurve.Core;

namespace PrecisionCurve.ViewModels
{
    /// <summary>A DPI stage as the UI sees it.</summary>
    public sealed class StageItem : ObservableObject
    {
        private readonly Action _onChanged;
        private string _name;
        private double _scaleX;
        private double _scaleY;

        public StageItem(StageDefinition definition, int index, Action onChanged)
        {
            _onChanged = onChanged;
            Index = index;
            _name = string.IsNullOrWhiteSpace(definition.Name) ? $"Stage {index + 1}" : definition.Name;
            _scaleX = definition.ScaleX;
            _scaleY = definition.ScaleY;
        }

        public int Index { get; }

        public string Name
        {
            get => _name;
            set { if (Set(ref _name, value)) _onChanged(); }
        }

        public double ScaleX
        {
            get => _scaleX;
            set
            {
                double clamped = Math.Clamp(value, 0.01, 64.0);
                if (Set(ref _scaleX, clamped))
                {
                    OnPropertyChanged(nameof(ScaleText));
                    _onChanged();
                }
            }
        }

        public double ScaleY
        {
            get => _scaleY;
            set
            {
                double clamped = Math.Clamp(value, 0.01, 64.0);
                if (Set(ref _scaleY, clamped))
                {
                    OnPropertyChanged(nameof(ScaleText));
                    _onChanged();
                }
            }
        }

        public string ScaleText => $"{ScaleX:0.00}x";
    }

    /// <summary>
    /// The whole UI state. One instance per process; the window binds to it and
    /// the tray icon drives it through the same commands the window uses, so
    /// there is exactly one code path that touches the driver.
    /// </summary>
    public sealed class MainViewModel : ObservableObject, IDisposable
    {
        private readonly DriverClient _driver;
        private readonly ProfileStore _store;
        private readonly HotkeyManager _hotkeys;
        private readonly DispatcherTimer _applyTimer;
        private readonly DispatcherTimer _statsTimer;

        private Profile _selectedProfile;
        private StageItem _selectedStage;
        private bool _suppressApply;
        private bool _disposed;

        private string _statusMessage = "Starting…";
        private string _driverStatus = "Disconnected";
        private bool _driverConnected;
        private string _statsText = string.Empty;
        private string _pollRateText = "—";
        private string _deviceText = "—";
        private string _hotkeyStatus = "Hotkeys off";
        private double _liveSpeed;
        private double _liveGain = 1.0;

        public MainViewModel(DriverClient driver, ProfileStore store)
        {
            _driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _hotkeys = new HotkeyManager();

            Profiles = new ObservableCollection<Profile>(_store.Profiles);
            Stages = new ObservableCollection<StageItem>();
            CurveModes = Enum.GetValues(typeof(CurveMode)).Cast<CurveMode>().ToArray();
            PresetNames = Profile.PresetNames;

            _applyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _applyTimer.Tick += (s, e) =>
            {
                _applyTimer.Stop();
                ApplyToDriver();
            };

            _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _statsTimer.Tick += (s, e) => RefreshStats();
            _statsTimer.Start();

            Commands = new Commands(this, _store);

            _selectedProfile = _store.ActiveProfile ?? Profiles.FirstOrDefault();
            if (_selectedProfile != null)
            {
                _store.ActiveProfileName = _selectedProfile.Name;
                LoadStages();
            }

            _driver.ConnectionChanged += (s, connected) =>
            {
                DriverConnected = connected;
                DriverStatus = connected ? $"Connected · v{DriverVersion}" : "Driver not connected";
            };
        }

        // ------------------------------------------------------------------
        // Collections / selection
        // ------------------------------------------------------------------

        public ObservableCollection<Profile> Profiles { get; }
        public ObservableCollection<StageItem> Stages { get; }
        public CurveMode[] CurveModes { get; }
        public string[] PresetNames { get; }
        public Commands Commands { get; }

        public Profile SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (value == null || ReferenceEquals(value, _selectedProfile)) return;

                _selectedProfile = value;
                _store.ActiveProfileName = value.Name;
                _suppressApply = true;
                try
                {
                    LoadStages();
                    RaiseCurveProperties();
                }
                finally
                {
                    _suppressApply = false;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(ProfileName));
                OnPropertyChanged(nameof(EffectiveDpiText));
                RaiseCurveChanged();
                QueueApply();
                _store.Save();
            }
        }

        public string ProfileName
        {
            get => _selectedProfile?.Name ?? "—";
            set
            {
                if (_selectedProfile == null || _selectedProfile.Name == value) return;
                _selectedProfile.Name = value;
                _store.ActiveProfileName = value;
                OnPropertyChanged();
                _store.Save();
            }
        }

        public StageItem SelectedStage
        {
            get => _selectedStage;
            set
            {
                if (!Set(ref _selectedStage, value)) return;
                if (value != null && _selectedProfile != null)
                {
                    _selectedProfile.ActiveStage = value.Index;
                }
                OnPropertyChanged(nameof(EffectiveDpiText));
                QueueApply();
            }
        }

        // ------------------------------------------------------------------
        // Curve parameters (proxied to the selected profile)
        // ------------------------------------------------------------------

        private CurveParameters Curve => _selectedProfile?.Curve;

        public CurveMode Mode
        {
            get => Curve?.Mode ?? CurveMode.Linear;
            set
            {
                if (Curve == null || Curve.Mode == value) return;
                Curve.Mode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsExponentRelevant));
                RaiseCurveChanged();
                QueueApply();
            }
        }

        public bool IsExponentRelevant => Mode == CurveMode.Motivity;
        public bool IsOffsetRelevant => Mode != CurveMode.Jump;

        public double Base
        {
            get => Curve?.Base ?? 1.0;
            set
            {
                if (Curve == null) return;
                Curve.Base = Math.Round(value, 3);
                OnPropertyChanged();
                RaiseCurveChanged();
                QueueApply();
            }
        }

        public double Offset
        {
            get => Curve?.Offset ?? 0.0;
            set
            {
                if (Curve == null) return;
                Curve.Offset = Math.Round(value, 2);
                OnPropertyChanged();
                RaiseCurveChanged();
                QueueApply();
            }
        }

        public double Accel
        {
            get => Curve?.Accel ?? 0.0;
            set
            {
                if (Curve == null) return;
                Curve.Accel = Math.Round(value, 3);
                OnPropertyChanged();
                RaiseCurveChanged();
                QueueApply();
            }
        }

        public double Exponent
        {
            get => Curve?.Exponent ?? 1.0;
            set
            {
                if (Curve == null) return;
                Curve.Exponent = Math.Round(value, 2);
                OnPropertyChanged();
                RaiseCurveChanged();
                QueueApply();
            }
        }

        public double Cap
        {
            get => Curve?.Cap ?? 1.0;
            set
            {
                if (Curve == null) return;
                Curve.Cap = Math.Round(Math.Max(value, Curve.Base), 2);
                OnPropertyChanged();
                RaiseCurveChanged();
                QueueApply();
            }
        }

        public double MaxSpeed
        {
            get => Curve?.MaxSpeed ?? 100.0;
            set
            {
                if (Curve == null) return;
                Curve.MaxSpeed = Math.Round(value, 1);
                OnPropertyChanged();
                RaiseCurveChanged();
                QueueApply();
            }
        }

        public int HardwareDpi
        {
            get => _selectedProfile?.HardwareDpi ?? 800;
            set
            {
                if (_selectedProfile == null) return;
                _selectedProfile.HardwareDpi = Math.Clamp(value, 50, 32000);
                OnPropertyChanged();
                OnPropertyChanged(nameof(EffectiveDpiText));
            }
        }

        public string EffectiveDpiText => _selectedProfile?.EffectiveDpiText ?? "—";

        // ------------------------------------------------------------------
        // Switches
        // ------------------------------------------------------------------

        public bool AccelerationEnabled
        {
            get => _selectedProfile?.Enabled ?? false;
            set
            {
                if (_selectedProfile == null || _selectedProfile.Enabled == value) return;
                _selectedProfile.Enabled = value;
                OnPropertyChanged();
                ApplyToDriver();
            }
        }

        public bool SnapEnabled
        {
            get => _selectedProfile?.Snap.Enabled ?? false;
            set
            {
                if (_selectedProfile == null) return;
                _selectedProfile.Snap.Enabled = value;
                OnPropertyChanged();
                RaiseCurveChanged();
                QueueApply();
            }
        }

        public int SnapLines
        {
            get => _selectedProfile?.Snap.LineCount ?? 2;
            set
            {
                if (_selectedProfile == null) return;
                _selectedProfile.Snap.LineCount = Math.Clamp(value, 1, 4);
                OnPropertyChanged();
                QueueApply();
            }
        }

        public double SnapTolerance
        {
            get => _selectedProfile?.Snap.ToleranceDegrees ?? 8.0;
            set
            {
                if (_selectedProfile == null) return;
                _selectedProfile.Snap.ToleranceDegrees = value;
                OnPropertyChanged();
                QueueApply();
            }
        }

        public double SnapStrength
        {
            get => _selectedProfile?.Snap.Strength ?? 1.0;
            set
            {
                if (_selectedProfile == null) return;
                _selectedProfile.Snap.Strength = value;
                OnPropertyChanged();
                QueueApply();
            }
        }

        public bool FramePacing
        {
            get => _selectedProfile?.FramePacing ?? true;
            set
            {
                if (_selectedProfile == null) return;
                _selectedProfile.FramePacing = value;
                OnPropertyChanged();
                QueueApply();
            }
        }

        public bool InvertX
        {
            get => _selectedProfile?.InvertX ?? false;
            set
            {
                if (_selectedProfile == null) return;
                _selectedProfile.InvertX = value;
                OnPropertyChanged();
                QueueApply();
            }
        }

        public bool InvertY
        {
            get => _selectedProfile?.InvertY ?? false;
            set
            {
                if (_selectedProfile == null) return;
                _selectedProfile.InvertY = value;
                OnPropertyChanged();
                QueueApply();
            }
        }

        public bool AutoSwitch
        {
            get => _store.AutoSwitch;
            set
            {
                if (_store.AutoSwitch == value) return;
                _store.AutoSwitch = value;
                OnPropertyChanged();
                _store.Save();
            }
        }

        public string ActiveProcess { get; private set; } = string.Empty;

        // ------------------------------------------------------------------
        // Status
        // ------------------------------------------------------------------

        public string StatusMessage
        {
            get => _statusMessage;
            set => Set(ref _statusMessage, value);
        }

        public string DriverStatus
        {
            get => _driverStatus;
            set => Set(ref _driverStatus, value);
        }

        public bool DriverConnected
        {
            get => _driverConnected;
            set => Set(ref _driverConnected, value);
        }

        public string DriverVersion { get; private set; } = "?";

        public string StatsText
        {
            get => _statsText;
            set => Set(ref _statsText, value);
        }

        public string PollRateText
        {
            get => _pollRateText;
            set => Set(ref _pollRateText, value);
        }

        public string DeviceText
        {
            get => _deviceText;
            set => Set(ref _deviceText, value);
        }

        public string HotkeyStatus
        {
            get => _hotkeyStatus;
            set => Set(ref _hotkeyStatus, value);
        }

        public double LiveSpeed
        {
            get => _liveSpeed;
            private set => Set(ref _liveSpeed, value);
        }

        public double LiveGain
        {
            get => _liveGain;
            private set => Set(ref _liveGain, value);
        }

        public event EventHandler<IReadOnlyList<(double Speed, double Gain)>> CurveChanged;

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        public void Initialize()
        {
            if (_driver.TryConnect() && _driver.TryGetInfo(out NativeMethods.PC_VERSION_INFO info))
            {
                DriverVersion = $"{info.DriverVersion >> 16}.{info.DriverVersion & 0xFFFF}";
                DriverConnected = true;
                DriverStatus = $"Connected · v{DriverVersion}";
            }
            else
            {
                DriverConnected = false;
                DriverStatus = "Driver not connected";
                StatusMessage = _driver.LastError;
            }

            ApplyToDriver();
            RaiseCurveChanged();
            RefreshStats();
        }

        public void SetPollRate(int reportsPerSecond, int deviceCount)
        {
            PollRateText = reportsPerSecond > 0 ? $"{reportsPerSecond} Hz (measured)" : "idle";
            DeviceText = deviceCount == 1 ? "1 mouse" : $"{deviceCount} mice";
        }

        public void SetActiveProcess(string process)
        {
            ActiveProcess = process ?? string.Empty;
            OnPropertyChanged(nameof(ActiveProcess));

            if (!AutoSwitch) return;
            Profile match = _store.MatchProcess(ActiveProcess);
            if (match != null && match != SelectedProfile)
            {
                SelectedProfile = match;
                StatusMessage = $"Auto-switched to “{match.Name}” for {ActiveProcess}";
            }
        }

        public IReadOnlyList<(double Speed, double Gain)> SampleCurve(int samples = 240)
        {
            return _selectedProfile == null
                ? new List<(double, double)>()
                : CurveMath.SampleCurve(_selectedProfile.Curve, samples);
        }

        private void RaiseCurveProperties()
        {
            OnPropertyChanged(nameof(Mode));
            OnPropertyChanged(nameof(Base));
            OnPropertyChanged(nameof(Offset));
            OnPropertyChanged(nameof(Accel));
            OnPropertyChanged(nameof(Exponent));
            OnPropertyChanged(nameof(Cap));
            OnPropertyChanged(nameof(MaxSpeed));
            OnPropertyChanged(nameof(HardwareDpi));
            OnPropertyChanged(nameof(AccelerationEnabled));
            OnPropertyChanged(nameof(SnapEnabled));
            OnPropertyChanged(nameof(SnapLines));
            OnPropertyChanged(nameof(SnapTolerance));
            OnPropertyChanged(nameof(SnapStrength));
            OnPropertyChanged(nameof(FramePacing));
            OnPropertyChanged(nameof(InvertX));
            OnPropertyChanged(nameof(InvertY));
            OnPropertyChanged(nameof(IsExponentRelevant));
            OnPropertyChanged(nameof(IsOffsetRelevant));
            OnPropertyChanged(nameof(EffectiveDpiText));
        }

        private void RaiseCurveChanged()
        {
            if (_selectedProfile == null) return;
            CurveChanged?.Invoke(this, SampleCurve());
            OnPropertyChanged(nameof(EffectiveDpiText));
        }

        /// <summary>Re-reads the stage list after an external edit (add/remove).</summary>
        public void ReloadStages()
        {
            LoadStages();
            RaiseCurveProperties();
            QueueApply();
        }

        private void LoadStages()
        {
            Stages.Clear();
            if (_selectedProfile == null) return;

            _selectedProfile.Stages ??= Profile.DefaultStages();

            for (int i = 0; i < _selectedProfile.Stages.Count; i++)
            {
                Stages.Add(new StageItem(_selectedProfile.Stages[i], i, OnStageEdited));
            }

            int active = Math.Clamp(_selectedProfile.ActiveStage, 0, Stages.Count - 1);
            _selectedStage = Stages.Count > 0 ? Stages[active] : null;
            OnPropertyChanged(nameof(SelectedStage));
        }

        private void OnStageEdited()
        {
            for (int i = 0; i < Stages.Count; i++)
            {
                _selectedProfile.Stages[i].ScaleX = Stages[i].ScaleX;
                _selectedProfile.Stages[i].ScaleY = Stages[i].ScaleY;
                _selectedProfile.Stages[i].Name = Stages[i].Name;
            }
            OnPropertyChanged(nameof(EffectiveDpiText));
            QueueApply();
        }

        private void QueueApply()
        {
            if (_suppressApply || _disposed) return;
            _applyTimer.Stop();
            _applyTimer.Start();
        }

        /// <summary>Pushes the selected profile to the kernel, all or nothing.</summary>
        public void ApplyToDriver()
        {
            if (_selectedProfile == null) return;

            NativeMethods.PC_SET_CONFIG config = ConfigBuilder.Build(_selectedProfile);

            if (!DriverConnected && !_driver.TryConnect())
            {
                StatusMessage = "Driver not available — changes are saved locally only.";
                return;
            }

            if (_driver.TrySetConfig(config, out NativeMethods.PC_SET_CONFIG_RESULT result))
            {
                // The driver mirrors the config's enable flag into its master
                // switch; keep the two in sync so the tray toggle shows the truth.
                _driver.TrySetEnabled(_selectedProfile.Enabled);
                StatusMessage = $"Applied “{_selectedProfile.Name}” · gen {result.Generation} · " +
                                ConfigBuilder.Describe(_selectedProfile, config);
            }
            else
            {
                DriverConnected = _driver.IsConnected;
                StatusMessage = $"Apply failed: {_driver.LastError}";
            }

            _store.Save();
            RaiseCurveChanged();
        }

        public void ToggleEnabled()
        {
            if (_selectedProfile == null) return;
            AccelerationEnabled = !AccelerationEnabled;
        }

        public void NextStage()
        {
            if (Stages.Count == 0) return;
            int next = (SelectedStage?.Index ?? 0) + 1;
            if (next >= Stages.Count) next = 0;
            SelectedStage = Stages[next];
            ApplyToDriver();
        }

        public void RefreshStats()
        {
            if (_driver == null) return;

            if (!_driver.IsConnected)
            {
                DriverConnected = false;
                DriverStatus = "Driver not connected";
                StatsText = "No kernel data";
                LiveSpeed = 0;
                LiveGain = 1.0;
                return;
            }

            if (!_driver.TryGetStats(out NativeMethods.PC_DRIVER_STATS stats))
            {
                DriverConnected = false;
                return;
            }

            DriverConnected = true;

            LiveSpeed = stats.LastSpeedQ16 / 65536.0;
            LiveGain = stats.LastGainQ16 / 65536.0;

            double dtMs = stats.LastDtTicks / 10000.0;
            StatsText = $"{stats.PacketsTotal:N0} packets · {stats.PacketsModified:N0} modified · " +
                        $"{stats.PacketsBypassed:N0} bypassed\n" +
                        $"speed {LiveSpeed:0.00} c/ms · gain {LiveGain:0.00}x · dt {dtMs:0.000} ms · " +
                        $"stage {stats.ActiveStage + 1}";
        }

        /// <summary>Clears the driver's packet counters.</summary>
        public void ResetStatistics()
        {
            if (!DriverConnected && !_driver.TryConnect())
            {
                StatusMessage = "Driver not available.";
                return;
            }

            StatusMessage = _driver.TryResetStats()
                ? "Statistics reset."
                : "Reset failed: " + _driver.LastError;
        }

        public void RegisterHotkeys(bool enabled)
        {
            _hotkeys.UnregisterAll();
            if (!enabled)
            {
                HotkeyStatus = "Hotkeys off";
                return;
            }

            var failures = new List<string>();
            Register("Ctrl+Alt+E", ToggleEnabled, "toggle", failures);
            Register("Ctrl+Alt+1", () => SelectStage(0), "stage 1", failures);
            Register("Ctrl+Alt+2", () => SelectStage(1), "stage 2", failures);
            Register("Ctrl+Alt+3", () => SelectStage(2), "stage 3", failures);
            Register("Ctrl+Alt+N", () => SelectNextProfile(), "next profile", failures);

            HotkeyStatus = failures.Count == 0
                ? "Hotkeys: Ctrl+Alt+E toggle · Ctrl+Alt+1-3 stage · Ctrl+Alt+N profile"
                : "Some hotkeys unavailable: " + string.Join(", ", failures);
        }

        private void Register(string gesture, Action action, string label, List<string> failures)
        {
            if (!_hotkeys.TryRegister(gesture, action, out string error))
            {
                failures.Add($"{label} ({error})");
            }
        }

        public void SelectStage(int index)
        {
            if (index < 0 || index >= Stages.Count) return;
            SelectedStage = Stages[index];
            ApplyToDriver();
        }

        public void SelectNextProfile()
        {
            if (Profiles.Count == 0) return;
            int index = Profiles.IndexOf(SelectedProfile);
            SelectedProfile = Profiles[(index + 1) % Profiles.Count];
        }

        public HotkeyManager Hotkeys => _hotkeys;

        public ProfileStore Store => _store;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _applyTimer.Stop();
            _statsTimer.Stop();
            _hotkeys.Dispose();
            _driver.Dispose();
            _store.Save();
        }
    }
}
