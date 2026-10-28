using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;
using PrecisionCurve.Core;

namespace PrecisionCurve.ViewModels
{
    /// <summary>
    /// Every user operation the app exposes. Both the window and the tray menu
    /// call these, so behaviour cannot drift between the two surfaces.
    /// </summary>
    public sealed class Commands
    {
        private readonly MainViewModel _vm;
        private readonly ProfileStore _store;

        public Commands(MainViewModel vm, ProfileStore store)
        {
            _vm = vm;
            _store = store;

            Apply = new RelayCommand(() => _vm.ApplyToDriver());
            ToggleEnabled = new RelayCommand(() => _vm.ToggleEnabled());
            NextStage = new RelayCommand(() => _vm.NextStage());
            NextProfile = new RelayCommand(() => _vm.SelectNextProfile());
            ResetStats = new RelayCommand(ResetStatistics);
            OpenConfigFolder = new RelayCommand(OpenFolders);

            NewProfile = new RelayCommand(p => CreateProfile(p as string ?? "Unity"));
            DuplicateProfile = new RelayCommand(DuplicateSelected);
            DeleteProfile = new RelayCommand(DeleteSelected,
                                             () => _vm.Profiles.Count > 1);

            AddStage = new RelayCommand(AddStage);
            RemoveStage = new RelayCommand(RemoveStage, () => _vm.Stages.Count > 1);
        }

        public ICommand Apply { get; }
        public ICommand ToggleEnabled { get; }
        public ICommand NextStage { get; }
        public ICommand NextProfile { get; }
        public ICommand ResetStats { get; }
        public ICommand OpenConfigFolder { get; }
        public ICommand NewProfile { get; }
        public ICommand DuplicateProfile { get; }
        public ICommand DeleteProfile { get; }
        public ICommand AddStage { get; }
        public ICommand RemoveStage { get; }

        private void CreateProfile(string preset)
        {
            Profile profile = Profile.FromPreset(preset);
            profile.Name = UniqueName(profile.Name);

            _store.Profiles.Add(profile);
            _vm.Profiles.Add(profile);
            _vm.SelectedProfile = profile;

            _store.Save();
            _vm.StatusMessage = $"Created profile “{profile.Name}” from preset {preset}.";
        }

        private void DuplicateSelected()
        {
            if (_vm.SelectedProfile == null) return;

            Profile copy = _vm.SelectedProfile.Clone();
            copy.Name = UniqueName(copy.Name + " copy");

            _store.Profiles.Add(copy);
            _vm.Profiles.Add(copy);
            _vm.SelectedProfile = copy;

            _store.Save();
        }

        private void DeleteSelected()
        {
            if (_vm.SelectedProfile == null || _vm.Profiles.Count <= 1) return;

            Profile doomed = _vm.SelectedProfile;
            int index = _vm.Profiles.IndexOf(doomed);

            _store.Profiles.Remove(doomed);
            _vm.Profiles.Remove(doomed);

            _vm.SelectedProfile = _vm.Profiles[Math.Clamp(index, 0, _vm.Profiles.Count - 1)];

            _store.Save();
            _vm.StatusMessage = $"Deleted profile “{doomed.Name}”.";
        }

        private void AddStage()
        {
            if (_vm.SelectedProfile == null) return;
            if (_vm.SelectedProfile.Stages.Count >= NativeMethods.StageMax)
            {
                _vm.StatusMessage = $"The kernel ABI supports {NativeMethods.StageMax} stages.";
                return;
            }

            _vm.SelectedProfile.Stages.Add(new StageDefinition
            {
                Name = $"Stage {_vm.SelectedProfile.Stages.Count + 1}",
                ScaleX = 1.0,
                ScaleY = 1.0
            });

            _vm.ReloadStages();
            _store.Save();
        }

        private void RemoveStage()
        {
            if (_vm.SelectedProfile == null || _vm.SelectedProfile.Stages.Count <= 1) return;

            _vm.SelectedProfile.Stages.RemoveAt(_vm.SelectedProfile.Stages.Count - 1);
            _vm.SelectedProfile.ActiveStage = Math.Min(
                _vm.SelectedProfile.ActiveStage,
                _vm.SelectedProfile.Stages.Count - 1);

            _vm.ReloadStages();
            _store.Save();
        }

        private void ResetStatistics()
        {
            _vm.ResetStatistics();
        }

        private void OpenFolders()
        {
            try
            {
                Directory.CreateDirectory(ProfileStore.ConfigDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = ProfileStore.ConfigDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _vm.StatusMessage = "Could not open the config folder: " + ex.Message;
            }
        }

        private string UniqueName(string basis)
        {
            string name = basis;
            int suffix = 2;

            while (_vm.Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                name = $"{basis} {suffix++}";
            }
            return name;
        }
    }
}
